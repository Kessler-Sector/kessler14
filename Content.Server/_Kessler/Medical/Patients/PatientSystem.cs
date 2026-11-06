using System.Linq;
using Content.Server._Kessler.Reputation;
using Content.Server.Popups;
using Content.Shared._Kessler.CCVar;
using Content.Shared._Kessler.Medical.Conditions;
using Content.Shared._Shitmed.Body.Components;
using Content.Shared._Shitmed.Medical.Surgery.Steps.Parts;
using Content.Shared.Bed.Components;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Kessler.Medical.Patients;

/// <summary>
/// Living NPC patients with conditions (docs/kessler/design/medical-science.md, M1 and M2). A patient's condition gets
/// worse stage by stage while they are out of stasis, shows symptoms, and is cured only by its specific treatment.
/// A cured patient is discharged at a medical bounty redemption machine for pay and Medical reputation.
/// </summary>
public sealed class PatientSystem : EntitySystem
{
    [Dependency] private readonly IComponentFactory _compFactory = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly KesslerReputationSystem _reputation = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;

    /// <summary>
    /// Seconds between condition updates.
    /// </summary>
    private const float UpdateInterval = 1f;

    /// <summary>
    /// How fast a condition progresses while the patient is buckled to a stasis bed.
    /// </summary>
    private const float StasisBedRate = 0.1f;

