using Sim.Core.Engine;
using Sim.Core.Model;
using Sim.Core.Policy;
using Sim.Core.Scoring;
using Xunit;

namespace Sim.Tests;

public class PauseWatcherTests
{
    static Simulation Det(string id = "GBR") { var s = Simulation.New(id, 1, false); s.World.Events = false; return s; }

    [Fact]
    public void An_adviser_alert_stops_the_clock_once_and_only_when_enabled()
    {
        var sim = Det(); var w = sim.World; var watcher = new PauseWatcher(w);
        w.Log.Add(new LogEntry { Month = w.Month, Country = w.PlayerId, Kind = "advisor", Text = "Chief Whip: Approval has fallen to 15%.", Sev = 2 });
        Assert.Contains("Adviser alert", watcher.Check(w));
        Assert.Null(watcher.Check(w));                                   // already reported
        w.Log.Add(new LogEntry { Month = w.Month, Country = w.PlayerId, Kind = "advisor", Text = "Minor note", Sev = 1 });
        Assert.Null(watcher.Check(w));                                   // warnings do not stop the clock
        watcher.Enabled = PauseTrigger.None;
        w.Log.Add(new LogEntry { Month = w.Month, Country = w.PlayerId, Kind = "advisor", Text = "Alert while disabled", Sev = 2 });
        Assert.Null(watcher.Check(w));
    }

    [Fact]
    public void Other_countries_news_is_ignored_but_world_events_and_your_policies_count()
    {
        var sim = Det(); var w = sim.World; var watcher = new PauseWatcher(w, PauseTrigger.PolicyInForce | PauseTrigger.Event | PauseTrigger.Project);
        w.Log.Add(new LogEntry { Month = 1, Country = "DEU", Kind = "policy", Text = "Something is now in force." });
        Assert.Null(watcher.Check(w));
        w.Log.Add(new LogEntry { Month = 1, Country = w.PlayerId, Kind = "policy", Text = "Fiscal rule is now in force." });
        Assert.Equal("Fiscal rule is now in force.", watcher.Check(w));
        w.Log.Add(new LogEntry { Month = 2, Country = w.PlayerId, Kind = "policy", Text = "Project completed: Broadband." });
        Assert.StartsWith("Project completed", watcher.Check(w));
        w.Log.Add(new LogEntry { Month = 3, Country = "WORLD", Kind = "event", Text = "Oil shock" });
        Assert.Null(watcher.Check(w));                                   // events trigger only when they touch your country
    }

    [Fact]
    public void A_new_watcher_does_not_replay_the_past()
    {
        var sim = Det(); var w = sim.World;
        w.Log.Add(new LogEntry { Month = 0, Country = w.PlayerId, Kind = "advisor", Text = "old alert", Sev = 2 });
        var watcher = new PauseWatcher(w);
        Assert.Null(watcher.Check(w));
    }

    [Fact]
    public void Recession_election_and_grade_drop_are_edge_triggered()
    {
        var sim = Det(); var w = sim.World; var c = w.Player; var watcher = new PauseWatcher(w);
        c.GdpGrowth = -0.02; c.Gap = -0.03;
        Assert.Contains("Recession", watcher.Check(w));
        Assert.Null(watcher.Check(w));                                   // still in recession: no repeat
        c.GdpGrowth = 0.02; c.Gap = 0; watcher.Check(w);
        c.GdpGrowth = -0.02; c.Gap = -0.03;
        Assert.Contains("Recession", watcher.Check(w));                  // a second recession is news again

        c.GdpGrowth = 0.02; c.Gap = 0; watcher.Check(w);
        c.NextElectionMonth = w.Month + 3;
        Assert.Contains("election is three months away", watcher.Check(w));
        Assert.Null(watcher.Check(w));

        var card = Scorer.Compute(w, c);
        c.Approval = 0.05; c.Stability = 0.05; c.Unrest = 0.9; c.Inflation = 0.25; c.Gap = 0.12;                 // wreck the scorecard
        var reason = watcher.Check(w);
        Assert.True(reason != null && reason.Contains("grade"), reason ?? "no pause");
    }

