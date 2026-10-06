using Sim.Core.Engine;
using Sim.Core.Events;
using Sim.Core.Model;
using Sim.Core.Policy;
using Xunit;

namespace Sim.Tests;

public class EventTests
{
    static Simulation Quiet(string id = "GBR", ulong seed = 1) { var s = Simulation.New(id, seed, false); s.World.Events = false; return s; }

    [Fact]
    public void Event_catalogue_is_consistent()
    {
        var ids = EventCatalog.Events.Select(e => e.Id).ToHashSet();
        Assert.True(EventCatalog.Events.Count >= 20);
        foreach (var e in EventCatalog.Events)
        {
            foreach (var n in e.Next) Assert.Contains(n.Id, ids);
            foreach (var cond in e.Cond)
            {
                int i = cond.IndexOfAny(new[] { '<', '>' });
                Assert.True(i > 0, $"{e.Id}: {cond}");
                EventEngine.Metric(Quiet().World.Player, new GlobalState(), cond[..i]); // throws on unknown metric
            }
            foreach (var pm in e.ProbMods) EventEngine.Metric(Quiet().World.Player, new GlobalState(), pm.Metric);
            if (e.Choices.Count > 0) Assert.InRange(e.DefaultChoice, 0, e.Choices.Count - 1);
        }
    }

    [Fact]
    public void Disaster_hits_capital_and_resilient_infrastructure_softens_it()
    {
        var weak = Quiet("USA"); var strong = Quiet("USA");
        strong.World.Player.AssetIdx[(int)Asset.Infrastructure] = 1.5;
        var def = EventCatalog.Find("earthquake_flood")!;
        double k0 = weak.World.Player.K.Sum();
        EventEngine.Trigger(weak.World, weak.World.Player, def);
        EventEngine.Trigger(strong.World, strong.World.Player, def);
        Assert.True(weak.World.Player.K.Sum() < k0);
        Assert.True(strong.World.Player.K.Sum() > weak.World.Player.K.Sum());
    }

    [Fact]
    public void Player_events_with_choices_create_decisions_that_apply_costs_and_default_on_timeout()
    {
        var sim = Quiet("GBR"); var w = sim.World; var c = w.Player;
        EventEngine.Trigger(w, c, EventCatalog.Find("earthquake_flood")!);
        var d = Assert.Single(w.Decisions);
        Assert.Equal(3, d.Labels.Count);
        double other0 = c.OtherRevenue;
        Assert.True(sim.Resolve(d.Id, 0));
        Assert.Empty(w.Decisions);
        Assert.True(c.OtherRevenue < other0, "relief package costs money");

        EventEngine.Trigger(w, c, EventCatalog.Find("strike_wave")!);
        Assert.Single(w.Decisions);
        w.Events = true;
        sim.Run(6);   // deadline passes
        Assert.Empty(w.Decisions);
        Assert.Contains(w.Log, l => l.Text.Contains("(default)"));
    }

    [Fact]
    public void Ai_countries_resolve_choices_automatically()
    {
        var sim = Quiet("GBR"); var w = sim.World;
        EventEngine.Trigger(w, w.Find("FRA")!, EventCatalog.Find("strike_wave")!);
        Assert.Empty(w.Decisions);
    }

    [Fact]
    public void Timed_modifiers_expire_and_are_removed()
    {
        var sim = Quiet("DEU"); var w = sim.World; var c = w.Player;
        EventEngine.ApplyEffects(w, c, new[] { new EffectDef { Kind = "mod", Key = "tfp", Value = 0.01, Months = 6 } }, "test");
        sim.Run(4); Assert.True(c.Mod("tfp") > 0);
        w.Events = true; sim.Run(60);
        Assert.InRange(c.Mod("tfp"), -1e-3, 1e-3);
        Assert.Empty(c.TimedMods);
    }

    [Fact]
    public void Global_events_move_world_variables_and_hit_every_country()
    {
        var sim = Quiet("GBR"); var w = sim.World;
        double cons0 = w.Countries.Sum(c => c.Cons), oil0 = w.Global.OilIdx;
        EventEngine.TriggerGlobal(w, EventCatalog.Find("pandemic")!);
        Assert.True(w.Countries.Sum(c => c.Cons) < cons0);
        Assert.True(w.Global.OilIdx != oil0);
        Assert.Contains(w.Scheduled, s => s.EventId == "post_pandemic_rebound");
    }

