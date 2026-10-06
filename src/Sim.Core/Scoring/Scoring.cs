using Sim.Core.Model;
using Sim.Core.Util;

namespace Sim.Core.Scoring;

public sealed class ScoreCard
{
    public double Total, Prosperity, Living, Stability, Sustainability, Resilience;
    public string Grade = "";
    public Dictionary<string, double> Detail = new();
}

/// <summary>Composite national scorecard: growth, living standards, stability, sustainability and resilience (0-100 each).</summary>
public static class Scorer
{
    public const double WProsperity = 0.25, WLiving = 0.25, WStability = 0.20, WSustainability = 0.15, WResilience = 0.15;

    static double Sig(double x, double mid, double scale) => 100.0 * Maths.Sigmoid((x - mid) / scale);

    public static ScoreCard Compute(World w, CountryState c)
    {
        var d = new Dictionary<string, double>();
        double years = Math.Max(0.5, w.Month / 12.0);

        // ---- prosperity: real GDP per head growth ----
        // judged on a blend of actual and potential output so overheating cannot be banked as prosperity
        double pc0 = c.Gdp0 / c.Pop0, pcNow = (0.5 * c.Gdp + 0.5 * Math.Min(c.Gdp, c.Potential)) / c.Pop;
        double cagr = Math.Pow(Math.Max(1e-6, pcNow / pc0), 1 / years) - 1;
        double prosperity = w.Month < 6 ? 50 : Sig(cagr, 0.015, 0.012);
        d["gdp_per_head_cagr"] = cagr;

        // ---- living standards ----
        double wageCagr = Math.Pow(Math.Max(1e-6, c.RealWageIdx), 1 / years) - 1;
        double living = (Sig(wageCagr, 0.01, 0.01) + Sig(-(c.Unemp - 0.05), 0, 0.02) + Sig(-(c.Gini - 0.38), 0, 0.05) + Sig(c.LifeExp - 75, 0, 6)) / 4;

        // ---- stability ----
        double infl = 100 * Math.Exp(-Math.Pow((c.Inflation - c.InflTarget) / 0.03, 2));
        double stability = (Sig(c.Approval - 0.40, 0, 0.12) + Sig(c.Stability - 0.55, 0, 0.12) + infl + Sig(-(c.Unrest - 0.25), 0, 0.12) + Sig(-(Math.Abs(c.Gap) - 0.04), 0, 0.03)) / 5;

        // ---- sustainability ----
        double ceiling = Math.Max(0.60, c.DebtGdp0);
        double intensity = c.EmissionsMt / Math.Max(1e-9, c.Gdp), intensity0 = c.EmissionsMt0 / Math.Max(1e-9, c.Gdp0);
        double cut = 1 - intensity / intensity0;
        double sustain = (Sig(-(c.DebtToGdp - ceiling), 0, 0.25) + Sig(-(c.DeficitToGdp - 0.03), 0, 0.03)
                          + Sig(cut - 0.15 * Math.Min(years, 10) / 10.0, 0.0, 0.12) + Sig(c.Renewables - 0.35, 0, 0.15)) / 4;
        d["emission_intensity_cut"] = cut;

        // ---- resilience ----
        double hhi = 0; double tot = c.SectorVa.Sum();
        for (int s = 0; s < Dim.Sectors; s++) { double sh = c.SectorVa[s] / Math.Max(1e-9, tot); hhi += sh * sh; }
        double diversification = (1 - hhi) / (1 - 1.0 / Dim.Sectors);
        double resilience = (Sig(c.Reserves - 4, 0, 2) + 100 * Math.Exp(-Math.Pow(c.CaToGdp / 0.06, 2))
                             + Sig(0.01 - c.RiskPremium, 0, 0.01) + Sig(diversification - 0.6, 0, 0.12)) / 4;
        if (c.InDefault) resilience = Math.Max(0, resilience - 30);

        var sc = new ScoreCard
        {
            Prosperity = prosperity, Living = living, Stability = stability, Sustainability = sustain, Resilience = resilience, Detail = d,
        };
        sc.Total = WProsperity * prosperity + WLiving * living + WStability * stability + WSustainability * sustain + WResilience * resilience;
        sc.Grade = sc.Total >= 85 ? "S" : sc.Total >= 75 ? "A" : sc.Total >= 65 ? "B" : sc.Total >= 55 ? "C" : sc.Total >= 45 ? "D" : "F";
        return sc;
    }
}
