namespace Content.Server._Kessler.WorkOrders;

/// <summary>
/// A mess made for a cleaning order. When it is cleaned up, the nearest player is paid their share.
/// </summary>
[RegisterComponent, Access(typeof(WorkOrderSystem))]
public sealed partial class WorkOrderMessComponent : Component
{
    [DataField]
    public EntityUid? Board;

    [DataField]
    public int OrderId;
}
