using Robust.Shared.Serialization;

namespace Content.Shared._NF.ShuttleRecords;

/**
 * A record of a shuttle that had been purchased.
 * This class is NOT a indication that the shuttle is still in the game, merely a transaction record of it.
 */
[Virtual, NetSerializable, Serializable]
[DataDefinition] // Kessler: saved with the sector services
public partial class ShuttleRecord( // Kessler: partial
    string name,
    string suffix,
    string ownerName,
    NetEntity entityUid,
    bool purchasedWithVoucher,
    uint purchasePrice,
    string vesselPrototypeId,
    TimeSpan? timeOfPurchase = null,
    TimeSpan? timeOfSale = null
)
{
    // Kessler: parameterless constructor required by DataDefinition
    public ShuttleRecord() : this("", "", "", NetEntity.Invalid, false, 0, "")
    {
    }
    // End Kessler

    [ViewVariables, DataField] // Kessler: DataField
    public string Name { get; set; } = name;

    /// <summary>
    /// The ID of the VesselPrototype this shuttle came from. Used internally
    /// for the shipyard statistics printout.
    /// </summary>
    [ViewVariables, DataField] // Kessler: DataField
    public string VesselPrototypeId { get; set; } = vesselPrototypeId;

    [ViewVariables, DataField] // Kessler: DataField
    public string? Suffix { get; set; } = suffix;

    [ViewVariables, DataField] // Kessler: DataField
    public string OwnerName { get; set; } = ownerName;

    /**
     * Entity is deleted when the ship gets sold.
     * Use EntityManager.EntityExists(EntityUid) to check if the entity still exists.
     */
    [ViewVariables, DataField] // Kessler: DataField
    public NetEntity EntityUid { get; set; } = entityUid;

    [ViewVariables, DataField] // Kessler: DataField
    public TimeSpan? TimeOfPurchase { get; set; } = timeOfPurchase;

    [ViewVariables, DataField] // Kessler: DataField
    public TimeSpan? TimeOfSale { get; set; } = timeOfSale;

    // The amount of spesos it had costed to purchase this vessel.
    [ViewVariables, DataField] // Kessler: DataField
    public uint PurchasePrice { get; set; } = purchasePrice;

    [ViewVariables, DataField] // Kessler: DataField
    public bool PurchasedWithVoucher { get; set; } = purchasedWithVoucher;
}
