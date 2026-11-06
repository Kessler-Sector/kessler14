#pragma warning disable RA0002 // Tests read component state directly.
using System.Linq;
using Content.Server._Kessler.Contracts;
using Content.Server._Kessler.Economy;
using Content.Server._Kessler.Market;
using Content.Server.Station.Systems;
using Content.Shared._Kessler.Economy;
using Content.Shared._Kessler.Market;
using Content.Shared.Stacks;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Kessler.Economy;

/// <summary>
/// Station needs: stocks drain over real time, sold goods refill them, and a low stock raises prices for its goods and
/// gets a contract posted.
/// </summary>
[TestFixture]
public sealed class StationNeedsTest
{
    [Test]
    public async Task NeedsDrainRefillAndPostContracts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var needs = entMan.System<StationNeedsSystem>();
        var contracts = entMan.System<ContractSystem>();
        var ledger = entMan.System<MarketLedgerSystem>();
        var stations = entMan.System<StationSystem>();
        var stacks = entMan.System<SharedStackSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var station = entMan.Spawn(null);
            entMan.AddComponent<StationDataComponent>(station);
            stations.AddGridToStation(station, testMap.Grid);
            var console = entMan.SpawnEntity("ComputerPalletConsoleNFNormalMarket", testMap.GridCoords);

            var materials = protoMan.Index<StationNeedPrototype>("NeedMaterials");
            var now = new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            foreach (var need in needs.Needs)
            {
                needs.GetLevel(station, need, now);
            }

            var start = needs.GetLevel(station, materials, now);
            Assert.That(start, Is.InRange(materials.Capacity * 0.3f, materials.Capacity * 0.9f), "a new station starts part-stocked");

            var later = needs.GetLevel(station, materials, now + TimeSpan.FromHours(2));
            Assert.That(later, Is.EqualTo(Math.Max(0, start - materials.DrainPerHour * 2)).Within(0.01), "stocks drain over real time");

            // Empty: the station pays more for materials and posts contracts for every low stock.
            var empty = now + TimeSpan.FromDays(10);
            foreach (var need in needs.Needs)
            {
                Assert.That(needs.GetLevel(station, need, empty), Is.Zero);
            }

            var category = protoMan.Index<MarketCategoryPrototype>("Materials");
            Assert.That(needs.DemandBoost(station, "Materials", empty), Is.EqualTo(1f + materials.MaxDemandBoost).Within(0.001));
            var market = ledger.GetMarket(console)!.Value;
            var shortPrice = ledger.Multiplier(market, category, empty);

            contracts.Refresh(empty);
            var board = entMan.GetComponent<StationContractsComponent>(station);
            foreach (var need in needs.Needs)
            {
                Assert.That(board.Contracts.Any(c => need.Contracts.Contains(c.Template)), $"a contract is posted for {need.ID}");
            }

            // Selling materials refills the stock and the price falls back.
            var sheets = entMan.SpawnEntity("SheetSteel", testMap.GridCoords);
            for (var i = 0; i < 12; i++)
            {
                stacks.SetCount(sheets, 50);
                needs.RestockFrom(station, sheets, empty);
            }

            Assert.That(needs.GetLevel(station, materials, empty), Is.EqualTo(materials.Capacity).Within(0.01), "sold goods refill the stock, up to full");
            Assert.That(needs.DemandBoost(station, "Materials", empty), Is.EqualTo(1f));
            Assert.That(ledger.Multiplier(market, category, empty), Is.LessThan(shortPrice), "a full stock pays normal prices again");
            Assert.That(needs.IsLow(station, materials, empty), Is.False);
        });

        await pair.CleanReturnAsync();
    }
}
