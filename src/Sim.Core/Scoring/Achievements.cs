using Sim.Core.Model;

namespace Sim.Core.Scoring;

public enum Tier { Bronze, Silver, Gold }

public sealed class AchievementDef
{
    public string Id = "", Name = "", Description = "";
    public Tier Tier;
    /// <summary>Only awarded when the game has ended (a final grade, say), so it cannot be earned by day-one luck.</summary>
    public bool AtEnd;
    public Func<World, bool> Earned = _ => false;
}

/// <summary>
/// Things worth being proud of, read from the history, command log and news log of a game (nothing extra is stored in the world). Awards are for runs on Normal or Hard;
/// sandbox and easy play are for learning and do not count. The evaluator is pure and deterministic; remembering what a player has earned is the UI's job.
/// </summary>
public static class Achievements
{
    /// <summary>Whether a game counts toward achievements.</summary>
    public static bool Eligible(World w) => w.Difficulty is Difficulty.Normal or Difficulty.Hard;

    static List<HistoryPoint> H(World w) => w.History.TryGetValue(w.PlayerId, out var h) ? h : new List<HistoryPoint>();
    static int Ok(World w, Func<Command, bool> f) => w.CommandLog.Count(l => l.Ok && l.Cmd.Country == w.PlayerId && f(l.Cmd));
    static int Wins(World w) => w.Log.Count(e => e.Country == w.PlayerId && e.Kind == "news" && e.Text.StartsWith("You win"));
    static int Losses(World w) => w.Log.Count(e => e.Country == w.PlayerId && e.Kind == "news" && e.Text.StartsWith("You lose"));

    /// <summary>Longest run of consecutive recorded months for which <paramref name="ok"/> holds.</summary>
    static int Streak(List<HistoryPoint> h, Func<HistoryPoint, bool> ok)
    {
        int best = 0, cur = 0;
        foreach (var p in h) { cur = ok(p) ? cur + 1 : 0; best = Math.Max(best, cur); }
        return best;
    }

    static double PerHead(HistoryPoint p) => p.Gdp / Math.Max(1e-9, p.Pop);

    static AchievementDef A(string id, string name, string description, Tier tier, Func<World, bool> earned, bool atEnd = false) =>
        new() { Id = id, Name = name, Description = description, Tier = tier, Earned = earned, AtEnd = atEnd };

