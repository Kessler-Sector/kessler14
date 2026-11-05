namespace Content.Server._Kessler.CryoSleep;

/// <summary>
/// Holds bodies in cryo storage on the sector map, so they are saved with the sector. Frontier kept them on a separate
/// paused map and in memory, so every cryo'd character was lost on restart. The bodies sit in a container on this
/// entity and are kept paused, so they don't starve or suffocate.
/// </summary>
[RegisterComponent]
public sealed partial class KesslerCryoStorageComponent : Component
{
    public const string ContainerId = "kessler_cryo_storage";

    /// <summary>
    /// The cryo system's list of stored bodies, written here right before each sector save and read back on load.
    /// </summary>
    [DataField]
    public List<KesslerStoredBody> Bodies = new();
}

[DataDefinition]
public sealed partial class KesslerStoredBody
{
    [DataField]
    public Guid UserId;

    [DataField]
    public EntityUid Body;

    [DataField]
    public EntityUid Cryopod;

    [DataField]
    public EntityUid Mind;

    [DataField]
    public string StationName = string.Empty;

    [DataField]
    public int CharacterSlot = -1;
}
