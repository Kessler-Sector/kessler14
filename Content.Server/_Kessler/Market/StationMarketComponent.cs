namespace Content.Server._Kessler.Market;

/// <summary>
/// A sale point's market: how saturated it is with each category of goods and how much it wants each. Lives on the
/// station (or on the grid for a sale point outside any station). Times are real UTC, so restarts need no special
/// handling: decay and drift are applied when read.
/// </summary>
[RegisterComponent, Access(typeof(MarketLedgerSystem))]
public sealed partial class StationMarketComponent : Component
{
    [DataField]
    public Dictionary<string, MarketEntry> Entries = new();
}

[DataDefinition]
public sealed partial class MarketEntry
{
    /// <summary>
    /// Base value of goods sold recently, as of <see cref="LastUpdate"/>.
    /// </summary>
    [DataField]
    public float Saturation;

    [DataField]
    public DateTime LastUpdate;

    /// <summary>
    /// Multiplier on price from how much the station wants the category, as of <see cref="DemandUpdated"/>.
    /// </summary>
    [DataField]
    public float Demand = 1f;

    [DataField]
    public DateTime DemandUpdated;
}
