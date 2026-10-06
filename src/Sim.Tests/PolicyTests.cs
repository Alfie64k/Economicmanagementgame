using Sim.Core.Data;
using Sim.Core.Engine;
using Sim.Core.Model;
using Sim.Core.Policy;
using Xunit;

namespace Sim.Tests;

public class PolicyTests
{
    static Simulation Det(string id, bool stochastic = false) => Simulation.New(id, 1, stochastic);

    [Fact]
    public void Catalogue_is_well_formed()
    {
        Assert.True(PolicyCatalog.Policies.Count >= 30);
        Assert.Equal(PolicyCatalog.Policies.Count, PolicyCatalog.Policies.Select(p => p.Id).Distinct().Count());
        foreach (var p in PolicyCatalog.Policies)
        {
            Assert.True(p.Pc > 0 && p.Delay >= 0, p.Id);
            foreach (var l in p.Budget.Keys) Assert.True(Enum.TryParse<BudgetLine>(l, out _), $"{p.Id}: {l}");
            foreach (var s in p.Subsidy.Keys) Assert.True(Enum.TryParse<Sector>(s, out _), $"{p.Id}: {s}");
        }
        foreach (var pr in PolicyCatalog.Projects)
        {
            Assert.True(Enum.TryParse<Asset>(pr.Asset, out _), pr.Id);
            Assert.True(pr.Cost > 0 && pr.Months > 0);
        }
    }

    [Fact]
    public void Commands_cost_political_capital_and_fail_when_broke()
    {
        var sim = Det("GBR"); var c = sim.World.Player;
        double pc0 = c.PoliticalCapital;
        var r = sim.Execute(Command.SetTax("GBR", Tax.Income, c.TaxRate[(int)Tax.Income] * 1.1));
        Assert.True(r.Ok); Assert.True(r.PcCost > 0);
        Assert.Equal(pc0 - r.PcCost, c.PoliticalCapital, 6);
        c.PoliticalCapital = 1;
        var r2 = sim.Execute(Command.SetBudget("GBR", BudgetLine.Social, c.Budget[(int)BudgetLine.Social] + 0.05));
        Assert.False(r2.Ok);
        Assert.Equal(1, c.PoliticalCapital, 6);
    }

    [Fact]
    public void Dry_run_prices_without_applying()
    {
        var sim = Det("FRA"); var c = sim.World.Player;
        double before = c.TaxRate[(int)Tax.Corporate];
        var r = CommandProcessor.Apply(sim.World, Command.SetTax("FRA", Tax.Corporate, before * 1.5), dryRun: true);
        Assert.True(r.Ok && r.PcCost > 0);
        Assert.Equal(before, c.TaxRate[(int)Tax.Corporate]);
    }

    [Fact]
    public void Enacted_policy_takes_effect_after_its_delay_and_repeal_reverses_it()
    {
        var sim = Det("USA"); var c = sim.World.Player;
        c.PoliticalCapital = 100; c.Coalition = 1; c.Approval = 0.9;
        var r = sim.Execute(Command.Enact("USA", "labour_flex"));
        Assert.True(r.Ok, r.Message);
        var def = PolicyCatalog.Policy("labour_flex")!;
        sim.Run(def.Delay - 1);
        Assert.False(c.Policies.Single().Active);
        sim.Run(2);
        Assert.True(c.Policies.Single().Active);
        sim.Run(36);
        Assert.True(c.Mod("nairu") < -0.008);
        c.PoliticalCapital = 100;
        Assert.True(sim.Execute(Command.Repeal("USA", "labour_flex")).Ok);
        sim.Run(60);
        Assert.InRange(c.Mod("nairu"), -0.002, 0.002);
    }

    [Fact]
    public void Exclusive_groups_block_conflicting_policies()
    {
        var sim = Det("GBR"); var c = sim.World.Player; c.PoliticalCapital = 100; c.Coalition = 1; c.Approval = 0.9;
        Assert.True(sim.Execute(Command.Enact("GBR", "open_immigration")).Ok);
        var r = sim.Execute(Command.Enact("GBR", "restrict_immigration"));
        Assert.False(r.Ok);
        Assert.Contains("Conflicts", r.Message);
    }

