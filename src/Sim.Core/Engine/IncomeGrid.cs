using System.Collections.Concurrent;
using Sim.Core.Model;
using Sim.Core.Policy;
using Sim.Core.Util;

namespace Sim.Core.Engine;

/// <summary>
/// A fixed lognormal distribution of earnings, 400 equal-weight cells at the quantile mid-points, scaled so mean earnings = 1 ("ME").
/// Tax and benefit schedules are evaluated cell by cell, which gives revenue, marginal rates, who gains and who loses, and inequality.
/// A lognormal under-weights the very top, so top-rate revenue is a little conservative; Gini → sigma uses G = 2·Φ(σ/√2) − 1.
/// </summary>
public sealed class IncomeGrid
{
    public const int N = 400;
    public readonly double[] M = new double[N], Pc = new double[N];
    /// <summary>Spending-pattern weights by VAT category (food, energy, housing, transport, services, goods, health/education), per cell; each cell sums to 1.</summary>
    public readonly double[][] VatW = new double[7][];
    /// <summary>Consumption of each cell relative to mean earnings.</summary>
    public readonly double[] Cons = new double[N];
    /// <summary>Who receives each benefit (mean 1 across cells): pension, unemployment, child, disability, housing, means-tested (unused: formula), other.</summary>
    public readonly double[][] Incidence = new double[7][];
    public readonly double[] Phi = new double[N];   // marginal propensity to consume relative to the average household

    static readonly ConcurrentDictionary<int, IncomeGrid> Cache = new();
    public static IncomeGrid For(double sigma) => Cache.GetOrAdd((int)Math.Round(sigma * 1000), k => new IncomeGrid(k / 1000.0));
    /// <summary>An uncached grid, for searching over sigma without filling the cache.</summary>
    public static IncomeGrid Fresh(double sigma) => new(Math.Round(sigma, 3));
    public static double SigmaFromGini(double gini) => Math.Round(Maths.Clamp(Math.Sqrt(2) * Maths.NormInv((Maths.Clamp(gini, 0.2, 0.7) + 1) / 2), 0.3, 1.4), 3);

    static readonly double[] VatLow = { .25, .12, .12, .08, .15, .20, .08 }, VatHigh = { .08, .04, .12, .11, .30, .25, .10 };

    IncomeGrid(double sigma)
    {
        double sum = 0;
        for (int i = 0; i < N; i++) { Pc[i] = (i + 0.5) / N; M[i] = Math.Exp(sigma * Maths.NormInv(Pc[i])); sum += M[i]; }
        double mean = sum / N;
        for (int i = 0; i < N; i++) M[i] /= mean;
        for (int k = 0; k < 7; k++) VatW[k] = new double[N];
        for (int i = 0; i < N; i++)
        {
            for (int k = 0; k < 7; k++) VatW[k][i] = VatLow[k] + (VatHigh[k] - VatLow[k]) * Pc[i];
            Cons[i] = Math.Max(0.15, M[i] * (0.95 - 0.40 * Pc[i]));
        }
        double phiSum = 0;
        for (int i = 0; i < N; i++) { Phi[i] = 1.20 - 0.85 * Pc[i] * Pc[i]; phiSum += Phi[i]; }
        for (int i = 0; i < N; i++) Phi[i] /= phiSum / N;
        // incidence profiles by percentile (0 = poorest)
        Func<double, double>[] prof =
        {
            p => 1.0,                                // pension: flat-rate
            p => Math.Max(0.05, 2.2 - 2.0 * p),     // unemployment
            p => 1.1 - 0.2 * p,                     // child
            p => Math.Max(0.05, 2.0 - 1.8 * p),     // disability
            p => Math.Max(0.02, 2.4 - 2.3 * p),     // housing
            p => 1.0,                                // means-tested (formula-based)
            p => 1.0,                                // other
        };
        for (int k = 0; k < 7; k++)
        {
            var a = new double[N]; double s = 0;
            for (int i = 0; i < N; i++) { a[i] = prof[k](Pc[i]); s += a[i]; }
            for (int i = 0; i < N; i++) a[i] /= s / N;
            Incidence[k] = a;
        }
    }
}

/// <summary>Income-tax and contribution schedule at one moment: thresholds already scaled by the indexation drift.</summary>
public sealed class TaxParams
{
    public double Allow, TaperStart, TaperRate;
    public int NB;
    public readonly double[] From = new double[FiscalParams.MaxBands], Rate = new double[FiscalParams.MaxBands];
    public double EeFrom, EeRate, EeUpper, EeRate2, ErFrom, ErRate;

