using System.Globalization;
using Sim.Core.Model;
using Sim.Core.Util;

namespace Sim.Core.Policy;

public enum PKind { Amount, Rate, Years, Months, Count, Choice, Factor }

/// <summary>One editable parameter of the tax-and-benefit code.</summary>
public sealed class PDef
{
    public string Key = "", Group = "", Label = "", Tip = "";
    public PKind Kind;
    public double Min, Max, Step;
    public string[] Options = Array.Empty<string>();
}

/// <summary>Registry of every parameter in <see cref="FiscalCode.P"/>: its bounds, labels, how it is shown and what changing it costs in political capital.</summary>
public static class FiscalParams
{
    public const int MaxBands = 8;
    public static readonly string[] IndexOptions = { "Earnings (neutral)", "Prices (CPI)", "Frozen" };
    public static readonly string[] PenIndexOptions = { "Earnings (neutral)", "Prices (CPI)", "Triple lock", "Frozen" };
    public static readonly string[] VatOptions = { "Zero-rated", "Reduced rate", "Standard rate", "Exempt" };
    public static readonly string[] VatCategories = { "food", "energy", "housing", "transport", "services", "goods", "health_edu" };
    public static readonly string[] VatCategoryNames = { "Food", "Energy and fuel", "Housing and rent", "Transport", "Services", "Goods", "Health and education" };
    public static string VatKey(string cat) => "Vat." + string.Concat(cat.Split('_').Select(s => char.ToUpperInvariant(s[0]) + s[1..]));

    public static string BandFrom(int i) => $"Inc.B{i}.From";
    public static string BandRate(int i) => $"Inc.B{i}.Rate";
    public const string BandsKey = "Inc.Bands";

    static readonly List<PDef> _all = Build();
    static readonly Dictionary<string, PDef> _by = _all.ToDictionary(d => d.Key);
    public static IReadOnlyList<PDef> All => _all;
    public static PDef? Def(string key) => _by.TryGetValue(key, out var d) ? d : BandDef(key);

    static PDef? BandDef(string key)
    {
        for (int i = 0; i < MaxBands; i++)
        {
            if (key == BandRate(i)) return new PDef { Key = key, Group = "Income tax", Label = $"Band {i + 1} rate", Kind = PKind.Rate, Min = 0, Max = 0.75, Step = 0.005, Tip = "Rate on the slice of taxable income inside this band." };
            if (i > 0 && key == BandFrom(i)) return new PDef { Key = key, Group = "Income tax", Label = $"Band {i + 1} starts at", Kind = PKind.Amount, Min = 0.02, Max = 40, Step = 0.01, Tip = "Taxable income above which this band's rate applies." };
        }
        return null;
    }

    static PDef A(string key, string group, string label, double min, double max, double step, string tip) =>
        new() { Key = key, Group = group, Label = label, Kind = PKind.Amount, Min = min, Max = max, Step = step, Tip = tip };
    static PDef R(string key, string group, string label, double min, double max, double step, string tip) =>
        new() { Key = key, Group = group, Label = label, Kind = PKind.Rate, Min = min, Max = max, Step = step, Tip = tip };
    static PDef K(string key, string group, string label, PKind kind, double min, double max, double step, string tip, string[]? opts = null) =>
        new() { Key = key, Group = group, Label = label, Kind = kind, Min = min, Max = max, Step = step, Tip = tip, Options = opts ?? Array.Empty<string>() };

