using Sim.Core.Model;
using Sim.Core.Util;

namespace Sim.Core.Engine;

/// <summary>Evolves world-level variables (commodities, world demand, global rates, climate).</summary>
public static class GlobalEngine
{
    public static void Step(World w, double dt)
    {
        var g = w.Global; var rng = w.WorldRng; bool st = w.Stochastic;
        double sq = Math.Sqrt(dt);
        int slot = w.Month % 12;

        double nOil = st ? rng.Normal() : 0, nFood = st ? rng.Normal() : 0, nRate = st ? rng.Normal() : 0, nCycle = st ? rng.Normal() : 0, nRisk = st ? rng.Normal() : 0;

        w.WorldCycle += -0.6 * w.WorldCycle * dt + 0.012 * sq * nCycle;
        g.WorldDemandIdx *= Math.Exp((g.WorldGrowthTrend + w.WorldCycle) * dt);
        g.WorldDemandTrend *= Math.Exp(g.WorldGrowthTrend * dt);

        double lnOil = Math.Log(g.OilIdx);
        lnOil += -0.30 * lnOil * dt + 0.30 * sq * nOil;
        g.OilIdx = Maths.Clamp(Math.Exp(lnOil), 0.3, 4.0);
        double lnFood = Math.Log(g.FoodIdx);
        lnFood += -0.40 * lnFood * dt + 0.12 * sq * nFood;
        g.FoodIdx = Maths.Clamp(Math.Exp(lnFood), 0.5, 2.5);
        g.OilYoY = g.OilIdx / g.OilRing[slot] - 1; g.OilRing[slot] = g.OilIdx;
        g.FoodYoY = g.FoodIdx / g.FoodRing[slot] - 1; g.FoodRing[slot] = g.FoodIdx;

        g.WorldRate = Maths.Clamp(g.WorldRate + 0.15 * (0.035 - g.WorldRate) * dt + 0.008 * sq * nRate, 0.0, 0.12);
        g.WorldInflation += (0.025 - g.WorldInflation) * 0.3 * dt;
        g.WorldPrice *= Math.Pow(1 + g.WorldInflation, dt);
        g.RiskAppetite = Maths.Clamp(g.RiskAppetite + 0.8 * (1 - g.RiskAppetite) * dt + 0.08 * sq * nRisk, 0.4, 1.3);
    }

    /// <summary>Global temperature responds to the sum of simulated emissions (scaled for roster coverage).</summary>
    public static void Climate(World w, double dt)
    {
        double mt = 0; foreach (var c in w.Countries) mt += c.EmissionsMt;
        double gt = mt / 1000.0 / w.RosterCoverage;
        w.Global.TempAnomaly += 0.00045 * gt * dt;
    }
}
