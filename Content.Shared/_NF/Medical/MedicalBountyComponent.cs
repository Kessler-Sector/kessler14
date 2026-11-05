using Content.Shared._NF.Medical.Prototypes;
using Robust.Shared.Prototypes; // Kessler

namespace Content.Server._NF.Medical.Components;

[RegisterComponent]
[AutoGenerateComponentState]
public sealed partial class MedicalBountyComponent : Component
{
    /// <summary>
    /// The bounty to use/used for damage generation.
    /// If null, a medical bounty type will be selected at random.
    /// </summary>
    [DataField(serverOnly: true)]
    public ProtoId<MedicalBountyPrototype>? Bounty = null; // Kessler: MedicalBountyPrototype<ProtoId, saves held a full copy of the prototype

    /// <summary>
    /// Maximum bounty value for this entity in spesos.
    /// Cached from bounty params on generation.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField] // Kessler: saved, or a loaded corpse rolls a new value
    public int MaxBountyValue;

    /// <summary>
    /// Ensures damage is only applied once, set to true on startup.
    /// </summary>
    [DataField] // Kessler: saved, or a loaded corpse gets its damage and reagents again
    public bool BountyInitialized;
}
