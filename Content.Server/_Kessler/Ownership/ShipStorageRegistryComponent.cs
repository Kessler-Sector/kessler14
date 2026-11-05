namespace Content.Server._Kessler.Ownership;

/// <summary>
/// The list of ships in storage. It lives on the sector map, so it is saved with the sector and always matches the
/// world: restoring an older save brings back the older list, never a list that points at ships still in the world.
/// </summary>
[RegisterComponent, Access(typeof(ShipStorageSystem))]
public sealed partial class ShipStorageRegistryComponent : Component
{
    [DataField]
    public List<StoredShip> Ships = new();
}

/// <summary>
/// Why a ship went into storage.
/// </summary>
public enum ShipStorageReason : byte
{
    /// <summary>Unpaid upkeep past the grace period. The debt must be paid to recall it.</summary>
    Impounded,

    /// <summary>The owner hadn't logged in for a while. Storage is free.</summary>
    Inactive,

    /// <summary>An admin stored it.</summary>
    Admin,
}

/// <summary>
/// One ship in storage: where its file is and who may recall it.
/// </summary>
[DataDefinition]
public sealed partial class StoredShip
{
    /// <summary>Identifies this entry; also the file name.</summary>
    [DataField]
    public Guid Id;

    /// <summary>The ship's file in the server data folder.</summary>
    [DataField]
    public string File = string.Empty;

    [DataField]
    public string ShipName = string.Empty;

    [DataField]
    public Guid OwnerUserId;

    [DataField]
    public int OwnerSlot = -1;

    [DataField]
    public int? OwnerProfileId;

    [DataField]
    public string OwnerName = string.Empty;

    [DataField]
    public ShipStorageReason Reason;

    /// <summary>Upkeep owed when it was stored. Paid on recall.</summary>
    [DataField]
    public int Debt;

    [DataField]
    public DateTime StoredAt;
}
