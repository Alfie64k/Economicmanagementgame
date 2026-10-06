using Sim.Core.Model;
using Sim.Core.Policy;
using Sim.Core.Util;

namespace Sim.Core.Scoring;

public sealed class JournalEntry
{
    public int Month;
    public string Kind = "";      // action | policy | project | election | crisis | event | milestone
    public string Title = "";
    public string Detail = "";
    public override string ToString() => $"{Month}: {Kind}: {Title}";
}

/// <summary>What moved in the twelve months after something you did. Describes, does not prove cause: other things happened too.</summary>
public sealed class Impact
{
    public JournalEntry Entry = new();
    public int Months;
    public double Growth, Inflation, Unemployment, DebtToGdp, Approval;   // change from the month of the action to Months later
    public bool Complete;                                                   // the full window has elapsed
}

public sealed class YearMetric
{
    public string Label = "", Start = "", End = "", Change = "";
    public double Delta;
    public bool HigherIsBetter = true;
    public int Verdict;           // +1 better, -1 worse, 0 flat
}

public sealed class YearReview
{
    public int Year, StartMonth, EndMonth;
    public List<YearMetric> Metrics = new();
    public List<JournalEntry> Actions = new(), Happenings = new();
    public string Headline = "";
    public int? GdpRankStart, GdpRankEnd, GdpPcRankStart, GdpPcRankEnd;
}

/// <summary>A readable record of the run built from the command log, the news log and history: no extra state to save.</summary>
public static class Journal
{
    public static List<JournalEntry> Build(World w)
    {
        var res = new List<JournalEntry>(); var c = w.Player;
        foreach (var l in w.CommandLog)
        {
            if (l.Cmd.Country != w.PlayerId || !l.Ok) continue;
            if (l.Cmd.Type == "autopilot") continue;
            // the message was written when the command ran, so it reads "from → to" correctly; describing it now would compare the new setting with itself
            string text = !string.IsNullOrWhiteSpace(l.Message) ? l.Message : PlanText.Describe(w, l.Cmd);
            res.Add(new JournalEntry { Month = l.Month, Kind = l.Cmd.Type is "enact" or "repeal" ? "policy" : l.Cmd.Type == "project" ? "project" : "action", Title = text });
        }
        foreach (var e in w.Log)
        {
            if (e.Country != w.PlayerId && e.Country != "WORLD") continue;
            switch (e.Kind)
            {
                case "policy" when e.Text.EndsWith("is now in force."): res.Add(new JournalEntry { Month = e.Month, Kind = "policy", Title = e.Text }); break;
                case "policy" when e.Text.StartsWith("Project completed"): res.Add(new JournalEntry { Month = e.Month, Kind = "project", Title = e.Text }); break;
                case "news" when e.Country == w.PlayerId && (e.Text.StartsWith("You win") || e.Text.StartsWith("You lose")): res.Add(new JournalEntry { Month = e.Month, Kind = "election", Title = e.Text }); break;
                case "crisis": res.Add(new JournalEntry { Month = e.Month, Kind = "crisis", Title = e.Text }); break;
                case "event": res.Add(new JournalEntry { Month = e.Month, Kind = "event", Title = e.Text }); break;
            }
        }
        foreach (var d in w.EventHistory.Where(d => d.Country == w.PlayerId))
            res.Add(new JournalEntry { Month = d.Month, Kind = "event", Title = $"Decision: {d.Name}", Detail = d.Choice });
        // milestones: first month growth turned negative, debt records, grade changes are not stored; keep to what history shows
        if (w.History.TryGetValue(w.PlayerId, out var h) && h.Count > 12)
        {
            double peak = h[0].DebtToGdp; int peakMonth = 0;
            foreach (var p in h) if (p.DebtToGdp > peak + 0.02) { peak = p.DebtToGdp; peakMonth = p.Month; }
            if (peakMonth > 0 && peak > h[0].DebtToGdp + 0.05) res.Add(new JournalEntry { Month = peakMonth, Kind = "milestone", Title = $"Public debt peaked at {Fmt.P(peak, 0)} of GDP" });
        }
        return res.OrderBy(e => e.Month).ThenBy(e => e.Kind, StringComparer.Ordinal).ThenBy(e => e.Title, StringComparer.Ordinal).ToList();
    }

    static HistoryPoint? At(World w, int month)
    {
        if (!w.History.TryGetValue(w.PlayerId, out var h) || h.Count == 0) return null;
        var p = h.LastOrDefault(x => x.Month <= month);
        return p != null && month - p.Month <= 1 ? p : null;       // a gap in the record means no reading, not a stale one
    }

    /// <summary>What happened after each thing you did, a year (and two) later. Only actions that change a setting are listed.</summary>
    public static List<Impact> Impacts(World w, int months = 12, IReadOnlyList<JournalEntry>? entries = null)
    {
        var res = new List<Impact>();
        foreach (var e in (entries ?? Build(w)).Where(e => e.Kind is "action" or "policy" or "project"))
        {
            var a = At(w, e.Month); var b = At(w, Math.Min(w.Month, e.Month + months));
            if (a == null || b == null) continue;
            res.Add(new Impact
            {
                Entry = e, Months = months, Complete = e.Month + months <= w.Month,
                Growth = b.Growth - a.Growth, Inflation = b.Inflation - a.Inflation, Unemployment = b.Unemployment - a.Unemployment,
                DebtToGdp = b.DebtToGdp - a.DebtToGdp, Approval = b.Approval - a.Approval,
            });
        }
        return res;
    }

