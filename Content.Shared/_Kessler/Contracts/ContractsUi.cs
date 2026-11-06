using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._Kessler.Contracts;

/// <summary>
/// What the Contracts PDA program shows: the viewer's own contracts and the open ones at every station.
/// </summary>
[Serializable, NetSerializable]
public sealed class ContractsUiState : BoundUserInterfaceState
{
    public readonly List<ContractEntry> Held;
    public readonly List<ContractEntry> Open;
    public readonly int MaxHeld;

    /// <summary>
    /// Why the viewer can't take contracts, if they can't (no character), as a locale id.
    /// </summary>
    public readonly string? Problem;

    public ContractsUiState(List<ContractEntry> held, List<ContractEntry> open, int maxHeld, string? problem)
    {
        Held = held;
        Open = open;
        MaxHeld = maxHeld;
        Problem = problem;
    }
}

[Serializable, NetSerializable]
public sealed class ContractEntry
{
    public NetEntity Station;
    public int Id;
    public string StationName = string.Empty;
    public string Item = string.Empty;
    public int Amount;
    public int Delivered;
    public int Reward;
    public int Reputation;
    public string Track = string.Empty;

    /// <summary>
    /// Real time left: to deliver for a held contract, before it's withdrawn for an open one.
    /// </summary>
    public TimeSpan Remaining;

    /// <summary>
    /// Real time to deliver once accepted, for an open contract.
    /// </summary>
    public TimeSpan TimeToDeliver;
}

[Serializable, NetSerializable]
public sealed class ContractAcceptMessage : CartridgeMessageEvent
{
    public readonly NetEntity Station;
    public readonly int Id;

    public ContractAcceptMessage(NetEntity station, int id)
    {
        Station = station;
        Id = id;
    }
}

[Serializable, NetSerializable]
public sealed class ContractAbandonMessage : CartridgeMessageEvent
{
    public readonly NetEntity Station;
    public readonly int Id;

    public ContractAbandonMessage(NetEntity station, int id)
    {
        Station = station;
        Id = id;
    }
}

[Serializable, NetSerializable]
public sealed class ContractsRefreshMessage : CartridgeMessageEvent;
