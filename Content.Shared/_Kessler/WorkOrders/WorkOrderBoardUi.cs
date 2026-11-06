using Robust.Shared.Serialization;

namespace Content.Shared._Kessler.WorkOrders;

[Serializable, NetSerializable]
public enum WorkOrderBoardUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class WorkOrderBoardEntry
{
    public string Title = string.Empty;
    public string Description = string.Empty;
    public int Payout;
    public int Reputation;
    public string Track = string.Empty;
    public int MinutesLeft;

    /// <summary>
    /// Cleaning orders: spots still to clean, and how many there were.
    /// </summary>
    public int Remaining;
    public int Total;
}

[Serializable, NetSerializable]
public sealed class WorkOrderBoardUiState : BoundUserInterfaceState
{
    public readonly List<WorkOrderBoardEntry> Orders;

    public WorkOrderBoardUiState(List<WorkOrderBoardEntry> orders)
    {
        Orders = orders;
    }
}
