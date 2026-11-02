namespace Content.Server._WF.Silicons.ShipAi;

[RegisterComponent]
public sealed partial class ShipAiScanComponent : Component
{
    [ViewVariables]
    public EntityUid Patient;

    [ViewVariables]
    public TimeSpan NextUpdate;
}
