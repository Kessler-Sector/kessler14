using System.Diagnostics.CodeAnalysis;
using Content.Server._NF.Station.Components;
using Content.Server.Maps;
using Content.Server.Shuttles.Components;
using Content.Server.StationEvents.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Database;

namespace Content.Server._NF.Shipyard.Systems;

public sealed partial class ShipyardSystem
{
    public bool TryPurchaseVessel(EntityUid station, VesselPrototype vessel, string owner, int paid, [NotNullWhen(true)] out EntityUid? shuttle, out EntityUid? shuttleStation)
    {
        shuttleStation = null;
        if (!TryPurchaseShuttle(station, vessel.ShuttlePath, out shuttle))
            return false;

        if (TryComp<ShuttleComponent>(shuttle, out var shuttleComp))
            shuttleComp.PlayerShuttle = true;

        var name = vessel.Name;
        if (_prototypeManager.TryIndex<GameMapPrototype>(vessel.ID, out var stationProto))
        {
            shuttleStation = _station.InitializeNewStation(stationProto.Stations[vessel.ID], [shuttle.Value]);
            name = Name(shuttleStation.Value);
            EnsureComp<ExtraShuttleInformationComponent>(shuttleStation.Value).Vessel = vessel.ID;
        }

        AssignShuttleDeedProperties((shuttle.Value, EnsureComp<ShuttleDeedComponent>(shuttle.Value)), shuttle, name, owner, paid <= 0);
        EntityManager.AddComponents(shuttle.Value, vessel.AddComponents);
        EnsureComp<LinkedLifecycleGridParentComponent>(shuttle.Value);

        _adminLogger.Add(LogType.ShipYardUsage, LogImpact.Low,
            $"{owner} bought shuttle {ToPrettyString(shuttle.Value)} as a ship AI for {paid} credits");
        return true;
    }

    public void SetDeedOwner(EntityUid uid, string owner)
    {
        if (!TryComp<ShuttleDeedComponent>(uid, out var deed))
            return;

        deed.ShuttleOwner = owner;
        Dirty(uid, deed);
    }

    public void CopyDeedToCard(EntityUid card, EntityUid grid)
    {
        if (!TryComp<ShuttleDeedComponent>(grid, out var shuttleDeed))
            return;

        AssignShuttleDeedProperties((card, EnsureComp<ShuttleDeedComponent>(card)),
            shuttleDeed.ShuttleUid,
            shuttleDeed.ShuttleName,
            shuttleDeed.ShuttleOwner,
            shuttleDeed.PurchasedWithVoucher);
    }
}
