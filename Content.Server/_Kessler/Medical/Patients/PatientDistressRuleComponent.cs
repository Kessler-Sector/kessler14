using Robust.Shared.Prototypes;

namespace Content.Server._Kessler.Medical.Patients;

/// <summary>
/// A sector event: a distress beacon in open space with a living patient in a stasis pod (docs/kessler/design/
/// medical-science.md, M7). Medical hears the coordinates on the radio; whoever brings the patient in and treats them
/// gets a rescue bonus on top of the discharge.
/// </summary>
[RegisterComponent, Access(typeof(PatientDistressRule))]
public sealed partial class PatientDistressRuleComponent : Component
{
    [DataField]
    public EntProtoId Pod = "MedicalPodPatient";

    [DataField]
    public float MinimumDistance = 800f;

    [DataField]
    public float MaximumDistance = 2500f;

    /// <summary>
    /// Spesos added to the patient's discharge pay.
    /// </summary>
    [DataField]
    public int RescueBonus = 3000;

    /// <summary>
    /// Real time before an unanswered beacon is gone.
    /// </summary>
    [DataField]
    public TimeSpan Lifetime = TimeSpan.FromMinutes(90);

    [DataField]
    public string Channel = "Medical";
}

/// <summary>
/// The wreck a distress beacon was sent from. Deleted once it expires, unless someone is aboard.
/// </summary>
[RegisterComponent, Access(typeof(PatientDistressRule))]
public sealed partial class DistressBeaconComponent : Component
{
    /// <summary>
    /// Real UTC time the beacon expires.
    /// </summary>
    [DataField]
    public DateTime Expires;
}
