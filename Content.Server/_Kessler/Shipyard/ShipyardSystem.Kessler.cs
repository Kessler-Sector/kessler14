using Content.Server._Kessler.Licences;
using Content.Shared._NF.Shipyard.Components;

namespace Content.Server._NF.Shipyard.Systems;

// Kessler: deed handling for ship storage (deeds can only be edited by the shipyard system), and pilot licences.
public sealed partial class ShipyardSystem
{
    [Dependency] private readonly KesslerLicenceSystem _kesslerLicences = default!;

    /// <summary>
    /// Removes the deed for <paramref name="shuttle"/> from every ID card that holds one, before the ship is stored.
    /// The ship's own deed goes into storage with it.
    /// </summary>
    public void ClearDeedsForStoredShip(EntityUid shuttle)
    {
        var query = EntityQueryEnumerator<ShuttleDeedComponent>();
        var toRemove = new List<EntityUid>();
        while (query.MoveNext(out var uid, out var deed))
        {
            if (uid != shuttle && deed.ShuttleUid == shuttle)
                toRemove.Add(uid);
        }

        foreach (var uid in toRemove)
            RemComp<ShuttleDeedComponent>(uid);
    }

    /// <summary>
    /// Puts the deed for a recalled ship on <paramref name="idCard"/>, copied from the deed the ship carries.
    /// </summary>
    public void AssignRecalledShipDeed(EntityUid idCard, EntityUid shuttle)
    {
        if (!TryComp<ShuttleDeedComponent>(shuttle, out var shipDeed))
            return;

        var name = shipDeed.ShuttleNameSuffix is { } suffix ? $"{shipDeed.ShuttleName} {suffix}" : shipDeed.ShuttleName;
        var deed = EnsureComp<ShuttleDeedComponent>(idCard);
        AssignShuttleDeedProperties((idCard, deed), shuttle, name, shipDeed.ShuttleOwner, shipDeed.PurchasedWithVoucher);
        AssignShuttleDeedProperties((shuttle, shipDeed), shuttle, name, shipDeed.ShuttleOwner, shipDeed.PurchasedWithVoucher);
    }
}