    static List<PDef> Build()
    {
        const string I = "Income tax", N = "Payroll & social contributions", C = "Corporation tax", V = "VAT & sales tax";
        var l = new List<PDef>
        {
            A("Inc.Allow", I, "Personal allowance", 0, 3, 0.005, "Income below this is untaxed (UK personal allowance, US standard deduction)."),
            A("Inc.TaperStart", I, "Allowance taper starts at", 0, 40, 0.05, "Above this income the allowance is withdrawn. Set the taper rate to zero for no taper."),
            R("Inc.TaperRate", I, "Allowance withdrawn per unit of income", 0, 1, 0.01, "UK: 50p of allowance lost per £1 over £100,000, which creates a 60% effective marginal rate."),
            K("Thr.Index", I, "Threshold indexation", PKind.Choice, 0, 2, 1, "How allowances, bands and contribution thresholds move each year. Prices or frozen lets earnings growth drag people into higher bands (fiscal drag).", IndexOptions),

            A("Pay.EeFrom", N, "Employee contributions start at", 0, 3, 0.005, "Lower earnings threshold for employee contributions."),
            R("Pay.EeRate", N, "Employee rate", 0, 0.4, 0.0025, "Employee social-security rate between the lower and upper thresholds."),
            A("Pay.EeUpper", N, "Employee upper limit (0 = none)", 0, 40, 0.05, "Above this the lower second-tier rate applies. 0 means the first rate is uncapped."),
            R("Pay.EeRate2", N, "Employee rate above the limit", 0, 0.4, 0.0025, "Rate on earnings above the upper limit."),
            A("Pay.ErFrom", N, "Employer contributions start at", 0, 3, 0.005, "Secondary threshold: employer contributions are due on pay above this."),
            R("Pay.ErRate", N, "Employer rate", 0, 0.5, 0.0025, "Employer social-security rate. It raises the cost of hiring and lowers wages in the long run."),

            R("Corp.Main", C, "Main rate", 0, 0.6, 0.0025, "Rate on most company profits."),
            R("Corp.Small", C, "Small-profits rate", 0, 0.6, 0.0025, "Reduced rate for profits below the small-profits limit."),
            A("Corp.SmallLimit", C, "Small-profits limit", 0, 2000, 1, "Annual profit below which the small-profits rate applies (0 = no small rate)."),
            R("Corp.Expensing", C, "Capital expensing", 0, 1, 0.01, "Share of capital investment deducted immediately. Full expensing costs revenue now and raises investment."),

            R("Vat.Std", V, "Standard rate", 0, 0.35, 0.0025, "VAT, GST or average sales-tax rate on most goods and services."),
            R("Vat.Red", V, "Reduced rate", 0, 0.3, 0.0025, "Rate for categories treated as reduced-rated."),
        };
        for (int i = 0; i < VatCategories.Length; i++)
            l.Add(K(VatKey(VatCategories[i]), V, VatCategoryNames[i], PKind.Choice, 0, 3, 1, "How this category of spending is taxed. Zero-rating and reduced rates favour the poor but cost revenue; exempt goods lose input credits.", VatOptions));

        const string P = "State pension", U = "Unemployment benefit", F = "Child benefit", D = "Disability benefit", H = "Housing benefit", M = "Means-tested support", O = "Other benefits";
        l.AddRange(new[]
        {
            A("Pen.Level", P, "Full state pension", 0, 1.5, 0.005, "Annual pension per recipient."),
            K("Pen.Age", P, "State pension age", PKind.Years, 55, 75, 1, "Raising it saves pension spending and keeps older people in work, and is politically toxic. It overlaps the 'Raise retirement age' policy."),
            K("Pen.Index", P, "Annual uprating", PKind.Choice, 0, 3, 1, "Triple lock: the larger of earnings growth, inflation and 2.5% every year, so pensions drift up against earnings.", PenIndexOptions),
            A("Une.Level", U, "Jobseeker benefit (annual)", 0, 1, 0.005, "Annual jobseeker support per recipient. A higher replacement rate raises the natural rate of unemployment."),
            K("Une.Months", U, "Maximum duration (months)", PKind.Months, 1, 36, 1, "Months before the main benefit runs out. Longer durations raise the natural rate of unemployment and cushion demand in a recession."),
            A("Chi.Level", F, "Per child, per year", 0, 0.5, 0.002, "Child benefit for the first child."),
            A("Chi.Threshold", F, "Withdrawn above income of (0 = universal)", 0, 15, 0.05, "Households above this income lose the benefit."),
            A("Dis.Level", D, "Disability and incapacity benefit", 0, 1, 0.005, "Annual benefit per recipient."),
            K("Dis.Elig", D, "Eligibility strictness", PKind.Factor, 0.5, 1.5, 0.01, "1.00 = current rules; below 1 tightens assessment (fewer claimants), above 1 loosens it."),
            A("Hou.Level", H, "Housing support per household", 0, 0.8, 0.005, "Annual housing benefit per recipient household."),
            A("Mt.Level", M, "Maximum award", 0, 1, 0.005, "Annual maximum of the main means-tested support (UK Universal Credit)."),
            R("Mt.Taper", M, "Taper: withdrawn per unit of earnings", 0, 0.9, 0.01, "Each pound earned above the work allowance cuts the award by this much. High tapers create poverty traps."),
            A("Mt.WorkAllow", M, "Work allowance", 0, 1.5, 0.005, "Earnings that can be kept before the taper begins."),
            K("Oth.Scale", O, "Other benefits (scale)", PKind.Factor, 0.5, 1.5, 0.01, "Everything not listed above, as a multiple of today's spending."),
        });
        return l;
    }

