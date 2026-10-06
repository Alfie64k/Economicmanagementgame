using System.Text.Json;
using Sim.Core.Model;
using Sim.Core.Util;

namespace Sim.Core.Regions;

public sealed class RegionDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Country { get; set; } = "";
    public double Area { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
}

public sealed class RegionStat
{
    public RegionDef Def = new();
    public double PopShare, GdpShare, Pop, Gdp, GdpPerHeadRel, GrowthSinceStart, Unemployment;
    public string LeadingSector = "";
}

/// <summary>
/// Illustrative sub-national economies for large countries. Region population and sector location weights are generated
/// deterministically from the region id (NOT statistical data); regional output is the national sector output re-allocated
/// through those weights, so regions always sum to the national totals and shift as the national sector mix changes.
/// </summary>
public static class Regions
{
    static List<RegionDef>? _all;
    static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };
    public static IReadOnlyList<RegionDef> All => _all ??= Load();

    static List<RegionDef> Load()
    {
        using var s = typeof(Regions).Assembly.GetManifestResourceStream("data/regions.json") ?? throw new InvalidOperationException("regions.json missing");
        return JsonSerializer.Deserialize<List<RegionDef>>(s, Opts) ?? new();
    }

    public static bool Has(string country) => All.Any(r => r.Country == country);

    static double H(string id, string salt)
    {
        ulong h = 1469598103934665603UL;
        foreach (char ch in id + "|" + salt) { h ^= ch; h *= 1099511628211UL; }
        h ^= h >> 30; h *= 0xBF58476D1CE4E5B9UL; h ^= h >> 27; h *= 0x94D049BB133111EBUL; h ^= h >> 31;   // avalanche
        return (h >> 11) * (1.0 / (1UL << 53));
    }

    public static List<RegionStat> Compute(CountryState c)
    {
        var defs = All.Where(r => r.Country == c.Id).ToList();
        if (defs.Count == 0) return new();
        int n = defs.Count, S = Dim.Sectors;
        var pop = defs.Select(d => (0.35 + 1.3 * H(d.Id, "pop")) * Math.Pow(Math.Max(1, d.Area), 0.3)).ToArray();
        double ps = pop.Sum(); for (int i = 0; i < n; i++) pop[i] /= ps;
        var loc = new double[n, S];
        for (int s = 0; s < S; s++)
        {
            double sum = 0;
            for (int i = 0; i < n; i++) { loc[i, s] = pop[i] * (0.2 + 1.8 * Math.Pow(H(defs[i].Id, "sec" + s), 1.6)); sum += loc[i, s]; }
            for (int i = 0; i < n; i++) loc[i, s] /= sum;
        }
        double gdp = c.SectorVa.Sum(), gdp0 = c.Va0.Sum(), natGrowth = gdp / Math.Max(1e-9, gdp0) - 1;
        double years = Math.Max(0.5, c.Tick / 12.0);
        var res = new List<RegionStat>();
        for (int i = 0; i < n; i++)
        {
            double g = 0, g0 = 0;
            for (int s = 0; s < S; s++) { g += c.SectorVa[s] * loc[i, s]; g0 += c.Va0[s] * loc[i, s]; }
            double share = g / Math.Max(1e-9, gdp);
            int lead = 0; double lq = 0;
            for (int s = 0; s < S; s++) { double q = loc[i, s] / Math.Max(1e-9, share); if (q > lq) { lq = q; lead = s; } }
            double growth = g / Math.Max(1e-9, g0) - 1;
            double grow = Math.Pow(Math.Max(1e-9, 1 + growth), 1 / years) - 1, nat = Math.Pow(Math.Max(1e-9, 1 + natGrowth), 1 / years) - 1;
            double u = Maths.Clamp(c.Unemp - 6 * (grow - nat) + (H(defs[i].Id, "u") - 0.5) * 0.4 * c.Unemp, 0.005, 0.5);
            res.Add(new RegionStat
            {
                Def = defs[i], PopShare = pop[i], GdpShare = share, Pop = pop[i] * c.Pop, Gdp = g,
                GdpPerHeadRel = share / pop[i], GrowthSinceStart = growth, Unemployment = u, LeadingSector = ((Sector)lead).ToString(),
            });
        }
        return res;
    }
}
