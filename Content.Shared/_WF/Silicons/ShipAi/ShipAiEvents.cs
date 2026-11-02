using Content.Shared.Access;
using Content.Shared.Actions;
using Content.Shared.Silicons.StationAi;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Content.Shared.DoAfter;
using Content.Shared.Eui;
using Robust.Shared.Serialization.TypeSerializers.Implementations;

namespace Content.Shared._WF.Silicons.ShipAi;

public enum ShipAiToggle : byte
{
    Engines,
    Generators,
    Bolts,
    Lights,
    ClearElectrified,
    Apcs,
    Shutters,
    BlastDoors,
    JobIcons,
}

public sealed partial class ShipAiToggleEvent : InstantActionEvent
{
    [DataField(required: true)]
    public ShipAiToggle Target;
}

public sealed partial class ShipAiOpenConsoleEvent : InstantActionEvent
{
    [DataField(required: true, customTypeSerializer: typeof(EnumSerializer))]
    public Enum? Key;
}

public sealed partial class ShipAiCameraScanEvent : EntityTargetActionEvent;

[Serializable, NetSerializable]
public sealed partial class ShipAiEjectDoAfterEvent : SimpleDoAfterEvent;

public enum ShipAiFaction : byte
{
    Cgp,
    Outlaw,
    Other,
}

[Serializable, NetSerializable]
public sealed class ShipAiBriefingEuiState(ShipAiFaction faction) : EuiStateBase
{
    public readonly ShipAiFaction Faction = faction;
}

[Serializable, NetSerializable]
public sealed class ShipAiEditAccessEvent : BaseStationAiAction;

[Serializable, NetSerializable]
public enum ShipAiAccessUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class ShipAiSetAccessMessage(List<ProtoId<AccessLevelPrototype>> access) : BoundUserInterfaceMessage
{
    public readonly List<ProtoId<AccessLevelPrototype>> Access = access;
}
