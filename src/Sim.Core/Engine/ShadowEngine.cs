using Sim.Core.Data;
using Sim.Core.Model;
using Sim.Core.Util;

namespace Sim.Core.Engine;

/// <summary>
/// The informal ("shadow") economy. Activity that goes unreported escapes tax, so the tax base shrinks by (1 - Shadow) / (1 - Shadow0)
/// (exactly 1.0 at the start; see <see cref="CountryState.ShadowMult"/>). The share drifts toward a target over about three years:
/// <list type="bullet">
/// <item>it rises with the tax-and-contribution wedge relative to the start (the five effective rates times the share of GDP each is levied on);</item>
/// <item>it rises with corruption relative to the start (once the change is large enough to be visible);</item>
/// <item>it falls with administration (enforcement) spending relative to its starting share;</item>
/// <item>a policy modifier ("shadow", a relative change in the starting share) lets compliance drives and amnesties move it directly.</item>
/// </list>
/// All of this is relative to the start, so a country whose drivers have not moved stays exactly where it began. Slopes are hand-calibrated
/// priors informed by the sign and rough size of cross-country tax-burden regressions, not estimates.
/// </summary>
public static class ShadowEngine
{
    /// <summary>Adjustment time of the share toward its target, in years.</summary>
    public const double AdjustYears = 3.0;
    /// <summary>Rise in the share per unit of extra statutory wedge (both as shares of GDP): a tax rise worth 4% of GDP lifts informality by 2 points.</summary>
    public const double WedgeSlope = 0.5;
    /// <summary>Rise in the share per point of corruption index (0 to 1), beyond a dead-band of <see cref="CorruptionBand"/>: the slow drift the model gives democracies is within measurement noise.</summary>
    public const double CorruptionSlope = 0.35, CorruptionBand = 0.015;
    /// <summary>Fall in the share, as a fraction of its starting size, for a doubling of administration spending.</summary>
    public const double EnforcementSlope = 0.15;
    /// <summary>Small side effects of informality on measured inequality and approval, per unit of share above the start (informal workers lack protection and services).</summary>
    public const double GiniSlope = 0.15, ApprovalSlope = 0.10;

    /// <summary>Sets the shadow share to its catalogue starting value (new games and version 2 saves).</summary>
    public static void Init(CountryState c)
    {
        double s = ShadowCatalog.Share(c.Id, c.Archetype);
        c.Shadow = c.Shadow0 = s;
        Array.Clear(c.ShadowDrivers);
        c.ShadowDrivers[0] = s;
    }

    /// <summary>Share of GDP each tax is levied on (the engine's own bases), constant so the wedge is exactly zero while the rates are unchanged.</summary>
    static double BaseShare(CountryState c, int t) => t switch
    {
        (int)Tax.Income => FiscalEngine.IncomeBaseShare,
        (int)Tax.Corporate => FiscalEngine.CorpBaseShare,
        (int)Tax.Consumption => c.Gdp0 > 0 ? c.Cons0 / c.Gdp0 : 0.6,
        (int)Tax.Payroll => FiscalEngine.PayrollBaseShare,
        _ => c.Gdp0 > 0 ? c.M0 / c.Gdp0 : 0.2,
    };

    /// <summary>Change in the statutory tax wedge since the start, as a share of GDP (positive = heavier taxes). Uses the five scalar effective rates, so it works for every country.</summary>
    public static double WedgeDelta(CountryState c)
    {
        double w = 0;
        for (int t = 0; t < Dim.Taxes; t++) w += (c.TaxRate[t] - c.TaxRate0[t]) * BaseShare(c, t);
        return w;
    }

    /// <summary>Administration spending relative to its starting share (1.0 = unchanged).</summary>
    public static double EnforcementRatio(CountryState c)
    {
        double b0 = c.Budget0[(int)BudgetLine.Admin];
        return b0 > 1e-6 ? Maths.Clamp(c.Budget[(int)BudgetLine.Admin] / b0, 0.25, 3.0) : 1.0;
    }

    /// <summary>Lowest and highest share the economy can settle at.</summary>
    public static (double Lo, double Hi) Bounds(CountryState c) =>
        (Math.Min(c.Shadow0, Math.Max(0.02, 0.4 * c.Shadow0)), Math.Max(c.Shadow0, Math.Min(0.80, c.Shadow0 + 0.20)));

    /// <summary>The share the economy is heading for with today's taxes, corruption, enforcement and policies. Fills <see cref="CountryState.ShadowDrivers"/>.</summary>
    public static double Target(CountryState c)
    {
        double s0 = c.Shadow0;
        double wedge = WedgeSlope * WedgeDelta(c);
        double corr = CorruptionSlope * Maths.Soft(c.Corruption - c.Corruption0, CorruptionBand);
        double enf = -EnforcementSlope * s0 * (EnforcementRatio(c) - 1.0);
        double pol = c.Mod("shadow") * s0;
        var (lo, hi) = Bounds(c);
        double target = Maths.Clamp(s0 + wedge + corr + enf + pol, lo, hi);
        var d = c.ShadowDrivers;
        d[0] = s0; d[1] = wedge; d[2] = corr; d[3] = enf; d[4] = target - (s0 + wedge + corr + enf);   // policy, plus whatever the bounds cut off
        return target;
    }

    /// <summary>One month: the share moves a little toward its target.</summary>
    public static void Step(CountryState c, double dt)
    {
        if (c.Shadow0 <= 0) return;                 // no data for this country (hand-built state)
        double target = Target(c);
        c.Shadow = Maths.Clamp(c.Shadow + (target - c.Shadow) * Math.Min(1.0, dt / AdjustYears), 0.0, 0.95);
    }
}
