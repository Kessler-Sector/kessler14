using Content.Server.Atmos.EntitySystems;
using Content.Server.Station.Systems;
using Content.Shared._Kessler.CCVar;
using Content.Shared.Atmos;
using Robust.Shared.Configuration;

namespace Content.Server._Kessler.Economy;

/// <summary>
/// Gas sale prices with market saturation (docs/kessler/design/departments.md, 5.1 and A2). Each station tracks how much
/// of each gas it has bought recently; that decays with a half-life in real time. The next mole sells for
/// <c>price / (1 + saturation / K)</c>, so selling <c>Q</c> moles into a fresh market pays
/// <c>price * K * ln(1 + Q / K)</c>: selling K moles pays 69% of the flat price, 4K pays 40% on average.
/// </summary>
public sealed class GasMarketSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly StationSystem _station = default!;

    /// <summary>
    /// What <paramref name="mixture"/> sells for at <paramref name="seller"/>'s station, paid by purity as before.
    /// With <paramref name="commit"/> the sale is recorded against the station's market.
    /// </summary>
    public double Price(EntityUid seller, GasMixture mixture, bool commit, DateTime? now = null)
    {
        if (!_cfg.GetCVar(KesslerCVars.GasMarketEnabled)
            || _station.GetOwningStation(seller) is not { } station)
        {
            return _atmosphere.GetPrice(mixture);
        }

        return PriceAt(EnsureComp<StationGasMarketComponent>(station), mixture, commit, now ?? DateTime.UtcNow);
    }

    /// <summary>
    /// What <paramref name="mixture"/> sells for in <paramref name="market"/>, paid by purity.
    /// </summary>
    public double PriceAt(StationGasMarketComponent market, GasMixture mixture, bool commit, DateTime time)
    {
        var k = Math.Max(1f, _cfg.GetCVar(KesslerCVars.GasMarketDepth));
        var halfLife = TimeSpan.FromHours(Math.Max(0.01f, _cfg.GetCVar(KesslerCVars.GasMarketHalfLifeHours)));

        double value = 0;
        float totalMoles = 0;
        float maxComponent = 0;
        for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
        {
            var moles = mixture.GetMoles(i);
            if (moles <= 0)
                continue;

            totalMoles += moles;
            maxComponent = Math.Max(maxComponent, moles);

            var saturation = GetSaturation(market, i, time, halfLife);
            var price = _atmosphere.GetGas(i).PricePerMole;
            value += price * k * (Math.Log(1 + (saturation + moles) / k) - Math.Log(1 + saturation / k));

            if (commit)
                market.Gases[i] = new GasMarketEntry { Saturation = saturation + moles, LastUpdate = time };
        }

        var purity = totalMoles > 0 ? maxComponent / totalMoles : 1f;
        return value * purity;
    }

    /// <summary>
    /// The station's current saturation for a gas, decayed to <paramref name="now"/>.
    /// </summary>
    public float GetSaturation(StationGasMarketComponent market, int gas, DateTime now, TimeSpan halfLife)
    {
        if (!market.Gases.TryGetValue(gas, out var entry))
            return 0;

        var elapsed = now - entry.LastUpdate;
        if (elapsed <= TimeSpan.Zero)
            return entry.Saturation;

        return entry.Saturation * (float) Math.Pow(0.5, elapsed / halfLife);
    }
}
