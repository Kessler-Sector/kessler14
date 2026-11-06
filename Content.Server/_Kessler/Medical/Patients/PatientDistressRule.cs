using System.Numerics;
using Content.Server.Shuttles.Systems;
using Content.Server.StationEvents.Events;
using Content.Shared.GameTicking.Components;
using Content.Shared.Maps;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Kessler.Medical.Patients;

/// <summary>
/// Spawns a distress beacon: a scrap of wreck in open space with a living patient in a stasis pod, shown on radar and
/// announced to medical with its coordinates. See <see cref="PatientDistressRuleComponent"/>.
/// </summary>
public sealed class PatientDistressRule : StationEventSystem<PatientDistressRuleComponent>
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefs = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly PatientSystem _patients = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ShuttleSystem _shuttle = default!;

    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(30);
    private TimeSpan _nextCleanup;

    protected override void Started(EntityUid uid, PatientDistressRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        var position = _random.NextVector2(component.MinimumDistance, component.MaximumDistance);
        if (SpawnBeacon(component, GameTicker.DefaultMap, position) is not { } beacon)
            return;

        var message = Loc.GetString("kessler-distress-announcement",
            ("name", Name(beacon)),
            ("x", (int) position.X),
            ("y", (int) position.Y));
        RadioSystem.SendRadioMessage(uid, message, component.Channel, MapSystem.GetMap(GameTicker.DefaultMap), escapeMarkup: false);
    }

    /// <summary>
    /// Makes the beacon's wreck at a position on a map and puts a patient's stasis pod on it.
    /// </summary>
    public EntityUid? SpawnBeacon(PatientDistressRuleComponent component, MapId map, Vector2 position)
    {
        if (!_mapManager.MapExists(map))
            return null;

        var grid = _mapManager.CreateGridEntity(map);
        _transform.SetMapCoordinates(grid, new MapCoordinates(position, map));

        var plating = new Tile(_tileDefs["Plating"].TileId);
        var lattice = new Tile(_tileDefs["Lattice"].TileId);
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                tiles.Add((new Vector2i(x, y), x == 0 || y == 0 ? plating : lattice));
            }
        }
        _maps.SetTiles(grid, tiles);

        _metaData.SetEntityName(grid, Loc.GetString("kessler-distress-beacon-name", ("code", _random.Next(100, 1000))));
        var iff = EnsureComp<IFFComponent>(grid);
        _shuttle.SetIFFColor(grid, Color.OrangeRed, iff);
        _shuttle.SetIFFReadOnly(grid, true, iff);

        var beacon = EnsureComp<DistressBeaconComponent>(grid);
        beacon.Expires = DateTime.UtcNow + component.Lifetime;

        var pod = Spawn(component.Pod, new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
        if (_container.TryGetContainer(pod, "entity_storage", out var storage))
        {
            foreach (var contained in storage.ContainedEntities)
            {
                if (TryComp<KesslerPatientComponent>(contained, out var patient))
                    _patients.AddRescueBonus((contained, patient), component.RescueBonus);
            }
        }

        return grid;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextCleanup)
            return;
        _nextCleanup = _timing.CurTime + CleanupInterval;

        DeleteExpiredBeacons(DateTime.UtcNow);
    }

    /// <summary>
    /// Removes beacons past their time that nobody is aboard; an unanswered patient is lost with them.
    /// </summary>
    public void DeleteExpiredBeacons(DateTime now)
    {
        var query = EntityQueryEnumerator<DistressBeaconComponent, MapGridComponent>();
        while (query.MoveNext(out var grid, out var beacon, out _))
        {
            if (beacon.Expires > now || HasPlayerAboard(grid))
                continue;

            QueueDel(grid);
        }
    }

    private bool HasPlayerAboard(EntityUid grid)
    {
        var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actors.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid == grid)
                return true;
        }

        return false;
    }
}
