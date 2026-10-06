using Sim.Core.Engine;
using Sim.Core.Model;
using Sim.Core.Policy;
using Xunit;

namespace Sim.Tests;

public class StagedPlanTests
{
    static Simulation Det(string id = "GBR") { var s = Simulation.New(id, 1, stochastic: false); s.World.Events = false; return s; }
    static Command Bump(Simulation s, Tax t, double factor) => Command.SetTax(s.World.PlayerId, t, s.World.Player.TaxRate[(int)t] * factor);

    [Fact]
    public void Staging_changes_nothing_visible_until_the_turn_is_played()
    {
        var sim = Det(); var c = sim.World.Player; sim.Run(3);
        double pc = c.PoliticalCapital, rate = c.TaxRate[(int)Tax.Income]; int log = sim.World.Log.Count, cmdLog = sim.World.CommandLog.Count;
        var r = sim.Stage(Bump(sim, Tax.Income, 1.05));
        Assert.True(r.Ok && r.Staged && r.PcCost > 0);
        Assert.Equal(pc, c.PoliticalCapital);
        Assert.Equal(rate, c.TaxRate[(int)Tax.Income]);
        Assert.Equal(log, sim.World.Log.Count);
        Assert.Equal(cmdLog, sim.World.CommandLog.Count);
        Assert.Equal(r.PcCost, sim.PendingPcCost(), 6);

        sim.Tick();
        Assert.True(c.TaxRate[(int)Tax.Income] > rate);
        Assert.Empty(sim.Plan);
        Assert.Single(sim.World.CommandLog);
        Assert.Contains(sim.World.Log, l => l.Kind == "policy");
    }

    [Fact]
    public void Staging_the_same_setting_again_replaces_it_and_returning_to_the_live_value_removes_it()
    {
        var sim = Det(); var live = sim.World.Player.TaxRate[(int)Tax.Income];
        sim.Stage(Command.SetTax("GBR", Tax.Income, live + 0.01));
        var second = sim.Stage(Command.SetTax("GBR", Tax.Income, live + 0.02));
        Assert.True(second.Replaced);
        Assert.Single(sim.Plan);
        Assert.Equal(live + 0.02, sim.Plan[0].Value, 9);

        var back = sim.Stage(Command.SetTax("GBR", Tax.Income, live));
        Assert.True(back.NoOp && back.Unstaged);
        Assert.Empty(sim.Plan);
    }

    [Fact]
    public void Different_instruments_stack_and_unstage_by_key()
    {
        var sim = Det();
        sim.Stage(Bump(sim, Tax.Income, 1.02)); sim.Stage(Bump(sim, Tax.Corporate, 1.02));
        sim.Stage(Command.SetBudget("GBR", BudgetLine.Health, sim.World.Player.Budget[(int)BudgetLine.Health] + 0.002));
        Assert.Equal(3, sim.Plan.Count);
        Assert.True(sim.Unstage("tax:Corporate"));
        Assert.False(sim.Unstage("tax:Corporate"));
        Assert.Equal(2, sim.Plan.Count);
        sim.ClearPlan(); Assert.Empty(sim.Plan);
    }

    [Fact]
    public void The_whole_plan_must_be_affordable()
    {
        var sim = Det(); var c = sim.World.Player; c.PoliticalCapital = 20;
        var a = sim.Stage(Bump(sim, Tax.Income, 1.10));
        Assert.True(a.Ok && a.Staged);
        var b = sim.Stage(Bump(sim, Tax.Consumption, 1.10));
        var items = sim.PlanItems();
        Assert.True(sim.PendingPcCost() <= c.PoliticalCapital + 1e-9);
        if (!b.Ok) { Assert.Contains("political capital", b.Message); Assert.Single(items); }
        // replacing an entry re-prices only the difference, so tightening the same item always works
        Assert.True(sim.Stage(Bump(sim, Tax.Income, 1.02)).Ok);
    }

    [Fact]
    public void Plan_items_flag_what_the_capital_does_not_stretch_to()
    {
        var sim = Det(); var c = sim.World.Player; c.PoliticalCapital = 100;
        sim.Stage(Bump(sim, Tax.Income, 1.1)); sim.Stage(Bump(sim, Tax.Consumption, 1.1));
        c.PoliticalCapital = sim.PendingPcCost() - 1;                // the capital shrinks after staging (an event, say)
        var items = sim.PlanItems();
        Assert.True(items[0].Affordable); Assert.False(items[1].Affordable);
    }

    [Fact]
    public void Proposing_then_repealing_a_policy_in_the_same_turn_cancels_out()
    {
        var sim = Det(); var c = sim.World.Player; c.PoliticalCapital = 100;
        Assert.True(sim.Stage(Command.Enact("GBR", "rnd_tax_credits")).Staged);
        var r = sim.Stage(Command.Repeal("GBR", "rnd_tax_credits"));
        Assert.True(r.Ok && r.Unstaged); Assert.Empty(sim.Plan);
    }

