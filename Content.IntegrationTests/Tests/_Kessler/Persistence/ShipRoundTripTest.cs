using Robust.Shared.IoC;
using Robust.Shared.Configuration;
using Content.Shared._Kessler.CCVar;
using System.Collections.Generic;
using System.Linq;
using Content.Server._Kessler.Persistence;
using Content.Server._NF.Bank;
using Content.Server._NF.ShuttleRecords.Components;
using Content.Server._NF.Shipyard.Systems;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.StationRecords.Systems;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Events;
using Content.Shared._NF.Bank.BUI;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.PDA;
using Content.Shared.Shuttles.Components;
using Content.Shared.Station.Components;
using Content.Shared.StationRecords;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Kessler.Persistence;

/// <summary>
/// Buys ships through a real shipyard console on the Dusk station, saves the sector, deletes the original (as a
/// restart would), loads the save and checks that the ships, their stations, deeds, records, IFF and docks survive and
/// that a loaded ship can still be flown, docked and sold.
/// </summary>
[TestFixture]
public sealed class ShipRoundTripTest
{
    private const int ShipCount = 3;

    private sealed record ShipInfo(
        string Name,
        EntityUid Grid,
        EntityUid Station,
        EntityUid DeedHolder,
        IFFFlags IffFlags,
        int StationRecords,
        int Docks,
        int TileCount);

