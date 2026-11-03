using Content.Server._NF.PublicTransit.Components;
using Content.Server._NF.PublicTransit.Prototypes;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Prototypes;

namespace Content.Server._NF.PublicTransit;

// Kessler: bus routes in persistent saves.
// Routes are derived from each stop's StationTransitComponent and hold types the serializer cannot write, so persistent
// saves leave them out and rebuild them after a load.
public sealed partial class PublicTransitSystem
{
    private Dictionary<ProtoId<PublicTransitRoutePrototype>, PublicTransitRoute>? _routesDetachedForSave;

    /// <summary>
    /// Empties the sector's bus routes for the duration of a persistent save. Call
    /// <see cref="RestoreRoutesAfterSave"/> afterwards.
    /// </summary>
    public void DetachRoutesForSave()
    {
        if (!TryComp<SectorPublicTransitComponent>(_sectorService.GetServiceEntity(), out var comp))
            return;

        _routesDetachedForSave = comp.Routes;
        comp.Routes = new();
    }

    public void RestoreRoutesAfterSave()
    {
        if (_routesDetachedForSave == null)
            return;

        if (TryComp<SectorPublicTransitComponent>(_sectorService.GetServiceEntity(), out var comp))
            comp.Routes = _routesDetachedForSave;

        _routesDetachedForSave = null;
    }

    /// <summary>
    /// Registers the bus stops of a loaded persistent save.
    /// </summary>
    /// <remarks>
    /// Stops normally register on <see cref="ComponentStartup"/>, but during a load the sector service entity that holds
    /// the routes may not be set up yet when a stop starts, so all loaded stops register again once the load is done.
    /// </remarks>
    public void RegisterLoadedStops(LoadResult result)
    {
        var args = new ComponentStartup();
        var query = EntityQueryEnumerator<StationTransitComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (result.Entities.Contains(uid))
                OnStationStartup((uid, comp), ref args);
        }
    }
}
