using System.Text.Json.Nodes;
using Sim.Core.Engine;
using Sim.Core.Model;
using Sim.Core.Policy;
using Sim.Core.Util;

namespace Sim.Core.Data;

/// <summary>
/// Loads <c>data/taxcodes.json</c> (approximate 2024/25 statutory parameters: eight named countries in detail, archetype templates for the rest)
/// and turns the entry for a country into its <see cref="FiscalCode"/>. Amounts in the file are either local currency ("lcu", with the country's
/// mean earnings) or multiples of mean earnings ("me"); everything inside the game is held in multiples of mean earnings.
/// </summary>
public static class TaxCodeCatalog
{
    static readonly Lazy<JsonObject?> Doc = new(Load, true);
    public static bool Available => Doc.Value != null;
    public static string Note => Doc.Value?["note"]?.GetValue<string>() ?? "";

    static JsonObject? Load()
    {
        using var s = typeof(TaxCodeCatalog).Assembly.GetManifestResourceStream("data/taxcodes.json");
        return s == null ? null : JsonNode.Parse(s) as JsonObject;
    }

    /// <summary>Parses an arbitrary document (used by the tests to exercise the loader on a small fixture).</summary>
    public static FiscalCode? BuildFrom(JsonObject doc, CountryState c) => BuildCore(doc, c);

    public static FiscalCode? Build(CountryState c) => Doc.Value is { } d ? BuildCore(d, c) : null;

    static readonly string[][] AmountPaths =
    {
        new[] { "income", "allowance" }, new[] { "income", "taperStart" }, new[] { "payroll", "employer", "from" }, new[] { "corp", "smallLimit" },
        new[] { "benefits", "pension", "level" }, new[] { "benefits", "unemployment", "level" }, new[] { "benefits", "child", "level" }, new[] { "benefits", "child", "threshold" },
        new[] { "benefits", "disability", "level" }, new[] { "benefits", "housing", "level" }, new[] { "benefits", "meanstest", "level" }, new[] { "benefits", "meanstest", "workAllowance" },
    };

    static double Num(JsonNode? n) => n == null ? 0 : n.GetValue<double>();

    /// <summary>Copy of the entry with every amount converted to multiples of mean earnings.</summary>
    static JsonObject ToMe(JsonObject o)
    {
        var c = (JsonObject)o.DeepClone();
        string unit = c["unit"]?.GetValue<string>() ?? "me";
        if (unit == "lcu")
        {
            double mean = Num(c["meanEarnings"]);
            if (mean <= 0) throw new InvalidDataException("an entry in lcu needs meanEarnings");
            void Div(JsonObject? parent, string key) { if (parent != null && parent[key] != null) parent[key] = Num(parent[key]) / mean; }
            foreach (var p in AmountPaths)
            {
                JsonNode? cur = c;
                for (int i = 0; i < p.Length - 1 && cur != null; i++) cur = cur[p[i]];
                Div(cur as JsonObject, p[^1]);
            }
            if (c["income"]?["bands"] is JsonArray bands) foreach (var b in bands) Div(b as JsonObject, "from");
            if (c["payroll"]?["employee"] is JsonArray ee) foreach (var b in ee) Div(b as JsonObject, "from");
        }
        c["unit"] = "me";
        return c;
    }

    static void Merge(JsonObject into, JsonObject over)
    {
        foreach (var kv in over)
        {
            if (kv.Value is JsonObject oo && into[kv.Key] is JsonObject io) Merge(io, oo);
            else into[kv.Key] = kv.Value?.DeepClone();
        }
    }

    /// <summary>The archetype template overlaid with the country's own entry, in multiples of mean earnings.</summary>
    public static JsonObject Merged(JsonObject doc, string id, string archetype)
    {
        var arch = doc["archetypes"]?[archetype] as JsonObject ?? doc["archetypes"]?["advanced"] as JsonObject ?? throw new InvalidDataException("no archetype template");
        var merged = ToMe(arch);
        if (doc["countries"]?[id] is JsonObject ent) Merge(merged, ToMe(ent));
        return merged;
    }

    public static double RawMeanEarnings(JsonObject doc, string id) => Num(doc["countries"]?[id]?["meanEarnings"]);

    static double Treat(string? s) => s switch { "zero" => 0, "reduced" => 1, "standard" => 2, "exempt" => 3, _ => 2 };

