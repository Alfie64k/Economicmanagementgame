using Sim.Core.Engine;
using Sim.Core.Model;
using Sim.Core.Util;

namespace Sim.Core.Policy;

/// <summary>First-order estimate of what a draft tax-and-benefit code would do, relative to the code in force now. Instant; the full dynamic path comes from the forecaster.</summary>
public sealed class FiscalEstimate
{
    public bool Changed;
    public double[] TaxGdp = new double[4];                 // annual revenue change by tax (Income, Corporate, Consumption, Payroll), share of GDP
    public double RevenueGdp, SpendGdp;                     // annual change in revenue and in benefit spending, share of GDP
    public double BalanceGdp => RevenueGdp - SpendGdp;
    public double[] BenSpendGdp = new double[7];            // annual change in spending by benefit strand
    public double DemandGdp;                                // indicative first-year effect on GDP (fraction), demand side only
    public double LongRunGdp;                               // supply side once labour and capital have adjusted (fraction)
    public double GiniDelta, PovertyDelta, ApprovalDelta, NairuDelta, LabourDelta;
    public double[] DecileNet = new double[10];            // change in net income by decile of earnings (fraction of today's net income)
    public double PcCost;
}

public static class FiscalDraft
{
    static readonly Tax[] Driven = { Tax.Income, Tax.Corporate, Tax.Consumption, Tax.Payroll };

    /// <summary>The code that results from applying the fiscal commands to the code in force (non-fiscal commands are ignored).</summary>
    public static Dictionary<string, double> Apply(CountryState c, IEnumerable<Command> cmds)
    {
        var f = c.Fiscal ?? throw new InvalidOperationException("no fiscal code");
        var p = new Dictionary<string, double>(f.P);
        foreach (var cmd in cmds)
        {
            if (cmd.Type != "fiscal") continue;
            if (cmd.Id == FiscalParams.BandsKey) { var b = FiscalParams.DecodeBands(cmd.Data); if (b != null) FiscalParams.SetBands(p, b); }
            else if (FiscalParams.Def(cmd.Id) is { } d) p[cmd.Id] = Maths.Clamp(cmd.Value, d.Min, d.Max);
            FiscalParams.Normalise(p);
        }
        return p;
    }

