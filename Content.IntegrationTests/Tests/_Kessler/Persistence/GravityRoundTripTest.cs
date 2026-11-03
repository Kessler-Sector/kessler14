using System.Linq;
using Content.Server._Kessler.Persistence;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Gravity;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Kessler.Persistence;

/// <summary>
/// A grid with a running gravity generator must still have gravity after a persistent save and load. The
/// generator's GravityActive flag isn't saved and its activation event only fires when its charge state changes,
/// so loaded grids used to have no gravity at all.
/// </summary>
[TestFixture]
public sealed class GravityRoundTripTest
{
    [Test]
    public async Task RunningGeneratorKeepsGravity()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.EntMan;
        var snapshots = entManager.System<SectorSnapshotSystem>();

        var testMap = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            var grid = testMap.Grid.Owner;
            entManager.EnsureComponent<GravityComponent>(grid);

            var generator = entManager.SpawnEntity("GravityGenerator", testMap.GridCoords);
#pragma warning disable RA0002
            entManager.GetComponent<PowerChargeComponent>(generator).Active = true;
#pragma warning restore RA0002
            var activated = new ChargedMachineActivatedEvent();
            entManager.EventBus.RaiseLocalEvent(generator, ref activated);
            Assert.That(entManager.GetComponent<GravityComponent>(grid).Enabled, "gravity before saving");

            var loadedMap = RoundTripTest.SaveAndLoad(entManager, testMap.MapUid,
                new ResPath("/kessler-roundtrip-test-gravity.yml"));
            var loadedGrid = snapshots.CollectTree([loadedMap])
                .Single(uid => entManager.HasComponent<MapGridComponent>(uid));

            Assert.That(entManager.GetComponent<GravityComponent>(loadedGrid).Enabled, "gravity after loading");
        });

        await pair.CleanReturnAsync();
    }
}