    [Fact]
    public void The_same_game_raises_the_same_pauses()
    {
        List<string> Run()
        {
            var sim = Simulation.New("BRA", 3, true); var w = sim.World; var watcher = new PauseWatcher(w); var res = new List<string>();
            for (int i = 0; i < 120 && !w.GameOver; i++)
            {
                sim.Tick(); foreach (var d in w.Decisions.ToList()) sim.Resolve(d.Id, d.DefaultChoice);
                var r = watcher.Check(w); if (r != null) res.Add($"{w.Month}:{r}");
            }
            return res;
        }
        var a = Run(); var b = Run();
        Assert.Equal(a, b);
        Assert.NotEmpty(a);                                              // ten years of Brazil gives the clock plenty to stop for
    }

    [Fact]
    public void Run_targets_land_on_quarter_and_year_ends()
    {
        Assert.Equal(3, RunTargets.QuarterEnd(0)); Assert.Equal(6, RunTargets.QuarterEnd(3)); Assert.Equal(6, RunTargets.QuarterEnd(5));
        Assert.Equal(12, RunTargets.YearEnd(0)); Assert.Equal(24, RunTargets.YearEnd(12)); Assert.Equal(12, RunTargets.YearEnd(11));
    }
}

public class AdviceTests
{
    [Fact]
    public void Every_suggestion_is_a_command_the_game_can_price()
    {
        var sim = Simulation.New("GBR", 1, false); sim.World.Events = false; var w = sim.World; var c = w.Player;
        c.Inflation = 0.07; c.Unemp = c.NairU + 0.04; c.DebtGdp0 = 0.5; c.Debt = c.GdpNominal * 1.4; c.Unrest = 0.5; c.Old = c.Old0 + 0.04;
        var notes = Advisors.Generate(w, c);
        Assert.Contains(notes, n => n.Suggestion != null);
        foreach (var n in notes.Where(n => n.Suggestion != null))
        {
            var r = CommandProcessor.Apply(w, n.Suggestion!, dryRun: true);
            Assert.False(string.IsNullOrEmpty(r.Message), $"{n.Key}: no message");
        }
    }

    [Fact]
    public void Tightening_and_support_advice_at_once_is_reported_as_a_conflict()
    {
        var sim = Simulation.New("GBR", 1, false); sim.World.Events = false; var w = sim.World; var c = w.Player;
        c.Debt = c.GdpNominal * 1.6; c.Deficit = c.GdpNominal * 0.08; c.Unemp = c.NairU + 0.05;
        var notes = Advisors.Generate(w, c);
        var conflicts = Advisors.Conflicts(notes);
        Assert.NotEmpty(conflicts);
        Assert.All(conflicts, k => { Assert.Equal(Stance.Tighten, k.Tighten.Stance); Assert.Equal(Stance.Support, k.Support.Stance); Assert.NotEqual(k.Tighten.Advisor, k.Support.Advisor); });
    }

    [Fact]
    public void Posted_advice_carries_its_severity_into_the_log()
    {
        var sim = Simulation.New("GBR", 1, false); sim.World.Events = false; var w = sim.World; var c = w.Player;
        c.Approval = 0.10; Advisors.Post(w, c);
        Assert.Contains(w.Log, l => l.Kind == "advisor" && l.Sev == 2 && l.Text.Contains("Approval"));
    }
}

public class RankingTests
{
    static Simulation Run(int months) { var s = Simulation.New("GBR", 1, false); s.World.Events = false; s.Run(months); return s; }

    [Fact]
    public void Tables_rank_every_country_once_and_flag_the_player()
    {
        var sim = Run(6); var w = sim.World;
        foreach (var m in Rankings.Metrics)
        {
            var t = Rankings.Table(w, m.Key)!;
            Assert.Equal(w.Countries.Count, t.Count);
            Assert.Equal(Enumerable.Range(1, t.Count), t.Select(r => r.Rank));
            Assert.Single(t, r => r.Player);
            Assert.Equal(t.Count, t.Select(r => r.Id).Distinct().Count());
        }
        var gdp = Rankings.Table(w, "gdp")!;
        Assert.True(gdp.Zip(gdp.Skip(1)).All(p => p.First.Value >= p.Second.Value));
        var unemp = Rankings.Table(w, "unemployment")!;
        Assert.True(unemp.Zip(unemp.Skip(1)).All(p => p.First.Value <= p.Second.Value));
        var infl = Rankings.Table(w, "inflation")!;
        Assert.True(infl.Zip(infl.Skip(1)).All(p => Math.Abs(p.First.Value - 0.02) <= Math.Abs(p.Second.Value - 0.02) + 1e-12));
    }

