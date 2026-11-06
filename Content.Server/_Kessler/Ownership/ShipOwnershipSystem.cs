using System.Diagnostics.CodeAnalysis;
using Content.Server._Kessler.Characters;
using Content.Server.Database;
using Content.Server.Preferences.Managers;
using Content.Shared._Kessler.CCVar;
using Content.Shared._NF.Bank.Components;
using Robust.Shared.Configuration;
using Robust.Server.Player;
using Robust.Shared.Network;

namespace Content.Server._Kessler.Ownership;

/// <summary>
/// Records who owns each bought ship (see <see cref="ShipOwnershipComponent"/>).
/// </summary>
public sealed class ShipOwnershipSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IServerPreferencesManager _prefs = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly KesslerCharacterSystem _characters = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipPurchasedEvent>(OnShipPurchased);
    }

    private void OnShipPurchased(ref ShipPurchasedEvent ev)
    {
        if (!_player.TryGetSessionByEntity(ev.Buyer, out var session))
        {
            Log.Warning($"{ToPrettyString(ev.Shuttle)} was bought by {ToPrettyString(ev.Buyer)}, who has no player; no owner recorded");
            return;
        }

        var slot = GetCharacterSlot(ev.Buyer, session.UserId);
        SetOwner(ev.Shuttle, session.UserId, slot, Name(ev.Buyer), ev.Price, ev.PurchasedWithVoucher);
    }

    /// <summary>
    /// Records <paramref name="userId"/>'s character in <paramref name="slot"/> as the owner of
    /// <paramref name="shuttle"/>, then looks up the character's database profile ID in the background.
    /// </summary>
    public void SetOwner(EntityUid shuttle, NetUserId userId, int slot, string ownerName, int price, bool purchasedWithVoucher)
    {
        var comp = EnsureComp<ShipOwnershipComponent>(shuttle);
        comp.OwnerUserId = userId.UserId;
        comp.OwnerSlot = slot;
        comp.OwnerProfileId = null;
        comp.OwnerName = ownerName.Trim();
        comp.PurchasePrice = price;
        comp.PurchasedWithVoucher = purchasedWithVoucher;
        comp.PurchasedAt = DateTime.UtcNow;
        comp.NextUpkeep = comp.PurchasedAt + TimeSpan.FromHours(_cfg.GetCVar(KesslerCVars.UpkeepIntervalHours));
        comp.UpkeepDebt = 0;
        comp.DebtSince = null;
        comp.ImpoundDue = false;

        if (slot >= 0)
            ResolveProfileId(shuttle, userId, slot);
        else
            Log.Warning($"No character slot for the owner of {ToPrettyString(shuttle)}; profile ID not recorded");
    }

    /// <summary>
    /// Sets the owning character's database profile ID directly. For tests and admin repairs.
    /// </summary>
    public void SetOwnerProfileId(Entity<ShipOwnershipComponent> ship, int? profileId)
    {
        ship.Comp.OwnerProfileId = profileId;
    }

    /// <summary>
    /// A ship back from storage starts a fresh upkeep cycle: storage is free and any debt has been paid.
    /// </summary>
    public void ResetAfterRecall(Entity<ShipOwnershipComponent> ship, DateTime now)
    {
        ship.Comp.NextUpkeep = now + TimeSpan.FromHours(_cfg.GetCVar(KesslerCVars.UpkeepIntervalHours));
        ship.Comp.UpkeepDebt = 0;
        ship.Comp.DebtSince = null;
        ship.Comp.ImpoundDue = false;
        ship.Comp.ChargePending = false;
    }

    public bool TryGetOwnership(EntityUid shuttle, [NotNullWhen(true)] out ShipOwnershipComponent? ownership)
    {
        return TryComp(shuttle, out ownership);
    }

    private int GetCharacterSlot(EntityUid buyer, NetUserId userId)
    {
        // The bank account knows which character this body is, even after a cryo swap or a restart.
        if (_characters.TryGetSlot(buyer, out _, out var slot))
            return slot;

        return _prefs.TryGetCachedPreferences(userId, out var prefs) ? prefs.SelectedCharacterIndex : -1;
    }

    private async void ResolveProfileId(EntityUid shuttle, NetUserId userId, int slot)
    {
        int? profileId;
        try
        {
            profileId = await _db.GetProfileIdAsync(userId, slot);
        }
        catch (Exception e)
        {
            Log.Error($"Looking up the owner profile of {ToPrettyString(shuttle)} failed: {e}");
            return;
        }

        if (!TryComp<ShipOwnershipComponent>(shuttle, out var comp)
            || comp.OwnerUserId != userId.UserId
            || comp.OwnerSlot != slot)
        {
            // The ship was sold or changed hands while we were waiting.
            return;
        }

        if (profileId == null)
            Log.Warning($"No database profile for the owner of {ToPrettyString(shuttle)} (slot {slot})");

        comp.OwnerProfileId = profileId;
    }
}
