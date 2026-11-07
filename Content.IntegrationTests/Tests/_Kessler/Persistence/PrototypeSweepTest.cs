#pragma warning disable RA0002 // Tests read component state directly.
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Content.Server._Kessler.Persistence;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Maps;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Kessler.Persistence;

/// <summary>
/// Every persisted prototype, spawned and map-initialized, must come back from a persistent save and load with the
/// same components, item slots and container contents. Catches setup that only happens on map init (which doesn't run
/// on load) and state that isn't saved, for the whole game at once rather than one bug report at a time.
/// </summary>
[TestFixture]
public sealed class PrototypeSweepTest
{
    /// <summary>
    /// Components that are expected to differ after a load: runtime-only by design, or rebuilt on demand.
    /// </summary>
    private static readonly HashSet<string> IgnoredComponents = new()
    {
        "MetaData",
        "Transform",
        // Marks an NPC whose AI is running; woken again as needed, and correctly absent on a dead one after a load.
        "ActiveNPC",
    };

    /// <summary>
    /// Components a loaded entity may gain without harm: added lazily at startup when missing.
    /// </summary>
    private static readonly HashSet<string> MayGainComponents = new()
    {
        // An empty manager some mobs only get on startup.
        "SolutionContainerManager",
    };

    /// <summary>
    /// Prototypes that are only valid when another system creates them and links them up, so spawning one on its own
    /// leaves required references unset.
    /// </summary>
    private static readonly HashSet<string> SkippedPrototypes = new()
    {
        // Made by a chameleon projector, which sets its user and projector.
        "ChameleonDisguise",
        // Known gap: a changeling's stolen identities are clones on a separate paused map that isn't saved, so they are
        // lost on load (and the save logs the missing references). Changelings aren't used on this server.
        "MobLing",
    };

    [Test]
    public async Task EveryPrototypeSurvivesSaveAndLoad()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var mapSys = entMan.System<SharedMapSystem>();
        var save = entMan.System<PersistenceSaveSystem>();
        var snapshots = entMan.System<SectorSnapshotSystem>();

        var prototypes = protoMan.EnumeratePrototypes<EntityPrototype>()
            .Where(p => !p.Abstract
                        && !pair.IsTestPrototype(p)
                        && !p.Components.ContainsKey("MapGrid")
                        && !p.Components.ContainsKey("Map")
                        && p.SetSuffix != "DEBUG"
                        && !SkippedPrototypes.Contains(p.ID)
                        && save.IsPersisted(p))
            .OrderBy(p => p.ID)
            .ToList();

        var testMap = await pair.CreateTestMap(true, "FloorSteel");
        const int spacing = 2;
        var side = (int) Math.Ceiling(Math.Sqrt(prototypes.Count));
        var spawnedAt = new Dictionary<Vector2i, string>();

        Dictionary<Vector2i, Dictionary<string, int>> before = null;
        Dictionary<Vector2i, Dictionary<string, int>> after = null;

