using System.Linq;
using Content.Server._Kessler.Economy;
using Content.Server._NF.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Station.Systems;
using Content.Shared._Kessler.CCVar;
using Content.Shared._Kessler.Market;
using Content.Shared.Examine;
using Content.Shared.Tag;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Kessler.Market;

/// <summary>
/// The sector market ledger for sold goods (docs/kessler/design/departments.md, 5.1 and C1). Each sale point belongs to
/// a station; each station tracks per category how much it has bought recently (decaying with a real-time half-life)
/// and how much it wants that category (a slow random walk). The next spesos of base value sell for
/// <c>demand / (1 + saturation / depth)</c>, never below the category's floor.
/// </summary>
public sealed class MarketLedgerSystem : EntitySystem
{
    [Dependency] private readonly IComponentFactory _compFactory = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly PricingSystem _pricing = default!;
    [Dependency] private readonly StationNeedsSystem _needs = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly TagSystem _tag = default!;

    /// <summary>
    /// Real time between demand drift steps.
    /// </summary>
    public static readonly TimeSpan DemandStep = TimeSpan.FromHours(6);

    public const float DemandStepSize = 0.05f;
    public const float MinDemand = 0.7f;
    public const float MaxDemand = 1.3f;

    private List<(MarketCategoryPrototype Proto, List<Type> Components)> _categories = new();

    public override void Initialize()
    {
        base.Initialize();
        _proto.PrototypesReloaded += OnPrototypesReloaded;
        SubscribeLocalEvent<NFCargoPalletConsoleComponent, ExaminedEvent>(OnPalletConsoleExamined);
        CacheCategories();
    }

    /// <summary>
    /// The pallet console shows what its station pays for each category right now.
    /// </summary>
    private void OnPalletConsoleExamined(Entity<NFCargoPalletConsoleComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange
            || !_cfg.GetCVar(KesslerCVars.CargoMarketEnabled)
            || GetMarket(ent) is not { } market)
        {
            return;
        }

