namespace Content.Server._Kessler.Ownership;

/// <summary>
/// Who owns a bought ship. Unlike the deed, which only stores the owner's name, this identifies the owning character
/// by account and database profile, so upkeep billing and storage can find them while they're offline and after the
/// character is renamed.
/// </summary>
[RegisterComponent, Access(typeof(ShipOwnershipSystem))]
public sealed partial class ShipOwnershipComponent : Component
{
    /// <summary>
    /// The owner's account (<c>NetUserId.UserId</c>).
    /// </summary>
    [DataField]
    public Guid OwnerUserId;

    /// <summary>
    /// The owner's character slot at the time of purchase. Only used to look up <see cref="OwnerProfileId"/>.
    /// </summary>
    [DataField]
    public int OwnerSlot = -1;

    /// <summary>
    /// The owning character's database profile ID. This is the stable identity: it survives renames and stays with the
    /// character if the player edits it, but a deleted and recreated character in the same slot gets a new one.
    /// Null until the database lookup finishes, or if it failed.
    /// </summary>
    [DataField]
    public int? OwnerProfileId;

    /// <summary>
    /// The owner's character name at the time of purchase, for display.
    /// </summary>
    [DataField]
    public string OwnerName = string.Empty;

    /// <summary>
    /// What the ship cost. Upkeep is a share of this.
    /// </summary>
    [DataField]
    public int PurchasePrice;

    /// <summary>
    /// Starter ships bought with a voucher pay no upkeep.
    /// </summary>
    [DataField]
    public bool PurchasedWithVoucher;

    /// <summary>
    /// When the ship was bought, in UTC.
    /// </summary>
    [DataField]
    public DateTime PurchasedAt;
}
