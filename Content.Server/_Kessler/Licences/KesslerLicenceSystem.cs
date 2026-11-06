using System.Linq;
using System.Threading.Tasks;
using Content.Server._Kessler.Reputation;
using Content.Server._NF.Bank;
using Content.Server.Database;
using Content.Shared._Kessler.CCVar;
using Content.Shared._Kessler.Licences;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Kessler.Licences;

/// <summary>
/// Per-character licences (docs/kessler/design/progression.md, 4.3). Reputation and time permit, money buys: a licence
/// needs reputation levels, a previous licence held for some real days, and a fee. Shipyards check them by ship size.
/// </summary>
public sealed class KesslerLicenceSystem : EntitySystem
{
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly KesslerReputationSystem _reputation = default!;

    /// <summary>
    /// Ship groups that don't need a pilot licence: job and staff shipyards keep their own access rules.
    /// </summary>
    private static readonly HashSet<ShipyardConsoleUiKey> ExemptGroups = new()
    {
        ShipyardConsoleUiKey.Security,
        ShipyardConsoleUiKey.Sr,
        ShipyardConsoleUiKey.Centcomm,
        ShipyardConsoleUiKey.Syndicate,
        ShipyardConsoleUiKey.Custom,
    };

    private readonly Dictionary<(Guid User, int Slot), Dictionary<string, DateTime>> _cache = new();
    private readonly HashSet<(Guid User, int Slot)> _loading = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawn);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _cache.Clear());
    }

    private void OnPlayerSpawn(PlayerSpawnCompleteEvent args)
    {
        if (TryComp<BankAccountComponent>(args.Mob, out var bank) && bank.CharacterSlot >= 0)
            _ = LoadAsync(args.Player.UserId, bank.CharacterSlot);
    }

    public IEnumerable<LicencePrototype> Licences()
    {
        return _proto.EnumeratePrototypes<LicencePrototype>().OrderBy(l => l.Order);
    }

    /// <summary>
    /// The licences needed to buy <paramref name="vessel"/> without a voucher.
    /// </summary>
    public List<LicencePrototype> RequiredFor(VesselPrototype vessel)
    {
        var required = new List<LicencePrototype>();
        if (ExemptGroups.Contains(vessel.Group))
            return required;

        foreach (var licence in Licences())
        {
            if (licence.ShipSizes.Contains(vessel.Category)
                || licence.MinShipPrice is { } min && vessel.Price >= min)
            {
                required.Add(licence);
            }
        }

        return required;
    }

    /// <summary>
    /// Whether <paramref name="buyer"/> may buy <paramref name="vessel"/>. False with a reason to show if not. While the
    /// character's licences are still being loaded the answer is no, with a "try again" reason.
    /// </summary>
    public bool CanBuyShip(EntityUid buyer, VesselPrototype vessel, out string reason)
    {
        reason = string.Empty;
        if (!_cfg.GetCVar(KesslerCVars.LicencesEnforced))
            return true;

        var required = RequiredFor(vessel);
        if (required.Count == 0)
            return true;

        if (!TryComp<ActorComponent>(buyer, out var actor)
            || !TryComp<BankAccountComponent>(buyer, out var bank)
            || bank.CharacterSlot < 0)
        {
            reason = Loc.GetString("kessler-licence-no-character");
            return false;
        }

        var key = (actor.PlayerSession.UserId.UserId, bank.CharacterSlot);
        if (!_cache.TryGetValue(key, out var held))
        {
            _ = LoadAsync(actor.PlayerSession.UserId, bank.CharacterSlot);
            reason = Loc.GetString("kessler-licence-loading");
            return false;
        }

        var missing = required.Where(l => !held.ContainsKey(l.ID)).ToList();
        if (missing.Count == 0)
            return true;

        reason = Loc.GetString("kessler-licence-ship-needs",
            ("licences", string.Join(", ", missing.Select(l => Loc.GetString(l.Name)))));
        return false;
    }

    /// <summary>
    /// The licences a character holds, with when each was granted, or null if the character has no profile.
    /// </summary>
    public async Task<Dictionary<string, DateTime>?> LoadAsync(NetUserId user, int slot)
    {
        var key = (user.UserId, slot);
        if (!_loading.Add(key) && _cache.TryGetValue(key, out var cached))
            return cached;

        try
        {
            if (await _db.GetProfileIdAsync(user, slot) is not { } profileId)
                return null;

            var held = await _db.GetLicencesAsync(profileId);
            _cache[key] = held;
            return held;
        }
        finally
        {
            _loading.Remove(key);
        }
    }

    /// <summary>
    /// What still stands between a character and <paramref name="licence"/>, as lines to show. Empty when it can be
    /// bought (the fee is checked when buying).
    /// </summary>
    public List<string> MissingRequirements(LicencePrototype licence, Dictionary<string, int> reputation,
        Dictionary<string, DateTime> held, DateTime now)
    {
        var missing = new List<string>();
        foreach (var requirement in licence.Reputation)
        {
            if (requirement.Track is { } track)
            {
                if (KesslerReputationSystem.GetLevel(reputation.GetValueOrDefault(track)) < requirement.Level)
                {
                    var trackName = _proto.TryIndex(track, out var trackProto) ? Loc.GetString(trackProto.Name) : track.Id;
                    missing.Add(Loc.GetString("kessler-licence-needs-track",
                        ("track", trackName), ("level", KesslerReputationSystem.LevelName(requirement.Level))));
                }
            }
            else if (!reputation.Values.Any(points => KesslerReputationSystem.GetLevel(points) >= requirement.Level))
            {
                missing.Add(Loc.GetString("kessler-licence-needs-any",
                    ("level", KesslerReputationSystem.LevelName(requirement.Level))));
            }
        }

        if (licence.Previous is { } previous)
        {
            var previousName = _proto.TryIndex(previous, out var previousProto) ? Loc.GetString(previousProto.Name) : previous.Id;
            if (!held.TryGetValue(previous, out var grantedAt))
            {
                missing.Add(Loc.GetString("kessler-licence-needs-previous", ("licence", previousName), ("days", licence.PreviousDays)));
            }
            else if (now - grantedAt < TimeSpan.FromDays(licence.PreviousDays))
            {
                var left = TimeSpan.FromDays(licence.PreviousDays) - (now - grantedAt);
                missing.Add(Loc.GetString("kessler-licence-needs-time", ("licence", previousName),
                    ("days", licence.PreviousDays), ("left", $"{(int) left.TotalDays}d {left.Hours}h")));
            }
        }

        return missing;
    }

    /// <summary>
    /// Buys <paramref name="licence"/> for the character <paramref name="buyer"/> is playing. Returns null on success,
    /// or the reason it failed.
    /// </summary>
    public async Task<string?> TryBuyAsync(EntityUid buyer, LicencePrototype licence)
    {
        if (!TryComp<ActorComponent>(buyer, out var actor)
            || !TryComp<BankAccountComponent>(buyer, out var bank)
            || bank.CharacterSlot < 0)
        {
            return Loc.GetString("kessler-licence-no-character");
        }

        var user = actor.PlayerSession.UserId;
        var slot = bank.CharacterSlot;
        if (await _db.GetProfileIdAsync(user, slot) is not { } profileId
            || await LoadAsync(user, slot) is not { } held
            || await _reputation.GetReputationAsync(user, slot) is not { } reputation)
        {
            return Loc.GetString("kessler-licence-no-character");
        }

        if (held.ContainsKey(licence.ID))
            return Loc.GetString("kessler-licence-already-held");

        var now = DateTime.UtcNow;
        var missing = MissingRequirements(licence, reputation, held, now);
        if (missing.Count > 0)
            return string.Join("\n", missing);

        if (Deleted(buyer))
            return Loc.GetString("kessler-licence-no-character");

        if (licence.Fee > 0 && !_bank.TryBankWithdraw(buyer, licence.Fee))
            return Loc.GetString("kessler-licence-cant-afford", ("fee", licence.Fee));

        if (!await _db.AddLicenceAsync(profileId, licence.ID, now))
        {
            // Refund: it was granted by something else in the meantime, or the profile went away.
            if (licence.Fee > 0)
                _bank.TryBankDeposit(buyer, licence.Fee);
            return Loc.GetString("kessler-licence-already-held");
        }

        held[licence.ID] = now;
        _cache[(user.UserId, slot)] = held;
        return null;
    }
}
