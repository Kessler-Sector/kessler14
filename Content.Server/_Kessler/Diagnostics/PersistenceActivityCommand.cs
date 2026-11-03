using System.Linq;
using Content.Server.Administration;
using Content.Server.Atmos.EntitySystems;
using Content.Server.GameTicking;
using Content.Shared.Administration;
using Content.Shared.Atmos;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._Kessler.Diagnostics;

/// <summary>
/// Creates repeatable activity on the largest grid of the sector map (wandering mobs and fires), for measuring tick
/// time on a server without players. Destructive: dev servers only.
/// </summary>
[AdminCommand(AdminFlags.Host)]
public sealed class PersistenceActivityCommand : LocalizedEntityCommands
{
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private const string MobPrototype = "MobMouse";

    public override string Command => "persistenceactivity";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2 || !int.TryParse(args[0], out var mobs) || !int.TryParse(args[1], out var fires))
        {
            shell.WriteError(Help);
            return;
        }

        if (!_map.TryGetMap(_ticker.DefaultMap, out var mapUid))
        {
            shell.WriteError(Loc.GetString("cmd-persistenceactivity-no-sector"));
            return;
        }

        // The largest grid on the sector map is the main station.
        Entity<MapGridComponent>? largest = null;
        var grids = EntityManager.EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (grids.MoveNext(out var uid, out var grid, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;
            if (largest == null || Box2.Area(grid.LocalAABB) > Box2.Area(largest.Value.Comp.LocalAABB))
                largest = (uid, grid);
        }

        if (largest is not { } station)
        {
            shell.WriteError(Loc.GetString("cmd-persistenceactivity-no-sector"));
            return;
        }

        // Tiles with breathable air, i.e. inside the station.
        var tiles = _map.GetAllTiles(station, station.Comp)
            .Where(t => _atmos.GetTileMixture(station.Owner, mapUid, t.GridIndices) is { Pressure: > 50f })
            .ToList();
        if (tiles.Count == 0)
        {
            shell.WriteError(Loc.GetString("cmd-persistenceactivity-no-sector"));
            return;
        }

        for (var i = 0; i < mobs; i++)
        {
            var tile = _random.Pick(tiles);
            EntityManager.SpawnEntity(MobPrototype, _map.GridTileToLocal(station, station.Comp, tile.GridIndices));
        }

        for (var i = 0; i < fires; i++)
        {
            var tile = _random.Pick(tiles);
            _atmos.GetTileMixture(station.Owner, mapUid, tile.GridIndices, true)?.AdjustMoles(Gas.Plasma, 30f);
            _atmos.HotspotExpose(station.Owner, tile.GridIndices, 1000f, 50f);
        }

        shell.WriteLine(Loc.GetString("cmd-persistenceactivity-done",
            ("mobs", mobs), ("fires", fires), ("grid", EntityManager.ToPrettyString(station))));
    }
}
