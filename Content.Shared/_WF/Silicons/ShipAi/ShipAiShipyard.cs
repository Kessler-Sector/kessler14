using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Eui;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Content.Shared.Roles;

namespace Content.Shared._WF.Silicons.ShipAi;

[Prototype]
public sealed partial class ShipAiShipyardPrototype : IPrototype
{
    public static readonly ProtoId<ShipAiShipyardPrototype> DefaultId = "Default";

    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public EntProtoId Projection;

    [DataField(required: true)]
    public EntProtoId Interface;

    [DataField(required: true)]
    public Dictionary<string, ShipAiShipyardPoint> Points = new();
}

[DataDefinition]
public sealed partial class ShipAiShipyardPoint
{
    [DataField(required: true)]
    public List<ShipyardConsoleUiKey> Groups = new();

    [DataField]
    public Dictionary<ShipyardConsoleUiKey, EntProtoId> Modules = new();

    [DataField(required: true)]
    public ProtoId<JobPrototype> Job;

    [DataField]
    public bool Free;

    [DataField]
    public List<ProtoId<VesselPrototype>>? Whitelist;

    [DataField]
    public List<ProtoId<VesselPrototype>> Blacklist = new();
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShipAiProjectionComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Ship;

    [ViewVariables]
    public EntityUid? ShipStation;

    [ViewVariables]
    public int Paid;

    [ViewVariables]
    public bool Done;

    [ViewVariables]
    public EntProtoId? Module;
}

[Serializable, NetSerializable]
public sealed record ShipAiShipyardListing(string Vessel, int Price, ShipyardConsoleUiKey Group);

[Serializable, NetSerializable]
public sealed class ShipAiShipyardEuiState(List<ShipAiShipyardListing> listings, int balance) : EuiStateBase
{
    public readonly List<ShipAiShipyardListing> Listings = listings;
    public readonly int Balance = balance;
}

[Serializable, NetSerializable]
public sealed class ShipAiShipyardBuyMessage(string vessel) : EuiMessageBase
{
    public readonly string Vessel = vessel;
}

[Serializable, NetSerializable]
public sealed class ShipAiPlaceCoreRequest(NetCoordinates coordinates, Direction direction) : EntityEventArgs
{
    public readonly NetCoordinates Coordinates = coordinates;
    public readonly Direction Direction = direction;
}
