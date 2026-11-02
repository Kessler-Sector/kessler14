using Content.Shared.Silicons.StationAi;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Silicons.ShipAi;

[RegisterComponent]
public sealed partial class ShipAiBrainComponent : Component
{
    [DataField]
    public Dictionary<ProtoId<StationAiCustomizationGroupPrototype>, ProtoId<StationAiCustomizationPrototype>> Look = new();

    [ViewVariables]
    public bool Briefed;
}