    [Test]
    public async Task PurchasedShipsRoundTrip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false,
            Connected = true,
            Dirty = true,
            Map = "Dusk",
        });
        var server = pair.Server;
        var entMan = server.EntMan;
        var ticker = entMan.System<GameTicker>();
        var shipyard = entMan.System<ShipyardSystem>();
        var itemSlots = entMan.System<ItemSlotsSystem>();
        var ui = entMan.System<UserInterfaceSystem>();
        var bank = entMan.System<BankSystem>();
        var inventory = entMan.System<InventorySystem>();
        var map = entMan.System<SharedMapSystem>();
        var records = entMan.System<StationRecordsSystem>();
        var persistenceSave = entMan.System<PersistenceSaveSystem>();
        var persistenceLoad = entMan.System<PersistenceLoadSystem>();
        var shuttles = entMan.System<ShuttleSystem>();

        await pair.RunTicksSync(10);

        var before = new List<ShipInfo>();
        var path = new ResPath("/kessler-ship-roundtrip.yml");
        var shuttleRecordsBefore = 0;
        var crateChildrenBefore = 0;
        var sectorBalancesBefore = new Dictionary<SectorBankAccount, int>();

        await server.WaitPost(() =>
        {
            var player = server.PlayerMan.Sessions.Single().AttachedEntity!.Value;
            Assert.That(bank.TryBankDeposit(player, 10_000_000), "Could not fund the test player's bank account");

            var console = FindConsole(entMan);
            var consoleComp = entMan.GetComponent<ShipyardConsoleComponent>(console);
            entMan.System<SharedTransformSystem>().SetCoordinates(player,
                entMan.GetComponent<TransformComponent>(console).Coordinates);
            ui.OpenUi(console, ShipyardConsoleUiKey.Shipyard, player);

            // The first ship is bought with the player character's own ID, the others with spare ID cards.
            Assert.That(inventory.TryGetSlotEntity(player, "id", out var pda));
            var ids = new List<EntityUid> { entMan.GetComponent<PdaComponent>(pda!.Value).ContainedId!.Value };
            for (var i = 1; i < ShipCount; i++)
                ids.Add(entMan.SpawnEntity("PassengerIDCard", entMan.GetComponent<TransformComponent>(player).Coordinates));

            var vessels = shipyard.GetAvailableShuttles(console, ShipyardConsoleUiKey.Shipyard, targetId: ids[0]).available;
            Assert.That(vessels, Has.Count.GreaterThanOrEqualTo(ShipCount));

            var bought = new HashSet<string>();
            foreach (var id in ids)
            {
                // Different vessels; the purchase handler picks the vessel by prototype id.
                var vessel = vessels.First(v => !bought.Contains(v));
                bought.Add(vessel);

                Assert.That(itemSlots.TryInsert(console, consoleComp.TargetIdSlot, id, player));
                // These tests are about saving ships, not pilot licences.
                var cfg = IoCManager.Resolve<IConfigurationManager>();
                var enforced = cfg.GetCVar(KesslerCVars.LicencesEnforced);
                cfg.SetCVar(KesslerCVars.LicencesEnforced, false);
                entMan.EventBus.RaiseLocalEvent(console,
                    new ShipyardConsolePurchaseMessage(vessel) { Actor = player, UiKey = ShipyardConsoleUiKey.Shipyard });
                cfg.SetCVar(KesslerCVars.LicencesEnforced, enforced);
                Assert.That(itemSlots.TryEject(console, consoleComp.TargetIdSlot, player, out _));

                Assert.That(entMan.TryGetComponent(id, out ShuttleDeedComponent deed) && deed.ShuttleUid != null,
                    $"Buying {vessel} did not put a deed on the ID card");
                before.Add(Describe(entMan, records, deed!.ShuttleUid!.Value, id));
            }

            // Cargo on the first ship.
            var cargoGrid = before[0].Grid;
            var gridCenter = entMan.GetComponent<MapGridComponent>(cargoGrid).LocalAABB.Center;
            var crate = entMan.SpawnEntity("CrateEngineeringToolbox", new EntityCoordinates(cargoGrid, gridCenter));
            crateChildrenBefore = entMan.GetComponent<TransformComponent>(crate).ChildCount;
            Assert.That(crateChildrenBefore, Is.GreaterThan(0));

            // Move every sector account away from its default balance.
            foreach (var account in SectorBank(entMan).Accounts.Keys.ToList())
                Assert.That(bank.TrySectorDeposit(account, 12_345, LedgerEntryType.TickingIncome));
            foreach (var (account, info) in SectorBank(entMan).Accounts)
                sectorBalancesBefore[account] = info.Balance;

            shuttleRecordsBefore = CountShuttleRecords(entMan, before.Select(s => s.Grid));
            Assert.That(shuttleRecordsBefore, Is.EqualTo(ShipCount), "Every purchase should add a shuttle record");

            var sectorMap = map.GetMap(ticker.DefaultMap);

            Assert.That(persistenceSave.TrySaveMap(sectorMap, path));

            // A restart starts from nothing: delete the sector map and every station.
            var stations = new List<EntityUid>();
            var stationQuery = entMan.EntityQueryEnumerator<StationDataComponent>();
            while (stationQuery.MoveNext(out var uid, out _))
                stations.Add(uid);
            entMan.DeleteEntity(sectorMap);
            foreach (var station in stations)
                entMan.DeleteEntity(station);
            entMan.System<SharedMindSystem>().WipeAllMinds();
        });

        await pair.RunTicksSync(5);

        var after = new List<ShipInfo>();
        EntityUid loadedMap = default;

        await server.WaitPost(() =>
        {
            var options = new MapLoadOptions { ExpectedCategory = FileCategory.Map };
            Assert.That(persistenceLoad.TryLoadSave(path, options, out var result));
            loadedMap = result!.Maps.Single().Owner;

            foreach (var ship in before)
            {
                var grid = FindShip(entMan, loadedMap, ship.Name);
                Assert.That(grid, Is.Not.Null, $"Ship {ship.Name} is missing after load");
                var holder = FindDeedHolder(entMan, grid!.Value, ship.Name);
                Assert.That(holder, Is.Not.Null, $"No ID card holds the deed for {ship.Name} after load");
                after.Add(Describe(entMan, records, grid.Value, holder!.Value));
            }

            Assert.Multiple(() =>
            {
                foreach (var (b, a) in before.Zip(after))
                {
                    Assert.That(a.IffFlags, Is.EqualTo(b.IffFlags), $"{b.Name}: IFF flags");
                    Assert.That(a.StationRecords, Is.EqualTo(b.StationRecords), $"{b.Name}: station records");
                    Assert.That(a.Docks, Is.EqualTo(b.Docks), $"{b.Name}: docked ports");
                    Assert.That(a.TileCount, Is.EqualTo(b.TileCount), $"{b.Name}: tiles");
                    Assert.That(entMan.GetComponent<StationDataComponent>(a.Station).Grids, Does.Contain(a.Grid),
                        $"{b.Name}: station does not list the grid");
                }

                Assert.That(CountShuttleRecords(entMan, after.Select(s => s.Grid)), Is.EqualTo(shuttleRecordsBefore),
                    "Shuttle records no longer point at the loaded ships");

                var crates = FindOnGrid(entMan, after[0].Grid, "CrateEngineeringToolbox");
                Assert.That(crates, Has.Count.EqualTo(1), "Cargo crate missing from the loaded ship");
                Assert.That(entMan.GetComponent<TransformComponent>(crates[0]).ChildCount, Is.EqualTo(crateChildrenBefore),
                    "Cargo crate contents changed");

                foreach (var (account, balance) in sectorBalancesBefore)
                {
                    Assert.That(SectorBank(entMan).Accounts[account].Balance, Is.EqualTo(balance),
                        $"Sector bank account {account} changed");
                }
            });
        });

        // Fly the first loaded ship away and dock it to the station again.
        var flown = after[0];
        EntityUid stationGrid = default;
        await server.WaitPost(() =>
        {
            stationGrid = FindStationGrid(entMan, loadedMap);
            shuttles.FTLToDock(flown.Grid, entMan.GetComponent<ShuttleComponent>(flown.Grid), stationGrid,
                startupTime: 0.5f, hyperspaceTime: 1f);
            Assert.That(entMan.HasComponent<FTLComponent>(flown.Grid), "Loaded ship could not start FTL");
        });

        for (var i = 0; i < 100 && entMan.HasComponent<FTLComponent>(flown.Grid); i++)
            await pair.RunTicksSync(30);

        await server.WaitPost(() =>
        {
            Assert.That(entMan.HasComponent<FTLComponent>(flown.Grid), Is.False, "FTL never finished");
            Assert.That(CountDocks(entMan, flown.Grid), Is.GreaterThan(0), "Loaded ship did not dock again after FTL");
        });

        // Sell the second loaded ship at the loaded station's shipyard.
        await server.WaitPost(() =>
        {
            var sold = after[1];
            var console = FindConsole(entMan, loadedMap);
            var station = entMan.GetComponent<StationMemberComponent>(stationGrid).Station;
            var result = shipyard.TrySellShuttle(station, sold.Grid, console, out var bill);
            Assert.That(result.Error, Is.EqualTo(ShipyardSystem.ShipyardSaleError.Success), $"Selling a loaded ship failed: {result.Error}");
            Assert.That(bill, Is.GreaterThan(0));
        });

        await pair.CleanReturnAsync();
    }

    private static ShipInfo Describe(IEntityManager entMan, StationRecordsSystem records, EntityUid grid, EntityUid holder)
    {
        var deed = entMan.GetComponent<ShuttleDeedComponent>(holder);
        Assert.That(deed.ShuttleUid, Is.EqualTo(grid), "Deed does not point at the ship");
        var station = entMan.GetComponent<StationMemberComponent>(grid).Station;
        var iff = entMan.TryGetComponent(grid, out IFFComponent iffComp) ? iffComp.Flags : IFFFlags.None;
        var tiles = 0;
        var mapSystem = entMan.System<SharedMapSystem>();
        var enumerator = mapSystem.GetAllTilesEnumerator(grid, entMan.GetComponent<MapGridComponent>(grid));
        while (enumerator.MoveNext(out _))
            tiles++;

        return new ShipInfo(
            deed.ShuttleName ?? "",
            grid,
            station,
            holder,
            iff,
            records.GetRecordsOfType<GeneralStationRecord>(station).Count(),
            CountDocks(entMan, grid),
            tiles);
    }

    private static SectorBankComponent SectorBank(IEntityManager entMan)
    {
        var query = entMan.EntityQueryEnumerator<SectorBankComponent>();
        Assert.That(query.MoveNext(out var bank), "No sector bank");
        Assert.That(query.MoveNext(out _), Is.False, "More than one sector bank");
        return bank!;
    }

    private static List<EntityUid> FindOnGrid(IEntityManager entMan, EntityUid grid, string prototype)
    {
        var found = new List<EntityUid>();
        var query = entMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var meta, out var xform))
        {
            if (meta.EntityPrototype?.ID == prototype && xform.GridUid == grid)
                found.Add(uid);
        }

        return found;
    }

    private static int CountDocks(IEntityManager entMan, EntityUid grid)
    {
        var count = 0;
        var query = entMan.EntityQueryEnumerator<DockingComponent, TransformComponent>();
        while (query.MoveNext(out _, out var dock, out var xform))
        {
            if (xform.GridUid == grid && dock.DockedWith is { } other && entMan.EntityExists(other) && dock.DockJoint != null)
                count++;
        }

        return count;
    }

    private static int CountShuttleRecords(IEntityManager entMan, IEnumerable<EntityUid> grids)
    {
        var set = grids.ToHashSet();
        var count = 0;
        var query = entMan.EntityQueryEnumerator<SectorShuttleRecordsComponent>();
        while (query.MoveNext(out _, out var comp))
        {
            foreach (var record in comp.ShuttleRecords.Values)
            {
                if (entMan.TryGetEntity(record.EntityUid, out var uid) && set.Contains(uid.Value))
                    count++;
            }
        }

        return count;
    }

    private static EntityUid FindConsole(IEntityManager entMan, EntityUid? onMap = null)
    {
        var query = entMan.EntityQueryEnumerator<ShipyardConsoleComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var xform, out var meta))
        {
            if (meta.EntityPrototype?.ID != "ComputerShipyard")
                continue;
            if (onMap != null && xform.MapUid != onMap)
                continue;
            return uid;
        }

        Assert.Fail("No civilian shipyard console found");
        return default;
    }

    private static EntityUid FindStationGrid(IEntityManager entMan, EntityUid onMap)
    {
        var console = FindConsole(entMan, onMap);
        return entMan.GetComponent<TransformComponent>(console).GridUid!.Value;
    }

    private static EntityUid? FindShip(IEntityManager entMan, EntityUid onMap, string name)
    {
        var query = entMan.EntityQueryEnumerator<ShuttleDeedComponent, MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var deed, out _, out var xform))
        {
            if (deed.ShuttleName == name && xform.MapUid == onMap)
                return uid;
        }

        return null;
    }

    private static EntityUid? FindDeedHolder(IEntityManager entMan, EntityUid grid, string name)
    {
        var query = entMan.EntityQueryEnumerator<ShuttleDeedComponent>();
        while (query.MoveNext(out var uid, out var deed))
        {
            if (uid != grid && deed.ShuttleName == name && deed.ShuttleUid == grid)
                return uid;
        }

        return null;
    }
}
