using Robust.Shared.Serialization;

namespace Content.Shared._WF.Silicons.ShipAi;

[Serializable, NetSerializable]
public enum ShipAiVisuals : byte
{
    Screen,
    Casing,
    Accessory,
    Static,
    NoBrain,
}

[Serializable, NetSerializable]
public enum ShipAiCoreVisuals : byte
{
    State,
}