    [Fact]
    public void Projects_are_cancelled_by_identity_not_by_position()
    {
        var sim = Det(); var c = sim.World.Player; c.PoliticalCapital = 100;
        Assert.True(sim.Execute(Command.StartProject("GBR", "broadband")).Ok);
        Assert.True(sim.Execute(Command.StartProject("GBR", "hsr")).Ok);
        c.PoliticalCapital = 100;
        Assert.True(sim.Stage(Command.CancelProject("GBR", "broadband")).Staged);
        sim.Tick();
        Assert.DoesNotContain(c.Projects, p => p.Id == "broadband");
        Assert.Contains(c.Projects, p => p.Id == "hsr");
    }

    [Fact]
    public void Previews_and_clones_never_carry_the_plan()
    {
        var sim = Det();
        sim.Stage(Bump(sim, Tax.Income, 1.1));
        var clone = Forecaster.Clone(sim);
        Assert.Empty(clone.World.Queue);
        Assert.Single(sim.Plan);                                     // the live plan is restored afterwards
        var prev = Forecaster.PreviewPlan(Forecaster.Snapshot(sim), sim.Plan.ToList(), 24);
        Assert.Equal(1, prev.Applied); Assert.Empty(prev.Skipped);
        Assert.NotEqual(prev.Series.First(s => s.Metric == "deficit").Baseline[23], prev.Series.First(s => s.Metric == "deficit").WithPolicy[23]);
    }

    [Fact]
    public void Preview_reports_commands_it_could_not_apply()
    {
        var sim = Det(); sim.World.Player.PoliticalCapital = 3;
        var prev = Forecaster.PreviewPlan(Forecaster.Snapshot(sim), new[] { Bump(sim, Tax.Income, 1.2) }, 12);
        Assert.Equal(0, prev.Applied); Assert.Single(prev.Skipped);
    }

    [Fact]
    public void Merged_layers_a_draft_over_the_plan_by_key()
    {
        var sim = Det(); var live = sim.World.Player.TaxRate[(int)Tax.Income];
        sim.Stage(Command.SetTax("GBR", Tax.Income, live + 0.01));
        var merged = sim.Merged(new[] { Command.SetTax("GBR", Tax.Income, live + 0.03), Command.SetTax("GBR", Tax.Corporate, 0.2) });
        Assert.Equal(2, merged.Count);
        Assert.Equal(live + 0.03, merged.First(m => m.Id == "Income").Value, 9);
        Assert.Single(sim.Plan);
    }

    [Fact]
    public void A_plan_survives_save_and_load_and_replays_deterministically()
    {
        var a = Det(); a.Run(5);
        a.Stage(Bump(a, Tax.Consumption, 1.08)); a.Stage(Command.SetBudget("GBR", BudgetLine.Education, a.World.Player.Budget[(int)BudgetLine.Education] + 0.003));
        var b = Simulation.Load(a.Save());
        Assert.Equal(2, b.Plan.Count);
        a.Tick(); b.Tick();
        Assert.Equal(a.StateHash(), b.StateHash());

        var c = Det(); c.Run(5);                                      // the same actions staged in a fresh run give the same state
        c.Stage(Bump(c, Tax.Consumption, 1.08)); c.Stage(Command.SetBudget("GBR", BudgetLine.Education, c.World.Player.Budget[(int)BudgetLine.Education] + 0.003));
        c.Tick();
        Assert.Equal(a.StateHash(), c.StateHash());
    }

    [Fact]
    public void Staging_the_current_setting_is_a_no_op_for_every_instrument()
    {
        var sim = Det(); var c = sim.World.Player;
        Assert.True(sim.Stage(Command.SetRate("GBR", false, 0)).NoOp);                         // already on the rule
        Assert.True(sim.Stage(Command.SetMinWage("GBR", c.MinWageRatio)).NoOp);
        Assert.True(sim.Stage(Command.SetFxRegime("GBR", c.Regime)).NoOp);
        Assert.True(sim.Stage(Command.SetCarbon("GBR", c.CarbonPrice)).NoOp);
        Assert.True(sim.Stage(Command.SetSubsidy("GBR", Sector.Energy, c.SectorSubsidy[(int)Sector.Energy])).NoOp);
        Assert.Empty(sim.Plan);
    }

    [Fact]
    public void Only_the_player_can_stage()
    {
        var sim = Det();
        Assert.False(sim.Stage(Command.SetTax("DEU", Tax.Income, 0.3)).Ok);
    }
}
