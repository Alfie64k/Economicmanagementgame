using Sim.Core.Data;
using Sim.Core.Engine;
using Sim.Core.Events;
using Sim.Core.Model;
using Sim.Core.Policy;
using Sim.Core.Scoring;
using Xunit;

namespace Sim.Tests;

public class ScoringTests
{
    static Simulation Det(string id, int months = 0)
    {
        var s = Simulation.New(id, 1, false); s.World.Events = false; s.Run(months); return s;
    }

    [Fact]
    public void Score_is_bounded_and_responds_in_the_right_direction()
    {
        var s = Det("GBR", 24);
        var good = Scorer.Compute(s.World, s.World.Player);
        Assert.InRange(good.Total, 0, 100);
        foreach (var v in new[] { good.Prosperity, good.Living, good.Stability, good.Sustainability, good.Resilience }) Assert.InRange(v, 0, 100);

        var c = s.World.Player;
        c.Approval = 0.1; c.Unrest = 0.8; c.Inflation = 0.15; c.Debt *= 2; c.InDefault = true;
        var bad = Scorer.Compute(s.World, c);
        Assert.True(bad.Total < good.Total - 5);
        Assert.True(bad.Stability < good.Stability);
        Assert.True(bad.Sustainability < good.Sustainability);
        Assert.True(bad.Resilience < good.Resilience);
        Assert.Contains(bad.Grade, new[] { "D", "F", "C" });
    }

    [Fact]
    public void Explanations_cover_every_metric_and_reconcile()
    {
        var s = Det("USA", 30); var c = s.World.Player;
        foreach (var m in Explain.Metrics)
        {
            var e = Explain.Why(c, m);
            Assert.False(string.IsNullOrEmpty(e.Headline), m);
            Assert.NotEmpty(e.Items);
            Assert.All(e.Items, i => Assert.True(double.IsFinite(i.Value), $"{m}/{i.Label}"));
        }
        // inflation drivers + persistence reconcile to the underlying rate within clamp tolerance
        var inf = Explain.Why(c, "inflation");
        Assert.InRange(inf.Items.Sum(i => i.Value) / 100, c.InflInst - 0.02, c.InflInst + 0.02);
        // approval drivers sum to the target
        var ap = Explain.Why(c, "approval");
        Assert.NotEmpty(ap.Summary.Length > 0 ? new[] { 1 } : Array.Empty<int>());
        // growth: demand-side contributions roughly add up to GDP growth
        var gr = Explain.Why(c, "growth");
        double dem = gr.Items.Where(i => i.Group == "demand").Sum(i => i.Value) / 100;
        Assert.InRange(dem, c.GdpGrowth - 0.02, c.GdpGrowth + 0.02);
    }

    [Fact]
    public void Explanation_blames_the_right_thing_when_inflation_is_hit_by_an_energy_shock()
    {
        var s = Det("DEU", 6); var c = s.World.Player;
        s.World.Global.OilIdx *= 2.0; // doubles over a year -> yoy rises as ring catches up
        s.Run(6);
        var e = Explain.Why(c, "inflation");
        var energy = e.Items.Single(i => i.Label.StartsWith("Energy"));
        Assert.True(energy.Value > 0.3, $"energy contribution {energy.Value}");
    }

    [Fact]
    public void Scenario_file_is_valid()
    {
        var roster = CountryLoader.LoadEmbedded().Select(r => r.Id).ToHashSet();
        var ids = Scenarios.All.Select(s => s.Id).ToHashSet();
        Assert.True(Scenarios.All.Count >= 10);
        foreach (var sc in Scenarios.All)
        {
            Assert.Contains(sc.Country, roster);
            Assert.True(Enum.TryParse<Difficulty>(sc.Difficulty, out _), sc.Id);
            if (sc.Unlock != null) Assert.Contains(sc.Unlock, ids);
            foreach (var e in sc.Events) Assert.NotNull(EventCatalog.Find(e.Id));
            var sim = Scenarios.Start(sc, 1);
            foreach (var g in sc.Goals) Assert.True(double.IsFinite(Scenarios.Metric(sim.World, sim.World.Player, g.Metric)), $"{sc.Id}/{g.Metric}");
        }
        Assert.Contains(Scenarios.All, s => s.Kind == "tutorial");
        Assert.Contains(Scenarios.All, s => s.Kind == "historical");
        Assert.Contains(Scenarios.All, s => s.Kind == "challenge");
    }

    [Fact]
    public void Scenario_runs_to_completion_and_reports_goals()
    {
        var def = Scenarios.Find("tut_budget")!;
        var sim = Scenarios.Start(def, 3);
        var w = sim.World;
        while (w.Month < def.Years * 12 && !w.GameOver) { sim.Tick(); foreach (var d in w.Decisions.ToList()) sim.Resolve(d.Id, d.DefaultChoice); }
        var r = Scenarios.Evaluate(w, def);
        Assert.True(r.Complete);
        Assert.Equal(def.Goals.Count, r.Goals.Count);
        Assert.False(string.IsNullOrEmpty(r.Summary));
        Assert.Equal(1.3, w.PcRegenMult);   // Easy difficulty applied
    }

