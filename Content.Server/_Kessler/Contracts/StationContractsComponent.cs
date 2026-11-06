using Content.Shared._Kessler.Contracts;
using Robust.Shared.Prototypes;

namespace Content.Server._Kessler.Contracts;

/// <summary>
/// The procurement contracts a station has posted, open or held. Saved with the station; times are real UTC.
/// </summary>
[RegisterComponent, Access(typeof(ContractSystem))]
public sealed partial class StationContractsComponent : Component
{
    [DataField]
    public List<ProcurementContract> Contracts = new();

    [DataField]
    public int NextId = 1;

    [DataField]
    public DateTime NextPost;
}

[DataDefinition]
public sealed partial class ProcurementContract
{
    [DataField]
    public int Id;

    [DataField]
    public ProtoId<ProcurementContractPrototype> Template;

    [DataField]
    public int Amount;

    [DataField]
    public int Delivered;

    [DataField]
    public int Minutes;

    [DataField]
    public int Reward;

    /// <summary>
    /// Open: when it's withdrawn if nobody takes it. Held: the delivery deadline.
    /// </summary>
    [DataField]
    public DateTime Expires;

    /// <summary>
    /// The holder's user id, or null while open.
    /// </summary>
    [DataField]
    public string? HolderUser;

    [DataField]
    public int HolderSlot = -1;

    [DataField]
    public string HolderName = string.Empty;

    public bool Held => HolderUser != null;
}
