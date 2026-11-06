using Robust.Shared.Serialization;

namespace Content.Shared._Kessler.Market;

/// <summary>
/// What the market board PDA program shows: every station with a pallet console and what it pays for each category of
/// goods right now, as a percentage of normal value.
/// </summary>
[Serializable, NetSerializable]
public sealed class MarketBoardUiState : BoundUserInterfaceState
{
    public readonly List<MarketBoardStation> Stations;

    /// <summary>
    /// The station the PDA is at, if any, so it can be marked.
    /// </summary>
    public readonly NetEntity? Here;

    public MarketBoardUiState(List<MarketBoardStation> stations, NetEntity? here)
    {
        Stations = stations;
        Here = here;
    }
}

[Serializable, NetSerializable]
public sealed class MarketBoardStation
{
    public readonly NetEntity Market;
    public readonly string Name;
    public readonly List<MarketBoardRate> Rates;

    public MarketBoardStation(NetEntity market, string name, List<MarketBoardRate> rates)
    {
        Market = market;
        Name = name;
        Rates = rates;
    }
}

[Serializable, NetSerializable]
public readonly record struct MarketBoardRate(string Category, int Percent);

/// <summary>
/// Asks the market board program for fresh prices.
/// </summary>
[Serializable, NetSerializable]
public sealed class MarketBoardRefreshMessage : Content.Shared.CartridgeLoader.CartridgeMessageEvent;
