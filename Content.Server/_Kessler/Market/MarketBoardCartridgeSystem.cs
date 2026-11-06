using System.Linq;
using Content.Server._Kessler.Economy;
using Content.Server._NF.Cargo.Components;
using Content.Server.Station.Systems;
using Content.Server.CartridgeLoader;
using Content.Shared._Kessler.CCVar;
using Content.Shared._Kessler.Market;
using Content.Shared.CartridgeLoader;
using Robust.Shared.Configuration;

namespace Content.Server._Kessler.Market;

/// <summary>
/// A PDA program for the market ledger (docs/kessler/design/departments.md, 5.1): lists what every station with a
/// pallet console pays for each category of goods right now, so haulers can see where a load is worth most.
/// </summary>
[RegisterComponent]
public sealed partial class MarketBoardCartridgeComponent : Component;

public sealed class MarketBoardCartridgeSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly MarketLedgerSystem _ledger = default!;
    [Dependency] private readonly StationNeedsSystem _needs = default!;
    [Dependency] private readonly StationSystem _station = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MarketBoardCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<MarketBoardCartridgeComponent, CartridgeMessageEvent>(OnMessage);
    }

    private void OnUiReady(Entity<MarketBoardCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        _cartridgeLoader.UpdateCartridgeUiState(args.Loader, BuildState(args.Loader));
    }

    private void OnMessage(Entity<MarketBoardCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        var loader = GetEntity(args.LoaderUid);
        _cartridgeLoader.UpdateCartridgeUiState(loader, BuildState(loader));
    }

    public MarketBoardUiState BuildState(EntityUid viewer)
    {
        var stations = new List<MarketBoardStation>();
        if (!_cfg.GetCVar(KesslerCVars.CargoMarketEnabled))
            return new MarketBoardUiState(stations, null);

        var now = DateTime.UtcNow;
        var seen = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<NFCargoPalletConsoleComponent>();
        while (query.MoveNext(out var console, out _))
        {
            if (_ledger.GetMarket(console) is not { } market || !seen.Add(market.Owner))
                continue;

            var rates = _ledger.Categories
                .Select(c => new MarketBoardRate(Loc.GetString(c.Name), (int) Math.Round(_ledger.Multiplier(market, c, now) * 100)))
                .ToList();
            var needs = new List<MarketBoardRate>();
            if (_station.GetOwningStation(console) == market.Owner)
            {
                needs = _needs.Needs
                    .Select(n => new MarketBoardRate(Loc.GetString(n.Name), (int) Math.Round(_needs.GetFraction(market.Owner, n, now) * 100)))
                    .ToList();
            }

            stations.Add(new MarketBoardStation(GetNetEntity(market.Owner), Name(market.Owner), rates, needs));
        }

        stations.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        var here = _ledger.GetMarket(viewer) is { } local && seen.Contains(local.Owner) ? GetNetEntity(local.Owner) : (NetEntity?) null;
        return new MarketBoardUiState(stations, here);
    }
}
