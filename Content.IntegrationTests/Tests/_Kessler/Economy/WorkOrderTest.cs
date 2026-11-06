using System.Collections.Generic;
#pragma warning disable RA0002 // Tests set component state directly.
using System.Linq;
using Content.Server._Kessler.Persistence;
using Content.Server._Kessler.WorkOrders;
using Content.Shared.Fluids.Components;
using Content.Shared.Interaction;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using Content.IntegrationTests.Pair;

namespace Content.IntegrationTests.Tests._Kessler.Economy;

/// <summary>
/// The work-order board keeps orders open, cleaning orders make messes that count down as they are cleaned,
/// delivery orders take the wanted item, and the board's orders survive a save and load.
/// </summary>
[TestFixture]
public sealed class WorkOrderTest
{
    private static void Floor(TestPair pair, Entity<MapGridComponent> grid, int size)
    {
        var tile = new Tile(pair.Server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
        var map = pair.Server.System<SharedMapSystem>();
        for (var x = -size; x <= size; x++)
        for (var y = -size; y <= size; y++)
            map.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), tile);
    }

    [Test]
    public async Task BoardPostsOrdersAndCleaningCountsDown()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var workOrders = entMan.System<WorkOrderSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            Floor(pair, testMap.Grid, 5);
            var boardUid = entMan.SpawnEntity("ComputerWorkOrderBoard", testMap.GridCoords);
            var board = entMan.GetComponent<WorkOrderBoardComponent>(boardUid);
            board.Allowed.Add("CleanStation");

            var now = DateTime.UtcNow;
            for (var i = 0; i < board.BaseOpen; i++)
                workOrders.UpdateBoard((boardUid, board), now);

            Assert.That(board.Orders, Has.Count.EqualTo(board.BaseOpen), "the board fills up to its base number of orders");
            var order = board.Orders[0];
            Assert.That(order.MessesTotal, Is.GreaterThan(0));

            var messes = entMan.EntityQuery<WorkOrderMessComponent>()
                .Where(m => m.OrderId == order.Id)
                .Select(m => m.Owner)
                .ToList();
            Assert.That(messes, Has.Count.EqualTo(order.MessesTotal));
            Assert.That(messes.All(entMan.HasComponent<PuddleComponent>), "messes are puddles");

            entMan.DeleteEntity(messes[0]);
            Assert.That(order.MessesLeft, Is.EqualTo(order.MessesTotal - 1), "cleaning one counts down");

            foreach (var mess in messes.Skip(1))
                entMan.DeleteEntity(mess);
            Assert.That(board.Orders.Any(o => o.Id == order.Id), Is.False, "a fully cleaned order is done");

            // Orders expire in real time.
            workOrders.UpdateBoard((boardUid, board), now + board.Lifetime + TimeSpan.FromMinutes(1));
            Assert.That(board.Orders.All(o => o.Expires > now + board.Lifetime), "expired orders are dropped");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BlownLightsCountDownAsTheyAreFixed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var workOrders = entMan.System<WorkOrderSystem>();
        var lights = entMan.System<SharedPoweredLightSystem>();
        var testMap = await pair.CreateTestMap();

        var lightUids = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            Floor(pair, testMap.Grid, 5);
            for (var i = 0; i < 5; i++)
                lightUids.Add(entMan.SpawnEntity("Poweredlight", new EntityCoordinates(testMap.Grid, i - 2, 0)));
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var boardUid = entMan.SpawnEntity("ComputerWorkOrderBoard", testMap.GridCoords);
            var board = entMan.GetComponent<WorkOrderBoardComponent>(boardUid);
            board.Allowed.Add("RepairLights");
            board.BaseOpen = 1; // One order to look at; a board fills to its base number at once.
            board.MaxOpen = 1;
            workOrders.UpdateBoard((boardUid, board), DateTime.UtcNow);

            var order = board.Orders.Single();
            var broken = lightUids.Where(entMan.HasComponent<WorkOrderRepairComponent>).ToList();
            Assert.That(broken, Has.Count.EqualTo(order.MessesTotal));
            Assert.That(broken, Has.Count.GreaterThanOrEqualTo(3));
            Assert.That(broken.All(l => entMan.GetComponent<LightBulbComponent>(lights.GetBulb(l)!.Value).State == LightBulbState.Broken),
                "the order's lights are blown");

            // Fix one: swap the broken bulb for a new one.
            var first = broken[0];
            lights.EjectBulb(first);
            var tube = entMan.SpawnEntity("LightTube", testMap.GridCoords);
            Assert.That(lights.InsertBulb(first, tube));
            Assert.That(order.MessesLeft, Is.EqualTo(order.MessesTotal - 1), "fixing a light counts down");

            // A broken bulb doesn't count.
            var second = broken[1];
            var dud = lights.EjectBulb(second)!.Value;
            lights.InsertBulb(second, dud);
            Assert.That(order.MessesLeft, Is.EqualTo(order.MessesTotal - 1), "putting a broken bulb back doesn't count");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeliveryTakesTheWantedItem()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var boardUid = entMan.SpawnEntity("ComputerWorkOrderBoard", testMap.GridCoords);
            var board = entMan.GetComponent<WorkOrderBoardComponent>(boardUid);
            board.Orders.Add(new WorkOrder
            {
                Id = 99,
                Prototype = "KitchenMeal",
                Target = "FoodBurgerCheese",
                Expires = DateTime.UtcNow + TimeSpan.FromHours(1),
            });

            var cook = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            var wrong = entMan.SpawnEntity("FoodPieApple", testMap.GridCoords);
            var burger = entMan.SpawnEntity("FoodBurgerCheese", testMap.GridCoords);

            var wrongEv = new InteractUsingEvent(cook, wrong, boardUid, testMap.GridCoords);
            entMan.EventBus.RaiseLocalEvent(boardUid, wrongEv);
            Assert.That(wrongEv.Handled, Is.False, "the wrong dish isn't taken");
            Assert.That(board.Orders, Has.Count.EqualTo(1));

            var ev = new InteractUsingEvent(cook, burger, boardUid, testMap.GridCoords);
            entMan.EventBus.RaiseLocalEvent(boardUid, ev);
            Assert.That(ev.Handled, "the wanted dish is taken");
            Assert.That(board.Orders, Is.Empty, "the order is done");
            Assert.That(entMan.IsQueuedForDeletion(burger) || entMan.Deleted(burger), "the dish is handed over");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OrdersSurviveSaveAndLoad()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var workOrders = entMan.System<WorkOrderSystem>();
        var snapshots = entMan.System<SectorSnapshotSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            Floor(pair, testMap.Grid, 5);
            var boardUid = entMan.SpawnEntity("ComputerWorkOrderBoard", testMap.GridCoords);
            var board = entMan.GetComponent<WorkOrderBoardComponent>(boardUid);
            board.Allowed.Add("CleanStation");
            board.BaseOpen = 1; // One order to look at; a board fills to its base number at once.
            board.MaxOpen = 1;
            workOrders.UpdateBoard((boardUid, board), DateTime.UtcNow);
            var order = board.Orders.Single();

            var loadedMap = Persistence.RoundTripTest.SaveAndLoad(entMan, testMap.MapUid,
                new ResPath("/kessler-roundtrip-test-work-orders.yml"));
            var tree = snapshots.CollectTree([loadedMap]).ToList();
            var loadedBoard = tree.Single(entMan.HasComponent<WorkOrderBoardComponent>);
            var loaded = entMan.GetComponent<WorkOrderBoardComponent>(loadedBoard).Orders.Single();

            Assert.Multiple(() =>
            {
                Assert.That(loaded.Prototype, Is.EqualTo(order.Prototype));
                Assert.That(loaded.MessesLeft, Is.EqualTo(order.MessesLeft));
                Assert.That(loaded.Expires, Is.EqualTo(order.Expires).Within(TimeSpan.FromSeconds(1)));
                var loadedMesses = tree.Where(entMan.HasComponent<WorkOrderMessComponent>).ToList();
                Assert.That(loadedMesses, Has.Count.EqualTo(order.MessesTotal));
                Assert.That(loadedMesses.All(m => entMan.GetComponent<WorkOrderMessComponent>(m).Board == loadedBoard),
                    "loaded messes point at the loaded board");
            });
        });

        await pair.CleanReturnAsync();
    }
}