    public static bool Same(IReadOnlyDictionary<string, double> a, IReadOnlyDictionary<string, double> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var kv in a) if (!b.TryGetValue(kv.Key, out var v) || Math.Abs(v - kv.Value) > 1e-9) return false;
        return true;
    }

    /// <summary>Political capital for moving from the code in force to <paramref name="draft"/> (each changed parameter priced on its own; the band table together).</summary>
    public static double PcCost(CountryState c, IReadOnlyDictionary<string, double> draft)
    {
        var f = c.Fiscal!; double cost = 0; bool bandsDone = false;
        foreach (var kv in draft)
        {
            double old = f.Get(kv.Key, double.NaN);
            if (kv.Key.StartsWith("Inc.B") || kv.Key == "Inc.NBands")
            {
                if (!bandsDone && BandsDiffer(f.P, draft)) { cost += FiscalParams.BandsCost(FiscalParams.Bands(f.P), FiscalParams.Bands(draft)); bandsDone = true; }
                continue;
            }
            if (double.IsNaN(old) || Math.Abs(old - kv.Value) < 1e-9) continue;
            if (FiscalParams.Def(kv.Key) is { } d) cost += FiscalParams.Cost(d, old, kv.Value);
        }
        return Math.Round(cost * (c.Gov == "autocracy" ? 0.7 : 1.0), 1);
    }

    static bool BandsDiffer(IReadOnlyDictionary<string, double> a, IReadOnlyDictionary<string, double> b)
    {
        var x = FiscalParams.Bands(a); var y = FiscalParams.Bands(b);
        if (x.Count != y.Count) return true;
        for (int i = 0; i < x.Count; i++) if (Math.Abs(x[i].From - y[i].From) > 1e-9 || Math.Abs(x[i].Rate - y[i].Rate) > 1e-9) return true;
        return false;
    }

    public static FiscalEstimate Evaluate(CountryState c, IReadOnlyDictionary<string, double> draft)
    {
        var f = c.Fiscal ?? throw new InvalidOperationException("no fiscal code");
        var bas = TaxCodeEngine.BaseEval(f);
        var cur = TaxCodeEngine.CurEval(f);
        var est = new FiscalEstimate { Changed = !Same(f.P, draft) };
        var nw = est.Changed ? TaxCodeEngine.Evaluate(f, draft, f.Drift, f.PenDrift, bas) : cur;
        double gdp = Math.Max(1e-9, c.Gdp);

        // revenue: new effective rates (as Recalc would set them) through the revenue function
        double[] deltaStat = { nw.AvgInc - bas.AvgInc, nw.CorpEff - bas.CorpEff, nw.VatEff - bas.VatEff, nw.AvgPay - bas.AvgPay };
        for (int k = 0; k < 4; k++)
        {
            int i = (int)Driven[k];
            double mult = Driven[k] == Tax.Corporate ? nw.ExpAdj : 1.0;
            double rate = Maths.Clamp((c.TaxRate0[i] + f.Calib[i] * deltaStat[k]) * mult + f.Offset[i], 0, 0.9);
            double d = FiscalEngine.TaxRevenueAt(c, Driven[k], rate, c.Gdp, c.Cons, c.Imports) - FiscalEngine.TaxRevenueAt(c, Driven[k], c.TaxRate[i], c.Gdp, c.Cons, c.Imports);
            est.TaxGdp[k] = d / gdp; est.RevenueGdp += d / gdp;
        }

        // benefit spending by strand
        double ageing = Math.Pow(c.Old / Math.Max(1e-6, c.Old0), 0.4);
        double pool = c.Budget[(int)BudgetLine.Social] * c.Potential * ageing;
        double stab = 0.35 * Math.Max(-0.02, c.Unemp - c.NairU) * c.Potential;
        double demand = 0, sSave = 1 - c.SavingsRate;
        for (int k = 0; k < 7; k++)
        {
            double dr = nw.Ratio[k] - f.BenRatio[k];
            double d = pool * f.Shares[k] * dr + (k == (int)Ben.Unemployment ? stab * (nw.Ratio[k] - f.BenRatio[k]) : 0);
            est.BenSpendGdp[k] = d / gdp; est.SpendGdp += d / gdp;
            demand += d * f.BenMpc[k];
        }
        // demand side: spending on benefits and tax cuts reach households; poorer households spend more of each extra pound
        double dTaxHh = -(est.TaxGdp[0] + est.TaxGdp[3] + est.TaxGdp[2]) * gdp;
        double mpcTax = -(f.Calib[(int)Tax.Income] * nw.MpcInc * FiscalEngine.IncomeBaseShare + f.Calib[(int)Tax.Payroll] * nw.MpcPay * FiscalEngine.PayrollBaseShare) * gdp - f.MpcTax * gdp;
        est.DemandGdp = 0.75 * sSave * (demand + dTaxHh + mpcTax) / gdp;

        double pa = f.Get("Pen.Age") - f.Get0("Pen.Age"), pa1 = draft.TryGetValue("Pen.Age", out var pn) ? pn - f.Get0("Pen.Age") : pa;
        double labNew = nw.Labour * (1 + 0.005 * pa1);
        est.LabourDelta = labNew / Math.Max(1e-9, f.LabourTarget) - 1;
        est.NairuDelta = nw.NairuAdj - f.NairuTarget;
        double dCorp = f.Calib[(int)Tax.Corporate] * (nw.Wedge - cur.Wedge);
        est.LongRunGdp = 0.6 * Math.Log(Math.Max(0.5, labNew / Math.Max(1e-9, f.LabourTarget))) - 0.6 * est.NairuDelta - 0.35 * 1.5 * dCorp;

        est.GiniDelta = nw.GiniNet - cur.GiniNet;
        est.PovertyDelta = nw.Poverty - cur.Poverty;
        double ap = 0; double[] voters = { 0.22, 0.05, 0.14, 0.08, 0.06, 0.10, 0.0 };
        for (int k = 0; k < 7; k++) ap += voters[k] * Math.Log(Math.Max(0.05, nw.Ratio[k]));
        est.ApprovalDelta = Math.Clamp(ap - 0.8 * (nw.Poverty - bas.Poverty), -0.25, 0.25) - f.ApprovalDelta;

        for (int d = 0; d < 10; d++)
        {
            double a = 0, b = 0; int n = IncomeGrid.N / 10;
            for (int i = d * n; i < (d + 1) * n; i++) { a += nw.Net[i] - cur.Net[i]; b += cur.Net[i]; }
            est.DecileNet[d] = b > 0 ? a / b : 0;
        }
        est.PcCost = est.Changed ? PcCost(c, draft) : 0;
        return est;
    }
}
