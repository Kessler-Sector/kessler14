using System.Linq;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Standing;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Kessler.Medical;

/// <summary>
/// Surgery can be done on yourself (surgery.can_operate_on_self, on by default for low population): lying down with a
/// scalpel in hand, choosing the first step of an incision starts it.
/// </summary>
[TestFixture]
public sealed class SelfSurgeryTest
{
    [TestCase(true, false)]
    [TestCase(false, false)]
    [TestCase(true, true)]
    [TestCase(false, true)]
    public async Task FirstIncisionStepStarts(bool onSelf, bool fromSave)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var body = entMan.System<SharedBodySystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var standing = entMan.System<StandingStateSystem>();
        var surgery = entMan.System<SharedSurgerySystem>();
        var testMap = await pair.CreateTestMap();

        EntityUid patient = default;
        EntityUid surgeon = default;
        await server.WaitPost(() =>
        {
            patient = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            if (fromSave)
            {
                // A body from a persistent save, as every character is after a restart.
                var loadedMap = Persistence.RoundTripTest.SaveAndLoad(entMan, testMap.MapUid,
                    new Robust.Shared.Utility.ResPath("/kessler-roundtrip-test-self-surgery.yml"));
                entMan.System<SharedMapSystem>().SetPaused(loadedMap, false);
                patient = entMan.System<Server._Kessler.Persistence.SectorSnapshotSystem>().CollectTree([loadedMap])
                    .Single(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "MobHuman");
            }

            var coords = entMan.GetComponent<TransformComponent>(patient).Coordinates;
            surgeon = onSelf ? patient : entMan.SpawnEntity("MobHuman", coords);
            var scalpel = entMan.SpawnEntity("Scalpel", coords);
            Assert.That(hands.TryPickupAnyHand(surgeon, scalpel), "the surgeon holds a scalpel");
            standing.Down(patient, playSound: false, dropHeldItems: false); // Lying down, scalpel still in hand.
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var torso = body.GetBodyChildrenOfType(patient, BodyPartType.Torso).Single().Id;
            Assert.That(surgery.IsLyingDown(patient, surgeon), "the patient is lying down");

            var step = surgery.GetSingleton("SurgeryStepOpenIncisionScalpel")!.Value;
            var canPerform = surgery.CanPerformStep(surgeon, patient, torso, step, false, out var popup, out var reason, out _);
            Assert.That(canPerform, $"the step can be performed: {reason} {popup}");

            var msg = new SurgeryStepChosenBuiMsg(entMan.GetNetEntity(torso), "SurgeryOpenIncision", "SurgeryStepOpenIncisionScalpel", false)
            {
                Actor = surgeon,
                Entity = entMan.GetNetEntity(patient),
                UiKey = SurgeryUIKey.Key,
            };
            var surgeryEnt = surgery.GetSingleton("SurgeryOpenIncision")!.Value;
            Assert.That(surgery.PreviousStepsComplete(patient, torso, (surgeryEnt, entMan.GetComponent<SurgeryComponent>(surgeryEnt)), "SurgeryStepOpenIncisionScalpel"),
                "nothing comes before the first step");
            Assert.That(surgery.IsStepComplete(patient, torso, "SurgeryStepOpenIncisionScalpel", surgeryEnt), Is.False,
                "the incision isn't already made");

            entMan.EventBus.RaiseLocalEvent(patient, msg);

            Assert.That(entMan.TryGetComponent<DoAfterComponent>(surgeon, out var doAfters) && doAfters.DoAfters.Count > 0,
                "choosing the step starts the incision");
        });

        await pair.CleanReturnAsync();
    }
}
