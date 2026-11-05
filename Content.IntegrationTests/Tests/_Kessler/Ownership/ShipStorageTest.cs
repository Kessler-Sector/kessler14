using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._Kessler.Ownership;
using Content.Server._Kessler.Persistence;
using Content.IntegrationTests.Tests._Kessler.Persistence;
using Content.Shared.Mind;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Kessler.Ownership;

/// <summary>
/// Ships go into storage files and come back with their contents and owner, nobody aboard is ever stored, and the
/// list of stored ships is saved with the sector.
/// </summary>
[TestFixture]
public sealed class ShipStorageTest
{
    private static EntityUid MakeShip(TestPair pair, MapId map, Vector2 position)
    {
        var server = pair.Server;
        var grid = server.MapMan.CreateGridEntity(map);
        server.EntMan.System<SharedTransformSystem>().SetWorldPosition(grid, position);
        var tile = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
        server.System<SharedMapSystem>().SetTile(grid.Owner, grid.Comp, Vector2i.Zero, tile);
        return grid.Owner;
    }

    [Test]
    public async Task StoreAndRecall()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var ownership = entMan.System<ShipOwnershipSystem>();
        var storage = entMan.System<ShipStorageSystem>();
        var res = server.ResolveDependency<IResourceManager>();
        var testMap = await pair.CreateTestMap();
        var owner = new NetUserId(Guid.NewGuid());

        await server.WaitAssertion(() =>
        {
            var ship = MakeShip(pair, testMap.MapId, new Vector2(50, 50));
            entMan.SpawnEntity("Crowbar", new EntityCoordinates(ship, 0.5f, 0.5f));
            ownership.SetOwner(ship, owner, -1, "Storage Captain", 50000, purchasedWithVoucher: false);

            Assert.That(storage.TryStoreShip(ship, ShipStorageReason.Admin, DateTime.UtcNow, out var problem), Is.True, problem);
            Assert.That(entMan.Deleted(ship), "the ship left the world");

            var entry = storage.GetStoredShips().Single(s => s.OwnerUserId == owner.UserId);
            Assert.Multiple(() =>
            {
                Assert.That(entry.OwnerName, Is.EqualTo("Storage Captain"));
                Assert.That(entry.Reason, Is.EqualTo(ShipStorageReason.Admin));
                Assert.That(res.UserData.Exists(new ResPath(entry.File)), "the ship's file was written");
            });

            Assert.That(storage.TryRecallShip(entry.Id, null, out var recalled, out problem), Is.True, problem);
            Assert.That(storage.GetStoredShips().Any(s => s.Id == entry.Id), Is.False, "the entry is gone");
            Assert.That(ownership.TryGetOwnership(recalled!.Value, out var comp), "the owner came back with the ship");
            Assert.Multiple(() =>
            {
                Assert.That(comp!.OwnerUserId, Is.EqualTo(owner.UserId));
                Assert.That(comp.UpkeepDebt, Is.EqualTo(0));
                Assert.That(comp.NextUpkeep, Is.GreaterThan(DateTime.UtcNow), "storage time isn't billed");
                Assert.That(entMan.GetComponent<TransformComponent>(recalled.Value).ChildEnumerator.MoveNext(out var child)
                            && entMan.GetComponent<MetaDataComponent>(child).EntityPrototype?.ID == "Crowbar",
                    "the cargo came back");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NotStoredWithSomeoneAboard()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var ownership = entMan.System<ShipOwnershipSystem>();
        var storage = entMan.System<ShipStorageSystem>();
        var mind = entMan.System<SharedMindSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var ship = MakeShip(pair, testMap.MapId, new Vector2(50, 50));
            ownership.SetOwner(ship, new NetUserId(Guid.NewGuid()), -1, "Occupied", 50000, purchasedWithVoucher: false);

            // A living character who could come back: a body with a mind.
            var human = entMan.SpawnEntity("MobHuman", new EntityCoordinates(ship, 0.5f, 0.5f));
            var mindId = mind.CreateMind(null);
            mind.TransferTo(mindId, human);

            Assert.That(storage.TryStoreShip(ship, ShipStorageReason.Inactive, DateTime.UtcNow, out var problem), Is.False);
            Assert.That(problem, Does.Contain("aboard"));
            Assert.That(entMan.Deleted(ship), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task InactiveOwnersShipIsStored()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var ownership = entMan.System<ShipOwnershipSystem>();
        var storage = entMan.System<ShipStorageSystem>();
        var testMap = await pair.CreateTestMap();
        var owner = new NetUserId(Guid.NewGuid());

        await server.WaitAssertion(() =>
        {
            var ship = MakeShip(pair, testMap.MapId, new Vector2(50, 50));
            ownership.SetOwner(ship, owner, -1, "Away Captain", 50000, purchasedWithVoucher: false);

            var now = DateTime.UtcNow;
            storage.SetLastSeen(owner.UserId, now - TimeSpan.FromDays(3));
            storage.StoreDueShips(now);
            Assert.That(entMan.Deleted(ship), Is.False, "three days away is not long enough");

            storage.SetLastSeen(owner.UserId, now - TimeSpan.FromDays(8));
            storage.StoreDueShips(now);
            Assert.That(entMan.Deleted(ship), "eight days away stores the ship");
            Assert.That(storage.GetStoredShips().Single(s => s.OwnerUserId == owner.UserId).Reason,
                Is.EqualTo(ShipStorageReason.Inactive));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StorageListIsSaved()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var ownership = entMan.System<ShipOwnershipSystem>();
        var storage = entMan.System<ShipStorageSystem>();
        var testMap = await pair.CreateTestMap();
        var owner = new NetUserId(Guid.NewGuid());

        await server.WaitAssertion(() =>
        {
            var ship = MakeShip(pair, testMap.MapId, new Vector2(50, 50));
            ownership.SetOwner(ship, owner, -1, "Saved Captain", 50000, purchasedWithVoucher: false);
            Assert.That(storage.TryStoreShip(ship, ShipStorageReason.Impounded, DateTime.UtcNow, out var problem), Is.True, problem);
            var stored = storage.GetStoredShips().Single(s => s.OwnerUserId == owner.UserId);

            // The list lives on a map; save that map and load it again.
            var query = entMan.EntityQueryEnumerator<ShipStorageRegistryComponent, MapComponent>();
            Assert.That(query.MoveNext(out var registryMap, out _, out _), "the list is kept on a map");

            var loadedMap = RoundTripTest.SaveAndLoad(entMan, registryMap, new ResPath("/kessler-roundtrip-test-storage.yml"));
            Assert.That(entMan.TryGetComponent<ShipStorageRegistryComponent>(loadedMap, out var loaded), "the loaded map has the list");
            var entry = loaded!.Ships.Single(s => s.Id == stored.Id);
            Assert.Multiple(() =>
            {
                Assert.That(entry.OwnerName, Is.EqualTo("Saved Captain"));
                Assert.That(entry.Reason, Is.EqualTo(ShipStorageReason.Impounded));
                Assert.That(entry.File, Is.EqualTo(stored.File));
                Assert.That(entry.StoredAt, Is.EqualTo(stored.StoredAt).Within(TimeSpan.FromSeconds(1)));
            });
        });

        await pair.CleanReturnAsync();
    }
}