    public static string[] Groups { get; } = _all.Select(d => d.Group).Distinct().ToArray();

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // normalisation

    /// <summary>The bands as (start, rate) pairs, in order.</summary>
    public static List<(double From, double Rate)> Bands(IReadOnlyDictionary<string, double> p)
    {
        int nb = Math.Clamp((int)Math.Round(p.TryGetValue("Inc.NBands", out var n) ? n : 1), 1, MaxBands);
        var l = new List<(double, double)>();
        for (int i = 0; i < nb; i++)
            l.Add((i == 0 ? 0 : (p.TryGetValue(BandFrom(i), out var f) ? f : i), p.TryGetValue(BandRate(i), out var r) ? r : 0));
        return l;
    }

    public static void SetBands(Dictionary<string, double> p, IReadOnlyList<(double From, double Rate)> bands)
    {
        for (int i = 0; i < MaxBands; i++) { p.Remove(BandFrom(i)); p.Remove(BandRate(i)); }
        int nb = Math.Clamp(bands.Count, 1, MaxBands);
        p["Inc.NBands"] = nb;
        double last = 0;
        for (int i = 0; i < nb; i++)
        {
            double from = i == 0 ? 0 : Math.Max(bands[i].From, last + 0.01);
            if (i > 0) { p[BandFrom(i)] = Math.Round(from, 4); last = from; }
            p[BandRate(i)] = Maths.Clamp(bands[i].Rate, 0, 0.75);
        }
    }

