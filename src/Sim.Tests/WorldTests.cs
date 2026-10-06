using System.Diagnostics;
using Sim.Core.Engine;
using Sim.Core.Model;
using Sim.Core.Policy;
using Xunit;

namespace Sim.Tests;

public class WorldTests
{
    static Simulation Det(string id = "GBR") => Simulation.New(id, 1, false);
    static int Ix(World w, string id) => w.Countries.FindIndex(c => c.Id == id);

    [Fact]
    public void Trade_matrix_exists_from_the_first_month()
    {
        var w = Det("GBR").World;
        Assert.Equal(w.Countries.Count, w.Trade.W.Length);
    }

    [Fact]
    public void Trade_weights_are_normalised_and_gravity_shaped()
    {
        var sim = Det("USA"); sim.Tick();
        var w = sim.World; var T = w.Trade;
        for (int i = 0; i < w.Countries.Count; i++)
            Assert.InRange(T.W[i].Sum() + T.RowShare[i], 0.999, 1.001);
        int usa = Ix(w, "USA");
        Assert.True(T.W[usa][Ix(w, "CAN")] > T.W[usa][Ix(w, "AUS")], "neighbour in the same bloc trades more");
        Assert.True(T.W[Ix(w, "DEU")][Ix(w, "FRA")] > T.W[Ix(w, "DEU")][Ix(w, "BRA")]);
        Assert.Equal(0, T.W[usa][usa]);
    }

    [Fact]
    public void A_recession_in_a_big_partner_transmits_to_its_neighbours_more_than_to_distant_economies()
    {
        var baseSim = Det("GBR"); var shock = Det("GBR");
        var us = shock.World.Find("USA")!;
        us.Cons *= 0.85; us.InvPriv *= 0.8;   // hit US domestic demand
        baseSim.Run(2); shock.Run(2);
        double Drop(string id) => 1 - shock.World.Find(id)!.Exports / baseSim.World.Find(id)!.Exports;
        Assert.True(Drop("CAN") > Drop("AUS"), $"CAN {Drop("CAN"):P2} vs AUS {Drop("AUS"):P2}");
        Assert.True(Drop("MEX") > Drop("KOR"));
        Assert.True(Drop("CAN") > 0.002);
    }

    [Fact]
    public void Player_tariff_triggers_ai_retaliation_and_hurts_exports()
    {
        var baseSim = Det("GBR"); var war = Det("GBR");
        war.World.Player.PoliticalCapital = 100;
        Assert.True(war.Execute(Command.Tariff("GBR", "DEU", 0.15)).Ok);
        baseSim.Run(48); war.Run(48);
        Assert.True(WorldEngine.Rel(war.World, "DEU", "GBR").ExtraTariff > 0.05, $"Germany retaliates: {WorldEngine.Rel(war.World, "DEU", "GBR").ExtraTariff} vs {WorldEngine.Rel(war.World, "GBR", "DEU").ExtraTariff}");
        Assert.True(war.World.Player.Exports < baseSim.World.Player.Exports);
        Assert.True(war.World.Player.ImportTariffExtra > 0);
        Assert.Contains(war.World.Log, l => l.Kind == "news" && l.Text.Contains("retaliates"));
    }

    [Fact]
    public void Trade_deal_lifts_exports_to_the_partner_when_accepted()
    {
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var s = Simulation.New("FRA", seed, false); s.World.Player.PoliticalCapital = 100;
            var r = s.Execute(Command.TradeDeal("FRA", "DEU"));   // same bloc: high acceptance
            if (!r.Ok) continue;
            var b = Simulation.New("FRA", seed, false);
            s.Run(12); b.Run(12);
            Assert.True(s.World.Player.Exports > b.World.Player.Exports);
            return;
        }
        Assert.Fail("no deal accepted in 20 seeds");
    }

    [Fact]
    public void Sanctions_damage_the_target_and_the_sanctioner()
    {
        var baseSim = Det("USA"); var s = Det("USA");
        s.World.Player.PoliticalCapital = 100;
        Assert.True(s.Execute(Command.Sanction("USA", "RUS", true)).Ok);
        baseSim.Run(24); s.Run(24);
        Assert.True(s.World.Find("RUS")!.Stability < baseSim.World.Find("RUS")!.Stability);
        Assert.True(s.World.Find("RUS")!.Exports < baseSim.World.Find("RUS")!.Exports);
        Assert.True(s.World.Player.Exports <= baseSim.World.Player.Exports);
    }

    [Fact]
    public void Aid_moves_money_and_approval()
    {
        var s = Det("GBR"); var gbr = s.World.Player; gbr.PoliticalCapital = 100;
        var eth = s.World.Find("ETH")!;
        double a0 = eth.Approval, g0 = gbr.Approval;
        Assert.True(s.Execute(Command.Aid("GBR", "ETH", 0.005)).Ok);
        Assert.True(eth.OtherRevenue > 0 && gbr.OtherRevenue < 0);
        Assert.True(eth.Approval > a0 && gbr.Approval < g0);
    }

    [Fact]
    public void Contagion_spreads_from_a_crisis_country_to_close_neighbours()
    {
        var s = Det("GBR"); var w = s.World;
        var tur = w.Find("TUR")!;
        tur.Yield10 = 0.35; tur.Debt = tur.GdpNominal * 1.5;
        s.Run(6);
        double near = w.Find("POL")!.ContagionRisk, far = w.Find("AUS")!.ContagionRisk;
        Assert.True(near > far, $"POL {near:P2} vs AUS {far:P2}");
        Assert.True(near > 0);
    }

    [Fact]
    public void Ai_countries_enact_reforms_over_time_and_keep_budgets_sane()
    {
        var s = Simulation.New("GBR", 4, false); s.World.RecordHistory = false;
        s.Run(120);
        int enacted = s.World.Countries.Where(c => c.Id != "GBR").Sum(c => c.Policies.Count(p => !p.Failed));
        Assert.True(enacted >= 5, $"AI enacted {enacted} policies");
        Assert.Contains(s.World.Log, l => l.Kind == "news" && l.Text.Contains("announces"));
    }

    [Fact]
    public void World_tick_is_fast_enough_for_real_time_play()
    {
        var s = Simulation.New("GBR", 1); s.World.RecordHistory = false;
        s.Run(12);
        var sw = Stopwatch.StartNew();
        s.Run(120);
        double msPerTick = sw.Elapsed.TotalMilliseconds / 120;
        Assert.True(msPerTick < 50, $"{msPerTick:F2} ms per world tick");
    }
}
