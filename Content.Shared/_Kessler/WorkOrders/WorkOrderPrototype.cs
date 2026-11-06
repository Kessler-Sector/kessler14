using Content.Shared._Kessler.Reputation;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._Kessler.WorkOrders;

public enum WorkOrderKind : byte
{
    /// <summary>
    /// Messes are spilled around the station; whoever cleans each one up is paid for it.
    /// </summary>
    Clean,

    /// <summary>
    /// A dish or drink is wanted; whoever hands it in at the board is paid.
    /// </summary>
    Deliver,
}

/// <summary>
/// A kind of generated station job for the work-order board (docs/kessler/design/progression.md, 4.6). Pay is the
/// expected minutes of work times the board's rate; reputation is one point per expected minute.
/// </summary>
[Prototype("workOrder")]
public sealed partial class WorkOrderPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Title shown on the board. Delivery orders get the wanted item as <c>item</c>.
    /// </summary>
    [DataField(required: true)]
    public LocId Title;

    [DataField(required: true)]
    public LocId Description;

    [DataField(required: true)]
    public WorkOrderKind Kind;

    [DataField]
    public ProtoId<ReputationTrackPrototype> Track = "Station";

    /// <summary>
    /// Expected minutes of work for the whole order.
    /// </summary>
    [DataField]
    public int Minutes = 5;

    [DataField]
    public float Weight = 1f;

    /// <summary>
    /// Clean: reagents a mess can be made of. Each mess is one reagent.
    /// </summary>
    [DataField]
    public List<string> MessReagents = new();

    [DataField]
    public int MessMin = 3;

    [DataField]
    public int MessMax = 6;

    [DataField]
    public FixedPoint2 MessVolume = 15;

    /// <summary>
    /// Deliver: items that can be asked for. One is picked per order.
    /// </summary>
    [DataField]
    public List<EntProtoId> Items = new();

    /// <summary>
    /// Deliver: drinks that can be asked for, by reagent, in any container. One is picked per order.
    /// </summary>
    [DataField]
    public List<string> Reagents = new();

    [DataField]
    public FixedPoint2 ReagentAmount = 10;
}
