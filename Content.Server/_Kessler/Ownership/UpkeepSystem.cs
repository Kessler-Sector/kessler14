using System.Linq;
using System.Threading.Tasks;
using Content.Server._NF.Bank;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Content.Server.Preferences.Managers;
using Content.Shared._Kessler.CCVar;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Preferences;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Kessler.Ownership;

/// <summary>
/// Bills ship owners daily upkeep from their bank account, online or offline. What they can't pay becomes debt on
/// the ship, and a ship left in debt past the grace period is flagged for impounding.
/// </summary>
/// <remarks>
/// Billing runs on real (UTC) time, stored on the ship, so a restart neither skips nor repeats a day; days missed
/// while the server was down are billed when it comes back.
/// </remarks>
public sealed class UpkeepSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IServerPreferencesManager _prefs = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly BankSystem _bank = default!;

    private TimeSpan _nextCheck;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + TimeSpan.FromSeconds(_cfg.GetCVar(KesslerCVars.UpkeepCheckSeconds));

        if (!_cfg.GetCVar(KesslerCVars.UpkeepEnabled))
            return;

        _ = RunBilling(DateTime.UtcNow);
    }

    /// <summary>
    /// The upkeep for one billing interval of this ship.
    /// </summary>
    public int GetDailyUpkeep(ShipOwnershipComponent ownership)
    {
        if (ownership.PurchasedWithVoucher || ownership.PurchasePrice <= 0)
            return 0;

        return Math.Max(1, (int) MathF.Round(ownership.PurchasePrice * _cfg.GetCVar(KesslerCVars.UpkeepRate)));
    }

    /// <summary>
    /// Bills every ship whose upkeep is due at <paramref name="now"/>. The returned task finishes when all charges,
    /// including those waiting on the database, are settled.
    /// </summary>
    public Task RunBilling(DateTime now)
    {
        var tasks = new List<Task>();
        var query = EntityQueryEnumerator<ShipOwnershipComponent>();
        while (query.MoveNext(out var uid, out var ownership))
        {
            if (TryBillShip((uid, ownership), now) is { } task)
                tasks.Add(task);
        }

        return Task.WhenAll(tasks);
    }

    private Task? TryBillShip(Entity<ShipOwnershipComponent> ship, DateTime now)
    {
        var ownership = ship.Comp;
        if (ownership.ChargePending || ownership.NextUpkeep > now)
            return null;

        var interval = TimeSpan.FromHours(_cfg.GetCVar(KesslerCVars.UpkeepIntervalHours));
        if (interval <= TimeSpan.Zero)
            return null;

        // Bill every interval that has passed, including any missed while the server was down.
        var periods = (int) ((now - ownership.NextUpkeep).Ticks / interval.Ticks) + 1;
        ownership.NextUpkeep += interval * periods;

        var fee = GetDailyUpkeep(ownership) * periods;
        if (fee <= 0 && ownership.UpkeepDebt <= 0)
            return null;

        var total = ownership.UpkeepDebt + fee;
        var userId = new NetUserId(ownership.OwnerUserId);

        // Online: go through the cached preferences, or the next profile save would overwrite a database change.
        if (_player.TryGetSessionById(userId, out var session)
            && _prefs.TryGetCachedPreferences(userId, out var prefs))
        {
            var paid = TryChargeOnline(session, prefs, ownership, total);
            SettleCharge(ship, total, paid, now);
            return null;
        }

        if (ownership.OwnerProfileId is not { } profileId)
        {
            Log.Warning($"{ToPrettyString(ship)} owes {total} upkeep, but its owner's profile is unknown; added to its debt");
            SettleCharge(ship, total, false, now);
            return null;
        }

        ownership.ChargePending = true;
        return ChargeOffline(ship, profileId, total, now);
    }

    private bool TryChargeOnline(ICommonSession session, PlayerPreferences prefs, ShipOwnershipComponent ownership, int total)
    {
        // If they're playing the owning character, charge through the body so its balance display updates.
        if (session.AttachedEntity is { } body
            && TryComp<BankAccountComponent>(body, out var bank)
            && bank.CharacterSlot == ownership.OwnerSlot)
        {
            return _bank.TryBankWithdraw(body, total);
        }

        if (ownership.OwnerSlot < 0
            || !prefs.Characters.TryGetValue(ownership.OwnerSlot, out var character)
            || character is not HumanoidCharacterProfile profile)
        {
            return false;
        }

        return _bank.TryBankWithdraw(session, prefs, profile, total, out _);
    }

    private async Task ChargeOffline(Entity<ShipOwnershipComponent> ship, int profileId, int total, DateTime now)
    {
        int? newBalance = null;
        try
        {
            newBalance = await _db.TryWithdrawBankBalanceAsync(profileId, total);
        }
        catch (Exception e)
        {
            Log.Error($"Charging upkeep for {ToPrettyString(ship)} failed: {e}");
        }

        if (!TryComp<ShipOwnershipComponent>(ship, out var ownership))
            return;

        ownership.ChargePending = false;
        SettleCharge((ship, ownership), total, newBalance != null, now);
    }

    private void SettleCharge(Entity<ShipOwnershipComponent> ship, int total, bool paid, DateTime now)
    {
        var ownership = ship.Comp;
        var shipName = Name(ship);

        if (paid)
        {
            ownership.UpkeepDebt = 0;
            ownership.DebtSince = null;
            ownership.ImpoundDue = false;
            Log.Info($"{ownership.OwnerName} paid {total} upkeep for {ToPrettyString(ship)}");
            NotifyOwner(ownership, Loc.GetString("upkeep-paid", ("ship", shipName), ("amount", total)));
            return;
        }

        ownership.UpkeepDebt = total;
        ownership.DebtSince ??= now;

        var grace = TimeSpan.FromDays(_cfg.GetCVar(KesslerCVars.UpkeepGraceDays));
        var impound = now - ownership.DebtSince.Value >= grace;
        if (impound && !ownership.ImpoundDue)
            Log.Info($"{ToPrettyString(ship)} has been in upkeep debt since {ownership.DebtSince:u} and is due for impounding");
        ownership.ImpoundDue = impound;

        Log.Info($"{ownership.OwnerName} couldn't pay {total} upkeep for {ToPrettyString(ship)}; debt is now {total}");
        NotifyOwner(ownership, Loc.GetString(impound ? "upkeep-unpaid-impound" : "upkeep-unpaid",
            ("ship", shipName), ("amount", total)));
    }

    private void NotifyOwner(ShipOwnershipComponent ownership, string message)
    {
        if (_player.TryGetSessionById(new NetUserId(ownership.OwnerUserId), out var session))
            _chat.DispatchServerMessage(session, message);
    }
}
