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
    }

    [Fact]
    public void Without_history_the_review_is_simply_unavailable()
    {
        var sim = Simulation.New("GBR", 1, false); sim.World.RecordHistory = false; sim.World.Events = false; sim.Run(24);
        var w = sim.World; w.History[w.PlayerId].RemoveRange(1, w.History[w.PlayerId].Count - 1);
        Assert.Null(Journal.Review(w, 24));
    }
}
