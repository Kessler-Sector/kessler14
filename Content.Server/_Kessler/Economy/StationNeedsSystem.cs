using System.Linq;
using Content.Server._NF.Cargo.Systems;
using Content.Server.Station.Systems;
using Content.Shared._Kessler.Contracts;
using Content.Shared._Kessler.Economy;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Kessler.Economy;

/// <summary>
/// A station's stocks (see <see cref="StationNeedPrototype"/>). Saved on the station with real UTC times; drain is
/// applied when read, so restarts need nothing special.
/// </summary>
[RegisterComponent, Access(typeof(StationNeedsSystem))]
public sealed partial class StationNeedsComponent : Component
{
    [DataField]
    public Dictionary<string, StationNeedState> Needs = new();
}

[DataDefinition]
public sealed partial class StationNeedState
{
    [DataField]
    public float Level;

    [DataField]
    public DateTime LastUpdate;
}

/// <summary>
/// Station needs (docs/kessler/design/departments.md, 5.3): stocks drain over real time, goods sold on the station's
/// pallets refill them, and low stocks raise prices and get contracts posted (see ContractSystem).
/// </summary>
public sealed class StationNeedsSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NFEntitySoldEvent>(OnEntitiesSold);
    }

    public IEnumerable<StationNeedPrototype> Needs => _proto.EnumeratePrototypes<StationNeedPrototype>().OrderBy(n => n.ID);

    /// <summary>
    /// A stock's level now, from 0 to its capacity.
    /// </summary>
    public float GetLevel(EntityUid station, StationNeedPrototype need, DateTime now)
    {
        var state = GetState(station, need, now);
        var hours = (float) (now - state.LastUpdate).TotalHours;
        if (hours > 0)
        {
            state.Level = Math.Max(0, state.Level - need.DrainPerHour * hours);
            state.LastUpdate = now;
        }

        return state.Level;
    }

    public float GetFraction(EntityUid station, StationNeedPrototype need, DateTime now)
    {
        return need.Capacity <= 0 ? 1f : GetLevel(station, need, now) / need.Capacity;
    }

    public bool IsLow(EntityUid station, StationNeedPrototype need, DateTime now)
    {
        return GetFraction(station, need, now) < need.LowThreshold;
    }

    public IEnumerable<StationNeedPrototype> LowNeeds(EntityUid station, DateTime now)
    {
        return Needs.Where(n => IsLow(station, n, now));
    }

    /// <summary>
    /// Adds goods to a stock, up to its capacity. Returns the units taken.
    /// </summary>
    public float Restock(EntityUid station, StationNeedPrototype need, float units, DateTime now)
    {
        var level = GetLevel(station, need, now);
        var taken = Math.Min(units, need.Capacity - level);
        if (taken <= 0)
            return 0;

        GetState(station, need, now).Level = level + taken;
        return taken;
    }

    /// <summary>
    /// The extra price multiplier a station's low stocks put on a market category: up to the need's boost when empty.
    /// </summary>
    public float DemandBoost(EntityUid station, string category, DateTime now)
    {
        if (!HasComp<StationNeedsComponent>(station))
            return 1f;

        var boost = 1f;
        foreach (var need in Needs)
        {
            if (need.Category != category)
                continue;

            var fraction = GetFraction(station, need, now);
            if (fraction >= need.LowThreshold || need.LowThreshold <= 0)
                continue;

            boost = Math.Max(boost, 1f + need.MaxDemandBoost * (1f - fraction / need.LowThreshold));
        }

        return boost;
    }

    private StationNeedState GetState(EntityUid station, StationNeedPrototype need, DateTime now)
    {
        var comp = EnsureComp<StationNeedsComponent>(station);
        if (!comp.Needs.TryGetValue(need.ID, out var state))
        {
            // New stations start part-stocked, some of them already short.
            state = new StationNeedState { Level = need.Capacity * _random.NextFloat(0.3f, 0.9f), LastUpdate = now };
            comp.Needs[need.ID] = state;
        }

        return state;
    }

    private void OnEntitiesSold(ref NFEntitySoldEvent ev)
    {
        if (_station.GetOwningStation(ev.Grid) is not { } station)
            return;

        var now = DateTime.UtcNow;
        foreach (var sold in ev.Sold)
        {
            RestockFrom(station, sold, now);
        }
    }

    /// <summary>
    /// Counts a sold entity, and anything inside it, towards the stocks it matches.
    /// </summary>
    public void RestockFrom(EntityUid station, EntityUid uid, DateTime now)
    {
        foreach (var need in Needs)
        {
            if (need.Contracts.Any(c => _proto.TryIndex(c, out var proto) && proto.Matches(uid, EntityManager, _whitelist)))
            {
                Restock(station, need, ProcurementMatching.Units(uid, EntityManager), now);
                break;
            }
        }

        if (!TryComp<ContainerManagerComponent>(uid, out var containers))
            return;

        foreach (var container in containers.Containers.Values)
        {
            foreach (var child in container.ContainedEntities)
            {
                RestockFrom(station, child, now);
            }
        }
    }
}
