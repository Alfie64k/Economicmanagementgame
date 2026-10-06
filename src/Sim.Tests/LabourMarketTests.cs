using System.Text.Json.Nodes;
using Sim.Core.Data;
using Sim.Core.Engine;
using Sim.Core.Model;
using Sim.Core.Policy;
using Xunit;

namespace Sim.Tests;

/// <summary>Wage bargaining, the wage-price spiral, hysteresis, strike risk and the labour share.</summary>
public class LabourMarketTests
{
    [Fact]
    public void Shipped_file_is_sound_for_every_country()
    {
        Assert.True(LabourCatalog.Available);
        var roster = DepthFixture.RosterWithArchetypes();
        Assert.Equal(28, roster.Count);
        Assert.Empty(LabourCatalog.Validate(DepthFixture.Shipped("labour.json"), roster));
    }

    [Fact]
    public void A_roster_country_missing_from_the_file_is_reported_and_falls_back_to_its_archetype()
    {
        var doc = DepthFixture.Shipped("labour.json");
        ((JsonObject)doc["countries"]!).Remove("FRA");
        Assert.Contains(LabourCatalog.Validate(doc, DepthFixture.RosterWithArchetypes()), p => p.StartsWith("FRA"));
        var p = LabourCatalog.From(doc, "FRA", "advanced");
        Assert.Equal(0.40, p.Coverage, 9);
    }

    [Fact]
    public void Starting_bargaining_power_follows_the_known_ordering()
    {
        double B(string id) { var p = LabourCatalog.For(id, "x"); return p.Coverage * p.Strength; }
        Assert.True(B("NOR") > B("DEU") && B("DEU") > B("GBR") && B("GBR") > B("USA"));
        Assert.True(LabourCatalog.For("FRA", "x").Coverage > 0.9 && LabourCatalog.For("USA", "x").Coverage < 0.2);
        Assert.True(B("CHN") < B("DEU") / 3 && B("SAU") < 0.01);         // state-run or banned unions bargain little whatever their coverage
        Assert.All(DepthFixture.Roster, id => Assert.InRange(LabourCatalog.For(id, "x").LabourShare, 0.30, 0.70));
    }

    [Fact]
    public void Every_country_starts_at_its_catalogue_values_with_the_dynamic_state_at_rest()
    {
        var w = Simulation.New("GBR", 1, false).World;
        foreach (var c in w.Countries)
        {
            var p = LabourCatalog.For(c.Id, c.Archetype);
            Assert.Equal(p.Coverage, c.UnionCoverage); Assert.Equal(p.Coverage, c.UnionCoverage0); Assert.Equal(p.Strength, c.UnionStrength);
            Assert.Equal(p.LabourShare, c.LabourIncomeShare); Assert.Equal(p.LabourShare, c.LabourIncomeShare0);
            Assert.Equal(0.0, c.WageGap); Assert.Equal(0.0, c.WagePremium); Assert.Equal(0.0, c.WageSpiral); Assert.Equal(0.0, c.StrikeRisk);
            Assert.Equal(0.0, c.LtuStock); Assert.Equal(0.0, c.NairuHyst);
            Assert.Equal(c.UnionCoverage * c.UnionStrength, c.BargainingPower);
        }
    }

    [Fact]
    public void Version_2_saves_load_with_the_new_state_at_its_starting_values_and_without_a_jump()
    {
        var a = DepthFixture.Quiet("GBR", 3); a.Run(30);
        var v2 = DepthFixture.AsVersion2(a.Save());
        Assert.DoesNotContain("\"Shadow\"", v2);
        Assert.DoesNotContain("EconomicDepth", v2);
        var b = Simulation.Load(v2);
        Assert.Equal(World.CurrentVersion, b.World.Version);
        Assert.Equal(3, b.World.Version);
        Assert.True(b.World.EconomicDepth);
        foreach (var c in b.World.Countries)
        {
            Assert.Equal(ShadowCatalog.Share(c.Id, c.Archetype), c.Shadow0); Assert.Equal(c.Shadow0, c.Shadow);
            var p = LabourCatalog.For(c.Id, c.Archetype);
            Assert.Equal(p.Coverage, c.UnionCoverage); Assert.Equal(p.LabourShare, c.LabourIncomeShare);
            Assert.Equal(0.0, c.WageGap); Assert.Equal(0.0, c.NairuHyst);
        }
        // nothing the old model tracked was touched, and the next month carries on from where the old game was
        Assert.Equal(a.StateHash(), b.StateHash());
        a.Tick(); b.Tick();
        foreach (var (x, y) in a.World.Countries.Zip(b.World.Countries))
        {
            Assert.InRange(y.Gdp / x.Gdp, 0.999, 1.001);
            Assert.InRange(y.Revenue / x.Revenue, 0.999, 1.001);
            Assert.InRange(y.Inflation - x.Inflation, -0.002, 0.002);
        }
    }

    [Fact]
    public void Current_saves_round_trip_the_new_state_exactly()
    {
        var a = Simulation.New("FRA", 5, true); a.Run(30);
        var b = Simulation.Load(a.Save());
        Assert.Equal(a.StateHash(), b.StateHash());
        foreach (var (x, y) in a.World.Countries.Zip(b.World.Countries))
        {
            Assert.Equal(x.Shadow, y.Shadow); Assert.Equal(x.Shadow0, y.Shadow0); Assert.Equal(x.ShadowDrivers, y.ShadowDrivers);
            Assert.Equal(x.UnionCoverage, y.UnionCoverage); Assert.Equal(x.WageGap, y.WageGap); Assert.Equal(x.WagePremium, y.WagePremium);
            Assert.Equal(x.StrikeRisk, y.StrikeRisk); Assert.Equal(x.LtuStock, y.LtuStock); Assert.Equal(x.NairuHyst, y.NairuHyst);
            Assert.Equal(x.LabourIncomeShare, y.LabourIncomeShare); Assert.Equal(x.LabourIncomeTrend, y.LabourIncomeTrend);
        }
        a.Run(24); b.Run(24);
        Assert.Equal(a.StateHash(), b.StateHash());
    }
}
