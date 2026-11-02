using System.Linq;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Radio.Components;
using Content.Shared._WF.Silicons.ShipAi;
using Content.Shared.Alert;
using Content.Shared.Damage;
using Content.Shared.Doors.Components;
using Content.Shared.Electrocution;
using Content.Shared.Popups;
using Content.Shared.Radio.Components;
using Content.Shared.Silicons.StationAi;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Content.Server.Destructible;
using Content.Shared.Rounding;
using Content.Server.Radio.EntitySystems;
using Content.Server.Chat.Managers;
using Content.Shared.CartridgeLoader;
using Content.Shared.Chat;
using Robust.Shared.Utility;

namespace Content.Server._WF.Silicons.ShipAi;

public sealed partial class ShipAiInterfaceSystem
{
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly ApcSystem _apc = default!;
    [Dependency] private readonly SharedElectrocutionSystem _electrocution = default!;
    [Dependency] private readonly ISharedPlayerManager _players = default!;
    [Dependency] private readonly DestructibleSystem _destructible = default!;
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;

    private static readonly ProtoId<AlertPrototype> CoreHealthAlert = "BorgHealth";

    private void InitializeHelpers()
    {
        SubscribeLocalEvent<ShipAiComponent, CartridgeLoaderNotificationSentEvent>(OnNotification);
    }

    private void OnNotification(Entity<ShipAiComponent> ent, ref CartridgeLoaderNotificationSentEvent args)
    {
        if (!TryComp<ActorComponent>(ent, out var actor))
            return;

        var message = FormattedMessage.EscapeText(args.Message);
        var wrappedMessage = Loc.GetString("pda-notification-message", ("header", args.Header), ("message", message));
        _chatManager.ChatMessageToOne(ChatChannel.Notifications, message, wrappedMessage, EntityUid.Invalid, false, actor.PlayerSession.Channel);
    }

    private void SyncRadio(Entity<ShipAiInterfaceComponent> ent)
    {
        if (!TryGetBrain(ent, out var brain))
            return;

        var channels = CompOrNull<EncryptionKeyHolderComponent>(ent)?.Channels is { } keys ? new HashSet<string>(keys) : new HashSet<string>();
        foreach (var channel in ent.Comp.BaseChannels)
            channels.Add(channel);
        if (TryComp<ShipAiCoreComponent>(brain, out var core) && core.Faction is { } faction &&
            core.StampChannels.TryGetValue(faction, out var factionChannels))
        {
            foreach (var channel in factionChannels)
                channels.Add(channel);
        }

        EnsureComp<ActiveRadioComponent>(brain).Channels = new HashSet<string>(channels);
        EnsureComp<IntrinsicRadioTransmitterComponent>(brain).Channels = channels;
    }

    private bool CanBoltDoor(Entity<DoorBoltComponent> door)
    {
        return !door.Comp.BoltWireCut && _power.IsPowered(door.Owner) &&
               (!TryComp<StationAiWhitelistComponent>(door, out var whitelist) || whitelist.Enabled);
    }

    private void ClearElectrified(EntityUid grid)
    {
        foreach (var electrified in OnGrid<ElectrifiedComponent>(grid))
        {
            if (HasComp<DoorComponent>(electrified))
                _electrocution.SetElectrified(electrified, false);
        }
    }

    private void ToggleApcs(EntityUid grid)
    {
        var apcs = OnGrid<ApcComponent>(grid);
        var turnOn = apcs.Any(apc => !apc.Comp.MainBreakerEnabled);

        foreach (var apc in apcs)
        {
            if (apc.Comp.MainBreakerEnabled != turnOn)
                _apc.ApcToggleBreaker(apc, apc.Comp);
        }
    }

    private void UpdateCoreAlert(Entity<ShipAiInterfaceComponent> ent, DamageChangedEvent? args)
    {
        if (!TryGetBrain(ent, out var brain))
            return;

        var total = CompOrNull<DamageableComponent>(ent)?.TotalDamage.Double() ?? 0;
        var destroyedAt = _destructible.DestroyedAt(ent).Double();
        var severity = (short) ContentHelpers.RoundToEqualLevels(total, destroyedAt, _alerts.GetMaxSeverity(CoreHealthAlert) + 1);
        _alerts.ShowAlert(brain, CoreHealthAlert, severity);

        if (args is not { DamageIncreased: true })
            return;

        var integrity = 1 - total / destroyedAt;
        var before = 1 - (total - (args.DamageDelta?.GetTotal().Double() ?? 0)) / destroyedAt;

        if (ent.Comp.DamageAlerts && total >= destroyedAt && TryComp<ShipAiCoreComponent>(brain, out var core))
        {
            RadioAlert(ent, brain, Loc.GetString("ship-ai-alert-destroyed"));
            EnsureComp<ShipAiLostComponent>(brain).NextAlert = _timing.CurTime + core.LostAlertInterval;
        }
        else if (ent.Comp.DamageAlerts && ent.Comp.AlertIntegrity.Any(level => integrity <= level && before > level))
        {
            RadioAlert(ent, brain, Loc.GetString("ship-ai-alert-damaged", ("integrity", (int) Math.Round(100 * integrity))));
        }

        if (_timing.CurTime < ent.Comp.NextAttackAlert)
            return;

        ent.Comp.NextAttackAlert = _timing.CurTime + ent.Comp.AttackAlertCooldown;
        _popup.PopupCursor(Loc.GetString("ship-ai-core-attacked"), brain, PopupType.LargeCaution);
        _audio.PlayGlobal(ent.Comp.AttackSound, brain);
    }

    private void RadioAlert(EntityUid source, EntityUid brain, string status)
    {
        if (!TryComp<ShipAiCoreComponent>(brain, out var core) ||
            !core.AlertChannels.TryGetValue(core.Faction ?? ShipAiFaction.Other, out var channel))
            return;

        var pos = _xforms.GetMapCoordinates(source);
        var position = $"({(int) pos.X}, {(int) pos.Y})";
        var location = _station.GetOwningStation(source) is { } station
            ? Loc.GetString("ship-ai-alert-on-ship", ("ship", Name(station)), ("position", position))
            : Loc.GetString("ship-ai-alert-in-space", ("position", position));
        var message = Loc.GetString("ship-ai-alert", ("location", location), ("status", status));
        _radio.SendRadioMessage(source, message, channel, source);
    }

    private void UpdateLost()
    {
        var query = EntityQueryEnumerator<ShipAiLostComponent, ShipAiCoreComponent>();
        while (query.MoveNext(out var uid, out var lost, out var core))
        {
            if (_timing.CurTime < lost.NextAlert)
                continue;

            lost.NextAlert = _timing.CurTime + core.LostAlertInterval;
            if (HasComp<ActorComponent>(uid))
                RadioAlert(uid, uid, Loc.GetString("ship-ai-alert-lost"));
        }
    }
}
