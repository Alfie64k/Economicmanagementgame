using Sim.Core.Data;
using Sim.Core.Model;
using Sim.Core.Util;

namespace Sim.Core.Engine;

/// <summary>
/// Wage bargaining, the wage-price spiral, long-term unemployment hysteresis, strike risk and the labour share of income.
/// <para>
/// <b>Wage setting.</b> Bargained wages are set against expected inflation; what they cannot see coming is the price shock: energy, food and
/// import prices, carbon pricing and the wage push of the previous month. Workers' claims follow the part of that shock beyond a 1-point
/// dead-band, indexed by bargaining power <c>B</c> (coverage times strength) and by how little they trust the central bank to bring prices back
/// (<c>1 - credibility</c>), plus a tightness term when unemployment is well below the natural rate. Settlements are staggered: contracts last about half
/// a year where nobody bargains and about two years where everybody does, so the wage premium <see cref="CountryState.WagePremium"/> moves slowly
/// and persists. Firms pass part of it into prices (<see cref="CountryState.WageSpiral"/>, added to the Phillips curve). Real wages fall first
/// (<see cref="CountryState.WageGap"/>), then the premium wins them back. A credible central bank damps the spiral twice: it lowers the claims and the pass-through.
/// </para>
/// <para>
/// <b>Hysteresis.</b> Unemployment more than a point above the natural rate feeds a stock of long-term unemployed who drain away only slowly; the stock raises
/// the natural rate by a fraction that is higher where insiders are protected (high bargaining power). It sits beside <see cref="CountryState.NairuParts"/>, not in it.
/// </para>
/// <para>
/// <b>Strikes</b> become likelier when real wages are down and bargaining power is high (<see cref="CountryState.StrikeRisk"/>); the existing "strike_wave" event
/// takes it as a probability multiplier, so realised strikes go through the usual per-country random streams.
/// </para>
/// <para>
/// <b>Labour share.</b> Its structural path follows bargaining power and slumps beyond a 2-point output gap; on top of that it is squeezed by any real-wage shortfall.
/// </para>
/// Everything is measured against the start and inside dead-bands, so a quiet economy leaves all of it at exactly its starting value.
/// </summary>
public static class LabourMarketEngine
{
    // ---- wage setting ----
    /// <summary>Price shocks, tightness and slack within this many points a year trigger no wage response.</summary>
    public const double Band = 0.01;
    /// <summary>Share of an unexpected price rise written into bargained wages (before the credibility discount), and how much of a fall is (downward wage rigidity).</summary>
    public const double Indexation = 2.0, FallWeight = 0.4;
    /// <summary>How much of the claim a fully credible central bank talks away (credibility is the share of the shock workers expect to be reversed).</summary>
    public const double Anchoring = 0.7;
    /// <summary>Extra premium per point of tightness (unemployment below the natural rate beyond the band), per unit of bargaining power.</summary>
    public const double TightnessWeight = 0.3;
    /// <summary>Share of the wage premium that reaches prices, and how much a fully credible central bank cuts it.</summary>
    public const double PassThrough = 0.5, CredibilityDamping = 0.4;
    /// <summary>Share of workers whose pay is fixed ahead (so a price shock cuts their real wage), and the pace at which the market restores a real-wage gap by itself (per year).</summary>
    public const double Exposure = 0.8, Restore = 0.4;
    public const double PremiumMin = -0.04, PremiumMax = 0.12, GapMin = -0.10, GapMax = 0.30;

    // ---- strikes ----
    /// <summary>A real-wage shortfall within this band causes no unrest; beyond it the hazard is 1 - exp(-K x B x excess).</summary>
    public const double StrikeBand = 0.01, StrikeK = 40.0;

    // ---- hysteresis ----
    /// <summary>Annual flow into long-term unemployment per point of slack beyond the band, annual exit rate, and the faster exit when the labour market is tight.</summary>
    public const double ScarInflow = 0.7, ScarExit = 0.2, ScarTight = 1.5;
    /// <summary>Fraction of the long-term stock that becomes a higher natural rate: the base, plus an insider-outsider add-on per unit of bargaining power.</summary>
    public const double ScarBase = 0.25, ScarUnion = 0.5;
    /// <summary>Ceilings: the long-term stock (share of the labour force) and the natural-rate add-on it can cause.</summary>
    public const double MaxStock = 0.10, MaxScar = 0.02;

    // ---- labour share ----
    /// <summary>Labour share per unit of bargaining power gained, per point of output gap beyond 2%, and its annual adjustment speed.</summary>
    public const double ShareBargain = 0.15, ShareGap = 0.15, GapBand = 0.02, ShareSpeed = 0.15;
    /// <summary>Wage-led demand: household income rises by this much of any gain in labour's share of GDP; and the small effect on measured inequality (Gini per unit of share).</summary>
    public const double ShareDemand = 0.2, ShareGini = 0.25;

