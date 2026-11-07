using Content.Server.Chat;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Kessler.Persistence;

/// <summary>
/// Unpausing a map with a mob that has no emote due used to overflow its "never" time and throw. Persistent loads and
/// stored ships use paused maps, and deleting a paused map unpauses what is on it.
/// </summary>
[TestFixture]
public sealed class AutoEmotePauseTest
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
}