    /// <summary>Clamp every value to its bounds and repair orderings (bands strictly increasing, contribution limits above their floors).</summary>
    public static void Normalise(Dictionary<string, double> p)
    {
        foreach (var d in _all)
            if (p.TryGetValue(d.Key, out var v))
            {
                v = Maths.Clamp(v, d.Min, d.Max);
                if (d.Kind is PKind.Years or PKind.Months or PKind.Choice or PKind.Count) v = Math.Round(v);
                p[d.Key] = v;
            }
        SetBands(p, Bands(p));
        if (p.TryGetValue("Pay.EeUpper", out var up) && up > 0 && p.TryGetValue("Pay.EeFrom", out var lo) && up < lo + 0.05) p["Pay.EeUpper"] = Math.Min(40, lo + 0.05);
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // display

    /// <summary>Nominal local-currency value of one unit of a parameter stored in mean-earnings multiples, now.</summary>
    public static double NominalFactor(CountryState c, string key)
    {
        var f = c.Fiscal; if (f == null) return 1;
        double earn = f.MeanEarn * c.RealWageIdx * c.PriceLevel;
        if (key == "Pen.Level") return earn * f.PenDrift;
        if (key.StartsWith("Inc.") || key.StartsWith("Pay.") || key == "Corp.SmallLimit") return earn * f.Drift;
        return earn;
    }

    public static string Show(CountryState c, string key, double v)
    {
        var d = Def(key); if (d == null) return v.ToString("0.###", CultureInfo.InvariantCulture);
        switch (d.Kind)
        {
            case PKind.Amount:
                if (key == "Inc.TaperStart" && v <= 0) return "none";
                if ((key is "Chi.Threshold" or "Pay.EeUpper" or "Corp.SmallLimit") && v <= 0) return "none";
                return Fmt.Amount(c.Currency, v * NominalFactor(c, key));
            case PKind.Rate: return Fmt.P(v, 1);
            case PKind.Years: return $"{v:0} years";
            case PKind.Months: return $"{v:0} months";
            case PKind.Choice: return d.Options[Math.Clamp((int)Math.Round(v), 0, d.Options.Length - 1)];
            case PKind.Factor: return $"×{v:0.00}";
            default: return v.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }

    public static string ShowBands(CountryState c, IReadOnlyList<(double From, double Rate)> b) =>
        string.Join(", ", b.Select(x => $"{Fmt.P(x.Rate, 1)} from {Fmt.Amount(c.Currency, x.From * NominalFactor(c, "Inc.B1.From"))}"));

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // political cost

    /// <summary>Political capital for changing one parameter from <paramref name="from"/> to <paramref name="to"/>. Starting priors: cuts to benefits and to allowances cost half as much again; pensions are the most sensitive.</summary>
    public static double Cost(PDef d, double from, double to)
    {
        double dv = to - from;
        if (Math.Abs(dv) < 1e-12) return 0;
        double cost;
        switch (d.Key)
        {
            case "Pen.Age": cost = dv > 0 ? 8 + 12 * dv : 4 + 3 * -dv; break;
            case "Thr.Index": case "Pen.Index": cost = 8; break;
            default:
                switch (d.Kind)
                {
                    case PKind.Rate: cost = 3 + 90 * Math.Abs(dv) * (d.Group is "Income tax" or "Payroll & social contributions" && dv > 0 ? 1.25 : 1.0); break;
                    case PKind.Amount:
                        {
                            double rel = Math.Abs(Math.Log(Math.Max(0.02, to) / Math.Max(0.02, from)));
                            bool benefit = d.Group is "State pension" or "Unemployment benefit" or "Child benefit" or "Disability benefit" or "Housing benefit" or "Means-tested support";
                            bool hurts = benefit ? dv < 0 : (d.Key is "Inc.Allow" or "Pay.EeFrom" or "Pay.ErFrom") ? dv < 0 : (d.Key is "Inc.TaperStart") ? dv < 0 : false;
                            cost = 3 + 28 * rel * (hurts ? 1.5 : 1.0) * (d.Key == "Pen.Level" ? 1.3 : 1.0);
                            break;
                        }
                    case PKind.Choice: cost = 6 + 2 * Math.Abs(dv); break;
                    case PKind.Months: cost = 3 + 0.5 * Math.Abs(dv) * (dv < 0 ? 1.5 : 1.0); break;
                    default: cost = 3 + 40 * Math.Abs(dv) * (dv < 0 && d.Key == "Dis.Elig" ? 1.5 : 1.0); break;
                }
                break;
        }
        return Math.Min(40, cost);
    }

    /// <summary>Cost of replacing the whole band table (the largest of its parts plus a small charge per band touched).</summary>
    public static double BandsCost(IReadOnlyList<(double From, double Rate)> from, IReadOnlyList<(double From, double Rate)> to)
    {
        double max = 0, extra = 0;
        int n = Math.Max(from.Count, to.Count);
        for (int i = 0; i < n; i++)
        {
            var a = i < from.Count ? from[i] : (From: to[i].From, Rate: 0.0);
            var b = i < to.Count ? to[i] : (From: from[i].From, Rate: a.Rate);
            double c = Cost(Def(BandRate(Math.Min(i, MaxBands - 1)))!, a.Rate, b.Rate);
            if (i > 0 && Math.Abs(a.From - b.From) > 1e-9) c = Math.Max(c, Cost(Def(BandFrom(Math.Min(i, MaxBands - 1)))!, a.From, b.From));
            if (c > 0) { max = Math.Max(max, c); extra += 1.5; }
        }
        return Math.Min(40, max + extra * 0.5);
    }

    public static string EncodeBands(IReadOnlyList<(double From, double Rate)> b) =>
        string.Join(",", b.Select(x => x.From.ToString("0.####", CultureInfo.InvariantCulture) + ":" + x.Rate.ToString("0.####", CultureInfo.InvariantCulture)));

    public static List<(double From, double Rate)>? DecodeBands(string s)
    {
        var l = new List<(double, double)>();
        foreach (var part in s.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var xy = part.Split(':');
            if (xy.Length != 2 || !double.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var f) || !double.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var r)) return null;
            if (!Maths.Finite(f) || !Maths.Finite(r)) return null;
            l.Add((f, r));
        }
        return l.Count is >= 1 and <= MaxBands ? l : null;
    }
}
