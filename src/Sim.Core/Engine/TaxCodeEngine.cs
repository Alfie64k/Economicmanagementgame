using Sim.Core.Model;
using Sim.Core.Policy;
using Sim.Core.Util;

namespace Sim.Core.Engine;

/// <summary>Everything the detailed tax-and-benefit code implies, evaluated over the income grid for one set of parameters.</summary>
public sealed class FiscalEval
{
    public double AvgInc, AvgEe, AvgEr, AvgPay, VatEff, CorpEff, Wedge, ExpAdj = 1.0;
    public double Labour = 1.0, GiniNet, Poverty, NairuAdj, SumAward;
    public double MpcInc, MpcPay;
    public double[] Ratio = { 1, 1, 1, 1, 1, 1, 1 };
    public double[] Spend = new double[7];             // mean per household, in mean-earnings units
    public double[] Tax = Array.Empty<double>(), Ee = Array.Empty<double>(), Er = Array.Empty<double>(), Vat = Array.Empty<double>(),
                    Ben = Array.Empty<double>(), Net = Array.Empty<double>(), Gain = Array.Empty<double>(), Ntr = Array.Empty<double>();
}

/// <summary>Tax and marginal-rate curve for the Budget charts, over earnings from 0 to <c>MaxMe</c> times the mean.</summary>
public sealed class RateCurve
{
    public double[] Income = Array.Empty<double>(), AvgRate = Array.Empty<double>(), IncomeTaxMarginal = Array.Empty<double>(), TotalMarginal = Array.Empty<double>(), Net = Array.Empty<double>();
    public double MaxMe;
}

/// <summary>
/// Derives the engine's effective tax rates and its benefit mix from the player's tax-and-benefit code, and feeds the extra channels
/// (labour supply, natural unemployment, who spends what, inequality, approval) back into the macro model.
/// Everything is relative to the starting code: with nothing changed every hook returns exactly its neutral value (1.0 or 0.0).
/// </summary>
public static class TaxCodeEngine
{
    sealed class EvalCache { public FiscalEval? Base, Cur; }

    static readonly Tax[] Driven = { Tax.Income, Tax.Corporate, Tax.Consumption, Tax.Payroll };
    // share of the electorate directly affected by each benefit (for approval)
    static readonly double[] Voters = { 0.22, 0.05, 0.14, 0.08, 0.06, 0.10, 0.0 };

    static EvalCache CacheOf(FiscalCode f)
    {
        if (f.Cache is EvalCache c) return c;
        c = new EvalCache(); f.Cache = c; return c;
    }

    public static FiscalEval BaseEval(FiscalCode f)
    {
        var c = CacheOf(f);
        return c.Base ??= Evaluate(f, f.P0, 1.0, 1.0, null);
    }