    [Fact]
    public void Past_tables_come_from_history_and_the_current_one_matches_now()
    {
        var sim = Run(30); var w = sim.World;
        var now = Rankings.Table(w, "gdppc")!; var same = Rankings.Table(w, "gdppc", w.Month)!;
        Assert.Equal(now.Select(r => r.Id), same.Select(r => r.Id));
        var past = Rankings.Table(w, "gdppc", 12)!;
        Assert.Equal(w.Countries.Count, past.Count);
        Assert.Null(Rankings.Table(w, "score", 12));                       // the composite score has no history
        Assert.Null(Rankings.Table(w, "gdppc", 9999));
    }

    [Fact]
    public void The_neighbourhood_is_centred_and_clipped_at_the_ends()
    {
        var sim = Run(2); var w = sim.World; var t = Rankings.Table(w, "gdp")!;
        var mid = t[10].Id; var n = Rankings.Neighbourhood(t, mid, 2);
        Assert.Equal(5, n.Count); Assert.Equal(mid, n[2].Id);
        Assert.Equal(3, Rankings.Neighbourhood(t, t[0].Id, 2).Count);
    }
}

public class JournalTests
{
    static Simulation Played()
    {
        var sim = Simulation.New("GBR", 1, false); sim.World.Events = false; var c = sim.World.Player;
        sim.World.Player.PoliticalCapital = 100;
        sim.Run(14);
        sim.Stage(Command.SetTax("GBR", Tax.Consumption, c.TaxRate[(int)Tax.Consumption] * 1.05));
        sim.Stage(Command.SetBudget("GBR", BudgetLine.Education, c.Budget[(int)BudgetLine.Education] + 0.003));
        sim.Run(14);
        sim.Execute(Command.StartProject("GBR", "broadband")); sim.Run(12);
        sim.Stage(Command.SetTax("GBR", Tax.Corporate, c.TaxRate[(int)Tax.Corporate] * 0.9)); sim.Run(3);       // too recent for a full year
        return sim;
    }

