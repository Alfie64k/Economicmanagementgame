using System.Text.Json.Nodes;

namespace Sim.Core.Data;

/// <summary>Starting labour-market structure of a country (from <c>data/labour.json</c>).</summary>
/// <param name="Coverage">Share of employees covered by collective agreements, 0 to 1.</param>
/// <param name="Strength">0 to 1: how much of that coverage is real bargaining power (coordination, freedom to strike).</param>
/// <param name="LabourShare">Labour compensation as a share of GDP; only its deviations are used by the engine.</param>
public readonly record struct LabourProfile(double Coverage, double Strength, double LabourShare);

static class DataFile
{
    public static JsonObject? Load(string resource)
    {
        using var s = typeof(DataFile).Assembly.GetManifestResourceStream(resource);
        return s == null ? null : JsonNode.Parse(s) as JsonObject;
    }

    public static double Num(JsonNode? n, double fallback = 0) => n == null ? fallback : n.GetValue<double>();
}

/// <summary>
/// Approximate starting size of each country's informal ("shadow") economy as a share of GDP (<c>data/shadow.json</c>, built by
/// <c>tools/data_import/build_shadow.py</c>). Falls back to an archetype value for any country not listed.
/// </summary>
public static class ShadowCatalog
{
    static readonly Lazy<JsonObject?> Doc = new(() => DataFile.Load("data/shadow.json"), true);
    public static bool Available => Doc.Value != null;
    public static string Note => Doc.Value?["note"]?.GetValue<string>() ?? "";

    // used only when the file or the entry is missing (a custom roster in a test, say)
    static readonly Dictionary<string, double> Fallback = new() { ["advanced"] = 0.10, ["hub"] = 0.09, ["emerging"] = 0.25, ["resource"] = 0.28, ["developing"] = 0.38 };

    public static double Share(string id, string archetype) => ShareFrom(Doc.Value, id, archetype);

    public static double ShareFrom(JsonObject? doc, string id, string archetype)
    {
        double v = DataFile.Num(doc?["countries"]?[id]?["share"], -1);
        if (v < 0) v = DataFile.Num(doc?["archetypes"]?[archetype]?["share"], -1);
        if (v < 0) v = Fallback.TryGetValue(archetype, out var f) ? f : 0.25;
        return Math.Clamp(v, 0.02, 0.70);
    }

    /// <summary>Structural problems with the data file (empty when it is sound).</summary>
    public static List<string> Validate(JsonObject doc, IEnumerable<(string Id, string Archetype)> roster)
    {
        var problems = new List<string>();
        foreach (var (id, arch) in roster)
        {
            var e = doc["countries"]?[id] as JsonObject;
            if (e == null) { problems.Add($"{id}: no entry"); continue; }
            double s = DataFile.Num(e["share"], -1), lo = DataFile.Num(e["lo"], -1), hi = DataFile.Num(e["hi"], -1);
            if (s is < 0.02 or > 0.70) problems.Add($"{id}: share {s} outside [0.02, 0.70]");
            if (!(0 < lo && lo <= s && s <= hi && hi < 1)) problems.Add($"{id}: range [{lo}, {hi}] does not bracket {s}");
            if (doc["archetypes"]?[arch] == null) problems.Add($"{id}: archetype {arch} has no fallback");
        }
        return problems;
    }
}

/// <summary>
/// Approximate union coverage, bargaining strength and labour share of income (<c>data/labour.json</c>, built by
/// <c>tools/data_import/build_labour.py</c>). A country's own entry is overlaid on its archetype's template.
/// </summary>
public static class LabourCatalog
{
    static readonly Lazy<JsonObject?> Doc = new(() => DataFile.Load("data/labour.json"), true);
    public static bool Available => Doc.Value != null;
    public static string Note => Doc.Value?["note"]?.GetValue<string>() ?? "";

    static readonly Dictionary<string, LabourProfile> Fallback = new()
    {
        ["advanced"] = new(0.40, 0.75, 0.58), ["hub"] = new(0.35, 0.65, 0.50), ["emerging"] = new(0.20, 0.50, 0.46),
        ["resource"] = new(0.20, 0.40, 0.42), ["developing"] = new(0.08, 0.30, 0.45),
    };

    public static LabourProfile For(string id, string archetype) => From(Doc.Value, id, archetype);

    public static LabourProfile From(JsonObject? doc, string id, string archetype)
    {
        var fb = Fallback.TryGetValue(archetype, out var f) ? f : Fallback["emerging"];
        var a = doc?["archetypes"]?[archetype];
        var e = doc?["countries"]?[id];
        double Pick(string key, double d) => DataFile.Num(e?[key], DataFile.Num(a?[key], d));
        return new LabourProfile(
            Math.Clamp(Pick("coverage", fb.Coverage), 0, 1), Math.Clamp(Pick("strength", fb.Strength), 0, 1), Math.Clamp(Pick("labourShare", fb.LabourShare), 0.20, 0.80));
    }

    /// <summary>Structural problems with the data file (empty when it is sound).</summary>
    public static List<string> Validate(JsonObject doc, IEnumerable<(string Id, string Archetype)> roster)
    {
        var problems = new List<string>();
        foreach (var (id, arch) in roster)
        {
            var e = doc["countries"]?[id] as JsonObject;
            if (e == null) { problems.Add($"{id}: no entry"); continue; }
            double c = DataFile.Num(e["coverage"], -1), s = DataFile.Num(e["strength"], -1), l = DataFile.Num(e["labourShare"], -1);
            if (c is < 0 or > 1) problems.Add($"{id}: coverage {c} outside [0, 1]");
            if (s is < 0 or > 1) problems.Add($"{id}: strength {s} outside [0, 1]");
            if (l is < 0.25 or > 0.70) problems.Add($"{id}: labour share {l} outside [0.25, 0.70]");
            if (doc["archetypes"]?[arch] == null) problems.Add($"{id}: archetype {arch} has no template");
        }
        return problems;
    }
}
