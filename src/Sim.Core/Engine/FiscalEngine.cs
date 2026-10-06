using Sim.Core.Model;
using Sim.Core.Util;

namespace Sim.Core.Engine;

public static class FiscalEngine
{
    public const double IncomeBaseShare = 0.65, CorpBaseShare = 0.25, PayrollBaseShare = 0.50;
    static readonly double[] Elasticity = { 0.80, 0.70, 0.90, 0.80, 0.60 }; // revenue elasticity to the statutory rate (Laffer-style concavity)

    public static double TaxRevenueReal(CountryState c, Tax t, double gdp, double cons, double imports)
    {
        int i = (int)t;
        double r = c.TaxRate[i], r0 = Math.Max(1e-4, c.TaxRate0[i]);
        double rr = Math.Max(0.0, r);
        double eff = r0 * Math.Pow(rr / r0, Elasticity[i]);
        double baseAmt = t switch
        {
            Tax.Income => IncomeBaseShare * gdp,
            Tax.Corporate => CorpBaseShare * gdp,
            Tax.Consumption => cons,
            Tax.Payroll => PayrollBaseShare * gdp,
            _ => imports,
        };
        return eff * baseAmt;
    }

    /// <summary>Sovereign risk premium over the baseline; evaluated relative to initial state so data-calibrated yields are preserved.</summary>
    public static double Risk(CountryState c)
    {
        double debt = c.DebtToGdp, def = c.DeficitToGdp;
        double fdw = 0.15 + 1.5 * c.ForeignDebtShare;
        double r = fdw * (0.04 * Math.Pow(Math.Max(0, debt - 0.6), 2) + 0.15 * Math.Max(0, def - 0.04));
        r += 0.15 * Math.Max(0, c.Inflation - 0.05);
        r += 0.03 * (1 - c.Stability) + 0.01 * c.Corruption;
        r += 0.004 * Math.Max(0, 4 - c.Reserves);
        r += c.Mod("risk");
        return r;
    }

    /// <summary>Risk-free 10y proxy: average of the current policy rate and the long-run neutral rate plus expected inflation.</summary>
    public static double YieldModel(CountryState c) => 0.5 * c.PolicyRate + 0.5 * (0.015 + c.InflExp) + 0.005;

    public static void AnchorSpread(CountryState c, double yield10)
    {
        double model = YieldModel(c);
        c.Spread = yield10 - model;
        c.SpreadStar = c.Spread;
        c.Spread0Risk = Risk(c);
        c.Spread0 = c.Spread; c.SpreadBase = c.Spread;
    }

    public static void Step(CountryState c, GlobalState g, double dt)
    {
        double P = c.PriceLevel;
        // ---- revenue (real, annualised) ----
        double revReal = 0;
        for (int t = 0; t < Dim.Taxes; t++) revReal += TaxRevenueReal(c, (Tax)t, c.Gdp, c.Cons, c.Imports);
        double resRev = c.ResourceRev0Share * c.Gdp * g.OilIdx * (1 - 0.25 * Math.Min(1, c.CarbonPrice / 200));
        revReal += resRev + c.OtherRevShare * c.Gdp + c.Mod("revenue") * c.Gdp;

        // ---- spending ----
        double socialReal = SocialReal(c);
        double gReal = c.GovCons + c.GovInv;
        double interest = c.Debt * c.AvgDebtCost;           // nominal, annualised
        double spendNom = (gReal + socialReal) * P + interest;
        double revNom = revReal * P + c.OtherRevenue;
        double deficit = spendNom - revNom;

        c.Revenue = revNom; c.Spending = spendNom; c.Interest = interest; c.Deficit = deficit;
        c.PrimaryBalance = revNom - (spendNom - interest);
        c.Debt = Math.Max(0, c.Debt + deficit * dt);
        c.OtherRevenue *= Math.Exp(-6 * dt); // one-offs fade (privatisation proceeds are booked once)

        // ---- markets: average cost of debt and sovereign spread ----
        double riskNow = Risk(c) - c.Spread0Risk;
        c.SpreadBase += (c.SpreadStar - c.SpreadBase) * dt / 3.0;   // inverted curves normalise over ~3 years
        c.Spread += (c.SpreadBase + riskNow - c.Spread) * 0.15;
        // floor: the real 10y yield cannot fall far below zero even when an inverted-curve spread is carried from the data
        double target = Maths.Clamp(Math.Max(YieldModel(c) + c.Spread, c.InflExp + 0.005), 0.0, 0.60);
        c.Yield10 += (target - c.Yield10) * 0.2;
        double maturity = c.Archetype == "advanced" || c.Archetype == "hub" ? 7.0 : 3.5;
        c.AvgDebtCost += (Math.Min(c.Yield10, 0.12) - c.AvgDebtCost) * dt / maturity;
        c.RealLoanRate = c.Yield10 + 0.015 - c.InflExp;

        // default tracking (full crisis handling lives in CrisisEngine)
        if (c.Yield10 > 0.20 && c.DebtToGdp > 0.9) c.CrisisMonths++; else c.CrisisMonths = Math.Max(0, c.CrisisMonths - 1);
    }

    /// <summary>Social transfers (real): baseline scaled by ageing plus automatic stabilisers.</summary>
    public static double SocialReal(CountryState c)
    {
        double ageing = Math.Pow(c.Old / Math.Max(1e-6, c.Old0), 0.4);
        double stab = 0.35 * Math.Max(-0.02, c.Unemp - c.NairU) * c.Potential;
        return c.Budget[(int)BudgetLine.Social] * c.Potential * ageing + stab;
    }
}
