using Robust.Shared.Prototypes;
using Content.Shared.Radio;
using Content.Shared.Roles;
namespace Content.Shared._WF.Silicons.ShipAi;

[RegisterComponent]
public sealed partial class ShipAiCoreComponent : Component
{
    [DataField]
    public ShipAiFaction? Faction;

    [DataField(required: true)]
    public Dictionary<ShipAiFaction, ComponentRegistry> StampComponents = new();

    [DataField]
    public Dictionary<ShipAiFaction, List<ProtoId<RadioChannelPrototype>>> StampChannels = new();

    [DataField]
    public Dictionary<ShipAiFaction, ProtoId<RadioChannelPrototype>> AlertChannels = new();

    [DataField(required: true)]
    public TimeSpan LostAlertInterval;

    [DataField]
    public Dictionary<ShipAiFaction, EntProtoId> IdCards = new();

    [DataField]
    public Dictionary<ShipAiFaction, ProtoId<JobPrototype>> Jobs = new();

    [DataField]
    public Dictionary<ShipAiFaction, List<EntProtoId>> StampActions = new();

    [ViewVariables]
    public bool Occupied;
}