    [Fact]
    public void Legislature_can_reject_a_bill_in_a_democracy_but_not_an_autocracy()
    {
        int failsDem = 0, failsAuto = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var s = Simulation.New("GBR", seed, false); var c = s.World.Player;
            c.PoliticalCapital = 100; c.Coalition = 0.2; c.Approval = 0.2;
            if (!s.Execute(Command.Enact("GBR", "pension_age")).Ok) failsDem++;
            var a = Simulation.New("CHN", seed, false); a.World.Player.PoliticalCapital = 100;
            if (!a.Execute(Command.Enact("CHN", "labour_flex")).Ok) failsAuto++;
        }
        Assert.True(failsDem > 5, $"democracy fails {failsDem}");
        Assert.True(failsAuto < 6, $"autocracy fails {failsAuto}");
    }

    [Fact]
    public void Projects_spend_money_deliver_asset_boost_and_need_maintenance()
    {
        var sim = Det("DEU"); var c = sim.World.Player; c.PoliticalCapital = 100;
        var r = sim.Execute(Command.StartProject("DEU", "broadband"));
        Assert.True(r.Ok, r.Message);
        sim.Run(6);
        Assert.True(c.ProjectFlow > 0);
        double idx0 = 1.0;
        sim.Run(120);
        var p = c.Projects.Single();
        Assert.True(p.Done);
        Assert.True(c.AssetBoost[(int)Asset.Digital] > 0.05);
        Assert.True(c.AssetIdx[(int)Asset.Digital] > idx0 + 0.05);
        Assert.True(c.MaintFlow > 0 && c.ProjectFlow == 0);
    }

    [Fact]
    public void Corruption_inflates_project_costs_on_average()
    {
        double clean = 0, dirty = 0;
        for (ulong s = 1; s <= 60; s++)
        {
            var a = Simulation.New("CHE", s, false); a.World.Player.PoliticalCapital = 100; a.Execute(Command.StartProject("CHE", "hsr"));
            var b = Simulation.New("NGA", s, false); b.World.Player.PoliticalCapital = 100; b.Execute(Command.StartProject("NGA", "hsr"));
            clean += a.World.Player.Projects[0].Overrun; dirty += b.World.Player.Projects[0].Overrun;
        }
        Assert.True(dirty > clean);
    }

    [Fact]
    public void Concurrent_project_limit_is_enforced()
    {
        var sim = Det("USA"); var c = sim.World.Player;
        foreach (var id in new[] { "hsr", "motorways", "ports", "metro", "grid", "wind_solar" }) { c.PoliticalCapital = 100; Assert.True(sim.Execute(Command.StartProject("USA", id)).Ok); }
        c.PoliticalCapital = 100;
        Assert.False(sim.Execute(Command.StartProject("USA", "broadband")).Ok);
    }

    [Fact]
    public void Carbon_price_lowers_emissions_and_adds_revenue()
    {
        var a = Det("DEU"); var b = Det("DEU"); var bc = b.World.Player;
        bc.PoliticalCapital = 100; bc.Coalition = 1; bc.Approval = 0.9;
        Assert.True(b.Execute(Command.Enact("DEU", "carbon_tax_100")).Ok);
        a.Run(120); b.Run(120);
        Assert.True(b.World.Player.EmissionsMt < a.World.Player.EmissionsMt);
        Assert.True(b.World.Player.Renewables > a.World.Player.Renewables);
    }

    [Fact]
    public void Command_queue_applies_at_the_next_tick_and_is_logged_for_replay()
    {
        var sim = Det("GBR"); var c = sim.World.Player;
        var cmd = Command.SetTax("GBR", Tax.Consumption, c.TaxRate[(int)Tax.Consumption] * 1.1);
        Assert.True(sim.Submit(cmd).Ok);
        Assert.Equal(c.TaxRate0[(int)Tax.Consumption], c.TaxRate[(int)Tax.Consumption]);
        sim.Tick();
        Assert.True(c.TaxRate[(int)Tax.Consumption] > c.TaxRate0[(int)Tax.Consumption]);
        Assert.Single(sim.World.CommandLog);

        // replay from seed + log reproduces the state
        var replay = Simulation.New("GBR", 1, false);
        replay.Submit(sim.World.CommandLog[0].Cmd); replay.Tick();
        Assert.Equal(sim.StateHash(), replay.StateHash());
    }

    [Fact]
    public void Advisors_flag_a_debt_problem_and_conflict_with_the_social_minister()
    {
        var sim = Det("USA"); var c = sim.World.Player;
        c.Debt *= 1.6; c.Unemp += 0.04; c.Approval = 0.25;
        sim.Tick();
        var notes = Advisors.Generate(sim.World, c);
        Assert.Contains(notes, n => n.Advisor == "Finance Minister" && n.Key == "debt");
        Assert.Contains(notes, n => n.Advisor == "Social Policy Minister" && n.Key == "unemp");
        Assert.Contains(notes, n => n.Suggestion != null);
    }

    [Fact]
    public void Preview_shows_the_effect_of_a_policy_and_fan_chart_bands_are_ordered()
    {
        var sim = Simulation.New("GBR", 5);
        var prev = Forecaster.Preview(sim, new[] { Command.SetBudget("GBR", BudgetLine.Infrastructure, sim.World.Player.Budget[(int)BudgetLine.Infrastructure] + 0.02) }, 24);
        var debt = prev.Single(p => p.Metric == "debt");
        Assert.True(debt.WithPolicy[^1] > debt.Baseline[^1]);

        var fan = Forecaster.FanChart(sim, 24, paths: 12);
        foreach (var f in fan)
            for (int t = 0; t < 24; t++) Assert.True(f.P10[t] <= f.P50[t] + 1e-12 && f.P50[t] <= f.P90[t] + 1e-12, f.Metric);
        var g = fan.Single(f => f.Metric == "growth");
        Assert.True(g.P90[23] > g.P10[23]);
    }
}
