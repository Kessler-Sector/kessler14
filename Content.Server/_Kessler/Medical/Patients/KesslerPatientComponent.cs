using Content.Shared._Kessler.Medical.Conditions;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Server._Kessler.Medical.Patients;

/// <summary>
/// A generated living patient with a condition (docs/kessler/design/medical-science.md, M1 and M2). Discharged at a
/// medical bounty redemption machine once cured. Everything that matters is saved: the condition's progress is kept as
/// the time left in the current stage, which only runs down while the server runs and the patient is out of stasis.
/// </summary>
[RegisterComponent, Access(typeof(PatientSystem))]
public sealed partial class KesslerPatientComponent : Component
{
    /// <summary>
    /// The condition; picked at random when the patient first appears if not set.
    /// </summary>
    [DataField]
    public ProtoId<MedicalConditionPrototype>? Condition;

    [DataField]
    public bool Initialized;

    [DataField]
    public int Stage;

    /// <summary>
    /// Time left in the current stage.
    /// </summary>
    [DataField]
    public TimeSpan StageRemaining;

    [DataField]
    public bool Cured;

    [DataField]
    public ProtoId<ReagentPrototype>? Toxin;

    [DataField]
    public ProtoId<ReagentPrototype>? Antidote;

    /// <summary>
    /// OrganFailure: the organ that is failing. Cured once the body has another organ of the kind and not this one.
    /// </summary>
    [DataField]
    public EntityUid? FailingOrgan;

    /// <summary>
    /// Whether breathing immunity was given for stasis, so only that is taken away again.
    /// </summary>
    [DataField]
    public bool GrantedBreathingImmunity;

    /// <summary>
    /// Extra pay on discharge, such as for answering a distress beacon.
    /// </summary>
    [DataField]
    public int RescueBonus;

    /// <summary>
    /// Seconds until the next symptom popup.
    /// </summary>
    [ViewVariables]
    public float NextSymptom = 20f;
}

/// <summary>
/// A container that holds patients in stasis: their condition doesn't progress and they don't need to breathe.
/// </summary>
[RegisterComponent]
public sealed partial class PatientStasisComponent : Component;