    /// <summary>
    /// Damage at which a cured patient's pay has halved; above twice this they can't be discharged.
    /// </summary>
    public const float DamageForHalfPay = 50f;

    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<KesslerPatientComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<KesslerPatientComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<KesslerPatientComponent, EntGotInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<KesslerPatientComponent, EntGotRemovedFromContainerMessage>(OnRemoved);
    }

    private void OnMapInit(Entity<KesslerPatientComponent> ent, ref MapInitEvent args)
    {
        Setup(ent);
    }

    /// <summary>
    /// Gives a new patient their condition. Does nothing for a patient that already has one, such as a loaded one.
    /// </summary>
    public void Setup(Entity<KesslerPatientComponent> ent, ProtoId<MedicalConditionPrototype>? condition = null)
    {
        if (ent.Comp.Initialized)
            return;

        if (condition != null)
            ent.Comp.Condition = condition;

        if (ent.Comp.Condition == null)
        {
            var pick = PickCondition();
            if (pick == null)
                return;
            ent.Comp.Condition = pick.ID;
        }

        if (!_proto.TryIndex(ent.Comp.Condition, out var proto) || proto.Stages.Count == 0)
            return;

        ent.Comp.Stage = 0;
        ent.Comp.StageRemaining = proto.Stages[0].Duration;
        ent.Comp.Cured = false;

        switch (proto.Kind)
        {
            case MedicalConditionKind.Poison when proto.Poisons.Count > 0:
                var poison = _random.Pick(proto.Poisons);
                ent.Comp.Toxin = poison.Toxin;
                ent.Comp.Antidote = poison.Antidote;
                break;
            case MedicalConditionKind.InternalBleeding:
                // Bleeders clamped before the condition started don't count.
                if (GetTorso(ent) is { } torso)
                    RemComp<InternalBleedersClampedComponent>(torso);
                break;
            case MedicalConditionKind.OrganFailure:
                ent.Comp.FailingOrgan = FirstOrgan(ent, proto);
                break;
        }

        ent.Comp.Initialized = true;
        ent.Comp.NextSymptom = _random.NextFloat(10f, 30f);
    }

    /// <summary>
    /// Gives a patient a new condition from its first stage, replacing any they had.
    /// </summary>
    public void SetCondition(Entity<KesslerPatientComponent> ent, ProtoId<MedicalConditionPrototype> condition)
    {
        ClearToxin(ent);
        ent.Comp.Initialized = false;
        ent.Comp.Condition = null;
        Setup(ent, condition);
    }

    private MedicalConditionPrototype? PickCondition()
    {
        var conditions = _proto.EnumeratePrototypes<MedicalConditionPrototype>().Where(c => c.Weight > 0).ToList();
        if (conditions.Count == 0)
            return null;

        var roll = _random.NextFloat() * conditions.Sum(c => c.Weight);
        foreach (var condition in conditions)
        {
            roll -= condition.Weight;
            if (roll <= 0)
                return condition;
        }

        return conditions[^1];
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        var dt = _accumulator;
        _accumulator = 0;

        var query = EntityQueryEnumerator<KesslerPatientComponent>();
        while (query.MoveNext(out var uid, out var patient))
        {
            Tick((uid, patient), dt);
        }
    }

    /// <summary>
    /// Advances a patient's condition by <paramref name="seconds"/> of server time.
    /// </summary>
    public void Tick(Entity<KesslerPatientComponent> ent, float seconds)
    {
        var patient = ent.Comp;
        if (!patient.Initialized
            || patient.Cured
            || InStasis(ent)
            || _mobState.IsDead(ent)
            || !_proto.TryIndex(patient.Condition, out var proto))
        {
            return;
        }

        if (CheckCured(ent, proto))
        {
            patient.Cured = true;
            ClearToxin(ent);
            _popup.PopupEntity(Loc.GetString("kessler-patient-cured", ("patient", ent.Owner)), ent, PopupType.Medium);
            return;
        }

        var rate = HasComp<StasisBedBuckledComponent>(ent) ? StasisBedRate : 1f;
        var minutes = seconds * rate / 60f;
        var stage = proto.Stages[Math.Clamp(patient.Stage, 0, proto.Stages.Count - 1)];

        if (stage.DamagePerMinute is { } damage)
            _damageable.TryChangeDamage(ent, damage * minutes, ignoreResistances: true, interruptsDoAfters: false, canSever: false);

        if (stage.BloodLossPerMinute > 0)
            RemoveBlood(ent, stage.BloodLossPerMinute * minutes);

        if (proto.Kind == MedicalConditionKind.Poison && patient.Toxin is { } toxin && stage.ToxinLevel > 0)
            TopUpToxin(ent, toxin, stage);

        // Move on to the next stage.
        if (stage.Duration > TimeSpan.Zero && patient.Stage < proto.Stages.Count - 1)
        {
            patient.StageRemaining -= TimeSpan.FromSeconds(seconds * rate);
            if (patient.StageRemaining <= TimeSpan.Zero)
            {
                patient.Stage++;
                patient.StageRemaining = proto.Stages[patient.Stage].Duration;
            }
        }

        patient.NextSymptom -= seconds;
        if (patient.NextSymptom <= 0)
        {
            patient.NextSymptom = _random.NextFloat(30f, 75f);
            if (stage.Symptoms.Count > 0)
                _popup.PopupEntity(Loc.GetString(_random.Pick(stage.Symptoms), ("patient", ent.Owner)), ent, PopupType.SmallCaution);
        }
    }

    /// <summary>
    /// Whether the condition's specific treatment has been given.
    /// </summary>
    private bool CheckCured(Entity<KesslerPatientComponent> ent, MedicalConditionPrototype proto)
    {
        switch (proto.Kind)
        {
            case MedicalConditionKind.Poison:
                return ent.Comp.Antidote is { } antidote
                       && GetChemicals(ent) is { } chemicals
                       && chemicals.GetTotalPrototypeQuantity(antidote.Id) >= proto.AntidoteAmount;
            case MedicalConditionKind.InternalBleeding:
                // Closing the incision removes the clamp again, so the cure is noticed while the surgery is open.
                return GetTorso(ent) is { } torso && HasComp<InternalBleedersClampedComponent>(torso);
            case MedicalConditionKind.OrganFailure:
                var organs = GetOrgans(ent, proto).ToList();
                if (ent.Comp.FailingOrgan == null)
                {
                    // The body's organs weren't there yet when the condition started; pick the first one now. A
                    // species without that organ gets a different condition instead.
                    if (organs.Count > 0)
                        ent.Comp.FailingOrgan = organs[0];
                    else
                        SwapToOtherCondition(ent, proto);
                    return false;
                }

                return organs.Count > 0 && !organs.Contains(ent.Comp.FailingOrgan.Value);
            default:
                return false;
        }
    }

    private EntityUid? FirstOrgan(EntityUid body, MedicalConditionPrototype proto)
    {
        foreach (var organ in GetOrgans(body, proto))
        {
            return organ;
        }

        return null;
    }

    private void SwapToOtherCondition(Entity<KesslerPatientComponent> ent, MedicalConditionPrototype current)
    {
        var others = _proto.EnumeratePrototypes<MedicalConditionPrototype>()
            .Where(c => c.Kind != MedicalConditionKind.OrganFailure && c.Weight > 0 && c.ID != current.ID)
            .ToList();
        if (others.Count > 0)
            SetCondition(ent, _random.Pick(others).ID);
    }

    private bool InStasis(EntityUid uid)
    {
        return _container.TryGetContainingContainer((uid, null, null), out var container)
               && HasComp<PatientStasisComponent>(container.Owner);
    }

    private EntityUid? GetTorso(EntityUid uid)
    {
        foreach (var (part, _) in _body.GetBodyChildrenOfType(uid, BodyPartType.Torso))
        {
            return part;
        }

        return null;
    }

    private IEnumerable<EntityUid> GetOrgans(EntityUid body, MedicalConditionPrototype proto)
    {
        if (proto.Organ == null || !_compFactory.TryGetRegistration(proto.Organ, out var registration))
            yield break;

        foreach (var (organ, _) in _body.GetBodyOrgans(body))
        {
            if (HasComp(organ, registration.Type))
                yield return organ;
        }
    }

    private Solution? GetChemicals(EntityUid uid)
    {
        if (!TryComp<BloodstreamComponent>(uid, out var bloodstream)
            || !_solution.ResolveSolution(uid, bloodstream.ChemicalSolutionName, ref bloodstream.ChemicalSolution, out var solution))
        {
            return null;
        }

        return solution;
    }

    private void TopUpToxin(EntityUid uid, string toxin, MedicalConditionStage stage)
    {
        if (!TryComp<BloodstreamComponent>(uid, out var bloodstream)
            || !_solution.ResolveSolution(uid, bloodstream.ChemicalSolutionName, ref bloodstream.ChemicalSolution, out var solution))
        {
            return;
        }

        var missing = stage.ToxinLevel - solution.GetTotalPrototypeQuantity(toxin);
        if (missing > 0)
            _solution.TryAddReagent(bloodstream.ChemicalSolution.Value, toxin, missing, out _);
    }

    /// <summary>
    /// The body clears the condition's toxin once cured; it is never metabolised otherwise.
    /// </summary>
    private void ClearToxin(Entity<KesslerPatientComponent> ent)
    {
        if (ent.Comp.Toxin is not { } toxin
            || !TryComp<BloodstreamComponent>(ent, out var bloodstream)
            || !_solution.ResolveSolution(ent.Owner, bloodstream.ChemicalSolutionName, ref bloodstream.ChemicalSolution, out var solution))
        {
            return;
        }

        var amount = solution.GetTotalPrototypeQuantity(toxin.Id);
        if (amount > 0)
            _solution.RemoveReagent(bloodstream.ChemicalSolution.Value, toxin.Id, amount);
    }

    /// <summary>
    /// Takes blood without spilling it, since the bleeding is internal.
    /// </summary>
    private void RemoveBlood(EntityUid uid, float amount)
    {
        if (!TryComp<BloodstreamComponent>(uid, out var bloodstream)
            || !_solution.ResolveSolution(uid, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution))
        {
            return;
        }

        _solution.SplitSolution(bloodstream.BloodSolution.Value, amount);
    }

    private void OnExamined(Entity<KesslerPatientComponent> ent, ref ExaminedEvent args)
    {
        if (!ent.Comp.Initialized || !_proto.TryIndex(ent.Comp.Condition, out var proto) || proto.Stages.Count == 0)
            return;

        var text = ent.Comp.Cured
            ? proto.CuredExamine
            : proto.Stages[Math.Clamp(ent.Comp.Stage, 0, proto.Stages.Count - 1)].Examine;
        args.PushMarkup(Loc.GetString(text, ("patient", ent.Owner)), -1);
    }

    private void OnInserted(Entity<KesslerPatientComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        if (!HasComp<PatientStasisComponent>(args.Container.Owner) || HasComp<BreathingImmunityComponent>(ent))
            return;

        AddComp<BreathingImmunityComponent>(ent);
        ent.Comp.GrantedBreathingImmunity = true;
    }

    private void OnRemoved(Entity<KesslerPatientComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        if (!ent.Comp.GrantedBreathingImmunity || !HasComp<PatientStasisComponent>(args.Container.Owner))
            return;

        RemComp<BreathingImmunityComponent>(ent);
        ent.Comp.GrantedBreathingImmunity = false;
    }

    /// <summary>
    /// Why a patient can't be discharged yet, as a locale id, or null if they can.
    /// </summary>
    public string? DischargeProblem(Entity<KesslerPatientComponent> ent)
    {
        if (!ent.Comp.Cured)
            return "kessler-patient-discharge-not-cured";
        if (!TryComp<MobStateComponent>(ent, out var mobState) || !_mobState.IsAlive(ent, mobState))
            return "medical-bounty-redemption-fail-not-alive";
        if (TryComp<DamageableComponent>(ent, out var damageable) && damageable.TotalDamage > DamageForHalfPay * 2)
            return "medical-bounty-redemption-fail-too-much-damage";
        return null;
    }

    /// <summary>
    /// What discharging the patient pays: the case's expected minutes times its rate, less for damage left untreated.
    /// </summary>
    public int Payout(Entity<KesslerPatientComponent> ent)
    {
        if (!_proto.TryIndex(ent.Comp.Condition, out var proto))
            return 0;

        var damage = TryComp<DamageableComponent>(ent, out var damageable) ? damageable.TotalDamage.Float() : 0f;
        var outcome = Math.Clamp(1f - damage / (DamageForHalfPay * 2), 0.5f, 1f);
        return (int) (proto.Minutes * proto.Rate * outcome * _cfg.GetCVar(KesslerCVars.PatientPayMultiplier)) + ent.Comp.RescueBonus;
    }

    /// <summary>
    /// Adds to what discharging the patient pays, such as for a rescue.
    /// </summary>
    public void AddRescueBonus(Entity<KesslerPatientComponent> ent, int amount)
    {
        ent.Comp.RescueBonus += amount;
    }

    /// <summary>
    /// Records a discharge: Medical reputation for whoever discharged the patient. The caller pays and removes them.
    /// </summary>
    public void Discharged(Entity<KesslerPatientComponent> ent, EntityUid actor)
    {
        if (_proto.TryIndex(ent.Comp.Condition, out var proto))
            _reputation.TryAward(actor, "Medical", proto.Minutes, Loc.GetString("kessler-reputation-reason-patient"));

        _popup.PopupEntity(Loc.GetString("kessler-patient-discharged", ("patient", ent.Owner)), ent, PopupType.Medium);
    }
}
