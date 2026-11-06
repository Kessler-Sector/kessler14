using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Shared._Kessler.Market;

/// <summary>
/// A kind of goods with its own price at each station (docs/kessler/design/departments.md, 5.1). Selling a lot of one
/// category at one station lowers what it pays for more; that recovers over real time. Each station's demand for a
/// category also drifts slowly, so the same goods are worth more in some places than others. Goods that match no
/// category sell at their normal price.
/// </summary>
[Prototype("marketCategory")]
public sealed partial class MarketCategoryPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>
    /// Categories are checked from the highest priority down; the first that matches is used.
    /// </summary>
    [DataField]
    public int Priority;

    /// <summary>
    /// Matches entities with any of these components.
    /// </summary>
    [DataField]
    public List<string> Components = new();

    /// <summary>
    /// Matches entities with any of these tags.
    /// </summary>
    [DataField]
    public List<ProtoId<TagPrototype>> Tags = new();

    /// <summary>
    /// Spesos of base value a station can take before its price for the category has halved.
    /// </summary>
    [DataField]
    public float Depth = 50000f;

    /// <summary>
    /// The lowest fraction of base value a station ever pays, however saturated.
    /// </summary>
    [DataField]
    public float Floor = 0.2f;
}
