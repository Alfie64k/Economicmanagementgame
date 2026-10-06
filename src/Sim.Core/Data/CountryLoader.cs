using System.Reflection;
using System.Text.Json;

namespace Sim.Core.Data;

public static class CountryLoader
{
    static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Loads the roster embedded in the assembly (data/countries.json).</summary>
    public static List<CountryData> LoadEmbedded()
    {
        var asm = typeof(CountryLoader).Assembly;
        using var s = asm.GetManifestResourceStream("data/countries.json")
            ?? throw new InvalidOperationException("Embedded data/countries.json not found");
        return Parse(s);
    }

    public static List<CountryData> LoadFile(string path)
    {
        using var s = File.OpenRead(path);
        return Parse(s);
    }

    public static List<CountryData> Parse(Stream s) =>
        JsonSerializer.Deserialize<List<CountryData>>(s, Opts) ?? throw new InvalidDataException("empty roster");

    /// <summary>Sanity invariants every roster entry must satisfy. Returns human-readable problems.</summary>
    public static List<string> Validate(CountryData d)
    {
        var p = new List<string>();
        void Chk(bool ok, string msg) { if (!ok) p.Add($"{d.Id}: {msg}"); }
        Chk(d.Id.Length == 3, "id must be ISO3");
        Chk(d.PopM > 0 && d.GdpLcuBn > 0 && d.UsdFx > 0, "positive pop/gdp/fx");
        Chk(Math.Abs(d.Sectors.Sum - 1) < 0.011, $"sector shares sum {d.Sectors.Sum:F3}");
        double demand = d.Demand.Cons + d.Demand.Inv + d.Demand.Gov + d.Demand.Exp - d.Demand.Imp;
        Chk(Math.Abs(demand - 1) < 0.03, $"demand identity {demand:F3}");
        Chk(d.Demog.Young > 0.05 && d.Demog.Old > 0.01 && d.Demog.Young + d.Demog.Old < 0.7, "age shares");
        Chk(d.Macro.Unemployment is > 0 and < 0.4, "unemployment range");
        Chk(d.Macro.Inflation is > -0.05 and < 3.0, "inflation range");
        Chk(d.Fiscal.Revenue is > 0.05 and < 0.6, "revenue range");
        Chk(d.Fiscal.Debt is >= 0 and < 3, "debt range");
        Chk(d.Lat is >= -90 and <= 90 && d.Lon is >= -180 and <= 180, "lat/lon");
        double progs = d.Fiscal.Health + d.Fiscal.Education + d.Fiscal.Defence + d.Fiscal.Infra + d.Fiscal.Rnd + 0.009;
        Chk(d.Demand.Gov - progs >= 0.015, $"admin residual too small ({d.Demand.Gov - progs:F3}); programme lines exceed government demand share");
        Chk(d.Demand.Inv > d.Demand.Gov * 0.2 && d.Demand.Inv < 0.6, "investment share range");
        return p;
    }
}
