using Content.Shared.Access;
using Robust.Shared.Prototypes;
using Robust.Shared.Audio;
using Content.Shared._NF.Shipyard;
using Content.Shared.Radio;

namespace Content.Shared._WF.Silicons.ShipAi;

[RegisterComponent]
public sealed partial class ShipAiInterfaceComponent : Component
{
    [DataField]
    public List<ProtoId<AccessLevelPrototype>> AccessLevels = new();

    [DataField]
    public Dictionary<ShipAiFaction, List<ProtoId<AccessLevelPrototype>>> FactionAccessLevels = new();

    [DataField]
    public List<ProtoId<RadioChannelPrototype>> BaseChannels = new();

    [DataField]
    public Dictionary<ShipyardConsoleUiKey, ShipAiFaction> Factions = new();

    [DataField(required: true)]
    public TimeSpan EjectDelay;

    [DataField(required: true)]
    public float RemoteGrace;

    [DataField(required: true)]
    public float RemoteRange;

    [DataField(required: true)]
    public TimeSpan BootTime;

    [DataField(required: true)]
    public float StaticDamage;

    [DataField(required: true)]
    public TimeSpan ScanInterval;

    [DataField(required: true)]
    public SoundSpecifier BootSound = default!;

    [DataField(required: true)]
    public SoundSpecifier ShutdownSound = default!;

    [DataField(required: true)]
    public SoundSpecifier AttackSound = default!;

    [DataField(required: true)]
    public TimeSpan AttackAlertCooldown;

    [ViewVariables]
    public TimeSpan NextAttackAlert;

    [DataField(required: true)]
    public bool DamageAlerts;

    [DataField(required: true)]
    public List<double> AlertIntegrity = new();

    [ViewVariables]
    public EntityUid? AccessTarget;

    [ViewVariables]
    public EntityUid? SplitBlocker;
}
