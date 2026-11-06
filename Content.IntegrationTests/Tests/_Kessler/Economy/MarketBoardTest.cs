using System.Linq;
using Content.Server._Kessler.Market;
using Content.Server.CartridgeLoader;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Kessler.Economy;

/// <summary>
/// The market board PDA program lists each market with a pallet console and its rates, and comes on every PDA.
/// </summary>
[TestFixture]
public sealed class MarketBoardTest
{
    [Test]
    public async Task ListsMarketsWithPalletConsoles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var board = entMan.System<MarketBoardCartridgeSystem>();
        var ledger = entMan.System<MarketLedgerSystem>();
        var loader = entMan.System<CartridgeLoaderSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var pda = entMan.SpawnEntity("PassengerPDA", testMap.GridCoords);
            Assert.That(loader.GetInstalled(pda).Any(p => entMan.HasComponent<MarketBoardCartridgeComponent>(p)),
                "the market board comes preinstalled");

            var empty = board.BuildState(pda);
            Assert.That(empty.Stations, Is.Empty, "no pallet console, no market");

            entMan.SpawnEntity("ComputerPalletConsoleNFNormalMarket", testMap.GridCoords);
            var state = board.BuildState(pda);
            Assert.That(state.Stations, Has.Count.EqualTo(1));
            Assert.That(state.Stations[0].Rates, Has.Count.EqualTo(ledger.Categories.Count()), "every category is listed");
            Assert.That(state.Here, Is.EqualTo(state.Stations[0].Market), "the PDA's own market is marked");
            Assert.That(state.Stations[0].Rates.All(r => r.Percent is >= 14 and <= 130));
        });

        await pair.CleanReturnAsync();
    }
}
