using Content.Shared._Kessler.Reputation;
using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Kessler.Licences;

/// <summary>
/// A licence a character can buy once they meet its requirements (docs/kessler/design/progression.md, 4.3). Licences
/// are per character, kept in the database, and never expire.
/// </summary>
[Prototype("licence")]
public sealed partial class LicencePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public LocId? Description;

    [DataField]
    public int Order;

    /// <summary>
    /// Spesos taken from the bank when the licence is bought.
    /// </summary>
    [DataField]
    public int Fee;

    /// <summary>
    /// Reputation needed. Each entry must be met.
    /// </summary>
    [DataField]
    public List<LicenceReputationRequirement> Reputation = new();

    /// <summary>
    /// A licence that must have been held for <see cref="PreviousDays"/> real days first.
    /// </summary>
    [DataField]
    public ProtoId<LicencePrototype>? Previous;

    [DataField]
    public int PreviousDays;

    /// <summary>
    /// Ship sizes this licence lets a character buy.
    /// </summary>
    [DataField]
    public List<VesselSize> ShipSizes = new();

    /// <summary>
    /// If set, ships at or above this price need this licence as well.
    /// </summary>
    [DataField]
    public int? MinShipPrice;
}

[DataDefinition]
public sealed partial class LicenceReputationRequirement
{
    /// <summary>
    /// The track, or none for "any one track".
    /// </summary>
    [DataField]
    public ProtoId<ReputationTrackPrototype>? Track;

    [DataField(required: true)]
    public int Level;
}
