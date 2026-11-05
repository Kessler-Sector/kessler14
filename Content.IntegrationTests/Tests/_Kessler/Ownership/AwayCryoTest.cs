using System.Linq;
using System.Numerics;
using Content.Server._Kessler.CryoSleep;
using Content.Server._Kessler.Ownership;
using Content.Server._Kessler.Persistence;
using Content.Server._NF.CryoSleep;
using Content.IntegrationTests.Tests._Kessler.Persistence;
using Content.Shared.Mind;
using Robust.Shared.EntitySerialization;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Kessler.Ownership;

/// <summary>
/// Bodies in cryo storage are saved with the sector (Frontier kept them in memory and lost them on restart), and the
/// SSD bodies of players who have been away a week go into cryo so their ships can be stored.
/// </summary>
[TestFixture]
public sealed class AwayCryoTest
{
    [Test]
    public async Task CryoStoredBodySurvivesSaveAndLoad()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var cryo = entMan.System<CryoSleepSystem>();
        var mind = entMan.System<SharedMindSystem>();
        var snapshots = entMan.System<SectorSnapshotSystem>();
        var testMap = await pair.CreateTestMap();
        // A player the server knows, who has since disconnected.
        var dummy = await server.AddDummySession("cryo-sleeper");
        var user = dummy.UserId;
        await server.RemoveDummySession(dummy);

        EntityUid human = default;
        await server.WaitPost(() =>
        {
            entMan.SpawnEntity("MachineCryoSleepPod", testMap.GridCoords);
            human = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            var mindId = mind.CreateMind(user, "Sleeper");
            mind.TransferTo(mindId, human);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(cryo.TryCryoStoreAwayBody(human), "there is a cryopod to use");
            Assert.That(cryo.IsInCryoStorage(human));
            Assert.That(entMan.GetComponent<MetaDataComponent>(human).EntityPaused, "stored bodies are paused");
            Assert.That(cryo.HasCryosleepingBody(user));

            entMan.EventBus.RaiseEvent(EventSource.Local, new BeforeSectorSaveEvent());
            var path = new ResPath("/kessler-roundtrip-test-cryo.yml");
            Assert.That(entMan.System<PersistenceSaveSystem>().TrySaveMap(testMap.MapUid, path));

            // A restart starts from nothing: delete the map and every mind.
            entMan.DeleteEntity(testMap.MapUid);
            foreach (var mindId in entMan.EntityQuery<MindComponent>(true).Select(m => m.Owner).ToList())
                entMan.DeleteEntity(mindId);

            var options = new MapLoadOptions { ExpectedCategory = FileCategory.Map };
            Assert.That(entMan.System<PersistenceLoadSystem>().TryLoadSave(path, options, out var result));
            var loadedMap = result!.Maps.Single().Owner;
            var tree = snapshots.CollectTree([loadedMap]).ToHashSet();

            Assert.That(tree.Any(uid => entMan.HasComponent<KesslerCryoStorageComponent>(uid)), "the loaded map keeps the storage");
            Assert.That(cryo.TryGetSleepingBody(user, out var loadedBody, out _), "the player still has a body in cryo");
            Assert.That(tree, Does.Contain(loadedBody!.Value), "it is the loaded body, not the old one");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AwayOwnersBodyGoesToCryoAndShipIsStored()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var cryo = entMan.System<CryoSleepSystem>();
        var mind = entMan.System<SharedMindSystem>();
        var ownership = entMan.System<ShipOwnershipSystem>();
        var storage = entMan.System<ShipStorageSystem>();
        var testMap = await pair.CreateTestMap();
        var dummy = await server.AddDummySession("away-captain");
        var owner = dummy.UserId;
        await server.RemoveDummySession(dummy);

        EntityUid ship = default;
        EntityUid body = default;
        await server.WaitPost(() =>
        {
            entMan.SpawnEntity("MachineCryoSleepPod", testMap.GridCoords);

            var grid = server.MapMan.CreateGridEntity(testMap.MapId);
            entMan.System<SharedTransformSystem>().SetWorldPosition(grid, new Vector2(50, 50));
            var tile = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
            server.System<SharedMapSystem>().SetTile(grid.Owner, grid.Comp, Vector2i.Zero, tile);
            ship = grid.Owner;
            ownership.SetOwner(ship, owner, -1, "Away Captain", 50000, purchasedWithVoucher: false);

            // The owner logged out aboard their ship: an SSD body with their mind.
            body = entMan.SpawnEntity("MobHuman", new EntityCoordinates(ship, 0.5f, 0.5f));
            var mindId = mind.CreateMind(owner, "Away Captain");
            mind.TransferTo(mindId, body);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var now = DateTime.UtcNow;
            storage.SetLastSeen(owner.UserId, now - TimeSpan.FromDays(8));
            storage.StoreDueShips(now);

            Assert.That(cryo.IsInCryoStorage(body), "the away owner's body went into cryo");
            Assert.That(entMan.Deleted(ship), "with the body gone, the ship was stored");
            Assert.That(storage.GetStoredShips().Single(s => s.OwnerUserId == owner.UserId).Reason,
                Is.EqualTo(ShipStorageReason.Inactive));
        });

        await pair.CleanReturnAsync();
    }
}
