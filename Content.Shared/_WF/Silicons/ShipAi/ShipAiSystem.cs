using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Silicons.StationAi;
using Robust.Shared.Containers;
using Content.Shared.SubFloor;
using Robust.Shared.Prototypes;
using Content.Shared.Administration;
using Content.Shared.Stunnable;
using Content.Shared.Intellicard;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Lock;
using Content.Shared.Roles;
using Content.Shared.Whitelist;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Map;
using System.Linq;
using System.Numerics;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Shared.Map.Components;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.Hands.Components;
using System.Diagnostics.CodeAnalysis;

namespace Content.Shared._WF.Silicons.ShipAi;

public sealed class ShipAiSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedPoweredLightSystem _lights = default!;
    [Dependency] private readonly SharedDoorSystem _doors = default!;
    [Dependency] private readonly LockSystem _lock = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _xforms = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly AnchorableSystem _anchorable = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipAiComponent, InRangeOverrideEvent>(OnInRange);
        SubscribeLocalEvent<ShipAiComponent, AccessibleOverrideEvent>(OnAccessible);
        SubscribeLocalEvent<ShipAiComponent, InteractionAttemptEvent>(OnInteractionAttempt,
            before: [typeof(AdminFrozenSystem), typeof(SharedStunSystem)],
            after: [typeof(SharedStationAiSystem)]);
        SubscribeLocalEvent<ShipAiComponent, BeforeInteractHandEvent>(OnBeforeInteractHand);
        SubscribeLocalEvent<TransformComponent, GettingInteractedWithAttemptEvent>(OnGettingInteractedWith, after: [typeof(SharedSubFloorHideSystem)]);
        SubscribeLocalEvent<ShipAiInterfaceComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<TransformComponent, ActivateInWorldEvent>(OnActivateInWorld,
            after: [typeof(LockSystem)],
            before: [typeof(OpenableSystem), typeof(SharedStorageSystem)]);
    }

    private void OnInteractUsing(Entity<ShipAiInterfaceComponent> ent, ref InteractUsingEvent args)
    {
        if (HasComp<IntellicardComponent>(args.Used))
            args.Handled = true;
    }

    private const string ShipAiPrefix = "ShipAi";

    public static readonly ProtoId<RoleLoadoutPrototype> Loadout = "JobShipAi";
    public static readonly ProtoId<JobPrototype> Job = "ShipAi";
    public static readonly ProtoId<StationAiCustomizationGroupPrototype> FaceGroup = "StationAiCoreIconography";
    public static readonly ProtoId<StationAiCustomizationGroupPrototype> ScreenGroup = "ShipAiScreen";
    public static readonly ProtoId<StationAiCustomizationGroupPrototype> HologramGroup = "StationAiHolograms";
    public static readonly ProtoId<StationAiCustomizationGroupPrototype> CasingGroup = "ShipAiCasing";
    public static readonly ProtoId<StationAiCustomizationGroupPrototype> AccessoryGroup = "ShipAiAccessory";
    public static readonly ProtoId<StationAiCustomizationPrototype> DefaultFace = "ShipAiFaceNeutral";
    public static readonly ProtoId<StationAiCustomizationPrototype> DefaultScreen = "ShipAiScreenBlue";
    public static readonly ProtoId<StationAiCustomizationPrototype> FaceHologram = "ShipAiHologramFace";

    public static ProtoId<StationAiCustomizationPrototype> Fallback(IPrototypeManager protos, ProtoId<StationAiCustomizationGroupPrototype> group)
    {
        return protos.Index<LoadoutGroupPrototype>(group.Id).Fallbacks[0].Id;
    }

    public static bool IsJob(IPrototypeManager protos, ProtoId<JobPrototype> job)
    {
        return job == Job ||
               protos.TryIndex(ShipAiShipyardPrototype.DefaultId, out var shipyard) &&
               shipyard.Points.Values.Any(point => point.Job == job);
    }

    public static bool TryGetFaceLayer(IPrototypeManager protos, ProtoId<StationAiCustomizationPrototype> face, [NotNullWhen(true)] out PrototypeLayerData? layer)
    {
        layer = null;
        return protos.TryIndex(face, out var proto) &&
               proto.LayerData.TryGetValue(StationAiState.Occupied.ToString(), out layer);
    }

    public static bool ShowsChoice(bool shipCore, ProtoId<StationAiCustomizationGroupPrototype> group, ProtoId<StationAiCustomizationPrototype> choice)
    {
        var ours = choice.Id.StartsWith(ShipAiPrefix, StringComparison.Ordinal);
        return shipCore ? group != FaceGroup || ours : !ours;
    }

    public static string? Chosen(RoleLoadout? loadout, ProtoId<StationAiCustomizationGroupPrototype> group)
    {
        return loadout != null && loadout.SelectedLoadouts.TryGetValue(group.Id, out var picks) && picks.Count > 0
            ? picks[0].Prototype.Id
            : null;
    }

    public bool OnOwnShip(EntityUid ai, EntityUid target)
    {
        var grid = Transform(ai).GridUid;
        return grid != null && grid == Transform(target).GridUid && HasComp<ShuttleDeedComponent>(grid) &&
               _containers.TryGetContainingContainer(ai, out var core) && Transform(core.Owner).Anchored;
    }

    public bool IsShipDevice(EntityUid ai, EntityUid target)
    {
        return Transform(target).Anchored && OnOwnShip(ai, target);
    }

    public bool NearShipTile(EntityUid grid, MapGridComponent gridComp, Vector2 localPos, float range)
    {
        var box = Box2.CenteredAround(localPos, new Vector2(range * 2 + 1, range * 2 + 1));
        return _map.GetLocalTilesEnumerator(grid, gridComp, box, ignoreEmpty: true).MoveNext(out _);
    }

    public bool TryGetCoreTile(EntityUid ship, EntityCoordinates coordinates, out EntityCoordinates tileCenter)
    {
        tileCenter = default;
        if (_xforms.GetGrid(coordinates) != ship ||
            !TryComp<MapGridComponent>(ship, out var grid) ||
            !_map.TryGetTileRef(ship, grid, coordinates, out var tile) ||
            tile.Tile.IsEmpty ||
            _turf.IsTileBlocked(tile, CollisionGroup.Impassable) ||
            !_anchorable.TileFree((ship, grid), tile.GridIndices, (int) CollisionGroup.MachineLayer, (int) CollisionGroup.MachineMask))
            return false;

        tileCenter = _map.GridTileToLocal(ship, grid, tile.GridIndices);
        return true;
    }

    private void OnInRange(Entity<ShipAiComponent> ent, ref InRangeOverrideEvent args)
    {
        args.Handled = true;
        args.InRange = OnOwnShip(ent, args.Target);
    }

    private void OnAccessible(Entity<ShipAiComponent> ent, ref AccessibleOverrideEvent args)
    {
        if (args.Accessible || args.User != ent.Owner)
            return;

        args.Handled = true;

        if (_containers.TryGetContainingContainer(args.Target, out _))
            return;

        args.Accessible = OnOwnShip(ent, args.Target);
    }

    private void OnInteractionAttempt(Entity<ShipAiComponent> ent, ref InteractionAttemptEvent args)
    {
        if (args.Target is not { } target)
            return;

        if (HasComp<StationAiWhitelistComponent>(target))
        {
            if (target != ent.Owner && !IsOwnCore(ent, target) && !OnOwnShip(ent, target))
                args.Cancelled = true;
            return;
        }

        if (!args.Cancelled ||
            !IsShipDevice(ent, target) ||
            !IsDevice(ent, target) && !IsLockOnly(ent, target))
            return;

        args.Cancelled = false;
    }

    private bool IsOwnCore(EntityUid ai, EntityUid target)
    {
        return _containers.TryGetContainingContainer(ai, out var core) && core.Owner == target;
    }

    private bool IsDevice(Entity<ShipAiComponent> ai, EntityUid target)
    {
        return _whitelist.CheckBoth(target, ai.Comp.DevicesBlacklist, ai.Comp.Devices);
    }

    private bool IsLockOnly(Entity<ShipAiComponent> ai, EntityUid target)
    {
        return HasComp<LockComponent>(target) && !HasComp<EntityStorageComponent>(target) && !IsDevice(ai, target);
    }

    private void OnActivateInWorld(Entity<TransformComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled ||
            !TryComp<ShipAiComponent>(args.User, out var ai) ||
            !IsShipDevice(args.User, ent) ||
            !IsLockOnly((args.User, ai), ent))
            return;

        args.Handled = true;
    }

    public EntityCoordinates DropCoordinates(EntityUid device, EntityUid user)
    {
        if (HasComp<HandsComponent>(user))
            return Transform(user).Coordinates;

        var xform = Transform(device);
        return xform.Coordinates.Offset(xform.LocalRotation.GetCardinalDir().ToVec());
    }

    public bool IsShutter(EntityUid uid, DoorComponent door)
    {
        return !door.ClickOpen && !HasComp<AirlockComponent>(uid);
    }

    private void OnGettingInteractedWith(Entity<TransformComponent> ent, ref GettingInteractedWithAttemptEvent args)
    {
        if (!args.Cancelled ||
            !HasComp<ShipAiComponent>(args.Uid) ||
            !HasComp<SubFloorHideComponent>(ent) ||
            !IsShipDevice(args.Uid, ent))
            return;

        args.Cancelled = false;
    }

    private void OnBeforeInteractHand(Entity<ShipAiComponent> ent, ref BeforeInteractHandEvent args)
    {
        if (args.Handled || !IsShipDevice(ent, args.Target))
            return;

        if (IsLockOnly(ent, args.Target))
        {
            args.Handled = true;
            if (TryComp<LockComponent>(args.Target, out var targetLock) && targetLock.Locked && targetLock.UnlockOnClick)
                _lock.TryUnlock(args.Target, ent, targetLock);
            return;
        }

        if (TryComp<DoorComponent>(args.Target, out var door) && IsShutter(args.Target, door))
        {
            args.Handled = true;
            _doors.TryToggleDoor(args.Target, door, ent, predicted: true);
            return;
        }

        if (!TryComp<PoweredLightComponent>(args.Target, out var light))
            return;

        args.Handled = true;
        _lights.SetState(args.Target, !light.On, light);
    }
}