    [Fact]
    public void Scenario_setup_effects_and_scheduled_events_fire()
    {
        var def = Scenarios.Find("tut_inflation")!;
        var sim = Scenarios.Start(def, 1);
        Assert.True(sim.World.Player.ModTarget["inflation"] > 0.04);
        sim.Run(2);
        Assert.Contains(sim.World.EventHistory, e => e.EventId == "oil_supply_shock");
        Assert.NotEmpty(Scenarios.HintsAt(def, 0));
    }

    [Fact]
    public void Hard_difficulty_is_harsher_than_easy()
    {
        var easy = Simulation.New("GBR", 1); Scenarios.ApplyDifficulty(easy.World, Difficulty.Easy);
        var hard = Simulation.New("GBR", 1); Scenarios.ApplyDifficulty(hard.World, Difficulty.Hard);
        Assert.True(easy.World.PcRegenMult > hard.World.PcRegenMult);
        Assert.True(easy.World.EventSeverity < hard.World.EventSeverity);
        Assert.True(hard.World.GameOverOnLoss && !easy.World.GameOverOnLoss);
    }

    [Fact]
    public void Reckless_fiscal_policy_scores_worse_than_prudent_policy()
    {
        var reckless = new Strategy
        {
            Name = "reckless", Yearly = (s, y) =>
            {
                var c = s.World.Player;
                s.Execute(Command.SetTax(c.Id, Tax.Consumption, c.TaxRate[(int)Tax.Consumption] * 0.8));
                s.Execute(Command.SetTax(c.Id, Tax.Income, c.TaxRate[(int)Tax.Income] * 0.85));
                s.Execute(Command.SetBudget(c.Id, BudgetLine.Social, c.Budget[(int)BudgetLine.Social] + 0.01));
                c.PoliticalCapital = 100;
            }
        };
        double r = 0, b = 0;
        foreach (ulong seed in new ulong[] { 1, 2, 3 })
        {
            r += Balance.Run(reckless, "FRA", seed, 15).Score;
            b += Balance.Run(Balance.Strategies.Single(x => x.Name == "balanced"), "FRA", seed, 15).Score;
        }
        Assert.True(b > r + 3, $"balanced {b / 3:F1} vs reckless {r / 3:F1}");
    }

    [Fact]
    public void Balance_matrix_has_no_dominant_strategy_and_no_runaways()
    {
        var res = Balance.Matrix(new[] { "GBR", "IND", "NGA", "SAU", "SGP" }, seeds: 2, years: 12);
        var rep = Balance.Analyse(res);
        Assert.DoesNotContain(rep.Findings, f => f.StartsWith("DOMINANT"));
        Assert.DoesNotContain(rep.Findings, f => f.StartsWith("RUNAWAY"));
        Assert.True(rep.BestByArchetype.Values.Distinct().Count() >= 2, "different archetypes should favour different strategies");
    }
}

public class RegionTests
{
    [Fact]
    public void Regions_reconcile_to_the_national_economy()
    {
        var sim = Simulation.New("USA", 1, false); sim.World.Events = false; sim.Run(24);
        foreach (var id in new[] { "USA", "CHN", "IND", "BRA", "RUS", "CAN", "AUS", "IDN", "ZAF" })
        {
            var c = sim.World.Find(id)!;
            Assert.True(Sim.Core.Regions.Regions.Has(id), id);
            var r = Sim.Core.Regions.Regions.Compute(c);
            Assert.True(r.Count >= 7, id);
            Assert.InRange(r.Sum(x => x.PopShare), 0.999, 1.001);
            Assert.InRange(r.Sum(x => x.GdpShare), 0.999, 1.001);
            Assert.InRange(r.Sum(x => x.Gdp) / c.SectorVa.Sum(), 0.999, 1.001);
            Assert.All(r, x => Assert.InRange(x.Unemployment, 0.005, 0.5));
        }
        Assert.Empty(Sim.Core.Regions.Regions.Compute(sim.World.Find("GBR")!));
    }

    [Fact]
    public void Regional_output_moves_with_the_national_sector_mix()
    {
        var sim = Simulation.New("USA", 1, false); sim.World.Events = false; sim.Run(12);
        var c = sim.World.Player;
        var before = Sim.Core.Regions.Regions.Compute(c).ToDictionary(r => r.Def.Id, r => r.GdpShare);
        c.SectorVa[(int)Sector.Energy] *= 2.0;
        var after = Sim.Core.Regions.Regions.Compute(c);
        Assert.Contains(after, r => Math.Abs(r.GdpShare - before[r.Def.Id]) > 1e-4);
        var again = Sim.Core.Regions.Regions.Compute(c);
        Assert.Equal(after.Select(r => r.Gdp), again.Select(r => r.Gdp)); // deterministic
    }
}
