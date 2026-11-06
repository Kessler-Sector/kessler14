#pragma warning disable RA0002 // Tests set component state directly.
using System.Linq;
using Content.Server._Kessler.Economy;
using Content.Server._Kessler.Persistence;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Kessler.Economy;

/// <summary>
/// Selling a lot of one gas at one station pays less and less, other gases aren't affected, the market recovers over
/// real time, and the saturation is saved.
/// </summary>
[TestFixture]
public sealed class GasMarketTest
{
    [Test]
    public async Task FloodingAStationLowersItsPrice()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var market = entMan.System<GasMarketSystem>();
        var atmos = entMan.System<AtmosphereSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var holder = entMan.SpawnEntity(null, testMap.GridCoords);
            var comp = entMan.AddComponent<StationGasMarketComponent>(holder);
            var now = DateTime.UtcNow;

            GasMixture Plasma(float moles)
            {
                var mix = new GasMixture();
                mix.AdjustMoles(Gas.Plasma, moles);
                return mix;
            }

            var flat = atmos.GetPrice(Plasma(50000));
            var first = market.PriceAt(comp, Plasma(50000), commit: true, now);
            var second = market.PriceAt(comp, Plasma(50000), commit: true, now);

            Assert.Multiple(() =>
            {
                Assert.That(first / flat, Is.EqualTo(Math.Log(2)).Within(0.01), "the first market-depth of gas pays 69%");
                Assert.That(second, Is.LessThan(first), "selling more of the same gas pays less");

                var oxygen = new GasMixture();
                oxygen.AdjustMoles(Gas.Oxygen, 1000);
                Assert.That(market.PriceAt(comp, oxygen, commit: false, now) / atmos.GetPrice(oxygen),
                    Is.EqualTo(1).Within(0.02), "other gases aren't affected");

                var rightAway = market.PriceAt(comp, Plasma(50000), commit: false, now);
                var dayLater = market.PriceAt(comp, Plasma(50000), commit: false, now + TimeSpan.FromDays(1));
                var weekLater = market.PriceAt(comp, Plasma(50000), commit: false, now + TimeSpan.FromDays(7));
                Assert.That(rightAway, Is.LessThan(second), "a third load pays less still");
                Assert.That(dayLater, Is.GreaterThan(rightAway), "the market recovers");
                Assert.That(weekLater / first, Is.EqualTo(1).Within(0.02), "after a week it has all but recovered");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SaturationIsSaved()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var market = entMan.System<GasMarketSystem>();
        var snapshots = entMan.System<SectorSnapshotSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var holder = entMan.SpawnEntity(null, testMap.GridCoords);
            var comp = entMan.AddComponent<StationGasMarketComponent>(holder);
            var mix = new GasMixture();
            mix.AdjustMoles(Gas.Plasma, 30000);
            var now = DateTime.UtcNow;
            market.PriceAt(comp, mix, commit: true, now);

            var loadedMap = Persistence.RoundTripTest.SaveAndLoad(entMan, testMap.MapUid,
                new ResPath("/kessler-roundtrip-test-gas-market.yml"));
            var loaded = snapshots.CollectTree([loadedMap])
                .Select(uid => entMan.TryGetComponent<StationGasMarketComponent>(uid, out var c) ? c : null)
                .Single(c => c != null)!;

            var halfLife = TimeSpan.FromHours(24);
            Assert.That(market.GetSaturation(loaded, (int) Gas.Plasma, now, halfLife), Is.EqualTo(30000).Within(1));
        });

        await pair.CleanReturnAsync();
    }
}
