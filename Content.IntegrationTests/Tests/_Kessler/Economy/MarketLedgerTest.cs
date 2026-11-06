#pragma warning disable RA0002 // Tests read component state directly.
using Content.Server._Kessler.Market;
using Content.Server.Cargo.Systems;
using Content.Shared._Kessler.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Kessler.Economy;

/// <summary>
/// The market ledger: goods sell by category, a station flooded with one category pays less for more, it recovers over
/// real time, and goods packed in a crate are priced as those goods.
/// </summary>
[TestFixture]
public sealed class MarketLedgerTest
{
    [Test]
    public async Task SaturationLowersAndRecovers()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var ledger = entMan.System<MarketLedgerSystem>();
        var pricing = entMan.System<PricingSystem>();
        var containers = entMan.System<SharedContainerSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var halfLife = cfg.GetCVar(KesslerCVars.CargoMarketHalfLifeHours);
            cfg.SetCVar(KesslerCVars.CargoMarketHalfLifeHours, 1f);
            try
            {
                var ore = entMan.SpawnEntity("SteelOre", testMap.GridCoords);
                var sheet = entMan.SpawnEntity("SheetSteel", testMap.GridCoords);
                var tomato = entMan.SpawnEntity("FoodTomato", testMap.GridCoords);
                var crate = entMan.SpawnEntity("CrateGenericSteel", testMap.GridCoords);

                Assert.That(ledger.GetCategory(ore)?.ID, Is.EqualTo("Ore"));
                Assert.That(ledger.GetCategory(sheet)?.ID, Is.EqualTo("Materials"));
                Assert.That(ledger.GetCategory(tomato)?.ID, Is.EqualTo("Produce"));
                Assert.That(ledger.GetCategory(crate), Is.Null, "a crate is no category of goods");

                var now = new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);
                var basePrice = pricing.GetPrice(ore);
                Assert.That(basePrice, Is.GreaterThan(0));

                // Uncategorised goods sell at their normal price.
                Assert.That(ledger.Price(ledger.BeginQuote(crate, now), crate), Is.EqualTo(pricing.GetPrice(crate)).Within(0.01));

                // A fresh market pays about the normal price, scaled by its demand.
                var quote = ledger.BeginQuote(crate, now);
                var first = ledger.Price(quote, ore);
                Assert.That(first / basePrice, Is.InRange(MarketLedgerSystem.MinDemand * 0.99, MarketLedgerSystem.MaxDemand));

                // Selling a lot in one go pays less and less.
                var last = first;
                for (var i = 0; i < 300; i++)
                {
                    last = ledger.Price(quote, ore);
                }
                Assert.That(last, Is.LessThan(first * 0.6), "a flooded market pays less");

                // Nothing is recorded until the sale goes through.
                Assert.That(ledger.Price(ledger.BeginQuote(crate, now), ore), Is.EqualTo(first).Within(first * 0.01));

                ledger.Commit(quote);
                var saturated = ledger.Price(ledger.BeginQuote(crate, now), ore);
                Assert.That(saturated, Is.LessThan(first * 0.6), "the sale saturates the station's market");

                // Ore packed in a crate is still priced as ore.
                var emptyCrate = pricing.GetPrice(crate);
                Assert.That(containers.TryGetContainer(crate, "entity_storage", out var storage));
                var packed = entMan.SpawnEntity("SteelOre", testMap.GridCoords);
                Assert.That(containers.Insert(packed, storage!));
                var crateQuote = ledger.Price(ledger.BeginQuote(crate, now), crate);
                Assert.That(crateQuote - emptyCrate, Is.EqualTo(saturated).Within(saturated * 0.01 + 0.01), "a crate doesn't hide the ore inside");

                // Other categories aren't affected.
                Assert.That(ledger.Price(ledger.BeginQuote(crate, now), sheet) / pricing.GetPrice(sheet),
                    Is.InRange(MarketLedgerSystem.MinDemand * 0.99, MarketLedgerSystem.MaxDemand));

                // After five half-lives (before demand drifts a step) the price has nearly recovered.
                var later = ledger.Price(ledger.BeginQuote(crate, now + TimeSpan.FromHours(5)), ore);
                Assert.That(later, Is.GreaterThan(first * 0.9), "saturation decays in real time");
            }
            finally
            {
                cfg.SetCVar(KesslerCVars.CargoMarketHalfLifeHours, halfLife);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DemandDriftsWithinBounds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var ledger = entMan.System<MarketLedgerSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var point = entMan.SpawnEntity("CrateGenericSteel", testMap.GridCoords);
            var market = ledger.GetMarket(point);
            Assert.That(market, Is.Not.Null);

            var now = new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var start = ledger.GetDemand(market!.Value, "Ore", now);
            Assert.That(ledger.GetDemand(market.Value, "Ore", now + TimeSpan.FromHours(1)), Is.EqualTo(start), "demand holds between steps");

            for (var day = 1; day <= 200; day++)
            {
                var demand = ledger.GetDemand(market.Value, "Ore", now + TimeSpan.FromDays(day));
                Assert.That(demand, Is.InRange(MarketLedgerSystem.MinDemand, MarketLedgerSystem.MaxDemand));
            }
        });

        await pair.CleanReturnAsync();
    }
}