    static FiscalCode? BuildCore(JsonObject doc, CountryState c)
    {
        var m = Merged(doc, c.Id, c.Archetype);
        var f = new FiscalCode { Label = m["label"]?.GetValue<string>() ?? c.Name, StatIndex = m["income"]?["index"]?.GetValue<string>() ?? "" };
        double earnIdx = Math.Max(1e-9, c.RealWageIdx * c.PriceLevel);
        double mean = Num(m["meanEarnings"]);
        if (mean <= 0) mean = 0.8 * FiscalEngine.IncomeBaseShare * c.Gdp0 * 1000.0 / Math.Max(1e-6, c.Pop * c.Working * c.Participation * (1 - c.Unemp0));
        f.MeanEarn = mean / earnIdx;
        f.Soc0 = c.Budget0[(int)BudgetLine.Social];
        f.Sigma = IncomeGrid.SigmaFromGini(Maths.Clamp(c.Gini0 + 0.10, 0.30, 0.65));   // first guess, refined below once the code is read

        var p = f.P0;
        var inc = m["income"] as JsonObject ?? new JsonObject();
        p["Inc.Allow"] = Num(inc["allowance"]); p["Inc.TaperStart"] = Num(inc["taperStart"]); p["Inc.TaperRate"] = Num(inc["taperRate"]);
        var bands = new List<(double, double)>();
        if (inc["bands"] is JsonArray ba) foreach (var b in ba) bands.Add((Num(b?["from"]), Num(b?["rate"])));
        if (bands.Count == 0) bands.Add((0, 0));
        FiscalParams.SetBands(p, bands.Select((b, i) => (i == 0 ? 0.0 : b.Item1, b.Item2)).ToList());
        p["Thr.Index"] = 0;                                  // the game starts neutral; the real-world setting is only noted

        var pay = m["payroll"] as JsonObject ?? new JsonObject();
        var ee = pay["employee"] as JsonArray ?? new JsonArray();
        p["Pay.EeFrom"] = ee.Count > 0 ? Num(ee[0]?["from"]) : 0; p["Pay.EeRate"] = ee.Count > 0 ? Num(ee[0]?["rate"]) : 0;
        p["Pay.EeUpper"] = ee.Count > 1 ? Num(ee[1]?["from"]) : 0; p["Pay.EeRate2"] = ee.Count > 1 ? Num(ee[1]?["rate"]) : 0;
        p["Pay.ErFrom"] = Num(pay["employer"]?["from"]); p["Pay.ErRate"] = Num(pay["employer"]?["rate"]);

        var corp = m["corp"] as JsonObject ?? new JsonObject();
        p["Corp.Main"] = Num(corp["main"]); p["Corp.Small"] = corp["small"] != null ? Num(corp["small"]) : Num(corp["main"]);
        p["Corp.SmallLimit"] = Num(corp["smallLimit"]); p["Corp.Expensing"] = Num(corp["expensing"]);

        var vat = m["vat"] as JsonObject ?? new JsonObject();
        p["Vat.Std"] = Num(vat["standard"]); p["Vat.Red"] = Num(vat["reduced"]);
        foreach (var cat in FiscalParams.VatCategories) p[FiscalParams.VatKey(cat)] = Treat(vat["categories"]?[cat]?.GetValue<string>());

        var ben = m["benefits"] as JsonObject ?? new JsonObject();
        var sh = ben["shares"] as JsonObject ?? new JsonObject();
        string[] names = { "pension", "unemployment", "child", "disability", "housing", "meanstest", "other" };
        double tot = names.Sum(n => Num(sh[n]));
        for (int k = 0; k < 7; k++) f.Shares[k] = tot > 0 ? Num(sh[names[k]]) / tot : (k == 6 ? 1.0 : 0.0);
        p["Pen.Level"] = Num(ben["pension"]?["level"]); p["Pen.Age"] = Num(ben["pension"]?["age"]) > 0 ? Num(ben["pension"]?["age"]) : 65; p["Pen.Index"] = 0;
        p["Une.Level"] = Num(ben["unemployment"]?["level"]); p["Une.Months"] = Num(ben["unemployment"]?["months"]) > 0 ? Num(ben["unemployment"]?["months"]) : 12;
        p["Chi.Level"] = Num(ben["child"]?["level"]); p["Chi.Threshold"] = Num(ben["child"]?["threshold"]);
        p["Dis.Level"] = Num(ben["disability"]?["level"]); p["Dis.Elig"] = 1.0;
        p["Hou.Level"] = Num(ben["housing"]?["level"]);
        p["Mt.Level"] = Num(ben["meanstest"]?["level"]); p["Mt.Taper"] = Num(ben["meanstest"]?["taper"]); p["Mt.WorkAllow"] = Num(ben["meanstest"]?["workAllowance"]);
        p["Oth.Scale"] = 1.0;
        FiscalParams.Normalise(p);

        f.P = new Dictionary<string, double>(p);
        f.LastRealWage = 0; f.LastPrice = 0;                 // initialised on the first step
        f.Init = true;

        // the grid's spread of market incomes is set so that, after the starting taxes and benefits, inequality of net income matches the country's Gini
        double lo = 0.55, hi = 1.15;                  // realistic earnings dispersion; richer redistribution than the data shows simply saturates the search
        for (int it = 0; it < 22; it++)
        {
            double mid = 0.5 * (lo + hi);
            double g = TaxCodeEngine.Evaluate(f, f.P0, 1.0, 1.0, null, IncomeGrid.Fresh(mid)).GiniNet;
            if (g < c.Gini0) lo = mid; else hi = mid;
        }
        f.Sigma = Math.Round(0.5 * (lo + hi), 3);
        f.Cache = null;

        // calibration: relative changes in the statutory average rate carry over one for one to the engine's effective rate
        // (where the code has no such tax at the start, a change is taken at face value instead)
        var bas = TaxCodeEngine.BaseEval(f);
        f.GiniScale = Math.Clamp(c.Gini0 / Math.Max(0.05, bas.GiniNet), 1.0, 1.5);
        var raw = RawCalib(c, bas);
        for (int i = 0; i < 4; i++) f.Calib[i] = raw[i] > 0 ? raw[i] : 1.0;
        for (int i = 0; i < Dim.Taxes; i++) f.Written[i] = c.TaxRate[i];
        TaxCodeEngine.Recalc(c, f);
        return f;
    }

