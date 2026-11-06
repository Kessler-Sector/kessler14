#pragma warning disable RA0002 // Tests read component state directly.
using System.Linq;
using Content.Server._Kessler.Medical.Patients;
using Content.Server._NF.Medical;
using Content.Shared._NF.Medical;
using Content.Shared._Shitmed.Body.Components;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._Shitmed.Medical.Surgery.Steps.Parts;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Kessler.Medical;

/// <summary>
/// Living NPC patients: conditions get worse over time outside stasis, only their specific treatment cures them, and a
/// cured patient is discharged at the redemption machine for pay.
/// </summary>
[TestFixture]
public sealed class PatientTest
{
    private static Entity<KesslerPatientComponent> SpawnPatient(IEntityManager entMan, PatientSystem patients,
        Robust.Shared.Map.EntityCoordinates coords, string condition)
    {
        var uid = entMan.SpawnEntity("MobHuman", coords);
        var comp = entMan.AddComponent<KesslerPatientComponent>(uid);
        patients.SetCondition((uid, comp), condition);
        Assert.That(comp.Initialized, "the patient got a condition");
        return (uid, comp);
    }

    [Test]
    public async Task PoisonCuredOnlyByItsAntidote()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var patients = entMan.System<PatientSystem>();
        var solutions = entMan.System<SharedSolutionContainerSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var patient = SpawnPatient(entMan, patients, testMap.GridCoords, "PatientPoisoning");
            Assert.That(patient.Comp.Toxin, Is.Not.Null);
            Assert.That(patient.Comp.Antidote, Is.Not.Null);

            var bloodstream = entMan.GetComponent<BloodstreamComponent>(patient);
            Assert.That(solutions.ResolveSolution(patient.Owner, bloodstream.ChemicalSolutionName, ref bloodstream.ChemicalSolution, out var chemicals));

            patients.Tick(patient, 60f);
            Assert.That(chemicals!.GetTotalPrototypeQuantity(patient.Comp.Toxin!.Value.Id).Float(), Is.GreaterThan(0), "the toxin shows up in the blood");
            Assert.That(entMan.GetComponent<DamageableComponent>(patient).TotalDamage.Float(), Is.GreaterThan(0), "the condition hurts");
            Assert.That(patients.DischargeProblem(patient), Is.Not.Null, "an untreated patient can't be discharged");

            // Generic toxin medicine doesn't cure it.
            solutions.TryAddReagent(bloodstream.ChemicalSolution!.Value, "Dylovene", 10, out _);
            patients.Tick(patient, 1f);
            Assert.That(patient.Comp.Cured, Is.False, "dylovene isn't the antidote");

            solutions.TryAddReagent(bloodstream.ChemicalSolution!.Value, patient.Comp.Antidote!.Value.Id, 10, out _);
            patients.Tick(patient, 1f);
            Assert.That(patient.Comp.Cured, "the antidote cures it");
            Assert.That(chemicals.GetTotalPrototypeQuantity(patient.Comp.Toxin!.Value.Id).Float(), Is.Zero, "the toxin is cleared");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StasisPodPausesTheCondition()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var patients = entMan.System<PatientSystem>();
        var containers = entMan.System<SharedContainerSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var patient = SpawnPatient(entMan, patients, testMap.GridCoords, "PatientLiverFailure");
            var pod = entMan.SpawnEntity("MedicalPod", testMap.GridCoords);
            Assert.That(containers.TryGetContainer(pod, "entity_storage", out var storage));
            Assert.That(containers.Insert(patient.Owner, storage!), "patients fit in an emergency medical pod");
            Assert.That(entMan.HasComponent<BreathingImmunityComponent>(patient), "patients in stasis don't need air");

            var remaining = patient.Comp.StageRemaining;
            patients.Tick(patient, 3600f);
            Assert.That(patient.Comp.Stage, Is.Zero);
            Assert.That(patient.Comp.StageRemaining, Is.EqualTo(remaining), "nothing happens in stasis");
            Assert.That(entMan.GetComponent<DamageableComponent>(patient).TotalDamage.Float(), Is.Zero);

            Assert.That(containers.Remove(patient.Owner, storage!));
            Assert.That(entMan.HasComponent<BreathingImmunityComponent>(patient), Is.False, "out of stasis they breathe again");

