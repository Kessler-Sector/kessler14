using Content.Server.Chat;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Kessler.Persistence;

/// <summary>
/// Times meaning "never" were TimeSpan.MaxValue, and adding the paused time to them on unpause overflowed and threw.
/// Persistent loads and stored ships use paused maps, and deleting a paused map unpauses what is on it.
/// </summary>
[TestFixture]
public sealed class PausedTimeTest
{
    [Test]
    public async Task UnpausingWithNoEmoteDueDoesNotThrow()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            var mob = entMan.SpawnEntity(null, testMap.GridCoords);
            var autoEmote = entMan.AddComponent<AutoEmoteComponent>(mob);
            Assert.That(autoEmote.NextEmoteTime, Is.EqualTo(TimeSpan.MaxValue), "nothing is due");
            mapSys.SetPaused(testMap.MapUid, true);
        });
        await pair.RunTicksSync(10);

        // Any error logged here fails the test.
        await server.WaitPost(() => mapSys.SetPaused(testMap.MapUid, false));
        await pair.RunTicksSync(1);

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Deleting a loaded (paused) map unpauses what is on it. A portable flasher's proximity trigger threw there in CI.
    /// </summary>
    [Test]
    public async Task DeletingALoadedMapDoesNotThrow()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();

        await server.WaitPost(() => entMan.SpawnEntity("PortableFlasher", testMap.GridCoords));
        await pair.RunTicksSync(5);

        EntityUid loadedMap = default;
        await server.WaitPost(() => loadedMap = RoundTripTest.SaveAndLoad(entMan, testMap.MapUid,
            new Robust.Shared.Utility.ResPath("/kessler-roundtrip-test-flasher.yml")));
        await pair.RunTicksSync(30);

        // Any error logged here fails the test.
        await server.WaitPost(() => entMan.DeleteEntity(loadedMap));
        await pair.RunTicksSync(1);

        await pair.CleanReturnAsync();
    }
}
