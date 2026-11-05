namespace Content.Server._Kessler.Ownership;

/// <summary>
/// Who owns a bought ship. Unlike the deed, which only stores the owner's name, this identifies the owning character
/// by account and database profile, so upkeep billing and storage can find them while they're offline and after the
/// character is renamed.
/// </summary>
[RegisterComponent, Access(typeof(ShipOwnershipSystem), typeof(UpkeepSystem))]
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

    /// <summary>
    /// When the next day of upkeep is due, in UTC. Set at purchase to one billing interval later.
    /// </summary>
    [DataField]
    public DateTime NextUpkeep;

    /// <summary>
    /// Upkeep the owner couldn't pay. It is added to the next charge; balances never go negative.
    /// </summary>
    [DataField]
    public int UpkeepDebt;

    /// <summary>
    /// When the ship first went into debt, in UTC, or null when it has none.
    /// </summary>
    [DataField]
    public DateTime? DebtSince;

    /// <summary>
    /// The ship has been in debt for longer than the grace period and should be impounded into storage.
    /// </summary>
    [DataField]
    public bool ImpoundDue;

    /// <summary>
    /// A charge for this ship is waiting on the database. Not saved: a restart simply bills again.
    /// </summary>
    [ViewVariables]
    public bool ChargePending;
}