    /// <summary>Unclamped ratio of the engine's starting effective rate to the statutory average rate, per tax (Income, Corporate, Consumption, Payroll); 0 when the code has no such tax.</summary>
    public static double[] RawCalib(CountryState c, FiscalEval bas)
    {
        double R(double x, double engine) => x > 5e-3 ? engine / x : 0;
        return new[]
        {
            R(bas.AvgInc, c.TaxRate0[(int)Tax.Income]), R(bas.CorpEff, c.TaxRate0[(int)Tax.Corporate]),
            R(bas.VatEff, c.TaxRate0[(int)Tax.Consumption]), R(bas.AvgPay, c.TaxRate0[(int)Tax.Payroll]),
        };
    }

    /// <summary>Structural problems with the data file (empty when it is sound).</summary>
    public static List<string> Validate(JsonObject doc, IEnumerable<(string Id, string Archetype)> roster)
    {
        var problems = new List<string>();
        foreach (var (id, arch) in roster)
        {
            JsonObject m;
            try { m = Merged(doc, id, arch); } catch (Exception ex) { problems.Add($"{id}: {ex.Message}"); continue; }
            void Chk(bool ok, string msg) { if (!ok) problems.Add($"{id}: {msg}"); }
            var bands = (m["income"]?["bands"] as JsonArray)?.Select(b => (From: Num(b?["from"]), Rate: Num(b?["rate"]))).ToList() ?? new();
            Chk(bands.Count is >= 1 and <= FiscalParams.MaxBands, "bands count");
            if (bands.Count > 0)
            {
                Chk(bands[0].From == 0, "first band must start at 0");
                for (int i = 1; i < bands.Count; i++) Chk(bands[i].From > bands[i - 1].From, "bands must be strictly increasing");
                foreach (var b in bands) Chk(b.Rate is >= 0 and <= 0.75, "band rate range");
            }
            var sh = m["benefits"]?["shares"] as JsonObject;
            Chk(sh != null && Math.Abs(sh.Sum(kv => Num(kv.Value)) - 1) < 0.006, "benefit shares must sum to 1");
            Chk(Num(m["vat"]?["standard"]) is >= 0 and <= 0.30, "VAT rate range");
            Chk(Num(m["corp"]?["main"]) is >= 0 and <= 0.45, "corporation tax range");
            Chk(Num(m["benefits"]?["pension"]?["age"]) is >= 55 and <= 75, "pension age range");
            Chk(Num(m["income"]?["allowance"]) is >= 0 and <= 3, "allowance range (multiples of mean earnings)");
        }
        return problems;
    }
}
