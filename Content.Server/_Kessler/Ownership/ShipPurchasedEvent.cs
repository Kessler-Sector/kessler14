namespace Content.Server._Kessler.Ownership;

/// <summary>
/// Raised (broadcast) by the shipyard after a ship has been bought and its deed assigned.
/// </summary>
/// <param name="Shuttle">The new ship's grid.</param>
/// <param name="Buyer">The mob that bought it.</param>
/// <param name="Price">What the ship cost, before any voucher.</param>
/// <param name="PurchasedWithVoucher">Whether a voucher paid for it.</param>
[ByRefEvent]
public readonly record struct ShipPurchasedEvent(EntityUid Shuttle, EntityUid Buyer, int Price, bool PurchasedWithVoucher);