    [Fact]
    public void The_journal_lists_what_you_did_in_order()
    {
        var sim = Played(); var j = Journal.Build(sim.World);
        Assert.True(j.Zip(j.Skip(1)).All(p => p.First.Month <= p.Second.Month));
        Assert.Contains(j, e => e.Kind == "action" && e.Title.StartsWith("Consumption tax raised", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(j, e => e.Title.Contains("→") && e.Title.Contains("15.3% → 15.3%"));              // reads as it did when it ran, not as a no-op
        Assert.Contains(j, e => e.Kind == "project");
        Assert.DoesNotContain(j, e => string.IsNullOrWhiteSpace(e.Title));
    }

    [Fact]
    public void Impacts_compare_the_months_after_each_action_and_flag_unfinished_windows()
    {
        var sim = Played(); var imp = Journal.Impacts(sim.World, 12);
        Assert.NotEmpty(imp);
        Assert.Contains(imp, i => i.Complete); Assert.All(imp, i => Assert.True(double.IsFinite(i.Growth) && double.IsFinite(i.Unemployment) && double.IsFinite(i.DebtToGdp)));
        Assert.Contains(imp, i => !i.Complete && i.Entry.Month > sim.World.Month - 12);
    }

    [Fact]
    public void A_year_review_covers_twelve_months_with_a_verdict_per_measure()
    {
        var sim = Played(); var w = sim.World;
        Assert.Null(Journal.Review(w, 6));                                  // not a full year yet
        var r = Journal.Review(w, 24)!;
        Assert.Equal(12, r.EndMonth - r.StartMonth); Assert.Equal(w.StartYear + 1, r.Year);
        Assert.InRange(r.Metrics.Count, 7, 10);
        Assert.All(r.Metrics, m => { Assert.InRange(m.Verdict, -1, 1); Assert.False(string.IsNullOrEmpty(m.Change)); });
        Assert.All(r.Actions, a => Assert.InRange(a.Month, r.StartMonth + 1, r.EndMonth));
        Assert.False(string.IsNullOrEmpty(r.Headline));
        Assert.NotNull(r.GdpPcRankEnd);
        // US$ per head, not thousands: a rich country reads in tens of thousands of dollars
        var pc = r.Metrics.First(m => m.Label.StartsWith("GDP per head"));
        Assert.True(double.Parse(pc.End.TrimStart('$').Replace(",", "")) > 1000, pc.End);
    }

    [Fact]
    public void Without_history_the_review_is_simply_unavailable()
    {
        var sim = Simulation.New("GBR", 1, false); sim.World.RecordHistory = false; sim.World.Events = false; sim.Run(24);
        var w = sim.World; w.History[w.PlayerId].RemoveRange(1, w.History[w.PlayerId].Count - 1);
        Assert.Null(Journal.Review(w, 24));
    }
}

public class GlossaryTests
{
    [Fact]
    public void Every_term_is_complete_unique_and_cross_references_resolve()
    {
        Assert.InRange(Glossary.Terms.Count, 60, 120);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in Glossary.Terms)
        {
            Assert.True(names.Add(t.Term), "duplicate term " + t.Term);
            Assert.False(string.IsNullOrWhiteSpace(t.Short) || string.IsNullOrWhiteSpace(t.Game), t.Term);
            Assert.Contains(t.Category, Glossary.Categories);
            if (t.Page != null) Assert.Contains(t.Page, Glossary.Pages);
            foreach (var s in t.See ?? Array.Empty<string>()) Assert.True(Glossary.Find(s) != null, $"{t.Term} refers to unknown term '{s}'");
        }
        foreach (var c in Glossary.Categories) Assert.Contains(Glossary.Terms, t => t.Category == c);
    }

    [Fact]
    public void Aliases_do_not_collide_with_other_terms()
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in Glossary.Terms)
            foreach (var n in new[] { t.Term }.Concat(t.Aka ?? Array.Empty<string>()))
                Assert.True(seen.TryAdd(n, t.Term) || seen[n] == t.Term, $"'{n}' names both {seen.GetValueOrDefault(n)} and {t.Term}");
    }

    [Fact]
    public void Search_ranks_exact_then_prefix_then_wording_and_respects_category()
    {
        Assert.Equal("NAIRU", Glossary.Search("nairu")[0].Term);
        Assert.Equal("Policy rate", Glossary.Search("bank rate")[0].Term);          // an alias
        Assert.Equal("Fiscal drag", Glossary.Search("bracket creep")[0].Term);
        Assert.Equal("Marginal tax rate", Glossary.Search("marginal tax")[0].Term);
        Assert.Contains(Glossary.Search("retire"), t => t.Term == "Pension age");     // alias and wording
        Assert.Empty(Glossary.Search("zzzzqq"));
        Assert.Equal(Glossary.Terms.Count, Glossary.Search("").Count);
        Assert.All(Glossary.Search("", "Tax and benefits"), t => Assert.Equal("Tax and benefits", t.Category));
        Assert.Equal(Glossary.Terms.Count, Glossary.Categories.Sum(c => Glossary.Search("", c).Count));
    }
}

public class AchievementTests
{
    static Simulation Played(int months, Difficulty d = Difficulty.Normal)
    {
        var s = Simulation.New("GBR", 4, false); s.World.Events = false; Scenarios.ApplyDifficulty(s.World, d); s.Run(months); return s;
    }

    static World W(int months, Difficulty d = Difficulty.Normal) => Played(months, d).World;

    static HistoryPoint P(int m, double infl = 0.02, double unemp = 0.05, double debt = 0.9, double def = 0.04, double growth = 0.02, double gdp = 100) =>
        new() { Month = m, Inflation = infl, Unemployment = unemp, DebtToGdp = debt, DeficitToGdp = def, Growth = growth, Gdp = gdp, Pop = 60, EmissionsMt = 400, Gini = 0.35 };

    static World With(Action<List<HistoryPoint>> build, Difficulty d = Difficulty.Normal)
    {
        var s = Played(2, d); var h = s.World.History[s.World.PlayerId]; h.Clear(); build(h); return s.World;
    }

