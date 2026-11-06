using Robust.Shared.IoC;
using Robust.Shared.Configuration;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Content.Server._Kessler.Persistence;
using Content.Server._NF.Bank;
using Content.Server._NF.Shipyard.Systems;
using Content.Server.Atmos.Components;
using Content.Server.GameTicking;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Kessler.CCVar;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Events;
using Content.Shared.CCVar;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.PDA;
using Content.Shared.Players;
using Content.Shared.Shuttles.Components;
using Content.Shared.Station.Components;
using Robust.Server.GameObjects;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Kessler.Persistence;

/// <summary>
/// Stage B: the persistent sector is saved at round end and loaded at round start. Covers a round restart with the
/// player still connected, and the save moved into a fresh server instance (a new clock and new systems, the closest a
/// test gets to a process restart). After each load: power, air, doors, FTL and docking, and selling at the shipyard.
/// </summary>
[TestFixture]
public sealed class PersistentSectorRestartTest
{
    private static readonly string[] SaveFiles = { "sector.yml", "sector.meta.json", "sector.snapshot.json" };

    private sealed record Baseline(string ShipName, float Powered, float Pressure, string CharacterName);

    [Test]
    public async Task SectorSurvivesRestarts()
    {
        Baseline baseline = null;
        var files = new Dictionary<string, byte[]>();

        // Round restart with the player connected.
        await using (var pair = await PoolManager.GetServerClient(new PoolSettings
                     {
                         DummyTicker = false,
                         Connected = true,
                         Dirty = true,
                         Map = "Dusk",
                     }))
        {
            var server = pair.Server;
            var entMan = server.EntMan;
            await pair.RunTicksSync(10);

            // Production generates the sector with worldgen, so the saved map carries the worldgen config and round
            // start must cope with a loaded, already configured map.
            server.CfgMan.SetCVar(CCVars.WorldgenEnabled, true);
            await server.WaitPost(() => entMan.System<GameTicker>().RestartRound());
            await pair.RunTicksSync(90);

            server.CfgMan.SetCVar(KesslerCVars.PersistenceEnabled, true);

            // The same door check on the generated sector, so a failure after a load means something.
            await CheckDoor(pair);

            await server.WaitPost(() =>
            {
                var player = server.PlayerMan.Sessions.Single().AttachedEntity!.Value;
                var ship = BuyShip(entMan, player);
                var grid = StationGrid(entMan);
                baseline = new Baseline(
                    entMan.GetComponent<ShuttleDeedComponent>(ship).ShuttleName!,
                    PoweredFraction(entMan, grid),
                    MeanPressure(entMan, grid),
                    entMan.GetComponent<MindComponent>(server.PlayerMan.Sessions.Single().GetMind()!.Value).CharacterName);

                // Saves at round end, then loads the save as the new round's sector.
                entMan.System<GameTicker>().RestartRound();
            });
            await pair.RunTicksSync(90);

            var session = server.PlayerMan.Sessions.Single();
            await server.WaitAssertion(() =>
            {
                var sector = entMan.System<PersistentSectorSystem>();
                Assert.That(entMan.System<GameTicker>().RunLevel, Is.EqualTo(GameRunLevel.InRound), "Round did not start");
                Assert.That(sector.SectorLoaded, "Round restart did not load the persistent sector");
                Assert.That(sector.LastLoadDifferences, Is.Zero, "Loaded sector differs from its save snapshot");

                Assert.That(session.AttachedEntity, Is.Not.Null);
                var mind = entMan.GetComponent<MindComponent>(session.GetMind()!.Value);
                Assert.That(mind.CharacterName, Is.EqualTo(baseline.CharacterName), "Player is not in their own body");
                Assert.That(mind.OwnedEntity, Is.EqualTo(session.AttachedEntity));
                Assert.That(entMan.System<GameTicker>().UserHasJoinedGame(session), "Player is still in the lobby");
            });

            await CheckSector(pair, baseline);

            var userData = server.ResolveDependency<IResourceManager>().UserData;
            var folder = new ResPath(server.CfgMan.GetCVar(KesslerCVars.PersistencePath)).ToRootedPath();
            foreach (var file in SaveFiles)
            {
                await using var stream = userData.OpenRead(folder / file);
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                files[file] = memory.ToArray();
            }

            await pair.CleanReturnAsync();
        }

        // The save moved into a fresh server instance.
        await using (var pair = await PoolManager.GetServerClient(new PoolSettings
                     {
                         DummyTicker = false,
                         Dirty = true,
                         Fresh = true,
                         Map = "Dusk",
                     }))
        {
            var server = pair.Server;
            var entMan = server.EntMan;
            var ticker = entMan.System<GameTicker>();

            // Wait in the lobby so the generated round is not saved over the copied save.
            server.CfgMan.SetCVar(CCVars.GameLobbyEnabled, true);
            await server.WaitPost(() => ticker.RestartRound());
            await pair.RunTicksSync(5);

            var userData = server.ResolveDependency<IResourceManager>().UserData;
            var folder = new ResPath(server.CfgMan.GetCVar(KesslerCVars.PersistencePath)).ToRootedPath();
            userData.CreateDir(folder);
            foreach (var (file, bytes) in files)
            {
                await using var stream = userData.OpenWrite(folder / file);
                await stream.WriteAsync(bytes);
            }

            server.CfgMan.SetCVar(KesslerCVars.PersistenceEnabled, true);
            server.CfgMan.SetCVar(CCVars.WorldgenEnabled, true);
            await server.WaitPost(() => ticker.StartRound());
            await pair.RunTicksSync(90);

            await server.WaitAssertion(() =>
            {
                var sector = entMan.System<PersistentSectorSystem>();
                Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));
                Assert.That(sector.SectorLoaded, "Fresh server did not load the persistent sector");
                Assert.That(sector.LastLoadDifferences, Is.Zero, "Loaded sector differs from its save snapshot");
            });

