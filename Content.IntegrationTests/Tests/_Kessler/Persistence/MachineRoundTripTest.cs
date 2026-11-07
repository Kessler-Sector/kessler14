#pragma warning disable RA0002 // Tests read component state directly.
using System.Linq;
using Content.Server._Kessler.Persistence;
using Content.Server._NF.Salvage;
using Content.Server.Worldgen.Components.Debris;
using Content.Server.Worldgen.Components.GC;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Power.Generator;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Kessler.Persistence;

/// <summary>
/// Machines keep working after a persistent save and load: beakers still go into chemistry machines, and a running
/// generator is still running.
/// </summary>
[TestFixture]
public sealed class MachineRoundTripTest
{
    private static readonly string[] BeakerMachines = { "ChemMaster", "ChemDispenser", "KitchenReagentGrinder" };

    [Test]
    public async Task BeakersStillFitAfterLoad()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var snapshots = entMan.System<SectorSnapshotSystem>();
        var itemSlots = entMan.System<ItemSlotsSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            for (var i = 0; i < BeakerMachines.Length; i++)
            {
                var machine = entMan.SpawnEntity(BeakerMachines[i], new EntityCoordinates(testMap.Grid, i * 2, 0));
                entMan.GetComponent<TransformComponent>(machine).Anchored = true;
            }
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var loadedMap = RoundTripTest.SaveAndLoad(entMan, testMap.MapUid, new ResPath("/kessler-roundtrip-test-machines.yml"));
            var tree = snapshots.CollectTree([loadedMap]).ToList();

            foreach (var id in BeakerMachines)
            {
                var machine = tree.Single(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == id);
                var beaker = entMan.SpawnEntity("Beaker", entMan.GetComponent<TransformComponent>(machine).Coordinates);
                Assert.That(entMan.TryGetComponent<ItemSlotsComponent>(machine, out var slots), $"a loaded {id} has item slots");
                var accepted = slots!.Slots.Any(s => itemSlots.CanInsert(machine, beaker, null, s.Value));
                var detail = string.Join("; ", slots.Slots.Select(s =>
                    $"{s.Key}: container {(s.Value.ContainerSlot == null ? "missing" : "ok")}, locked {s.Value.Locked}, has item {s.Value.HasItem}"));
                Assert.That(accepted, $"a loaded {id} takes a beaker ({detail})");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TorsoKeepsItsCavitySlotAfterLoad()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var snapshots = entMan.System<SectorSnapshotSystem>();
        var body = entMan.System<SharedBodySystem>();
        var itemSlots = entMan.System<ItemSlotsSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitPost(() => entMan.SpawnEntity("MobHuman", testMap.GridCoords));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var loadedMap = RoundTripTest.SaveAndLoad(entMan, testMap.MapUid, new ResPath("/kessler-roundtrip-test-cavity.yml"));
            var human = snapshots.CollectTree([loadedMap])
                .Single(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "MobHuman");
            var torso = body.GetBodyChildrenOfType(human, BodyPartType.Torso).Single();
            Assert.That(itemSlots.TryGetSlot(torso.Id, torso.Component.ContainerName, out _),
                "a loaded torso still has its cavity slot (cavity implant surgery, lodged objects)");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Space debris and salvage mobs that lost their grid used to log "invalid entityUid reference" on every save,
    /// which made a successful shutdown save look like it failed.
    /// </summary>
    [Test]
    public async Task DebrisAndStraySalvageMobsSaveWithoutErrors()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            // Off the grid, as debris and stray mobs are.
            var offGrid = new MapCoordinates(new System.Numerics.Vector2(50, 50), testMap.MapId);
            var mob = entMan.SpawnEntity("Crowbar", offGrid);
            entMan.AddComponent<NFSalvageMobRestrictionsComponent>(mob);
            var debris = entMan.SpawnEntity("Crowbar", offGrid);
            entMan.AddComponent<OwnedDebrisComponent>(debris);
            var gc = entMan.AddComponent<GCAbleObjectComponent>(debris);
            gc.Queue = "SpaceDebris";

            // Any error logged while saving fails the test.
            RoundTripTest.SaveAndLoad(entMan, testMap.MapUid, new ResPath("/kessler-roundtrip-test-debris.yml"));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RunningGeneratorStaysOnAfterLoad()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var snapshots = entMan.System<SectorSnapshotSystem>();
        var testMap = await pair.CreateTestMap();

        EntityUid generator = default;
        await server.WaitPost(() =>
        {
            generator = entMan.SpawnEntity("PortableGeneratorPacman", testMap.GridCoords);
            entMan.GetComponent<TransformComponent>(generator).Anchored = true;
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            // Switched on just before saving, with no tick in between: without fuel it would switch itself off.
            entMan.GetComponent<FuelGeneratorComponent>(generator).On = true;
            var loadedMap = RoundTripTest.SaveAndLoad(entMan, testMap.MapUid, new ResPath("/kessler-roundtrip-test-generator.yml"));
            var loaded = snapshots.CollectTree([loadedMap]).Single(entMan.HasComponent<FuelGeneratorComponent>);
            Assert.That(entMan.GetComponent<TransformComponent>(loaded).Anchored, "the generator is still anchored");
            Assert.That(entMan.GetComponent<FuelGeneratorComponent>(loaded).On, "a generator saved running is still running");
        });

        await pair.CleanReturnAsync();
    }
}
