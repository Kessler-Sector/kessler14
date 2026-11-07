#pragma warning disable RA0002 // Tests read component state directly.
using System.Linq;
using Content.Server._Kessler.Persistence;
using Content.Shared.Silicons.StationAi;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Kessler.Persistence;

/// <summary>
/// A ship AI's remote eye is saved with its core. It used to be left out, so a loaded core pointed at an invalid
/// entity and never made a new eye: the AI could no longer look around its ship after a restart.
/// </summary>
[TestFixture]
public sealed class ShipAiRoundTripTest
{
    [Test]
    public async Task CoreKeepsItsEye()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var snapshots = entMan.System<SectorSnapshotSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitPost(() => entMan.SpawnEntity("ComputerShipAiFilled", testMap.GridCoords));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var query = entMan.EntityQueryEnumerator<StationAiCoreComponent>();
            Assert.That(query.MoveNext(out _, out var original), "the filled ship AI has a core");
            Assert.That(original!.RemoteEntity, Is.Not.Null, "the core has an eye before saving");

            var loadedMap = RoundTripTest.SaveAndLoad(entMan, testMap.MapUid, new ResPath("/kessler-roundtrip-test-ship-ai.yml"));
            var tree = snapshots.CollectTree([loadedMap]).ToList();
            var core = tree.Single(entMan.HasComponent<StationAiCoreComponent>);
            var eye = entMan.GetComponent<StationAiCoreComponent>(core).RemoteEntity;

            Assert.That(eye, Is.Not.Null, "the loaded core has an eye");
            Assert.That(eye!.Value.IsValid() && entMan.EntityExists(eye.Value) && tree.Contains(eye.Value),
                "the loaded core's eye is the loaded eye, not an invalid reference");
        });

        await pair.CleanReturnAsync();
    }
}
