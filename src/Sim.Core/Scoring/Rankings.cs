using Sim.Core.Model;
using Sim.Core.Util;

namespace Sim.Core.Scoring;

public sealed class RankRow
{
    public string Id = "", Name = "";
    public double Value;       // shown figure
    public int Rank;           // 1 = best
    public bool Player;
}

public sealed class RankMetric
{
    public string Key = "", Label = "", Tip = "";
    public bool HigherIsBetter = true;
    /// <summary>Value for a country at a point of its history (null when the figure cannot be read from history).</summary>
    public Func<HistoryPoint, double>? FromHistory;
    /// <summary>Value for a country now.</summary>
    public Func<World, CountryState, double> Now = (_, _) => 0;
    /// <summary>Optional transform used for ordering only (so "closest to target" can rank on distance).</summary>
    public Func<HistoryPoint?, CountryState, double, double>? SortKey;
    public Func<double, string> Format = v => v.ToString("0.0");
}

/// <summary>League tables across the simulated countries: where you stand on each headline measure now, and a year ago.</summary>
public static class Rankings
{
    public static readonly RankMetric[] Metrics =
    {
        new() { Key = "score", Label = "Overall score", Tip = "The game's composite scorecard (prosperity, living standards, stability, sustainability, resilience).", Now = (w, c) => Scorer.Compute(w, c).Total, Format = v => v.ToString("0") },
        new() { Key = "gdppc", Label = "GDP per head (US$)", Tip = "Nominal GDP per person at market exchange rates.", FromHistory = h => h.GdpUsdBn * 1000 / Math.Max(1e-9, h.Pop), Now = (_, c) => c.GdpPerCapitaUsd, Format = v => "$" + v.ToString("N0") },
        new() { Key = "gdp", Label = "GDP (US$ bn)", Tip = "Size of the economy at market exchange rates.", FromHistory = h => h.GdpUsdBn, Now = (_, c) => c.GdpUsdBn, Format = v => "$" + v.ToString("N0") + "bn" },
        new() { Key = "growth", Label = "Real growth", Tip = "Real GDP growth over the last twelve months.", FromHistory = h => h.Growth, Now = (_, c) => c.GdpGrowth, Format = v => Fmt.P(v, 1) },
        new() { Key = "inflation", Label = "Inflation", Tip = "Ranked by distance from a 2% target: the best inflation is the one nearest it.", FromHistory = h => h.Inflation, Now = (_, c) => c.Inflation,
                HigherIsBetter = false, SortKey = (_, _, v) => Math.Abs(v - 0.02), Format = v => Fmt.P(v, 1) },
        new() { Key = "unemployment", Label = "Unemployment", Tip = "Lower is better.", HigherIsBetter = false, FromHistory = h => h.Unemployment, Now = (_, c) => c.Unemp, Format = v => Fmt.P(v, 1) },
        new() { Key = "debt", Label = "Public debt / GDP", Tip = "Lower is better.", HigherIsBetter = false, FromHistory = h => h.DebtToGdp, Now = (_, c) => c.DebtToGdp, Format = v => Fmt.P(v, 0) },
        new() { Key = "deficit", Label = "Budget deficit / GDP", Tip = "Lower is better.", HigherIsBetter = false, FromHistory = h => h.DeficitToGdp, Now = (_, c) => c.DeficitToGdp, Format = v => Fmt.P(v, 1) },
        new() { Key = "approval", Label = "Approval", Tip = "Share of voters who approve of the government.", FromHistory = h => h.Approval, Now = (_, c) => c.Approval, Format = v => Fmt.P(v, 0) },
        new() { Key = "gini", Label = "Inequality (Gini)", Tip = "Lower is better.", HigherIsBetter = false, FromHistory = h => h.Gini, Now = (_, c) => c.Gini, Format = v => v.ToString("0.00") },
        new() { Key = "stability", Label = "Stability", Tip = "Political and social stability index.", FromHistory = h => h.Stability, Now = (_, c) => c.Stability, Format = v => Fmt.P(v, 0) },
        new() { Key = "emissions", Label = "Emissions per head", Tip = "Tonnes of CO2-equivalent per person. Lower is better.", HigherIsBetter = false, FromHistory = h => h.EmissionsMt / Math.Max(1e-9, h.Pop), Now = (_, c) => c.EmissionsMt / Math.Max(1e-9, c.Pop), Format = v => v.ToString("0.0") + " t" },
    };

    public static RankMetric Metric(string key) => Metrics.FirstOrDefault(m => m.Key == key) ?? Metrics[0];

    /// <summary>The league table for a metric, best first. <paramref name="atMonth"/> reads history (null for now); metrics without a history reading return null for past months.</summary>
    public static List<RankRow>? Table(World w, string key, int? atMonth = null)
    {
        var m = Metric(key); var rows = new List<(RankRow row, double sort)>();
        foreach (var c in w.Countries)
        {
            double v; HistoryPoint? hp = null;
            if (atMonth == null || atMonth == w.Month) v = m.Now(w, c);
            else
            {
                if (m.FromHistory == null || !w.History.TryGetValue(c.Id, out var list)) return null;
                hp = At(list, atMonth.Value); if (hp == null) return null;
                v = m.FromHistory(hp);
            }
            double sort = m.SortKey != null ? m.SortKey(hp, c, v) : v;
            rows.Add((new RankRow { Id = c.Id, Name = c.Name, Value = v, Player = c.Id == w.PlayerId }, sort));
        }
        var ordered = (m.SortKey != null || !m.HigherIsBetter) ? rows.OrderBy(r => r.sort).ThenBy(r => r.row.Id, StringComparer.Ordinal) : rows.OrderByDescending(r => r.sort).ThenBy(r => r.row.Id, StringComparer.Ordinal);
        var res = ordered.Select(r => r.row).ToList();
        for (int i = 0; i < res.Count; i++) res[i].Rank = i + 1;
        return res;
    }

    public static int? RankOf(World w, string key, string id, int? atMonth = null) => Table(w, key, atMonth)?.FirstOrDefault(r => r.Id == id)?.Rank;

    /// <summary>Countries nearest to <paramref name="id"/> in the table, itself included, for "who am I being compared with".</summary>
    public static List<RankRow> Neighbourhood(List<RankRow> table, string id, int each = 2)
    {
        int i = table.FindIndex(r => r.Id == id); if (i < 0) return new();
        int lo = Math.Max(0, i - each), hi = Math.Min(table.Count - 1, i + each);
        return table.GetRange(lo, hi - lo + 1);
    }

    static HistoryPoint? At(List<HistoryPoint> list, int month)
    {
        int lo = 0, hi = list.Count - 1;
        while (lo <= hi) { int mid = (lo + hi) / 2; if (list[mid].Month == month) return list[mid]; if (list[mid].Month < month) lo = mid + 1; else hi = mid - 1; }
        return null;
    }
}
