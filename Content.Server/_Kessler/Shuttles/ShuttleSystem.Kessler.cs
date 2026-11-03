using Content.Server.Shuttles.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;

namespace Content.Server.Shuttles.Systems;

// Kessler: persistent saves only contain the sector map, so ships in hyperspace would be lost.
public sealed partial class ShuttleSystem
{
    /// <summary>
    /// Runs every pending FTL jump to completion at once, so every ship is back on a real map before a persistent save.
    /// Returns the number of ships that were in FTL.
    /// </summary>
    public int CompleteFtlForSave()
    {
        var ships = new List<Entity<FTLComponent, ShuttleComponent>>();
        var query = EntityQueryEnumerator<FTLComponent, ShuttleComponent>();
        while (query.MoveNext(out var uid, out var ftl, out var shuttle))
        {
            ships.Add((uid, ftl, shuttle));
        }

        foreach (var ship in ships)
        {
            // Each step advances the state; Cooldown is the state after arrival.
            if (ship.Comp1.State == FTLState.Starting)
                UpdateFTLStarting(ship);
            if (ship.Comp1.State == FTLState.Travelling)
                UpdateFTLTravelling(ship);
            if (ship.Comp1.State == FTLState.Arriving)
                UpdateFTLArriving(ship);

            RemComp<FTLComponent>(ship);
            _console.RefreshShuttleConsoles(ship);
        }

        return ships.Count;
    }
}
