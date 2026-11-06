using Content.Shared._Kessler.Contracts;
using Content.Shared._Kessler.Market;
using Robust.Shared.Prototypes;

namespace Content.Shared._Kessler.Economy;

/// <summary>
/// A stock every trading station keeps (docs/kessler/design/departments.md, 5.3). It drains over real time and is
/// refilled by goods sold on the station's pallets. When it runs low the station posts contracts for it and pays more
/// for its category of goods, so demand is real and finite and comes back every day.
/// </summary>
[Prototype("stationNeed")]
public sealed partial class StationNeedPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>
    /// Goods that refill the stock, and the contracts posted when it's low.
    /// </summary>
    [DataField(required: true)]
    public List<ProtoId<ProcurementContractPrototype>> Contracts = new();

    /// <summary>
    /// Units of goods a full stock holds.
    /// </summary>
    [DataField]
    public float Capacity = 300f;

    /// <summary>
    /// Units used per real hour.
    /// </summary>
    [DataField]
    public float DrainPerHour = 20f;

    /// <summary>
    /// Below this fraction of capacity the stock counts as low.
    /// </summary>
    [DataField]
    public float LowThreshold = 0.5f;

    /// <summary>
    /// Market category whose price rises while the stock is low, by up to <see cref="MaxDemandBoost"/> when empty.
    /// </summary>
    [DataField]
    public ProtoId<MarketCategoryPrototype>? Category;

    [DataField]
    public float MaxDemandBoost = 0.5f;
}
