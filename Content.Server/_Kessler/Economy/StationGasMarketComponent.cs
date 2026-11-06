namespace Content.Server._Kessler.Economy;

/// <summary>
/// How saturated a station's market is with each gas. Selling adds to it, and it recovers over real time, so flooding
/// one station with one gas pays less and less while other stations still pay full price. Saved with the station.
/// </summary>
[RegisterComponent, Access(typeof(GasMarketSystem))]
public sealed partial class StationGasMarketComponent : Component
{
    /// <summary>
    /// Moles sold recently, per gas index, decayed to <see cref="GasMarketEntry.LastUpdate"/>.
    /// </summary>
    [DataField]
    public Dictionary<int, GasMarketEntry> Gases = new();
}

[DataDefinition]
public sealed partial class GasMarketEntry
{
    [DataField]
    public float Saturation;

    [DataField]
    public DateTime LastUpdate;
}