    public static TaxParams Of(IReadOnlyDictionary<string, double> p, double drift)
    {
        double G(string k) => p.TryGetValue(k, out var v) ? v : 0;
        var t = new TaxParams { Allow = G("Inc.Allow") * drift, TaperRate = G("Inc.TaperRate"), TaperStart = G("Inc.TaperStart") * drift };
        var bands = FiscalParams.Bands(p);
        t.NB = bands.Count;
        for (int i = 0; i < bands.Count; i++) { t.From[i] = bands[i].From * drift; t.Rate[i] = bands[i].Rate; }
        t.EeFrom = G("Pay.EeFrom") * drift; t.EeRate = G("Pay.EeRate"); t.EeUpper = G("Pay.EeUpper") * drift; t.EeRate2 = G("Pay.EeRate2");
        t.ErFrom = G("Pay.ErFrom") * drift; t.ErRate = G("Pay.ErRate");
        return t;
    }

    /// <summary>Personal allowance at income <paramref name="m"/> (after any taper).</summary>
    public double AllowanceAt(double m) =>
        TaperRate > 0 && TaperStart > 0 && m > TaperStart ? Math.Max(0, Allow - TaperRate * (m - TaperStart)) : Allow;

    public double Income(double m)
    {
        double ti = m - AllowanceAt(m);
        if (ti <= 0) return 0;
        double tax = 0;
        for (int i = 0; i < NB; i++)
        {
            double lo = From[i], hi = i + 1 < NB ? From[i + 1] : double.MaxValue;
            if (ti > lo) tax += Rate[i] * (Math.Min(ti, hi) - lo);
        }
        return tax;
    }

    public double Employee(double m)
    {
        if (m <= EeFrom) return 0;
        bool capped = EeUpper > 0 && EeUpper > EeFrom;
        double top = capped ? Math.Min(m, EeUpper) : m;
        double t = EeRate * (top - EeFrom);
        if (capped && m > EeUpper) t += EeRate2 * (m - EeUpper);
        return t;
    }

    public double Employer(double m) => m > ErFrom ? ErRate * (m - ErFrom) : 0;
}

/// <summary>Benefit rules at one moment, as ratios to their starting values (so the same code works at any scale).</summary>
public sealed class BenParams
{
    public double Pen, Une, UneLvl, UneDurRatio, Chi, Dis, Hou, Oth, DisElig;
    public double MtLevel, MtTaper, MtWork, UneLevel, UneDur, ChiThr;

    static double Rel(double now, double start, double floor) => start > 1e-6 ? now / start : 1.0 + now / floor;
    static double Dur(double months) => months / (months + 12.0);

    public static BenParams Of(IReadOnlyDictionary<string, double> p, IReadOnlyDictionary<string, double> p0, double penDrift)
    {
        double G(string k) => p.TryGetValue(k, out var v) ? v : 0;
        double G0(string k) => p0.TryGetValue(k, out var v) ? v : 0;
        var b = new BenParams
        {
            Pen = Rel(G("Pen.Level"), G0("Pen.Level"), 0.1) * penDrift * Math.Exp(-0.075 * (G("Pen.Age") - G0("Pen.Age"))),
            Dis = Rel(G("Dis.Level"), G0("Dis.Level"), 0.05) * (G("Dis.Elig") > 0 ? G("Dis.Elig") : 1.0) / (G0("Dis.Elig") > 0 ? G0("Dis.Elig") : 1.0),
            Hou = Rel(G("Hou.Level"), G0("Hou.Level"), 0.05),
            Oth = (G("Oth.Scale") > 0 ? G("Oth.Scale") : 1.0) / (G0("Oth.Scale") > 0 ? G0("Oth.Scale") : 1.0),
            MtLevel = G("Mt.Level"), MtTaper = G("Mt.Taper"), MtWork = G("Mt.WorkAllow"),
            UneLevel = G("Une.Level"), UneDur = Dur(G("Une.Months")), ChiThr = G("Chi.Threshold"),
        };
        b.Chi = Rel(G("Chi.Level"), G0("Chi.Level"), 0.02);
        b.UneLvl = Rel(G("Une.Level"), G0("Une.Level"), 0.05);
        b.UneDurRatio = Dur(Math.Max(1, G("Une.Months"))) / Dur(Math.Max(1, G0("Une.Months")));
        b.Une = b.UneLvl * b.UneDurRatio;
        b.DisElig = (G("Dis.Elig") > 0 ? G("Dis.Elig") : 1.0) / (G0("Dis.Elig") > 0 ? G0("Dis.Elig") : 1.0);
        return b;
    }

    public double Award(double m) => MtLevel <= 0 ? 0 : Math.Max(0, MtLevel - MtTaper * Math.Max(0, m - MtWork));
    public double MtMetr(double m) => MtLevel > 0 && m > MtWork && Award(m) > 0 ? MtTaper : 0;
}
