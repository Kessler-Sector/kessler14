using Content.Shared._Kessler.WorkOrders;
using Robust.Shared.Prototypes;

namespace Content.Server._Kessler.WorkOrders;

/// <summary>
/// A board that keeps generated station jobs open for its station. Orders are saved with the sector; deadlines are
/// real UTC time, so they keep running while the server is down.
/// </summary>
[RegisterComponent, Access(typeof(WorkOrderSystem))]
public sealed partial class WorkOrderBoardComponent : Component
{
    [DataField]
    public List<WorkOrder> Orders = new();

    [DataField]
    public int NextId = 1;

    /// <summary>
    /// When the next order above <see cref="BaseOpen"/> may be posted.
    /// </summary>
    [DataField]
    public DateTime NextGenerate;

    /// <summary>
    /// Orders kept open even with one player online.
    /// </summary>
    [DataField]
    public int BaseOpen = 3;

    /// <summary>
    /// One more open order per this many players online, up to <see cref="MaxOpen"/>.
    /// </summary>
    [DataField]
    public int PlayersPerExtraOrder = 5;

    [DataField]
    public int MaxOpen = 8;

    [DataField]
    public TimeSpan Interval = TimeSpan.FromMinutes(5);

    [DataField]
    public TimeSpan Lifetime = TimeSpan.FromHours(1);

    /// <summary>
    /// Only the prototypes listed here are posted; empty means all of them.
    /// </summary>
    [DataField]
    public List<ProtoId<WorkOrderPrototype>> Allowed = new();

    [ViewVariables]
    public TimeSpan NextUpdate;
}

[DataDefinition]
public sealed partial class WorkOrder
{
    [DataField]
    public int Id;

    [DataField]
    public ProtoId<WorkOrderPrototype> Prototype;

    /// <summary>
    /// Deliver: the wanted item prototype or drink reagent.
    /// </summary>
    [DataField]
    public string? Target;

    [DataField]
    public bool TargetIsReagent;

    [DataField]
    public DateTime Expires;

    /// <summary>
    /// Clean: messes made and messes still there.
    /// </summary>
    [DataField]
    public int MessesTotal;

    [DataField]
    public int MessesLeft;
}
