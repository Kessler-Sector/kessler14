using System.Collections.Generic;
using System.Linq;
using Content.Server._Kessler.Persistence;
using Content.Server.Nyanotrasen.Kitchen.Components;
using Content.Shared.Chemistry.EntitySystems;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Kessler.Persistence;

/// <summary>
/// Save/load round trips through the Kessler persistence save and load path, covering the defects found by the
/// Stage A <c>persistenceroundtrip</c> run.
/// </summary>
[TestFixture]
public sealed class RoundTripTest
{
    private static readonly string[] Prototypes =
    {
        // Construction node actions used to empty their tanks on load.
        "Sink",
        "SinkWide",
        "SinkStemlessWater",
        "Icebox",
        // Migrations used to delete their attached hood/helmet on load.
        "ClothingOuterSuitRad",
        "ClothingOuterHardsuitVoidParamed",
        // Migrations used to replace or delete these on load.
        "FoodTacoBeef",
        "ClothingHeadHatHairflower",
        "SpaceMedipen",
        "Ashtray",
        // save: false prototypes, and things that reference them.
        "MobCow",
        "CrateNPCCow",
        "MechRipley",
        "KitchenDeepFryer",
    };

    [Test]
    [TestCaseSource(nameof(Prototypes))]
    public async Task EntityRoundTrip(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var snapshots = entManager.System<SectorSnapshotSystem>();

        var testMap = await pair.CreateTestMap();
        SectorSnapshot before = null;
        SectorSnapshot after = null;

        await server.WaitPost(() =>
        {
            entManager.SpawnEntity(prototype, testMap.GridCoords);
            before = snapshots.Snapshot(snapshots.CollectTree([testMap.MapUid]));

            var loaded = SaveAndLoad(entManager, testMap.MapUid, new ResPath($"/kessler-roundtrip-test-{prototype}.yml"));
            after = snapshots.Snapshot(snapshots.CollectTree([loaded]));
        });

        var diffs = SectorSnapshot.Compare(before, after);
        Assert.That(diffs, Is.Empty,
            string.Join("\n", diffs.Select(d => $"{d.Metric} {d.Key}: {d.Before} -> {d.After}")));
        Assert.That(SectorSnapshot.ChangedPapers(before, after), Is.Zero);

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A loaded deep fryer must use its saved oil solution, not a new detached one.
    /// </summary>
    [Test]
    public async Task DeepFryerKeepsSolution()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var solutions = entManager.System<SharedSolutionContainerSystem>();
        var snapshots = entManager.System<SectorSnapshotSystem>();

        var testMap = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            var fryer = entManager.SpawnEntity("KitchenDeepFryer", testMap.GridCoords);
            var name = entManager.GetComponent<DeepFryerComponent>(fryer).SolutionName;
            Assert.That(solutions.TryGetSolution(fryer, name, out var soln, out _));
            solutions.TryAddReagent(soln!.Value, "OilOlive", 50);

            var loadedMap = SaveAndLoad(entManager, testMap.MapUid, new ResPath("/kessler-roundtrip-test-fryer.yml"));
            var loadedFryer = snapshots.CollectTree([loadedMap])
                .Single(uid => entManager.HasComponent<DeepFryerComponent>(uid));

            Assert.That(solutions.TryGetSolution(loadedFryer, name, out _, out var loadedSolution));
            var component = entManager.GetComponent<DeepFryerComponent>(loadedFryer);
            Assert.Multiple(() =>
            {
                Assert.That(component.Solution, Is.SameAs(loadedSolution));
                Assert.That(loadedSolution!.Volume.Float(), Is.EqualTo(50f));
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Migrations for prototypes that no longer exist must still apply when a save is loaded.
    /// </summary>
    [Test]
    public async Task MigrationAppliesToRemovedPrototypes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var res = server.ResolveDependency<IResourceManager>();
        var persistenceSave = entManager.System<PersistenceSaveSystem>();
        var persistenceLoad = entManager.System<PersistenceLoadSystem>();
        var snapshots = entManager.System<SectorSnapshotSystem>();

        var testMap = await pair.CreateTestMap();
        var path = new ResPath("/kessler-roundtrip-test-migration.yml");

        await server.WaitPost(() =>
        {
            entManager.SpawnEntity("ClothingOuterArmorBasic", testMap.GridCoords);
            Assert.That(persistenceSave.TrySaveMap(testMap.MapUid, path));

            // migration.yml maps the removed ClothingOuterVestKevlar to ClothingOuterArmorBasic.
            var text = res.UserData.ReadAllText(path)
                .Replace("proto: ClothingOuterArmorBasic", "proto: ClothingOuterVestKevlar");
            res.UserData.WriteAllText(path, text);

            Assert.That(persistenceLoad.TryLoadSave(path, LoadOptions, out var result));
            var counts = snapshots.Snapshot(snapshots.CollectTree([result!.Maps.Single().Owner])).PrototypeCounts;
            Assert.That(counts.GetValueOrDefault("ClothingOuterArmorBasic"), Is.EqualTo(1));
        });

        await pair.CleanReturnAsync();
    }

    private static readonly MapLoadOptions LoadOptions = new()
    {
        ExpectedCategory = FileCategory.Map,
        DeserializationOptions = DeserializationOptions.Default with { PauseMaps = true },
    };

    /// <summary>
    /// Saves a map and loads it into a new paused map through the persistence systems, returning the new map's uid.
    /// </summary>
    internal static EntityUid SaveAndLoad(IEntityManager entManager, EntityUid map, ResPath path)
    {
        Assert.That(entManager.System<PersistenceSaveSystem>().TrySaveMap(map, path));
        Assert.That(entManager.System<PersistenceLoadSystem>().TryLoadSave(path, LoadOptions, out var result));
        return result!.Maps.Single().Owner;
    }
}
