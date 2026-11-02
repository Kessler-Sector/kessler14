using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Silicons.ShipAi;

[RegisterComponent]
public sealed partial class ShipAiModuleComponent : Component
{
    [DataField]
    public bool Enabled = true;

    [DataField]
    public ComponentRegistry Components = new();

    [DataField]
    public List<EntProtoId> Actions = new();

    [ViewVariables]
    public List<EntityUid> GrantedActions = new();
}