    /// <summary>The twelve months ending at <paramref name="endMonth"/> (normally the end of a calendar year).</summary>
    public static YearReview? Review(World w, int endMonth)
    {
        int start = endMonth - 12; if (start < 0) return null;
        var a = At(w, start); var b = At(w, endMonth); if (a == null || b == null) return null;
        var hist = w.History[w.PlayerId].Where(p => p.Month > start && p.Month <= endMonth).ToList();
        var r = new YearReview { Year = w.StartYear + (endMonth - 1) / 12, StartMonth = start, EndMonth = endMonth };
        void M(string label, string s, string e, double delta, bool higherBetter, string change, double flat)
        {
            int v = Math.Abs(delta) < flat ? 0 : (delta > 0) == higherBetter ? 1 : -1;
            r.Metrics.Add(new YearMetric { Label = label, Start = s, End = e, Delta = delta, HigherIsBetter = higherBetter, Change = change, Verdict = v });
        }
        double growth = b.Gdp / Math.Max(1e-9, a.Gdp) - 1, avgInfl = hist.Count > 0 ? hist.Average(p => p.Inflation) : b.Inflation;
        double pcA = a.GdpUsdBn * 1000 / Math.Max(1e-9, a.Pop), pcB = b.GdpUsdBn * 1000 / Math.Max(1e-9, b.Pop);   // US$ billions over millions of people
        M("Real GDP growth", Fmt.P(a.Growth, 1), Fmt.P(growth, 1), growth - a.Growth, true, $"{growth * 100:+0.0;-0.0}% over the year", 0.003);
        M("Inflation (average)", Fmt.P(a.Inflation, 1), Fmt.P(avgInfl, 1), -(Math.Abs(avgInfl - 0.02) - Math.Abs(a.Inflation - 0.02)), true, $"{avgInfl * 100:0.0}% against a 2% anchor", 0.002);
        M("Unemployment", Fmt.P(a.Unemployment, 1), Fmt.P(b.Unemployment, 1), b.Unemployment - a.Unemployment, false, $"{(b.Unemployment - a.Unemployment) * 100:+0.0;-0.0}pp", 0.002);
        M("Public debt / GDP", Fmt.P(a.DebtToGdp, 0), Fmt.P(b.DebtToGdp, 0), b.DebtToGdp - a.DebtToGdp, false, $"{(b.DebtToGdp - a.DebtToGdp) * 100:+0.0;-0.0}pp", 0.005);
        M("Budget deficit / GDP", Fmt.P(a.DeficitToGdp, 1), Fmt.P(b.DeficitToGdp, 1), b.DeficitToGdp - a.DeficitToGdp, false, $"{(b.DeficitToGdp - a.DeficitToGdp) * 100:+0.0;-0.0}pp", 0.003);
        M("Approval", Fmt.P(a.Approval, 0), Fmt.P(b.Approval, 0), b.Approval - a.Approval, true, $"{(b.Approval - a.Approval) * 100:+0;-0}pts", 0.01);
        M("Inequality (Gini)", a.Gini.ToString("0.000"), b.Gini.ToString("0.000"), b.Gini - a.Gini, false, $"{(b.Gini - a.Gini) * 100:+0.0;-0.0} pts", 0.002);
        M("GDP per head (US$)", "$" + pcA.ToString("N0"), "$" + pcB.ToString("N0"), pcB / Math.Max(1e-9, pcA) - 1, true, $"{(pcB / Math.Max(1e-9, pcA) - 1) * 100:+0.0;-0.0}%", 0.005);
        var entries = Build(w).Where(e => e.Month > start && e.Month <= endMonth).ToList();
        r.Actions = entries.Where(e => e.Kind is "action" or "policy" or "project").ToList();
        r.Happenings = entries.Where(e => e.Kind is "election" or "crisis" or "event" or "milestone").ToList();
        r.GdpPcRankStart = Rankings.RankOf(w, "gdppc", w.PlayerId, start); r.GdpPcRankEnd = Rankings.RankOf(w, "gdppc", w.PlayerId, endMonth);
        r.GdpRankStart = Rankings.RankOf(w, "gdp", w.PlayerId, start); r.GdpRankEnd = Rankings.RankOf(w, "gdp", w.PlayerId, endMonth);
        int good = r.Metrics.Count(m => m.Verdict > 0), bad = r.Metrics.Count(m => m.Verdict < 0);
        r.Headline = good >= bad + 3 ? "A strong year." : good > bad ? "A year of progress." : bad >= good + 3 ? "A hard year." : bad > good ? "A mixed year, tilting the wrong way." : "A steady year.";
        return r;
    }
}