            await CheckSector(pair, baseline);

            // A round restart while the saved character's player has not connected to this server.
            await server.WaitPost(() => ticker.RestartRound());
            await pair.RunTicksSync(5);
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby));

            server.CfgMan.SetCVar(KesslerCVars.PersistenceEnabled, false);
            server.CfgMan.SetCVar(CCVars.WorldgenEnabled, false);
            await pair.CleanReturnAsync();
        }
    }

    /// <summary>
    /// Checks a loaded sector: power and air match the baseline, a door opens and closes, the bought ship flies to
    /// the station and docks, and it can be sold.
    /// </summary>
    private static async Task CheckSector(Pair.TestPair pair, Baseline baseline)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        EntityUid ship = default;

        await CheckDoor(pair);

        await server.WaitPost(() =>
        {
            var grid = StationGrid(entMan);
            Assert.Multiple(() =>
            {
                Assert.That(PoweredFraction(entMan, grid), Is.GreaterThanOrEqualTo(baseline.Powered - 0.05f), "Power");
                Assert.That(MeanPressure(entMan, grid), Is.EqualTo(baseline.Pressure).Within(5).Percent, "Air pressure");
            });

            ship = FindShip(entMan, baseline.ShipName);
            var shuttles = entMan.System<ShuttleSystem>();
            shuttles.FTLToDock(ship, entMan.GetComponent<ShuttleComponent>(ship), grid, startupTime: 0.5f, hyperspaceTime: 1f);
            Assert.That(entMan.HasComponent<FTLComponent>(ship), "Ship could not start FTL");
        });

        for (var i = 0; i < 100 && entMan.HasComponent<FTLComponent>(ship); i++)
            await pair.RunTicksSync(30);

        await server.WaitPost(() =>
        {
            Assert.That(entMan.HasComponent<FTLComponent>(ship), Is.False, "FTL never finished");
            Assert.That(CountDocks(entMan, ship), Is.GreaterThan(0), "Ship did not dock after FTL");

            var console = FindConsole(entMan);
            var station = entMan.GetComponent<StationMemberComponent>(StationGrid(entMan)).Station;
            var result = entMan.System<ShipyardSystem>().TrySellShuttle(station, ship, console, out _);
            Assert.That(result.Error, Is.EqualTo(ShipyardSystem.ShipyardSaleError.Success), $"Selling failed: {result.Error}");
        });
    }

    /// <summary>
    /// Opens and closes a powered airlock on the station. Airlocks close on their own after a few seconds, so the
    /// state is checked right after each step.
    /// </summary>
    private static async Task CheckDoor(Pair.TestPair pair)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var doors = entMan.System<SharedDoorSystem>();
        EntityUid door = default;

        await server.WaitPost(() =>
        {
            door = FindDoor(entMan, StationGrid(entMan));
            Assert.That(doors.TryOpen(door), "Door does not open");
        });
        await pair.RunTicksSync(45);
        await server.WaitPost(() =>
        {
            Assert.That(entMan.GetComponent<DoorComponent>(door).State, Is.EqualTo(DoorState.Open), "Door did not open");
            Assert.That(doors.TryClose(door), "Door does not close");
        });
        await pair.RunTicksSync(45);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<DoorComponent>(door).State, Is.EqualTo(DoorState.Closed), "Door did not close");
        });
    }

    private static EntityUid BuyShip(IEntityManager entMan, EntityUid player)
    {
        var shipyard = entMan.System<ShipyardSystem>();
        var itemSlots = entMan.System<ItemSlotsSystem>();
        Assert.That(entMan.System<BankSystem>().TryBankDeposit(player, 10_000_000));

        var console = FindConsole(entMan);
        var consoleComp = entMan.GetComponent<ShipyardConsoleComponent>(console);
        entMan.System<SharedTransformSystem>().SetCoordinates(player, entMan.GetComponent<TransformComponent>(console).Coordinates);
        entMan.System<UserInterfaceSystem>().OpenUi(console, ShipyardConsoleUiKey.Shipyard, player);

        Assert.That(entMan.System<InventorySystem>().TryGetSlotEntity(player, "id", out var pda));
        var id = entMan.GetComponent<PdaComponent>(pda!.Value).ContainedId!.Value;
        var vessel = shipyard.GetAvailableShuttles(console, ShipyardConsoleUiKey.Shipyard, targetId: id).available.First();

        Assert.That(itemSlots.TryInsert(console, consoleComp.TargetIdSlot, id, player));
        // These tests are about saving ships, not pilot licences.
        var cfg = IoCManager.Resolve<IConfigurationManager>();
        var enforced = cfg.GetCVar(KesslerCVars.LicencesEnforced);
        cfg.SetCVar(KesslerCVars.LicencesEnforced, false);
        entMan.EventBus.RaiseLocalEvent(console,
            new ShipyardConsolePurchaseMessage(vessel) { Actor = player, UiKey = ShipyardConsoleUiKey.Shipyard });
        cfg.SetCVar(KesslerCVars.LicencesEnforced, enforced);
        Assert.That(itemSlots.TryEject(console, consoleComp.TargetIdSlot, player, out _));

        var deed = entMan.GetComponent<ShuttleDeedComponent>(id);
        Assert.That(deed.ShuttleUid, Is.Not.Null, $"Buying {vessel} failed");
        return deed.ShuttleUid!.Value;
    }

    private static float PoweredFraction(IEntityManager entMan, EntityUid grid)
    {
        int total = 0, powered = 0;
        var query = entMan.EntityQueryEnumerator<ApcPowerReceiverComponent, TransformComponent>();
        while (query.MoveNext(out var receiver, out var xform))
        {
            if (xform.GridUid != grid)
                continue;
            total++;
            if (receiver.Powered)
                powered++;
        }

        Assert.That(total, Is.GreaterThan(0));
        return (float) powered / total;
    }

    /// <summary>
    /// Mean pressure of the station's air-filled tiles.
    /// </summary>
    private static float MeanPressure(IEntityManager entMan, EntityUid grid)
    {
        var atmos = entMan.GetComponent<GridAtmosphereComponent>(grid);
        var pressures = atmos.Tiles.Values
            .Where(t => !t.Space && t.Air is { } air && air.Pressure > 10f)
            .Select(t => t.Air!.Pressure)
            .ToList();
        Assert.That(pressures, Is.Not.Empty);
        return pressures.Average();
    }

    private static EntityUid FindDoor(IEntityManager entMan, EntityUid grid)
    {
        var query = entMan.EntityQueryEnumerator<AirlockComponent, DoorComponent, ApcPowerReceiverComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var door, out var power, out var xform))
        {
            if (xform.GridUid != grid || door.State != DoorState.Closed || !power.Powered)
                continue;
            if (entMan.TryGetComponent(uid, out DoorBoltComponent bolts) && bolts.BoltsDown)
                continue;
            return uid;
        }

        Assert.Fail("No powered, closed, unbolted airlock on the station");
        return default;
    }

    private static int CountDocks(IEntityManager entMan, EntityUid grid)
    {
        var count = 0;
        var query = entMan.EntityQueryEnumerator<DockingComponent, TransformComponent>();
        while (query.MoveNext(out var dock, out var xform))
        {
            if (xform.GridUid == grid && dock.DockedWith is { } other && entMan.EntityExists(other) && dock.DockJoint != null)
                count++;
        }

        return count;
    }

    private static EntityUid FindConsole(IEntityManager entMan)
    {
        var defaultMap = entMan.System<SharedMapSystem>().GetMap(entMan.System<GameTicker>().DefaultMap);
        var query = entMan.EntityQueryEnumerator<ShipyardConsoleComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var xform, out var meta))
        {
            if (meta.EntityPrototype?.ID == "ComputerShipyard" && xform.MapUid == defaultMap)
                return uid;
        }

        Assert.Fail("No civilian shipyard console on the sector map");
        return default;
    }

    private static EntityUid StationGrid(IEntityManager entMan)
    {
        return entMan.GetComponent<TransformComponent>(FindConsole(entMan)).GridUid!.Value;
    }

    private static EntityUid FindShip(IEntityManager entMan, string name)
    {
        var query = entMan.EntityQueryEnumerator<ShuttleDeedComponent, MapGridComponent>();
        while (query.MoveNext(out var uid, out var deed, out _))
        {
            if (deed.ShuttleName == name)
                return uid;
        }

        Assert.Fail($"Ship {name} not found after load");
        return default;
    }
}