            patients.Tick(patient, (float) remaining.TotalSeconds + 1);
            Assert.That(patient.Comp.Stage, Is.EqualTo(1), "out of stasis the condition moves on to its next stage");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task InternalBleedingCuredByClampingBleeders()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var patients = entMan.System<PatientSystem>();
        var body = entMan.System<SharedBodySystem>();
        var bloodstream = entMan.System<SharedBloodstreamSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var patient = SpawnPatient(entMan, patients, testMap.GridCoords, "PatientInternalBleeding");
            var before = bloodstream.GetBloodLevelPercentage(patient.Owner);
            patients.Tick(patient, 120f);
            Assert.That(bloodstream.GetBloodLevelPercentage(patient.Owner), Is.LessThan(before), "the patient loses blood");

            var torso = body.GetBodyChildrenOfType(patient, BodyPartType.Torso).First().Id;
            entMan.AddComponent<InternalBleedersClampedComponent>(torso);
            patients.Tick(patient, 1f);
            Assert.That(patient.Comp.Cured, "clamping the internal bleeders cures it");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LiverFailureCuredByTransplant()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var patients = entMan.System<PatientSystem>();
        var body = entMan.System<SharedBodySystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var patient = SpawnPatient(entMan, patients, testMap.GridCoords, "PatientLiverFailure");
            var failing = patient.Comp.FailingOrgan;
            Assert.That(failing, Is.Not.Null, "the failing liver is picked");
            Assert.That(entMan.HasComponent<LiverComponent>(failing!.Value));

            // Taking the liver out isn't a cure by itself.
            var organ = entMan.GetComponent<OrganComponent>(failing.Value);
            var slot = organ.SlotId;
            var torso = body.GetBodyChildrenOfType(patient, BodyPartType.Torso).First().Id;
            Assert.That(body.RemoveOrgan(failing.Value));
            patients.Tick(patient, 1f);
            Assert.That(patient.Comp.Cured, Is.False, "no liver is no cure");

            var donor = entMan.SpawnEntity("OrganHumanLiver", testMap.GridCoords);
            Assert.That(body.InsertOrgan(torso, donor, slot), "the donor liver goes in");
            patients.Tick(patient, 1f);
            Assert.That(patient.Comp.Cured, "a working liver cures it");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CuredPatientIsDischargedForPay()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var patients = entMan.System<PatientSystem>();
        var containers = entMan.System<SharedContainerSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var patient = SpawnPatient(entMan, patients, testMap.GridCoords, "PatientInternalBleeding");
            var machine = entMan.SpawnEntity("MachineMedicalBountyRedemption", testMap.GridCoords);
            var doctor = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            Assert.That(containers.TryGetContainer(machine, "body_container", out var slot));
            Assert.That(containers.Insert(patient.Owner, slot!));

            // Not yet treated: refused, and the patient stays.
            entMan.EventBus.RaiseLocalEvent(machine, new RedeemMedicalBountyMessage { Actor = doctor });
            Assert.That(entMan.Deleted(patient) || entMan.IsQueuedForDeletion(patient), Is.False, "an untreated patient isn't discharged");

            var torso = entMan.System<SharedBodySystem>().GetBodyChildrenOfType(patient, BodyPartType.Torso).First().Id;
            entMan.AddComponent<InternalBleedersClampedComponent>(torso);
            patients.Tick(patient, 1f);
            Assert.That(patients.DischargeProblem(patient), Is.Null);
            var payout = patients.Payout(patient);
            Assert.That(payout, Is.EqualTo(25 * 300), "an undamaged patient pays the full minutes times rate");

            entMan.EventBus.RaiseLocalEvent(machine, new RedeemMedicalBountyMessage { Actor = doctor });
            Assert.That(entMan.IsQueuedForDeletion(patient) || entMan.Deleted(patient), "the patient leaves once discharged");

            var cash = entMan.EntityQuery<StackComponent>()
                .Where(s => s.StackTypeId == "Credit")
                .Sum(s => s.Count);
            Assert.That(cash, Is.EqualTo(payout), "the doctor is paid");
        });

        await pair.CleanReturnAsync();
    }
}
