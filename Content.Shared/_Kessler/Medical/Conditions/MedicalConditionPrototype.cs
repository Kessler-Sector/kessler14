using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._Kessler.Medical.Conditions;

public enum MedicalConditionKind : byte
{
    /// <summary>
    /// A toxin from <see cref="MedicalConditionPrototype.Poisons"/> keeps coming back in the patient's blood until its
    /// antidote is in the blood too. A blood sample names the toxin.
    /// </summary>
    Poison,

    /// <summary>
    /// The patient loses blood with no wound to show for it, until the internal bleeders in the torso are clamped by
    /// surgery.
    /// </summary>
    InternalBleeding,

    /// <summary>
    /// One organ (<see cref="MedicalConditionPrototype.Organ"/>) is failing; the patient is cured once it has been
    /// replaced by another working organ of the same kind.
    /// </summary>
    OrganFailure,
}

/// <summary>
/// A condition a generated patient can have (docs/kessler/design/medical-science.md, M2): stages that get worse over
/// time, symptoms that can be seen, and a hidden cause with one specific treatment.
/// </summary>
[Prototype("medicalCondition")]
public sealed partial class MedicalConditionPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Shown only to admins and in logs; players have to work out what the patient has.
    /// </summary>
    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public MedicalConditionKind Kind;

    [DataField]
    public float Weight = 1f;

    /// <summary>
    /// Expected minutes of work to treat the case. Pay is minutes times <see cref="Rate"/>; Medical reputation is one
    /// point a minute.
    /// </summary>
    [DataField]
    public int Minutes = 15;

    /// <summary>
    /// Spesos a minute for this case's tier.
    /// </summary>
    [DataField]
    public int Rate = 200;

    [DataField(required: true)]
    public List<MedicalConditionStage> Stages = new();

    /// <summary>
    /// Poison: the toxins a case can have, each with the antidote that cures it.
    /// </summary>
    [DataField]
    public List<PoisonCure> Poisons = new();

    /// <summary>
    /// Poison: units of antidote that must be in the blood at once.
    /// </summary>
    [DataField]
    public FixedPoint2 AntidoteAmount = 5;

    /// <summary>
    /// OrganFailure: the component that marks the organ kind, such as <c>Liver</c>.
    /// </summary>
    [DataField]
    public string? Organ;

    /// <summary>
    /// What the patient says or does now and then once cured, shown when examined.
    /// </summary>
    [DataField]
    public LocId CuredExamine = "kessler-patient-examine-cured";
}

[DataDefinition]
public sealed partial class MedicalConditionStage
{
    /// <summary>
    /// How long the stage lasts while the patient is out of stasis. Zero means the stage lasts until cured.
    /// </summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.Zero;

    /// <summary>
    /// Damage dealt each minute.
    /// </summary>
    [DataField]
    public DamageSpecifier? DamagePerMinute;

    /// <summary>
    /// Units of blood lost each minute, without a puddle or a visible wound.
    /// </summary>
    [DataField]
    public float BloodLossPerMinute;

    /// <summary>
    /// Poison: units of the toxin kept in the blood.
    /// </summary>
    [DataField]
    public FixedPoint2 ToxinLevel;

    /// <summary>
    /// Shown when the patient is examined.
    /// </summary>
    [DataField(required: true)]
    public LocId Examine;

    /// <summary>
    /// Popups shown now and then around the patient, picked at random.
    /// </summary>
    [DataField]
    public List<LocId> Symptoms = new();
}

[DataDefinition]
public sealed partial class PoisonCure
{
    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Toxin;

    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Antidote;
}
