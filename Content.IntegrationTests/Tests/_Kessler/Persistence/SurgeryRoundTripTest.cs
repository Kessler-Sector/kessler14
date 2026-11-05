using System.Linq;
using Content.Server._Kessler.Persistence;
using Content.Shared._Shitmed.Medical.Surgery.Steps.Parts;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Kessler.Persistence;

/// <summary>
/// Surgery state must survive a persistent save and load: a lost arm stays lost, its hand stays gone, and an
/// operation left half-done (an open incision) can be finished after a restart.
/// </summary>
[TestFixture]
public sealed class SurgeryRoundTripTest
{
    [Test]
    public async Task AmputationAndOpenIncisionPersist()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.EntMan;
        var snapshots = entManager.System<SectorSnapshotSystem>();
        var body = entManager.System<SharedBodySystem>();
        var hands = entManager.System<SharedHandsSystem>();

        var testMap = await pair.CreateTestMap();

        EntityUid human = default;
        await server.WaitPost(() =>
        {
            human = entManager.SpawnEntity("MobHuman", testMap.GridCoords);
        });
        await pair.RunTicksSync(5);

        EntityUid loaded = default;
        await server.WaitPost(() =>
        {
            Assert.That(hands.GetHandCount(human), Is.EqualTo(2), "a fresh human has two hands");

            var leftArm = body.GetBodyChildrenOfType(human, BodyPartType.Arm, symmetry: BodyPartSymmetry.Left).Single();
            entManager.DeleteEntity(leftArm.Id);

            var torso = body.GetBodyChildrenOfType(human, BodyPartType.Torso).Single();
            entManager.EnsureComponent<IncisionOpenComponent>(torso.Id);
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            Assert.That(hands.GetHandCount(human), Is.EqualTo(1), "losing an arm loses its hand");

            var loadedMap = RoundTripTest.SaveAndLoad(entManager, testMap.MapUid,
                new ResPath("/kessler-roundtrip-test-surgery.yml"));
            loaded = snapshots.CollectTree([loadedMap])
                .Single(uid => entManager.HasComponent<BodyComponent>(uid)
                               && entManager.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "MobHuman");
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(body.GetBodyChildrenOfType(loaded, BodyPartType.Arm, symmetry: BodyPartSymmetry.Left), Is.Empty,
                    "the left arm stays gone");
                Assert.That(body.GetBodyChildrenOfType(loaded, BodyPartType.Arm, symmetry: BodyPartSymmetry.Right).Count(), Is.EqualTo(1),
                    "the right arm is still there");
                Assert.That(hands.GetHandCount(loaded), Is.EqualTo(1), "only one hand after loading");

                var torso = body.GetBodyChildrenOfType(loaded, BodyPartType.Torso).Single();
                Assert.That(entManager.HasComponent<IncisionOpenComponent>(torso.Id), "the incision is still open");
            });
        });

        await pair.CleanReturnAsync();
    }
}
