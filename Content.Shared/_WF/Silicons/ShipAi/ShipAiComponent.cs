using Content.Shared.Whitelist;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Silicons.ShipAi;

[RegisterComponent, NetworkedComponent]
public sealed partial class ShipAiComponent : Component
{
    [DataField(required: true)]
    public EntityWhitelist Devices = new();

    [DataField(required: true)]
    public EntityWhitelist DevicesBlacklist = new();
}
