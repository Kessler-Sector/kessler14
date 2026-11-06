#pragma warning disable RA0002 // Tests set component state directly.
using System.Linq;
using Content.Server._Kessler.Contracts;
using Content.Server._NF.Cargo.Systems;
using Content.Server.Preferences.Managers;
using System.Collections.Generic;
using Content.Shared.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Mind;
using Content.Shared.Preferences;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Kessler.Economy;

/// <summary>
/// Procurement contracts: stations with a pallet console post them, a character accepts up to two, selling the goods on
/// the issuing station's pallets fills them, and the last delivery pays the reward into the bank.
/// </summary>
[TestFixture]
public sealed class ContractTest
{
    [Test]
    public async Task AcceptDeliverAndGetPaid()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var prefsManager = server.ResolveDependency<IServerPreferencesManager>();
        var contracts = entMan.System<ContractSystem>();
        var stations = entMan.System<StationSystem>();
        var stacks = entMan.System<SharedStackSystem>();
        var mind = entMan.System<SharedMindSystem>();
        var testMap = await pair.CreateTestMap();
        var session = pair.Player!;

        EntityUid hauler = default;
        EntityUid station = default;
        var slot = 0;
        var before = 0;
        await server.WaitPost(() =>
        {
            Assert.That(prefsManager.TryGetCachedPreferences(session.UserId, out var prefs));
            slot = prefs!.SelectedCharacterIndex;
            before = ((HumanoidCharacterProfile) prefs.Characters[slot]).BankBalance;

            hauler = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            entMan.AddComponent<BankAccountComponent>(hauler).CharacterSlot = slot;
            mind.TransferTo(mind.CreateMind(session.UserId, "Hauler"), hauler);

            station = entMan.Spawn(null);
            entMan.AddComponent<StationDataComponent>(station);
            stations.AddGridToStation(station, testMap.Grid);
            entMan.SpawnEntity("ComputerPalletConsoleNFNormalMarket", testMap.GridCoords);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var now = DateTime.UtcNow;
            Assert.That(contracts.IssuerStations(), Does.Contain(station), "a station with a pallet console issues contracts");

            contracts.Refresh(now);
            var board = entMan.GetComponent<StationContractsComponent>(station);
            var posted = board.Contracts.Count;
            Assert.That(posted, Is.GreaterThanOrEqualTo(1), "the station posts a contract (more if its stocks are low)");
            contracts.Refresh(now);
            Assert.That(board.Contracts, Has.Count.EqualTo(posted), "and waits before posting another");

            board.Contracts.Clear();
            var steel = contracts.Post((station, board), now, "ContractSteel")!;
            var second = contracts.Post((station, board), now, "ContractGlass")!;
            var third = contracts.Post((station, board), now, "ContractGlass")!;

            Assert.That(contracts.Accept(hauler, station, steel.Id, now), Is.Null);
            Assert.That(steel.Held);
            Assert.That(contracts.Accept(hauler, station, steel.Id, now), Is.EqualTo("kessler-contracts-gone"), "a held contract can't be taken again");
            Assert.That(contracts.Accept(hauler, station, second.Id, now), Is.Null);
            Assert.That(contracts.Accept(hauler, station, third.Id, now), Is.EqualTo("kessler-contracts-too-many"), "two at a time");

            var state = contracts.BuildState(hauler, now);
            Assert.That(state.Held, Has.Count.EqualTo(2));
            Assert.That(state.Open.Select(e => e.Id), Is.EquivalentTo(new[] { third.Id }));

            // Selling part of the steel counts towards the contract.
            HashSet<EntityUid> Sheets(string proto, int count)
            {
                var set = new HashSet<EntityUid>();
                while (count > 0)
                {
                    var stack = entMan.SpawnEntity(proto, testMap.GridCoords);
                    var size = Math.Min(count, 50);
                    stacks.SetCount(stack, size);
                    set.Add(stack);
                    count -= size;
                }
                return set;
            }

            var goods = Sheets("SheetSteel", steel.Amount - 10);
            goods.UnionWith(Sheets("SheetGlass", 5));
            var sale = new NFEntitySoldEvent(goods, testMap.Grid, hauler);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref sale);
            Assert.That(steel.Delivered, Is.EqualTo(steel.Amount - 10));
            Assert.That(second.Delivered, Is.EqualTo(5), "each contract counts its own goods");

            // Someone else's sale doesn't count.
            var stranger = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            var more = entMan.SpawnEntity("SheetSteel", testMap.GridCoords);
            stacks.SetCount(more, 30);
            var strangerSale = new NFEntitySoldEvent(new HashSet<EntityUid> { more }, testMap.Grid, stranger);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref strangerSale);
            Assert.That(steel.Delivered, Is.EqualTo(steel.Amount - 10));

            // The last of it completes the contract and pays.
            var rest = entMan.SpawnEntity("SheetSteel", testMap.GridCoords);
            stacks.SetCount(rest, 30);
            var finalSale = new NFEntitySoldEvent(new HashSet<EntityUid> { rest }, testMap.Grid, hauler);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref finalSale);
            Assert.That(board.Contracts, Does.Not.Contain(steel), "a completed contract is gone");

            Assert.That(prefsManager.TryGetCachedPreferences(session.UserId, out var prefs));
            Assert.That(((HumanoidCharacterProfile) prefs!.Characters[slot]).BankBalance, Is.EqualTo(before + steel.Reward), "the reward goes to the bank");

            // Abandoning drops it; running out of time drops it too.
            Assert.That(contracts.Abandon(hauler, station, second.Id));
            Assert.That(board.Contracts, Does.Not.Contain(second));
            Assert.That(contracts.Accept(hauler, station, third.Id, now), Is.Null);
            contracts.Refresh(now + TimeSpan.FromHours(3));
            Assert.That(board.Contracts, Does.Not.Contain(third), "an expired contract drops without penalty");
        });

        await server.WaitPost(() => entMan.DeleteEntity(hauler));
        await pair.CleanReturnAsync();
    }
}
