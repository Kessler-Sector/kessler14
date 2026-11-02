using Content.Server._CorvaxNext.Silicons.Borgs;
using Content.Server.Holopad;
using Content.Shared._CorvaxNext.Silicons.Borgs.Components;
using Content.Shared._WF.Silicons.ShipAi;
using Robust.Shared.Map.Components;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Content.Shared.Silicons.StationAi;
using Content.Shared.Verbs;
using Content.Shared.Wires;
using Content.Server.Shuttles.Systems;
using Robust.Shared.Player;
using Content.Server.Silicons.StationAi;
using Content.Server.Ghost;
using Content.Shared.Power;

namespace Content.Server._WF.Silicons.ShipAi;

public sealed partial class ShipAiInterfaceSystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedWiresSystem _wires = default!;
    [Dependency] private readonly AiRemoteControlSystem _remote = default!;
    [Dependency] private readonly DockingSystem _docking = default!;
    [Dependency] private readonly GhostSystem _ghosts = default!;

    private static readonly TimeSpan RemoteCheckInterval = TimeSpan.FromSeconds(1);

    private TimeSpan _nextRemoteCheck;

    private void InitializeTransfer()
    {
        SubscribeLocalEvent<ShipAiInterfaceComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAlternativeVerbs, after: [typeof(HolopadSystem)]);
        SubscribeLocalEvent<ShipAiInterfaceComponent, GetVerbsEvent<Verb>>(OnGetVerbs, after: [typeof(StationAiSystem)]);
        SubscribeLocalEvent<ShipAiInterfaceComponent, ShipAiEjectDoAfterEvent>(OnEjectDoAfter);
        SubscribeLocalEvent<ShipAiInterfaceComponent, PowerChangedEvent>(OnCorePowerChanged, before: [typeof(StationAiSystem)]);
        SubscribeLocalEvent<ShipAiComponent, EntityTerminatingEvent>(OnBrainTerminating);
    }

    private void OnCorePowerChanged(Entity<ShipAiInterfaceComponent> ent, ref PowerChangedEvent args)
    {
        if (!args.Powered && TryGetBrain(ent, out var brain))
            LeaveBorg(brain);
    }

    private void OnBrainTerminating(Entity<ShipAiComponent> ent, ref EntityTerminatingEvent args)
    {
        if (TryUnlinkBorg(ent, out var mind))
            _ghosts.OnGhostAttempt(mind, false, forced: true);
    }

    private void OnGetVerbs(Entity<ShipAiInterfaceComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        var customizeVerb = Loc.GetString("station-ai-customization-menu");
        args.Verbs.RemoveWhere(verb => verb.Text == customizeVerb);

        if (!TryGetBrain(ent, out var held) || held != args.User)
            return;

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString(ent.Comp.DamageAlerts ? "ship-ai-alerts-off-verb" : "ship-ai-alerts-on-verb"),
            Act = () => ent.Comp.DamageAlerts = !ent.Comp.DamageAlerts,
        });
    }

    private void OnGetAlternativeVerbs(Entity<ShipAiInterfaceComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        var projectorVerb = Loc.GetString("holopad-activate-projector-verb");
        args.Verbs.RemoveWhere(verb => verb.Text == projectorVerb);

        if (!args.CanInteract || !args.CanAccess || !args.CanComplexInteract)
            return;

        var user = args.User;
        if (!TryGetBrain(ent, out var held) || held == user || !_wires.IsPanelOpen(ent.Owner))
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("ship-ai-eject-verb"),
            Act = () => StartEject(ent, user, held),
            Priority = 1,
        });
    }

    private void StartEject(Entity<ShipAiInterfaceComponent> ent, EntityUid user, EntityUid held)
    {
        if (!_doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, ent.Comp.EjectDelay, new ShipAiEjectDoAfterEvent(), ent, target: ent)
            {
                BreakOnMove = true,
                BreakOnDamage = true,
                NeedHand = true,
            }))
            return;

        var target = CompOrNull<StationAiHeldComponent>(held)?.CurrentConnectedEntity ?? held;
        _popup.PopupCursor(Loc.GetString("ship-ai-eject-warning", ("user", Identity.Entity(user, EntityManager))), target, PopupType.LargeCaution);
    }

    private void OnEjectDoAfter(Entity<ShipAiInterfaceComponent> ent, ref ShipAiEjectDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled ||
            !_wires.IsPanelOpen(ent.Owner) ||
            !TryGetBrain(ent, out var held) ||
            !_containers.TryGetContainer(ent, StationAiHolderComponent.Container, out var slot))
            return;

        args.Handled = true;
        LeaveBorg(held);
        if (_containers.Remove(held, slot))
            _hands.PickupOrDrop(args.User, held);
    }

    private void UpdateRemoteRange()
    {
        if (_timing.CurTime < _nextRemoteCheck)
            return;

        _nextRemoteCheck = _timing.CurTime + RemoteCheckInterval;
        var query = EntityQueryEnumerator<AiRemoteControllerComponent, TransformComponent>();
        while (query.MoveNext(out var borg, out var remote, out var xform))
        {
            if (remote.AiHolder is not { } brain || !TryGetInterface(brain, out _) || InRemoteRange(brain, borg))
                continue;

            _remote.ReturnToAi((borg, remote));
        }

        var eyes = EntityQueryEnumerator<ShipAiEyeComponent>();
        while (eyes.MoveNext(out var eye, out var eyeComp))
        {
            KeepEyeNearShip((eye, eyeComp));
        }
    }

    private bool DockedTo(EntityUid ship, EntityUid other)
    {
        foreach (var dock in _docking.GetDocks(ship))
        {
            if (dock.Comp.DockedWith is { } otherDock && Transform(otherDock).GridUid == other)
                return true;
        }

        return false;
    }

    public bool InRemoteRange(EntityUid brain, EntityUid target)
    {
        if (!TryGetInterface(brain, out var iface) ||
            Transform(iface).GridUid is not { } ship ||
            !TryComp<MapGridComponent>(ship, out var shipComp))
            return false;

        var xform = Transform(target);
        if (xform.GridUid == ship)
            return true;

        var corePos = _xforms.GetMapCoordinates(iface);
        if (xform.MapID != corePos.MapId)
            return false;

        if (xform.GridUid is { } grid && TryComp<MapGridComponent>(grid, out var gridComp))
        {
            var bounds = _xforms.GetWorldMatrix(grid).TransformBox(gridComp.LocalAABB);
            return (bounds.ClosestPoint(corePos.Position) - corePos.Position).Length() <= iface.Comp.RemoteRange;
        }

        var local = _xforms.ToCoordinates((ship, null), _xforms.GetMapCoordinates(xform)).Position;
        return _shipAi.NearShipTile(ship, shipComp, local, iface.Comp.RemoteGrace);
    }

    public bool CanBroadcastTo(EntityUid source, EntityUid receiver)
    {
        if (!HasComp<ShipAiInterfaceComponent>(source) || Transform(source).GridUid is not { } ship)
            return true;

        return Transform(receiver).GridUid is { } grid && (grid == ship || DockedTo(ship, grid));
    }

    private void LeaveBorg(EntityUid brain)
    {
        if (CompOrNull<StationAiHeldComponent>(brain)?.CurrentConnectedEntity is { } borg &&
            TryComp<AiRemoteControllerComponent>(borg, out var remote))
            _remote.ReturnToAi((borg, remote));
    }
}
