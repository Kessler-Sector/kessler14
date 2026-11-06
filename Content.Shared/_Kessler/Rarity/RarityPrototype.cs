using Robust.Shared.Prototypes;

namespace Content.Shared._Kessler.Rarity;

/// <summary>
/// A rarity tier and the items in it. Examining an item in a tier shows the tier's name in its colour. Items inherit
/// the tier of the nearest parent prototype that is listed, so a stack or a spawner variant needs no entry of its own.
/// Departments add their items here instead of editing each entity.
/// </summary>
[Prototype("rarity")]
public sealed partial class RarityPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Shown on examine.
    /// </summary>
    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public Color Color = Color.White;

    /// <summary>
    /// Higher is rarer. If an item is listed under two tiers, the rarer one wins.
    /// </summary>
    [DataField]
    public int Order;

    [DataField]
    public List<EntProtoId> Entities = new();
}
