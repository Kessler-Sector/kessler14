using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Ame.Components;
using Content.Server.Ame.EntitySystems;
using Content.Server.Power.Generator;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.Silicons.ShipAi;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Database;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Power.Generator;
using Content.Shared.Silicons.StationAi;
using Robust.Shared.Player;
using Content.Server.StationRecords;
using Content.Server.StationRecords.Systems;
using Content.Shared.Overlays;
using Content.Shared.Access;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Silicons.ShipAi;

public sealed partial class ShipAiInterfaceSystem
{
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly AmeControllerSystem _ame = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedDoorSystem _doors = default!;
    [Dependency] private readonly GeneratorSystem _generator = default!;
    [Dependency] private readonly SharedPoweredLightSystem _lights = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly SharedPowerReceiverSystem _power = default!;
    [Dependency] private readonly ShipAiSystem _shipAi = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly StationRecordsSystem _records = default!;

    private void InitializeControls()
    {
        SubscribeLocalEvent<ShipAiComponent, ShipAiToggleEvent>(OnToggle);
        SubscribeLocalEvent<ShipAiComponent, ShipAiOpenConsoleEvent>(OnOpenConsole);
        SubscribeLocalEvent<ShipAiComponent, PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<DoorComponent, ShipAiEditAccessEvent>(OnEditAccess);

        Subs.BuiEvents<ShipAiInterfaceComponent>(ShipAiAccessUiKey.Key, subs =>
        {
            subs.Event<ShipAiSetAccessMessage>(OnSetAccess);
        });
    }

    private bool TryGetInterface(EntityUid brain, out Entity<ShipAiInterfaceComponent> iface)
    {
        iface = default;
        if (!_containers.TryGetContainingContainer(brain, out var container) ||
            !TryComp<ShipAiInterfaceComponent>(container.Owner, out var comp))
            return false;

        iface = (container.Owner, comp);
        return true;
    }

    private void OnPlayerDetached(Entity<ShipAiComponent> ent, ref PlayerDetachedEvent args)
    {
        _ui.CloseUserUis(ent.Owner);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        if (!HasComp<ShipAiComponent>(ev.Mob) || !TryGetInterface(ev.Mob, out var iface))
            return;

        ApplyIdentity(iface, ev.Mob, ev.Player.UserId, Name(ev.Mob));
        EnsureIdCard(ev.Mob);
        StartBoot(iface);
        UpdateCoreLook(ev.Mob);
        Brief(ev.Mob, ev.Player);

        if (ev.JobId != null && TryComp<StationRecordsComponent>(ev.Station, out var records))
        {
            _records.CreateGeneralRecord(ev.Station, null, Name(ev.Mob), ev.Profile.Age, Loc.GetString("ship-ai-record-species"),
                ev.Profile.Gender, ev.JobId, null, null, ev.Profile, records);
        }
    }

    private void OnToggle(Entity<ShipAiComponent> ent, ref ShipAiToggleEvent args)
    {
        if (args.Handled)
            return;

        if (args.Target == ShipAiToggle.JobIcons)
        {
            args.Handled = true;
            if (!RemComp<ShowJobIconsComponent>(ent))
                AddComp<ShowJobIconsComponent>(ent);
            return;
        }

        if (Transform(ent).GridUid is not { } grid || !_shipAi.OnOwnShip(ent, grid))
            return;

        args.Handled = true;

        switch (args.Target)
        {
            case ShipAiToggle.Engines:
                ToggleEngines(grid);
                break;
            case ShipAiToggle.Generators:
                ToggleGenerators(grid, ent);
                break;
            case ShipAiToggle.Bolts:
                ToggleBolts(grid, ent);
                break;
            case ShipAiToggle.Lights:
                ToggleLights(grid);
                break;
            case ShipAiToggle.ClearElectrified:
                ClearElectrified(grid);
                break;
            case ShipAiToggle.Apcs:
                ToggleApcs(grid);
                break;
            case ShipAiToggle.Shutters:
                ToggleShutters(grid, ent, blastDoors: false);
                break;
            case ShipAiToggle.BlastDoors:
                ToggleShutters(grid, ent, blastDoors: true);
                break;
        }
    }

    private void OnOpenConsole(Entity<ShipAiComponent> ent, ref ShipAiOpenConsoleEvent args)
    {
        if (args.Handled || args.Key is not { } key || !TryGetInterface(ent, out var iface))
            return;

        args.Handled = true;
        _ui.TryToggleUi(iface.Owner, key, ent.Owner);
    }

    private HashSet<Entity<T>> OnGrid<T>(EntityUid grid) where T : IComponent
    {
        var found = new HashSet<Entity<T>>();
        _lookup.GetGridEntities(grid, found);
        return found;
    }

    private void ToggleEngines(EntityUid grid)
    {
        var thrusters = OnGrid<ThrusterComponent>(grid);
        var turnOn = !thrusters.Any(thruster => thruster.Comp.Enabled);

        foreach (var thruster in thrusters)
        {
            var ev = new SignalReceivedEvent(turnOn ? thruster.Comp.OnPort : thruster.Comp.OffPort);
            RaiseLocalEvent(thruster, ref ev);
        }
    }