        await server.WaitAssertion(() =>
        {
            var gridComp = entMan.GetComponent<MapGridComponent>(testMap.Grid);
            var floor = new Tile(tileDefs["FloorSteel"].TileId);
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = -1; x <= side * spacing; x++)
            for (var y = -1; y <= side * spacing; y++)
                tiles.Add((new Vector2i(x, y), floor));
            mapSys.SetTiles(testMap.Grid, gridComp, tiles);

            for (var i = 0; i < prototypes.Count; i++)
            {
                var tile = new Vector2i(i % side * spacing, i / side * spacing);
                spawnedAt[tile] = prototypes[i].ID;
                try
                {
                    entMan.SpawnEntity(prototypes[i].ID, mapSys.GridTileToLocal(testMap.Grid, gridComp, tile));
                }
                catch (Exception e)
                {
                    Assert.Fail($"Spawning {prototypes[i].ID} threw: {e.Message}");
                }
            }

        });

        // Let spawned things settle for a moment (mob states, delayed setup), as they always have before a real save.
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            before = Collect(entMan, mapSys, save, testMap.Grid);

            var loadedMap = RoundTripTest.SaveAndLoad(entMan, testMap.MapUid, new ResPath("/kessler-prototype-sweep.yml"));
            var loadedGrid = snapshots.CollectTree([loadedMap]).Single(entMan.HasComponent<MapGridComponent>);
            after = Collect(entMan, mapSys, save, loadedGrid);
        });

        // Compare tile by tile, and summarise by kind of difference so the report reads as a to-do list.
        var report = new StringBuilder();
        var byKind = new Dictionary<string, SortedSet<string>>();
        var failing = 0;
        foreach (var tile in before.Keys.Union(after.Keys))
        {
            var b = before.GetValueOrDefault(tile) ?? new Dictionary<string, int>();
            var a = after.GetValueOrDefault(tile) ?? new Dictionary<string, int>();
            var lines = new List<string>();
            foreach (var fact in b.Keys.Union(a.Keys))
            {
                var diff = a.GetValueOrDefault(fact) - b.GetValueOrDefault(fact);
                if (diff == 0)
                    continue;

                if (diff > 0 && Kind(fact) is var gained && gained.StartsWith("component ")
                    && MayGainComponents.Contains(gained["component ".Length..]))
                {
                    continue;
                }

                var kind = (diff < 0 ? "lost: " : "gained: ") + Kind(fact);
                lines.Add($"  {kind} ({fact})");
                var owner = spawnedAt.GetValueOrDefault(tile, "?");
                if (!byKind.TryGetValue(kind, out var set))
                    byKind[kind] = set = new SortedSet<string>();
                set.Add(owner);
            }

            if (lines.Count == 0)
                continue;

            failing++;
            report.AppendLine($"{spawnedAt.GetValueOrDefault(tile, "?")} at {tile}:");
            foreach (var line in lines.Take(20))
                report.AppendLine(line);
        }

        var summary = new StringBuilder();
        summary.AppendLine($"{failing} of {prototypes.Count} prototypes differ after save and load.");
        foreach (var (kind, owners) in byKind.OrderByDescending(k => k.Value.Count))
            summary.AppendLine($"{owners.Count,5}  {kind}  e.g. {string.Join(", ", owners.Take(6))}");

        TestContext.Out.WriteLine(summary.ToString());
        TestContext.Out.WriteLine(report.ToString());
        Assert.That(failing, Is.Zero, summary.ToString());

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The kind of a fact without the entity path: "component X", "slot Y", "container Z holds P".
    /// </summary>
    private static string Kind(string fact)
    {
        var at = fact.LastIndexOf(" :: ", StringComparison.Ordinal);
        return at < 0 ? fact : fact[(at + 4)..];
    }

    private static Dictionary<Vector2i, Dictionary<string, int>> Collect(IEntityManager entMan, SharedMapSystem mapSys, PersistenceSaveSystem save, EntityUid grid)
    {
        var gridComp = entMan.GetComponent<MapGridComponent>(grid);
        var result = new Dictionary<Vector2i, Dictionary<string, int>>();
        var xform = entMan.GetComponent<TransformComponent>(grid);
        var children = new List<EntityUid>();
        var enumerator = xform.ChildEnumerator;
        while (enumerator.MoveNext(out var c))
            children.Add(c);
        foreach (var child in children)
        {
            var childXform = entMan.GetComponent<TransformComponent>(child);
            var tile = mapSys.TileIndicesFor(grid, gridComp, childXform.Coordinates);
            if (!result.TryGetValue(tile, out var facts))
                result[tile] = facts = new Dictionary<string, int>();
            AddFacts(entMan, save, child, string.Empty, facts, 0);
        }

        return result;
    }

    private static void AddFacts(IEntityManager entMan, PersistenceSaveSystem save, EntityUid uid, string path, Dictionary<string, int> facts, int depth)
    {
        // Left out of persistent saves on purpose (effects, things that despawn on a timer).
        if (entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype is { } entProto && !save.IsPersisted(entProto))
            return;

        var proto = entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID ?? "(no prototype)";
        var here = path.Length == 0 ? proto : $"{path} > {proto}";
        void Add(string fact)
        {
            var key = $"{here} :: {fact}";
            facts[key] = facts.GetValueOrDefault(key) + 1;
        }

        Add("exists");
        var factory = entMan.ComponentFactory;
        foreach (var comp in entMan.GetComponents(uid))
        {
            var name = factory.GetComponentName(comp.GetType());
            if (!IgnoredComponents.Contains(name))
                Add($"component {name}");
        }

        if (entMan.TryGetComponent<ItemSlotsComponent>(uid, out var slots))
        {
            foreach (var (id, slot) in slots.Slots)
            {
                var held = slot.Item is { } item ? entMan.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID : null;
                Add(held == null ? $"slot {id}" : $"slot {id} holds {held}");
            }
        }

        if (depth > 6 || !entMan.TryGetComponent<ContainerManagerComponent>(uid, out var containers))
            return;

        foreach (var (id, container) in containers.Containers)
        {
            Add($"container {id}");
            foreach (var contained in container.ContainedEntities)
                AddFacts(entMan, save, contained, $"{here} [{id}]", facts, depth + 1);
        }
    }
}
