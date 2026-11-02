namespace Content.Server._WF.Silicons.ShipAi;

[RegisterComponent]
public sealed partial class ShipAiShipyardPointComponent : Component
{
    [DataField(required: true)]
    public string Point = string.Empty;
}
