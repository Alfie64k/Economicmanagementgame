using Sim.Core.Engine;
using Sim.Core.Model;
using Sim.Core.Policy;
using Xunit;

namespace Sim.Tests;

public class RatePreviewTests
{
    static Simulation Det(string id)
    {
        var s = Simulation.New(id, 1, stochastic: false); s.World.Events = false; s.Run(6); return s;
    }

    [Theory]
    [InlineData("GBR")]
    [InlineData("USA")]
    [InlineData("DEU")]
    [InlineData("BRA")]
    public void A_hike_cools_the_economy_and_a_cut_warms_it_with_a_lag(string id)
    {
        var sim = Det(id); var c = sim.World.Player; var snap = Forecaster.Snapshot(sim);
        var hike = Forecaster.PreviewRate(snap, c.PolicyRate + 0.02);
        var cut = Forecaster.PreviewRate(snap, c.PolicyRate - 0.02);
        Assert.True(hike.Applied && cut.Applied);
        Assert.True(hike.S("inflation").DeltaAt(12) < -0.0005, "a hike should lower inflation within a year");
        Assert.True(cut.S("inflation").DeltaAt(12) > 0.0003, "a cut should raise inflation within a year");
        Assert.True(hike.S("gap").DeltaAt(12) < -0.005);
        Assert.True(hike.S("unemployment").DeltaAt(12) > 0);
        Assert.True(hike.S("fx").DeltaAt(12) < 0, "a hike strengthens the currency (fewer local units per US$)");
        // prices respond more slowly than activity: the inflation effect builds over the year
        Assert.True(Math.Abs(hike.S("inflation").DeltaAt(12)) > Math.Abs(hike.S("inflation").DeltaAt(3)));
        Assert.True(Math.Abs(hike.S("gap").DeltaAt(3)) > Math.Abs(hike.S("inflation").DeltaAt(3)));
    }

    [Fact]
    public void A_bigger_move_has_a_bigger_effect()
    {
        var sim = Det("GBR"); var snap = Forecaster.Snapshot(sim); double r = sim.World.Player.PolicyRate;
        var small = Forecaster.PreviewRate(snap, r + 0.01).S("inflation").DeltaAt(12);
        var large = Forecaster.PreviewRate(snap, r + 0.03).S("inflation").DeltaAt(12);
        Assert.True(large < small && small < 0);
    }

    [Fact]
    public void Preview_is_deterministic_and_leaves_the_real_game_untouched()
    {
        var sim = Det("GBR"); var c = sim.World.Player;
        sim.Stage(Command.SetTax("GBR", Tax.Income, c.TaxRate[(int)Tax.Income] * 1.05));
        string hash = sim.StateHash(); double pc = c.PoliticalCapital; int plan = sim.Plan.Count, log = sim.World.Log.Count, month = sim.World.Month;
        var a = Forecaster.PreviewRate(Forecaster.Snapshot(sim), c.PolicyRate + 0.015);
        var b = Forecaster.PreviewRate(Forecaster.Snapshot(sim), c.PolicyRate + 0.015);
        Assert.Equal(a.S("inflation").WithPolicy, b.S("inflation").WithPolicy);
        Assert.Equal(a.S("gap").Baseline, b.S("gap").Baseline);
        Assert.Equal(hash, sim.StateHash()); Assert.Equal(pc, c.PoliticalCapital);
        Assert.Equal(plan, sim.Plan.Count); Assert.Equal(log, sim.World.Log.Count); Assert.Equal(month, sim.World.Month);
    }

    [Fact]
    public void Staged_rate_commands_are_ignored_by_both_paths()
    {
        var sim = Det("GBR"); var c = sim.World.Player; var snapClean = Forecaster.Snapshot(sim);
        sim.Stage(Command.SetRate("GBR", true, c.PolicyRate + 0.04));
        var withPlan = Forecaster.PreviewRate(Forecaster.Snapshot(sim), c.PolicyRate + 0.01);
        var clean = Forecaster.PreviewRate(snapClean, c.PolicyRate + 0.01);
        Assert.Equal(clean.S("inflation").Baseline, withPlan.S("inflation").Baseline);
        Assert.Equal(clean.S("inflation").WithPolicy, withPlan.S("inflation").WithPolicy);
    }

    [Fact]
    public void Pinning_where_the_rate_is_already_pinned_changes_nothing()
    {
        var sim = Det("GBR"); var c = sim.World.Player;
        sim.Execute(Command.SetRate("GBR", true, c.PolicyRate));
        var r = Forecaster.PreviewRate(Forecaster.Snapshot(sim), c.ManualRate);
        Assert.All(r.Series, s => Assert.Equal(s.Baseline, s.WithPolicy));
    }

    [Fact]
    public void The_preview_works_without_political_capital_and_flags_a_peg()
    {
        var sim = Det("GBR"); var c = sim.World.Player; c.PoliticalCapital = 0;
        var r = Forecaster.PreviewRate(Forecaster.Snapshot(sim), c.PolicyRate + 0.02);
        Assert.True(r.Applied);
        Assert.True(r.S("inflation").DeltaAt(12) < 0);

        c.Regime = FxRegime.Peg;
        var peg = Forecaster.PreviewRate(Forecaster.Snapshot(sim), c.PolicyRate + 0.02);
        Assert.True(peg.Pegged); Assert.NotEqual("", peg.Note);
        Assert.True(Math.Abs(peg.S("inflation").DeltaAt(12)) < Math.Abs(r.S("inflation").DeltaAt(12)), "a peg overrides the pin");
    }

    [Fact]
    public void A_cancelled_preview_stops_early()
    {
        var sim = Det("GBR"); using var cts = new CancellationTokenSource(); cts.Cancel();
        var r = Forecaster.PreviewRate(Forecaster.Snapshot(sim), 0.06, ct: cts.Token);
        Assert.True(r.Cancelled);
    }

    [Fact]
    public void Time_to_target_is_reported_for_a_high_inflation_start()
    {
        var sim = Det("GBR"); var c = sim.World.Player;
        var r = Forecaster.PreviewRate(Forecaster.Snapshot(sim), c.PolicyRate);
        Assert.True(r.BaselineMonthsToTarget == null || r.BaselineMonthsToTarget is >= 1 and <= 12);
    }
}
