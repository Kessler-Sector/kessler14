namespace Content.Shared._WF.Silicons.ShipAi;

[RegisterComponent]
public sealed partial class ShipAiEyeComponent : Component
{
    [DataField(required: true)]
    public float MercyRange;
}