        var now = DateTime.UtcNow;
        using (args.PushGroup(nameof(MarketLedgerSystem)))
        {
            args.PushMarkup(Loc.GetString("kessler-market-examine-header"));
            foreach (var category in Categories)
            {
                var percent = (int) Math.Round(Multiplier(market, category, now) * 100);
                var color = percent >= 100 ? "green" : percent >= 70 ? "yellow" : "red";
                args.PushMarkup(Loc.GetString("kessler-market-examine-line",
                    ("category", Loc.GetString(category.Name)), ("percent", percent), ("color", color)));
            }
        }
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _proto.PrototypesReloaded -= OnPrototypesReloaded;
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<MarketCategoryPrototype>())
            CacheCategories();
    }

    private void CacheCategories()
    {
        _categories = _proto.EnumeratePrototypes<MarketCategoryPrototype>()
            .OrderByDescending(c => c.Priority)
            .ThenBy(c => c.ID)
            .Select(c => (c, c.Components
                .Select(name => _compFactory.TryGetRegistration(name, out var reg) ? reg.Type : null)
                .OfType<Type>()
                .ToList()))
            .ToList();
    }

    public IEnumerable<MarketCategoryPrototype> Categories => _categories.Select(c => c.Proto);

    /// <summary>
    /// The market a sale point sells into: its station's, or its grid's if it isn't on a station.
    /// </summary>
    public Entity<StationMarketComponent>? GetMarket(EntityUid salePoint)
    {
        var owner = _station.GetOwningStation(salePoint) ?? Transform(salePoint).GridUid;
        if (owner == null)
            return null;

        return (owner.Value, EnsureComp<StationMarketComponent>(owner.Value));
    }

    /// <summary>
    /// Starts pricing a batch of goods at a sale point. Goods priced through the quote saturate it as they go, so a big
    /// load is priced the same whether sold at once or bit by bit; nothing is recorded until <see cref="Commit"/>.
    /// </summary>
    public MarketQuote BeginQuote(EntityUid salePoint, DateTime? now = null)
    {
        var enabled = _cfg.GetCVar(KesslerCVars.CargoMarketEnabled);
        return new MarketQuote(enabled ? GetMarket(salePoint) : null, now ?? DateTime.UtcNow);
    }

    /// <summary>
    /// What an entity sells for in the quote's market, including what it contains. Each item is priced by its own
    /// category, so goods packed in a crate are priced as those goods.
    /// </summary>
    public double Price(MarketQuote quote, EntityUid uid)
    {
        var total = _pricing.GetPrice(uid);
        if (quote.Market == null || total <= 0)
            return total;

        var children = new List<(EntityUid Uid, double Price)>();
        if (TryComp<ContainerManagerComponent>(uid, out var containers))
        {
            foreach (var container in containers.Containers.Values)
            {
                foreach (var child in container.ContainedEntities)
                {
                    children.Add((child, _pricing.GetPrice(child)));
                }
            }
        }

        // Some entities set their own price regardless of what they hold; those are priced as one item.
        var own = total - children.Sum(c => c.Price);
        if (own < -0.01)
            return PriceOne(quote, uid, total);

        var price = PriceOne(quote, uid, Math.Max(0, own));
        foreach (var (child, childPrice) in children)
        {
            if (childPrice > 0)
                price += Price(quote, child);
        }

        return price;
    }

    private double PriceOne(MarketQuote quote, EntityUid uid, double basePrice)
    {
        if (basePrice <= 0 || quote.Market is not { } market || GetCategory(uid) is not { } category)
            return basePrice;

        var saturation = GetSaturation(market, category, quote.Now) + quote.Pending.GetValueOrDefault(category.ID);
        var demand = GetDemand(market, category.ID, quote.Now) * _needs.DemandBoost(market, category.ID, quote.Now);
        var depth = Math.Max(1f, category.Depth);

        var value = depth * (Math.Log(1 + (saturation + basePrice) / depth) - Math.Log(1 + saturation / depth));
        value = Math.Max(value, basePrice * category.Floor);

        quote.Pending[category.ID] = quote.Pending.GetValueOrDefault(category.ID) + (float) basePrice;
        return value * demand;
    }

    /// <summary>
    /// Records the goods priced through a quote as sold.
    /// </summary>
    public void Commit(MarketQuote quote)
    {
        if (quote.Market is not { } market)
            return;

        foreach (var (id, amount) in quote.Pending)
        {
            if (!_proto.TryIndex<MarketCategoryPrototype>(id, out var category))
                continue;

            var saturation = GetSaturation(market, category, quote.Now);
            var entry = GetEntry(market, id, quote.Now);
            entry.Saturation = saturation + amount;
            entry.LastUpdate = quote.Now;
        }

        quote.Pending.Clear();
    }

    /// <summary>
    /// The category an entity sells as, or null for goods with no market of their own.
    /// </summary>
    public MarketCategoryPrototype? GetCategory(EntityUid uid)
    {
        foreach (var (proto, components) in _categories)
        {
            foreach (var type in components)
            {
                if (HasComp(uid, type))
                    return proto;
            }

            foreach (var tag in proto.Tags)
            {
                if (_tag.HasTag(uid, tag))
                    return proto;
            }
        }

        return null;
    }

    /// <summary>
    /// What the next spesos of base value in a category sells for at a market, as a fraction.
    /// </summary>
    public float Multiplier(Entity<StationMarketComponent> market, MarketCategoryPrototype category, DateTime now)
    {
        var saturation = GetSaturation(market, category, now);
        var multiplier = Math.Max(category.Floor, 1f / (1f + saturation / Math.Max(1f, category.Depth)));
        return multiplier * GetDemand(market, category.ID, now) * _needs.DemandBoost(market, category.ID, now);
    }

    public float GetSaturation(Entity<StationMarketComponent> market, MarketCategoryPrototype category, DateTime now)
    {
        if (!market.Comp.Entries.TryGetValue(category.ID, out var entry) || entry.Saturation <= 0)
            return 0;

        var elapsed = now - entry.LastUpdate;
        if (elapsed <= TimeSpan.Zero)
            return entry.Saturation;

        var halfLife = TimeSpan.FromHours(Math.Max(0.01f, _cfg.GetCVar(KesslerCVars.CargoMarketHalfLifeHours)));
        return entry.Saturation * (float) Math.Pow(0.5, elapsed / halfLife);
    }

    /// <summary>
    /// How much a market wants a category, after drifting a step for every <see cref="DemandStep"/> since last read.
    /// </summary>
    public float GetDemand(Entity<StationMarketComponent> market, string category, DateTime now)
    {
        var entry = GetEntry(market, category, now);
        var steps = (int) Math.Floor((now - entry.DemandUpdated) / DemandStep);
        if (steps <= 0)
            return entry.Demand;

        // Past a few weeks the walk has forgotten where it started anyway.
        for (var i = 0; i < Math.Min(steps, 100); i++)
        {
            entry.Demand = Math.Clamp(entry.Demand + _random.NextFloat(-DemandStepSize, DemandStepSize), MinDemand, MaxDemand);
        }

        entry.DemandUpdated += DemandStep * steps;
        return entry.Demand;
    }

    private MarketEntry GetEntry(Entity<StationMarketComponent> market, string category, DateTime now)
    {
        if (!market.Comp.Entries.TryGetValue(category, out var entry))
        {
            entry = new MarketEntry
            {
                LastUpdate = now,
                DemandUpdated = now,
                Demand = _random.NextFloat(0.9f, 1.1f),
            };
            market.Comp.Entries[category] = entry;
        }

        return entry;
    }
}

/// <summary>
/// A batch of goods being priced at one market. See <see cref="MarketLedgerSystem.BeginQuote"/>.
/// </summary>
public sealed class MarketQuote
{
    public readonly Entity<StationMarketComponent>? Market;
    public readonly DateTime Now;

    /// <summary>
    /// Base value priced so far per category, not yet recorded.
    /// </summary>
    public readonly Dictionary<string, float> Pending = new();

    public MarketQuote(Entity<StationMarketComponent>? market, DateTime now)
    {
        Market = market;
        Now = now;
    }
}