    public static FiscalEval CurEval(FiscalCode f)
    {
        var c = CacheOf(f);
        return c.Cur ??= Evaluate(f, f.P, f.Drift, f.PenDrift, BaseEval(f));
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // evaluation

    /// <summary>
    /// Evaluates a parameter set against the income grid. <paramref name="bas"/> is the starting code's evaluation, used for ratios and
    /// differences; pass null when evaluating the starting code itself.
    /// </summary>
    public static FiscalEval Evaluate(FiscalCode f, IReadOnlyDictionary<string, double> P, double drift, double penDrift, FiscalEval? bas, IncomeGrid? grid = null)
    {
        const int N = IncomeGrid.N;
        var g = grid ?? IncomeGrid.For(f.Sigma);
        var tp = TaxParams.Of(P, drift);
        var bp = BenParams.Of(P, f.P0, penDrift);
        double G(string k) => P.TryGetValue(k, out var v) ? v : 0;
        var e = new FiscalEval { Tax = new double[N], Ee = new double[N], Er = new double[N], Vat = new double[N], Ben = new double[N], Net = new double[N], Gain = new double[N], Ntr = new double[N] };

        // VAT treatment of each spending category
        double std = G("Vat.Std"), red = G("Vat.Red");
        var tau = new double[7];
        for (int k = 0; k < 7; k++)
        {
            int t = (int)Math.Round(G(FiscalParams.VatKey(FiscalParams.VatCategories[k])));
            tau[k] = t switch { 0 => 0, 1 => red, 2 => std, _ => 0.25 * std };
        }

        // corporation tax: statutory average over main and small-profit rates, cost of capital after expensing
        double lim = G("Corp.SmallLimit") * drift;
        double ws = lim <= 0 ? 0 : Maths.NormCdf((Math.Log(lim) - 8.0) / 2.5);
        e.CorpEff = (1 - ws) * G("Corp.Main") + ws * G("Corp.Small");
        double expensing = G("Corp.Expensing");
        e.Wedge = e.CorpEff * (1 - 0.75 * expensing);
        e.ExpAdj = 1.0 - 0.4 * (expensing - f.Get0("Corp.Expensing"));

        // benefit spending ratios (strand by strand)
        double Cover(double thr) { if (thr <= 0) return 1; int n = 0; for (int i = 0; i < N; i++) if (g.M[i] < thr) n++; return Math.Max(1e-3, (double)n / N); }
        double chiCover = Cover(bp.ChiThr) / Cover(f.Get0("Chi.Threshold"));
        double[] unit = new double[7];
        for (int k = 0; k < 7; k++) unit[k] = f.Shares[k] * f.Soc0 / FiscalEngine.IncomeBaseShare;
        var r = new double[7];
        r[(int)Ben.Pension] = bp.Pen; r[(int)Ben.Unemployment] = bp.Une; r[(int)Ben.Child] = bp.Chi * chiCover; r[(int)Ben.Disability] = bp.Dis;
        r[(int)Ben.Housing] = bp.Hou; r[(int)Ben.Other] = bp.Oth; r[(int)Ben.MeansTested] = 1.0;

        double sumTax = 0, sumEe = 0, sumEr = 0, sumVat = 0, sumCons = 0, sumAward = 0;
        double owi = bp.UneLevel * bp.UneDur + bp.Award(0);
        const double h = 0.005;
        for (int i = 0; i < N; i++)
        {
            double m = g.M[i];
            double tax = tp.Income(m), ee = tp.Employee(m), er = tp.Employer(m);
            double vr = 0; for (int k = 0; k < 7; k++) vr += g.VatW[k][i] * tau[k];
            double vat = g.Cons[i] * vr;
            double award = bp.Award(m);
            double ben = award;
            for (int k = 0; k < 7; k++) if (k != (int)Ben.MeansTested) { double s = unit[k] * r[k] * g.Incidence[k][i]; ben += s; e.Spend[k] += s; }
            e.Tax[i] = tax; e.Ee[i] = ee; e.Er[i] = er; e.Vat[i] = vat; e.Ben[i] = ben;
            e.Net[i] = m - tax - ee - vat + ben;
            double dTax = (tp.Income(m + h) - tax) / h, dEe = (tp.Employee(m + h) - ee) / h;
            e.Ntr[i] = Math.Max(0.05, 1 - dTax - dEe - bp.MtMetr(m));
            e.Gain[i] = Math.Max(0.03, m - tax - ee + award - owi);
            sumTax += tax; sumEe += ee; sumEr += er; sumVat += vat; sumCons += g.Cons[i]; sumAward += award;
        }
        e.AvgInc = sumTax / N; e.AvgEe = sumEe / N; e.AvgEr = sumEr / N; e.AvgPay = (sumEe + sumEr) / N;
        e.VatEff = sumCons > 0 ? sumVat / sumCons : 0;
        e.SumAward = sumAward; e.Spend[(int)Ben.MeansTested] = sumAward / N;
        for (int k = 0; k < 7; k++) { if (k != (int)Ben.MeansTested) e.Spend[k] /= N; e.Ratio[k] = r[k]; }
        if (bas != null) e.Ratio[(int)Ben.MeansTested] = bas.SumAward > 1e-9 ? sumAward / bas.SumAward : 1.0 + sumAward / 0.02;

        // inequality and poverty of net income
        var sorted = (double[])e.Net.Clone(); Array.Sort(sorted);
        double tot = 0, wsum = 0;
        for (int i = 0; i < N; i++) { tot += sorted[i]; wsum += (i + 1) * sorted[i]; }
        e.GiniNet = tot > 0 ? 2 * wsum / (N * tot) - (N + 1.0) / N : 0;
        double line = 0.6 * 0.5 * (sorted[N / 2 - 1] + sorted[N / 2]); int poor = 0;
        for (int i = 0; i < N; i++) if (e.Net[i] < line) poor++;
        e.Poverty = (double)poor / N;

        if (bas != null)
        {
            // labour supply: the net gain from working (extensive margin, strongest among the poor) and the take-home on the next pound (intensive margin)
            double acc = 0, wt = 0;
            for (int i = 0; i < N; i++)
            {
                double eps = 0.05 + 0.30 * (1 - g.Pc[i]);
                double lg = Math.Clamp(Math.Log(e.Gain[i] / bas.Gain[i]), -1, 1), ln = Math.Clamp(Math.Log(e.Ntr[i] / bas.Ntr[i]), -1, 1);
                acc += g.M[i] * (eps * lg + 0.10 * ln); wt += g.M[i];
                e.MpcInc += (g.Phi[i] - 1) * (e.Tax[i] - bas.Tax[i]) / N;
                e.MpcPay += (g.Phi[i] - 1) * ((e.Ee[i] + e.Er[i]) - (bas.Ee[i] + bas.Er[i])) / N;
            }
            e.Labour = Math.Exp(acc / wt);
        }
        // natural rate of unemployment: replacement rate, duration and how strict disability assessment is
        e.NairuAdj = Math.Clamp(0.025 * Math.Log(Math.Max(0.05, bp.UneLvl)) + 0.015 * Math.Log(bp.UneDurRatio) + 0.010 * Math.Log(Math.Max(0.5, bp.DisElig)), -0.03, 0.06);
        return e;
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // monthly step

    /// <summary>Called at the start of each country-month: moves thresholds with their indexation, absorbs direct writes to the tax rates, and recomputes whatever changed.</summary>
    public static void Step(CountryState c)
    {
        if (c.Fiscal is not { Init: true } f) return;
        bool drifted = UpdateDrift(c, f);
        bool external = Absorb(c, f);
        if (f.Dirty || drifted || external) Recalc(c, f);
        if (f.LabourCur != f.LabourTarget) f.LabourCur += (f.LabourTarget - f.LabourCur) * 0.04;
        if (f.NairuCur != f.NairuTarget) f.NairuCur += (f.NairuTarget - f.NairuCur) * 0.04;
    }

    static bool UpdateDrift(CountryState c, FiscalCode f)
    {
        double rw = c.RealWageIdx, pr = c.PriceLevel, earn = rw * pr;
        if (f.LastPrice <= 0)
        {
            f.LastRealWage = rw; f.LastPrice = pr;
            for (int i = 0; i < 12; i++) { f.EarnRing[i] = earn; f.PriceRing[i] = pr; }
            return false;
        }
        double gE = earn / (f.LastRealWage * f.LastPrice), gP = pr / f.LastPrice;
        double e12 = f.EarnRing[f.RingPos], p12 = f.PriceRing[f.RingPos];            // a year ago
        f.EarnRing[f.RingPos] = earn; f.PriceRing[f.RingPos] = pr; f.RingPos = (f.RingPos + 1) % 12;
        f.LastRealWage = rw; f.LastPrice = pr;

        bool changed = false;
        int ti = (int)Math.Round(f.Get("Thr.Index"));
        if (ti == 1) { f.Drift *= gP / gE; changed = true; }
        else if (ti == 2) { f.Drift /= gE; changed = true; }

        int pi = (int)Math.Round(f.Get("Pen.Index"));
        if (pi == 1) { f.PenDrift *= gP / gE; changed = true; }
        else if (pi == 3) { f.PenDrift /= gE; changed = true; }
        else if (pi == 2)
        {
            double yrE = e12 > 0 ? earn / e12 : 1, yrP = p12 > 0 ? pr / p12 : 1;
            double floor = Math.Pow(Math.Max(Math.Max(yrE, yrP), 1.025), 1.0 / 12.0);
            f.PenDrift *= floor / gE; changed = true;
        }
        return changed;
    }

    /// <summary>Someone (the one-slider tax control, the cabinet autopilot) wrote a tax rate directly: keep the difference as an offset on top of the code.</summary>
    static bool Absorb(CountryState c, FiscalCode f)
    {
        bool any = false;
        foreach (var t in Driven)
        {
            int i = (int)t;
            if (c.TaxRate[i] != f.Written[i]) { f.Offset[i] += c.TaxRate[i] - f.Written[i]; any = true; }
        }
        return any;
    }

    /// <summary>Re-evaluates the code and writes the four driven effective rates into the country state.</summary>
    public static void Recalc(CountryState c, FiscalCode f)
    {
        var cache = CacheOf(f);
        var bas = BaseEval(f);
        var cur = Evaluate(f, f.P, f.Drift, f.PenDrift, bas);
        cache.Cur = cur;

        double Rate(Tax t, double delta, double mult)
        {
            int i = (int)t;
            double r = (c.TaxRate0[i] + f.Calib[i] * delta) * mult + f.Offset[i];
            return Maths.Clamp(r, 0, 0.9);
        }
        c.TaxRate[(int)Tax.Income] = f.Written[(int)Tax.Income] = Rate(Tax.Income, cur.AvgInc - bas.AvgInc, 1.0);
        c.TaxRate[(int)Tax.Payroll] = f.Written[(int)Tax.Payroll] = Rate(Tax.Payroll, cur.AvgPay - bas.AvgPay, 1.0);
        c.TaxRate[(int)Tax.Consumption] = f.Written[(int)Tax.Consumption] = Rate(Tax.Consumption, cur.VatEff - bas.VatEff, 1.0);
        c.TaxRate[(int)Tax.Corporate] = f.Written[(int)Tax.Corporate] = Rate(Tax.Corporate, cur.CorpEff - bas.CorpEff, cur.ExpAdj);

        f.CorpInvDelta = f.Calib[(int)Tax.Corporate] * (cur.Wedge - bas.Wedge) + f.Offset[(int)Tax.Corporate];
        double mix = 1.0;
        for (int k = 0; k < 7; k++) { f.BenRatio[k] = cur.Ratio[k]; mix += f.Shares[k] * (cur.Ratio[k] - 1.0); }
        f.Mix = mix;
        f.LabourTarget = Math.Clamp(cur.Labour * (1.0 + 0.005 * (f.Get("Pen.Age") - f.Get0("Pen.Age"))), 0.85, 1.15);
        f.NairuTarget = cur.NairuAdj;
        f.GiniDelta = f.GiniScale * (cur.GiniNet - bas.GiniNet);
        f.PovertyDelta = cur.Poverty - bas.Poverty;
        double ap = 0;
        for (int k = 0; k < 7; k++) ap += Voters[k] * Math.Log(Math.Max(0.05, cur.Ratio[k]));
        f.ApprovalDelta = Math.Clamp(1.0 * ap - 0.8 * f.PovertyDelta, -0.25, 0.25);
        f.MpcTax = -(f.Calib[(int)Tax.Income] * cur.MpcInc * FiscalEngine.IncomeBaseShare + f.Calib[(int)Tax.Payroll] * cur.MpcPay * FiscalEngine.PayrollBaseShare);
        f.Dirty = false;
    }

    /// <summary>Marks the code as changed and applies it at once (used after a command edits it).</summary>
    public static void Touch(CountryState c)
    {
        if (c.Fiscal is not { Init: true } f) return;
        Absorb(c, f);
        f.Dirty = true;
        Recalc(c, f);
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // hooks used by the macro model (all neutral when the code is untouched)

    public static bool Active(CountryState c) => c.Fiscal is { Init: true };
    /// <summary>Change in the corporate cost of capital in effective-rate units, for investment and FDI.</summary>
    public static double CorpDelta(CountryState c) =>
        c.Fiscal is { Init: true } f ? f.CorpInvDelta : c.TaxRate[(int)Tax.Corporate] - c.TaxRate0[(int)Tax.Corporate];
    /// <summary>Multiplier on labour supply from marginal and participation tax rates, benefit incentives and the pension age.</summary>
    public static double LabourIdx(CountryState c) => c.Fiscal is { Init: true } f ? f.LabourCur : 1.0;
    /// <summary>Addition to the natural rate of unemployment from benefit generosity.</summary>
    public static double NairuAdj(CountryState c) => c.Fiscal is { Init: true } f ? f.NairuCur : 0.0;
    /// <summary>Spending on benefits relative to the start (the strands weighted by their shares).</summary>
    public static double BenefitMix(CountryState c) => c.Fiscal is { Init: true } f ? f.Mix : 1.0;
    /// <summary>How much the automatic unemployment stabiliser has been scaled by the benefit rules.</summary>
    public static double StabilizerMix(CountryState c) => c.Fiscal is { Init: true } f ? f.BenRatio[(int)Ben.Unemployment] : 1.0;
    /// <summary>The part of the one-slider tax control left over once the code's own contribution is removed (for the inequality equation).</summary>
    public static double IncomeTaxOffset(CountryState c) => c.Fiscal is { Init: true } f ? f.Offset[(int)Tax.Income] : c.TaxRate[(int)Tax.Income] - c.TaxRate0[(int)Tax.Income];
    public static double GiniDelta(CountryState c) => c.Fiscal is { Init: true } f ? f.GiniDelta : 0.0;
    public static double ApprovalDelta(CountryState c) => c.Fiscal is { Init: true } f ? f.ApprovalDelta : 0.0;

    /// <summary>
    /// Real local-currency adjustment to household disposable income for who gains and who loses: poorer households spend more of each extra pound,
    /// so unemployment and means-tested support lift demand more than pensions, and a cut for the poor more than a cut at the top.
    /// </summary>
    public static double MpcAdj(CountryState c)
    {
        if (c.Fiscal is not { Init: true } f) return 0.0;
        double adj = f.MpcTax * c.Gdp;
        double ageing = Math.Pow(c.Old / Math.Max(1e-6, c.Old0), 0.4);
        double basePool = c.Budget[(int)BudgetLine.Social] * c.Potential * ageing;
        double stab = 0.35 * Math.Max(-0.02, c.Unemp - c.NairU) * c.Potential;
        for (int k = 0; k < 7; k++)
        {
            double dr = f.BenRatio[k] - 1.0;
            if (dr == 0.0) continue;
            double d = basePool * f.Shares[k] * dr + (k == (int)Ben.Unemployment ? stab * dr : 0.0);
            adj += (f.BenMpc[k] - 1.0) * d;
        }
        return adj;
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // charts

    /// <summary>Average and marginal tax rates, and net income, from nothing up to <paramref name="maxMe"/> times mean earnings.</summary>
    public static RateCurve Curve(FiscalCode f, IReadOnlyDictionary<string, double> P, double drift, double maxMe, int points = 241)
    {
        var tp = TaxParams.Of(P, drift);
        var bp = BenParams.Of(P, f.P0, f.PenDrift);
        var cv = new RateCurve { MaxMe = maxMe, Income = new double[points], AvgRate = new double[points], IncomeTaxMarginal = new double[points], TotalMarginal = new double[points], Net = new double[points] };
        double h = maxMe / (points - 1) / 8;
        for (int i = 0; i < points; i++)
        {
            double m = maxMe * i / (points - 1);
            double tax = tp.Income(m), ee = tp.Employee(m);
            double dt = (tp.Income(m + h) - tax) / h, de = (tp.Employee(m + h) - ee) / h;
            cv.Income[i] = m;
            cv.AvgRate[i] = m > 1e-9 ? (tax + ee) / m : 0;
            cv.IncomeTaxMarginal[i] = dt;
            cv.TotalMarginal[i] = dt + de + bp.MtMetr(m);
            cv.Net[i] = m - tax - ee + bp.Award(m);
        }
        return cv;
    }
}
