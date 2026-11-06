using System.Threading.Tasks;
using Content.Server._Kessler.Characters;
using Content.Server._Kessler.Reputation;
using Content.Shared._Kessler.Licences;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Kessler.Licences;

/// <summary>
/// A licensing office terminal: shows the character using it their reputation and licences, and sells licences.
/// </summary>
[RegisterComponent]
public sealed partial class LicenceOfficeComponent : Component;

/// <summary>
/// Runs licensing office terminals. Each viewer sees their own character, so state goes out as a message to that
/// viewer rather than as the shared UI state.
/// </summary>
public sealed class LicenceOfficeSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly KesslerCharacterSystem _characters = default!;
    [Dependency] private readonly KesslerLicenceSystem _licences = default!;
    [Dependency] private readonly KesslerReputationSystem _reputation = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LicenceOfficeComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<LicenceOfficeComponent, LicenceOfficeBuyMessage>(OnBuy);
    }

    private void OnOpened(Entity<LicenceOfficeComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (args.UiKey is LicenceOfficeUiKey)
            _ = SendStateAsync(ent, args.Actor, null);
    }

    private void OnBuy(Entity<LicenceOfficeComponent> ent, ref LicenceOfficeBuyMessage args)
    {
        _ = BuyAsync(ent, args.Actor, args.Licence);
    }

    private async Task BuyAsync(EntityUid ent, EntityUid actor, string licenceId)
    {
        try
        {
            string notice;
            if (!_proto.TryIndex<LicencePrototype>(licenceId, out var licence))
            {
                notice = Loc.GetString("cmd-licence-unknown", ("id", licenceId));
            }
            else
            {
                var failure = await _licences.TryBuyAsync(actor, licence);
                notice = failure ?? Loc.GetString("cmd-licence-bought", ("licence", Loc.GetString(licence.Name)), ("fee", licence.Fee));
            }

            await SendStateAsync(ent, actor, notice);
        }
        catch (Exception e)
        {
            Log.Error($"Licence purchase at {ToPrettyString(ent)} failed: {e}");
        }
    }

    /// <summary>
    /// Builds what <paramref name="viewer"/> sees and sends it to them, if they still have the terminal open.
    /// </summary>
    public async Task SendStateAsync(EntityUid office, EntityUid viewer, string? notice)
    {
        var state = await BuildStateAsync(viewer);
        state.Notice = notice;

        if (Deleted(office) || Deleted(viewer) || !_ui.IsUiOpen(office, LicenceOfficeUiKey.Key, viewer))
            return;

        _ui.ServerSendUiMessage(office, LicenceOfficeUiKey.Key, state, viewer);
    }

    public async Task<LicenceOfficeStateMessage> BuildStateAsync(EntityUid viewer)
    {
        var state = new LicenceOfficeStateMessage();
        if (!_characters.TryGetSlot(viewer, out var user, out var slot))
        {
            state.Problem = Loc.GetString("kessler-licence-no-character");
            return state;
        }

        var held = await _licences.LoadAsync(user, slot);
        var reputation = await _reputation.GetReputationAsync(user, slot);
        if (held == null || reputation == null)
        {
            state.Problem = Loc.GetString("kessler-licence-no-character");
            return state;
        }

        if (!Deleted(viewer))
            state.CharacterName = Name(viewer);

        foreach (var track in _reputation.Tracks())
        {
            var points = reputation.GetValueOrDefault(track.ID);
            var level = KesslerReputationSystem.GetLevel(points);
            var thresholds = KesslerReputationSystem.LevelThresholds;
            state.Tracks.Add(new LicenceOfficeTrack
            {
                Name = Loc.GetString(track.Name),
                Description = track.Description is { } description ? Loc.GetString(description) : string.Empty,
                LevelName = KesslerReputationSystem.LevelName(level),
                Points = points,
                LevelStart = level == 0 ? 0 : thresholds[level - 1],
                NextLevel = level < thresholds.Length ? thresholds[level] : null,
            });
        }

        var now = DateTime.UtcNow;
        foreach (var licence in _licences.Licences())
        {
            var entry = new LicenceOfficeLicence
            {
                Id = licence.ID,
                Name = Loc.GetString(licence.Name),
                Description = licence.Description is { } description ? Loc.GetString(description) : string.Empty,
                Fee = licence.Fee,
            };

            if (held.TryGetValue(licence.ID, out var grantedAt))
                entry.HeldSince = grantedAt;
            else
                entry.Missing = _licences.MissingRequirements(licence, reputation, held, now);

            state.Licences.Add(entry);
        }

        return state;
    }
}