    public static readonly IReadOnlyList<AchievementDef> All = new[]
    {
        A("first_budget", "First budget", "Change a tax, a department budget or the tax code and see it through a turn.", Tier.Bronze,
            w => Ok(w, c => c.Type is "tax" or "budget" or "fiscal") >= 1),
        A("reformer", "Reformer", "Get five policies through the legislature.", Tier.Bronze, w => Ok(w, c => c.Type == "enact") >= 5),
        A("builder", "Builder", "Complete three investment projects.", Tier.Bronze, w => w.Player.Projects.Count(p => p.Done) >= 3),
        A("re_elected", "Re-elected", "Win an election.", Tier.Bronze, w => Wins(w) >= 1),
        A("survivor", "Ten years in office", "Reach the tenth year of the game.", Tier.Bronze, w => w.Month >= 120),
        A("tax_architect", "Tax architect", "Make three changes to the detailed tax-and-benefit code.", Tier.Silver, w => Ok(w, c => c.Type == "fiscal") >= 3),
        A("steady_hand", "Steady hand", "Keep inflation within a point of target for five years in a row.", Tier.Silver,
            w => Streak(H(w), p => Math.Abs(p.Inflation - w.Player.InflTarget) <= 0.01) >= 60),
        A("balanced_books", "Balanced books", "Run a budget surplus for two years in a row.", Tier.Silver, w => Streak(H(w), p => p.DeficitToGdp <= 0) >= 24),
        A("debt_diet", "Debt diet", "Cut public debt by 15 points of GDP within five years.", Tier.Silver, w => DebtCut(H(w), 0.15, 60)),
        A("job_machine", "Job machine", "Bring unemployment down three points below where you started, and keep it there for a year.", Tier.Silver,
            w => { var h = H(w); return h.Count > 0 && Streak(h, p => p.Unemployment <= h[0].Unemployment - 0.03) >= 12; }),
        A("rising_tide", "Rising tide", "Lift real GDP per head by a quarter.", Tier.Silver, w => { var h = H(w); return h.Count > 1 && PerHead(h[^1]) >= PerHead(h[0]) * 1.25; }),
        A("climber", "Climber", "Move up five places in the GDP-per-head league table.", Tier.Silver,
            w => Rankings.RankOf(w, "gdppc", w.PlayerId, 0) is int a && Rankings.RankOf(w, "gdppc", w.PlayerId) is int b && a - b >= 5),
        A("podium", "Podium", "Finish in the top three for GDP per head, having started outside it.", Tier.Silver,
            w => Rankings.RankOf(w, "gdppc", w.PlayerId, 0) is int a && a > 3 && Rankings.RankOf(w, "gdppc", w.PlayerId) is int b && b <= 3),
        A("fair_shares", "Fair shares", "Cut inequality (Gini) by 0.03 while the economy grows.", Tier.Silver,
            w => { var h = H(w); return h.Count > 1 && h[0].Gini - h[^1].Gini >= 0.03 && h[^1].Gdp > h[0].Gdp; }),
        A("crisis_survivor", "Back from the brink", "Come through a deep recession (growth below −3%) and end with output above its pre-crisis peak.", Tier.Silver, w => Recovered(H(w))),
        A("long_game", "The long game", "Reach year thirty.", Tier.Silver, w => w.Month >= 360),
        A("soft_landing", "Soft landing", "Bring inflation from 6% or more back to within a point of target within thirty months, without unemployment rising more than two points.", Tier.Gold, w => SoftLanding(w)),
        A("green_turn", "Green turn", "Cut emissions by a quarter while real GDP grows by a tenth.", Tier.Gold,
            w => { var h = H(w); return h.Count > 1 && h[^1].EmissionsMt <= h[0].EmissionsMt * 0.75 && h[^1].Gdp >= h[0].Gdp * 1.10; }),
        A("hat_trick", "Hat-trick", "Win three elections without losing one.", Tier.Gold, w => Wins(w) >= 3 && Losses(w) == 0),
        A("top_marks", "Top marks", "Finish a run of at least five years with grade A or better.", Tier.Gold,
            w => w.Month >= 60 && Scorer.Compute(w, w.Player).Grade is "A" or "S", atEnd: true),
    };

    static bool DebtCut(List<HistoryPoint> h, double cut, int within)
    {
        for (int i = 0; i < h.Count; i++)
            for (int j = i + 1; j < h.Count && h[j].Month - h[i].Month <= within; j++)
                if (h[i].DebtToGdp - h[j].DebtToGdp >= cut) return true;
        return false;
    }

    static bool Recovered(List<HistoryPoint> h)
    {
        double peak = double.MinValue;
        for (int i = 0; i < h.Count; i++)
        {
            peak = Math.Max(peak, h[i].Gdp);
            if (h[i].Growth <= -0.03 && h[^1].Gdp >= peak && h[^1].Month - h[i].Month >= 12) return true;
        }
        return false;
    }

    static bool SoftLanding(World w)
    {
        var h = H(w); double target = w.Player.InflTarget;
        for (int i = 0; i < h.Count; i++)
        {
            if (h[i].Inflation < 0.06 || (i > 0 && h[i - 1].Inflation >= 0.06)) continue;     // measure from the first month of each inflation episode
            double maxU = h[i].Unemployment;
            for (int j = i + 1; j < h.Count && h[j].Month - h[i].Month <= 30; j++)
            {
                maxU = Math.Max(maxU, h[j].Unemployment);
                if (h[j].Inflation <= target + 0.01) { if (maxU - h[i].Unemployment <= 0.02) return true; break; }
            }
        }
        return false;
    }

    /// <summary>Ids of the achievements earned by the game so far. Those marked <see cref="AchievementDef.AtEnd"/> count only when <paramref name="ended"/> is true. Empty for ineligible games.</summary>
    public static List<string> Check(World w, bool ended = false)
    {
        var res = new List<string>();
        if (!Eligible(w) || w.History.Count == 0) return res;
        foreach (var a in All)
        {
            if (a.AtEnd && !ended) continue;
            bool got; try { got = a.Earned(w); } catch { got = false; }
            if (got) res.Add(a.Id);
        }
        return res;
    }

    public static AchievementDef? Find(string id) => All.FirstOrDefault(a => a.Id == id);
}