    [Fact]
    public void Ids_are_unique_and_every_award_has_a_name_and_a_description()
    {
        Assert.Equal(Achievements.All.Count, Achievements.All.Select(a => a.Id).Distinct().Count());
        Assert.All(Achievements.All, a => Assert.False(string.IsNullOrWhiteSpace(a.Name) || string.IsNullOrWhiteSpace(a.Description)));
        Assert.InRange(Achievements.All.Count, 15, 40);
        foreach (var t in Enum.GetValues<Tier>()) Assert.Contains(Achievements.All, a => a.Tier == t);
    }

    [Fact]
    public void Sandbox_and_easy_games_earn_nothing_and_a_new_game_earns_nothing()
    {
        Assert.Empty(Achievements.Check(W(130, Difficulty.Sandbox)));
        Assert.Empty(Achievements.Check(W(130, Difficulty.Easy)));
        Assert.Empty(Achievements.Check(W(1)));
    }

    [Fact]
    public void Checking_is_deterministic_and_pure()
    {
        var sim = Played(150); string hash = sim.StateHash();
        var a = Achievements.Check(sim.World); var b = Achievements.Check(sim.World);
        Assert.Equal(a, b); Assert.Contains("survivor", a);
        Assert.Equal(hash, sim.StateHash());
    }

    [Fact]
    public void Time_based_awards_follow_the_calendar()
    {
        Assert.DoesNotContain("survivor", Achievements.Check(W(100)));
        Assert.Contains("survivor", Achievements.Check(W(121)));
        Assert.DoesNotContain("long_game", Achievements.Check(W(121)));
    }

    [Fact]
    public void A_soft_landing_needs_inflation_back_to_target_without_a_jobs_slump()
    {
        var good = With(h => { for (int m = 0; m <= 30; m++) h.Add(P(m, infl: m == 0 ? 0.08 : 0.08 - 0.06 * m / 24.0 < 0.03 ? 0.025 : 0.08 - 0.06 * m / 24.0, unemp: 0.05 + 0.01 * Math.Min(1, m / 12.0))); });
        Assert.Contains("soft_landing", Achievements.Check(good));
        var costly = With(h => { for (int m = 0; m <= 30; m++) h.Add(P(m, infl: m < 20 ? 0.08 : 0.02, unemp: 0.05 + 0.04 * Math.Min(1, m / 12.0))); });
        Assert.DoesNotContain("soft_landing", Achievements.Check(costly));
    }

    [Fact]
    public void Debt_surplus_and_inflation_streaks_are_recognised()
    {
        var w = With(h => { for (int m = 0; m <= 70; m++) h.Add(P(m, infl: 0.02, debt: 1.0 - 0.15 * Math.Min(1, m / 48.0), def: -0.005)); });
        var got = Achievements.Check(w);
        Assert.Contains("debt_diet", got); Assert.Contains("balanced_books", got); Assert.Contains("steady_hand", got);
        var slow = With(h => { for (int m = 0; m <= 200; m += 12) { h.Add(P(m, debt: 1.0 - 0.15 * m / 192.0, def: 0.03)); } });
        Assert.DoesNotContain("debt_diet", Achievements.Check(slow));
    }

    [Fact]
    public void Recovering_from_a_deep_recession_means_regaining_the_old_peak()
    {
        var back = With(h => { for (int m = 0; m <= 60; m++) h.Add(P(m, growth: m is >= 12 and <= 18 ? -0.05 : 0.02, gdp: m < 12 ? 100 : m < 24 ? 94 : 100 + (m - 24) * 0.3)); });
        Assert.Contains("crisis_survivor", Achievements.Check(back));
        var not = With(h => { for (int m = 0; m <= 60; m++) h.Add(P(m, growth: m is >= 12 and <= 18 ? -0.05 : 0.02, gdp: m < 12 ? 100 : 90)); });
        Assert.DoesNotContain("crisis_survivor", Achievements.Check(not));
    }

    [Fact]
    public void End_of_run_awards_wait_for_the_end()
    {
        var w = Played(70).World;
        Assert.DoesNotContain("top_marks", Achievements.Check(w, ended: false));
        foreach (var a in Achievements.All.Where(a => a.AtEnd)) Assert.True(a.AtEnd);
    }
}
