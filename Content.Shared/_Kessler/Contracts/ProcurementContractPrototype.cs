using Content.Shared._Kessler.Reputation;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;

namespace Content.Shared._Kessler.Contracts;

/// <summary>
/// A kind of procurement contract for the contract board (docs/kessler/design/departments.md, 5.2): a station wants N
/// of something, delivered by selling it on one of its pallets. Stations post these on their own; a character accepts
/// one in the Contracts PDA program and is paid on top of the pallet price when the last of it is sold.
/// </summary>
[Prototype("procurementContract")]
public sealed partial class ProcurementContractPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// What's wanted, in the plural, as shown on the board ("steel sheets").
    /// </summary>
    [DataField(required: true)]
    public LocId Item;

    /// <summary>
    /// Matches stacks of this type; each item in a stack counts.
    /// </summary>
    [DataField]
    public string? Stack;

    /// <summary>
    /// Matches entities passing this whitelist (and not <see cref="Blacklist"/>), one each, or by stack count.
    /// </summary>
    [DataField]
    public EntityWhitelist? Whitelist;

    [DataField]
    public EntityWhitelist? Blacklist;

    [DataField]
    public int MinAmount = 10;

    [DataField]
    public int MaxAmount = 30;

    /// <summary>
    /// Expected minutes of work per unit. Pay is the minutes times <c>kessler.contracts.rate</c>; reputation is a point
    /// a minute on <see cref="Track"/>.
    /// </summary>
    [DataField]
    public float MinutesPerUnit = 0.2f;

    [DataField]
    public ProtoId<ReputationTrackPrototype> Track = "Freight";

    /// <summary>
    /// Real hours to deliver once accepted.
    /// </summary>
    [DataField]
    public float Hours = 2f;

    [DataField]
    public float Weight = 1f;
}