    /// <summary>Sets union coverage and the labour share to their catalogue starting values and clears the dynamic state (new games and version 2 saves).</summary>
    public static void Init(CountryState c)
    {
        var p = LabourCatalog.For(c.Id, c.Archetype);
        c.UnionCoverage = c.UnionCoverage0 = p.Coverage;
        c.UnionStrength = p.Strength;
        c.LabourIncomeShare = c.LabourIncomeShare0 = c.LabourIncomeTrend = p.LabourShare;
        c.WageGap = c.WagePremium = c.WageSpiral = c.StrikeRisk = 0;
        c.LtuStock = c.NairuHyst = 0;
    }

    /// <summary>Bargaining power at the start (coverage times strength).</summary>
    public static double StartingPower(CountryState c) => c.UnionCoverage0 * c.UnionStrength;

    /// <summary>Coverage follows the policy modifier "bargaining" (union-rights and flexibility reforms); exactly the starting value while it is zero.</summary>
    public static void RefreshCoverage(CountryState c) =>
        c.UnionCoverage = Maths.Clamp(c.UnionCoverage0 + c.Mod("bargaining"), 0.0, 0.99);

    /// <summary>
    /// Long-term unemployment hysteresis, run before the natural rate is added up. Cyclical slack is measured against last month's total natural rate,
    /// so the scarring it causes does not itself count as slack.
    /// </summary>
    public static void Scar(CountryState c, double dt)
    {
        RefreshCoverage(c);
        double cyc = c.Unemp - c.NairU;
        double inflow = ScarInflow * Math.Max(0.0, Maths.Soft(cyc, Band));
        double outflow = (ScarExit + ScarTight * Math.Max(0.0, -cyc)) * c.LtuStock;
        c.LtuStock = Maths.Clamp(c.LtuStock + (inflow - outflow) * dt, 0.0, MaxStock);
        c.NairuHyst = Math.Min(MaxScar, (ScarBase + ScarUnion * c.BargainingPower) * c.LtuStock);
    }

    /// <summary>
    /// A pay settlement (the "wagecatchup" event effect): the given share of the real-wage shortfall is made good at once and the market real wage rises with it.
    /// Does nothing while workers are not behind, so it is inert in a quiet economy.
    /// </summary>
    public static void CatchUp(CountryState c, double share)
    {
        share = Maths.Clamp(share, 0.0, 1.0);
        double behind = c.WageGap;
        if (behind <= 0 || share <= 0) return;
        c.RealWageIdx *= Math.Exp(share * behind);
        c.WageGap = behind * (1 - share);
    }

    /// <summary>Annual rate at which the wage premium catches up with its target: contracts last 0.4 years with no bargaining and 2 years with full coverage.</summary>
    public static double Persistence(double power) => 1.0 / (0.4 + 1.6 * power);

    /// <summary>
    /// Wage bargaining, run after the market real wage has moved: price shock, wage premium, price push, real-wage gap, strike risk and labour share.
    /// Reads last month's inflation drivers (this month's are not yet known).
    /// </summary>
    public static void Bargain(CountryState c, double dt)
    {
        double power = c.BargainingPower, cred = MacroEngine.EffectiveCredibility(c, c.DeficitToGdp);
        var d = c.InflDrivers;
        double shock = d[2] + d[3] + d[4] + d[5] + c.WageSpiral;          // energy and food, currency, monetisation, carbon and policy, and last month's wage push
        double news = Maths.Soft(shock, Band);
        if (news < 0) news *= FallWeight;
        double tight = Maths.Soft(c.NairU - c.Unemp, Band);
        double claim = power * (Indexation * (1 - Anchoring * cred) * news + TightnessWeight * tight);
        c.WagePremium = Maths.Clamp(c.WagePremium + (claim - c.WagePremium) * Math.Min(1.0, Persistence(power) * dt), PremiumMin, PremiumMax);
        c.WageSpiral = PassThrough * (1 - CredibilityDamping * cred) * c.WagePremium;

        double gap0 = c.WageGap;
        c.WageGap = Maths.Clamp(gap0 + (Exposure * news - c.WagePremium - Restore * gap0) * dt, GapMin, GapMax);
        c.RealWageIdx *= Math.Exp(-(c.WageGap - gap0));

        double excess = Maths.Soft(c.WageGap, StrikeBand);
        c.StrikeRisk = excess > 0 ? 1.0 - Math.Exp(-StrikeK * power * excess) : 0.0;

        double target = c.LabourIncomeShare0 + ShareBargain * (power - StartingPower(c)) + ShareGap * Maths.Soft(c.Gap, GapBand);
        c.LabourIncomeTrend += (target - c.LabourIncomeTrend) * Math.Min(1.0, ShareSpeed * dt);
        c.LabourIncomeShare = Maths.Clamp(c.LabourIncomeTrend * Math.Exp(-c.WageGap), 0.15, 0.85);
    }
}
