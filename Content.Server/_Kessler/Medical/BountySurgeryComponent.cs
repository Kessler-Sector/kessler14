using Content.Shared.Body.Part;

namespace Content.Server._Kessler.Medical;

/// <summary>
/// A medical bounty corpse that arrived with limbs detached. Saved, so a loaded corpse still knows which limbs it
/// needs back.
/// </summary>
[RegisterComponent, Access(typeof(BountySurgerySystem))]
public sealed partial class BountySurgeryComponent : Component
{
    [DataField]
    public List<MissingLimb> Missing = new();

    [DataField]
    public int BonusPerLimb;

    /// <summary>
    /// Detached limbs not yet put in the corpse's container.
    /// </summary>
    [DataField]
    public List<EntityUid> DetachedParts = new();
}

[DataDefinition]
public sealed partial class MissingLimb
{
    [DataField]
    public BodyPartType Type;

    [DataField]
    public BodyPartSymmetry Symmetry;
}