    [Fact]
    public void Imf_programme_brings_loan_conditionality_and_expires()
    {
        var sim = Quiet("TUR"); var w = sim.World; var c = w.Player;
        EventEngine.ApplyEffects(w, c, new[] { new EffectDef { Kind = "imf", Key = "loan", Value = 0.06, Months = 24 } }, "imf");
        Assert.True(c.OtherRevenue > 0 && c.Autopilot && c.ImfAutopilot);
        w.Events = true; sim.Run(30);
        Assert.False(c.Autopilot);
        Assert.Contains(w.Log, l => l.Text.Contains("IMF programme ends"));
    }

    [Fact]
    public void Default_cuts_debt_and_marks_the_country_for_two_years()
    {
        var sim = Quiet("ARG"); var w = sim.World; var c = w.Player;
        double d0 = c.Debt;
        EventEngine.ApplyEffects(w, c, new[] { new EffectDef { Kind = "default", Key = "haircut", Value = 0.4 } }, "default");
        Assert.Equal(d0 * 0.6, c.Debt, 6);
        Assert.True(c.InDefault);
        w.Events = true; sim.Run(26);
        Assert.False(c.InDefault);
    }

    [Fact]
    public void A_sustained_debt_crisis_forces_the_sovereign_crisis_event()
    {
        var sim = Simulation.New("GBR", 1, false); var w = sim.World; var c = w.Find("ITA") ?? w.Find("FRA")!;
        c.CrisisMonths = 7;
        sim.Tick();
        Assert.Contains(w.EventHistory, e => e.EventId == "sovereign_debt_crisis" && e.Country == c.Id);
    }

    [Fact]
    public void Elections_end_the_game_only_when_configured_and_ai_governments_change()
    {
        var sandbox = Simulation.New("GBR", 2, false);
        sandbox.World.Player.Approval = 0.05; sandbox.World.Player.NextElectionMonth = 3;
        sandbox.Run(6);
        Assert.False(sandbox.World.GameOver);

        var hard = Simulation.New("GBR", 2, false); hard.World.GameOverOnLoss = true;
        hard.World.Player.Approval = 0.02; hard.World.Player.NextElectionMonth = 3;
        hard.World.Player.Gov = "democracy";
        for (int i = 0; i < 6; i++) { hard.World.Player.Approval = 0.02; hard.Tick(); }
        Assert.True(hard.World.GameOver);
        Assert.Contains("polls", hard.World.GameOverReason);
        Assert.Contains(sandbox.World.Log.Concat(hard.World.Log), l => l.Text.Contains("government changes hands") || l.Text.Contains("wins the election") || l.Text.Contains("lose the election"));
    }

    [Fact]
    public void Unstable_autocracy_can_be_overthrown()
    {
        int overthrown = 0;
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var s = Simulation.New("EGY", seed, false); s.World.Events = true;
            var c = s.World.Player; c.Stability = 0.05; c.Unrest = 0.9;
            for (int m = 0; m < 24 && !s.World.GameOver; m++) { c.Stability = 0.05; c.Unrest = 0.9; s.Tick(); }
            if (s.World.GameOver) overthrown++;
        }
        Assert.True(overthrown > 3, $"overthrown {overthrown}/30");
    }

    [Fact]
    public void Event_stream_is_deterministic_for_a_seed()
    {
        string Run(ulong seed)
        {
            var s = Simulation.New("GBR", seed); s.World.RecordHistory = false;
            while (s.World.Month < 240) { s.Tick(); foreach (var d in s.World.Decisions.ToList()) s.Resolve(d.Id, d.DefaultChoice); }
            return s.StateHash() + ":" + s.World.EventHistory.Count;
        }
        Assert.Equal(Run(9), Run(9));
        Assert.NotEqual(Run(9), Run(10));
    }

    [Fact]
    public void Save_load_preserves_events_and_decisions()
    {
        var s = Simulation.New("GBR", 3); s.World.RecordHistory = false;
        EventEngine.Trigger(s.World, s.World.Player, EventCatalog.Find("cyberattack")!);
        s.Run(30);
        var l = Simulation.Load(s.Save());
        Assert.Equal(s.World.Decisions.Count, l.World.Decisions.Count);
        Assert.Equal(s.World.EventHistory.Count, l.World.EventHistory.Count);
        s.Run(24); l.Run(24);
        Assert.Equal(s.StateHash(), l.StateHash());
    }
}