    private void ToggleGenerators(EntityUid grid, EntityUid ai)
    {
        var generators = OnGrid<FuelGeneratorComponent>(grid);
        var ames = OnGrid<AmeControllerComponent>(grid);
        var turnOn = !generators.Any(generator => generator.Comp.On) && !ames.Any(ame => ame.Comp.Injecting);

        foreach (var generator in generators)
        {
            if (turnOn && (_generator.GetFuel(generator) <= 0 || _generator.GetIsClogged(generator)))
                continue;

            _generator.SetFuelGeneratorOn(generator, turnOn, generator.Comp);
        }

        foreach (var ame in ames)
        {
            _ame.SetInjecting(ame, turnOn, ai, ame.Comp);
        }
    }

    private void ToggleBolts(EntityUid grid, EntityUid ai)
    {
        var doors = OnGrid<DoorBoltComponent>(grid).Where(CanBoltDoor).ToList();
        var bolt = doors.Any(door => !door.Comp.BoltsDown);

        foreach (var door in doors)
        {
            _doors.TrySetBoltDown(door, bolt, ai);
        }
    }

    private void ToggleShutters(EntityUid grid, EntityUid ai, bool blastDoors)
    {
        var shutters = OnGrid<DoorComponent>(grid)
            .Where(door => _shipAi.IsShutter(door, door.Comp) &&
                           door.Comp.CanPry != blastDoors &&
                           (!TryComp<StationAiWhitelistComponent>(door, out var whitelist) || whitelist.Enabled))
            .ToList();
        var close = shutters.Any(door => door.Comp.State == DoorState.Open);

        foreach (var door in shutters)
        {
            if (close)
                _doors.TryClose(door, door.Comp, ai);
            else
                _doors.TryOpen(door, door.Comp, ai);
        }
    }

    private void ToggleLights(EntityUid grid)
    {
        var lights = OnGrid<PoweredLightComponent>(grid);
        var turnOn = !lights.Any(light => light.Comp.On);

        foreach (var light in lights)
        {
            _lights.SetState(light, turnOn, light.Comp);
        }
    }

    private void OnEditAccess(EntityUid uid, DoorComponent door, ShipAiEditAccessEvent args)
    {
        if (!_shipAi.OnOwnShip(args.User, uid) ||
            !TryGetInterface(args.User, out var iface) ||
            !_accessReader.GetMainAccessReader(uid, out _))
            return;

        iface.Comp.AccessTarget = uid;
        _ui.OpenUi(iface.Owner, ShipAiAccessUiKey.Key, args.User);
        UpdateAccessUi(iface);
    }

    private void OnSetAccess(Entity<ShipAiInterfaceComponent> ent, ref ShipAiSetAccessMessage args)
    {
        if (ent.Comp.AccessTarget is not { } target ||
            !TryGetInterface(args.Actor, out var iface) || iface.Owner != ent.Owner ||
            !_shipAi.OnOwnShip(args.Actor, target) ||
            !_accessReader.GetMainAccessReader(target, out var reader))
            return;

        var allowed = AllowedAccess(ent);
        if (args.Access.Any(access => !allowed.Contains(access)))
            return;

        var newAccess = reader.Value.Comp.AccessLists
            .SelectMany(set => set)
            .Where(access => !allowed.Contains(access))
            .Concat(args.Access)
            .Distinct()
            .ToList();

        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(args.Actor):player} set the access of {ToPrettyString(target):entity} to [{string.Join(", ", newAccess)}]");

        _accessReader.SetAccesses(reader.Value, newAccess);
        var updated = new OnAccessOverriderAccessUpdatedEvent(args.Actor);
        RaiseLocalEvent(target, ref updated);
        UpdateAccessUi(ent);
    }

    private void UpdateAccessUi(Entity<ShipAiInterfaceComponent> ent)
    {
        if (ent.Comp.AccessTarget is not { } target || !_accessReader.GetMainAccessReader(target, out var reader))
            return;

        var current = reader.Value.Comp.AccessLists.SelectMany(set => set).ToArray();
        var state = new AccessOverriderComponent.AccessOverriderBoundUserInterfaceState(
            true,
            true,
            current,
            AllowedAccess(ent).ToArray(),
            null,
            string.Empty,
            Loc.GetString("ship-ai-access-target", ("door", Name(target))),
            Color.White);

        _ui.SetUiState(ent.Owner, ShipAiAccessUiKey.Key, state);
    }

    private List<ProtoId<AccessLevelPrototype>> AllowedAccess(Entity<ShipAiInterfaceComponent> ent)
    {
        var allowed = new List<ProtoId<AccessLevelPrototype>>(ent.Comp.AccessLevels);
        if (TryGetBrain(ent, out var brain) &&
            CompOrNull<ShipAiCoreComponent>(brain)?.Faction is { } faction &&
            ent.Comp.FactionAccessLevels.TryGetValue(faction, out var extra))
            allowed.AddRange(extra);
        return allowed;
    }
}
