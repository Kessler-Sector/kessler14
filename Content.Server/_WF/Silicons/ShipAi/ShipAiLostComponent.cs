namespace Content.Server._WF.Silicons.ShipAi;

[RegisterComponent]
public sealed partial class ShipAiLostComponent : Component
{
    [ViewVariables]
    public TimeSpan NextAlert;
}
