using Robust.Shared.Prototypes;

namespace Content.Shared._Kessler.Reputation;

/// <summary>
/// One NPC body a character earns reputation with (docs/kessler/design/progression.md). Reputation is per character
/// and kept in the database, so it counts while the character is offline, in cryo or stored.
/// </summary>
[Prototype("reputationTrack")]
public sealed partial class ReputationTrackPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// The NPC body, e.g. "Dusk Station Authority".
    /// </summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>
    /// What earns it, shown in the reputation list.
    /// </summary>
    [DataField]
    public LocId? Description;

    [DataField]
    public int Order;
}
