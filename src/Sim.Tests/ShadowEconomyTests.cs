using System.Text.Json.Nodes;
using Sim.Core.Data;
using Sim.Core.Engine;
using Sim.Core.Model;
using Sim.Core.Policy;
using Xunit;

namespace Sim.Tests;

/// <summary>Helpers shared by the shadow-economy and labour-market tests.</summary>
public static class DepthFixture
{
    public static readonly string[] Roster = CountryLoader.LoadEmbedded().Select(d => d.Id).ToArray();

    /// <summary>Every field the version 3 model added to a country; a version 2 save has none of them.</summary>
    public static readonly string[] V3CountryFields =
    {
        "Shadow", "Shadow0", "ShadowDrivers", "UnionCoverage", "UnionCoverage0", "UnionStrength", "WageGap", "WagePremium", "WageSpiral", "StrikeRisk",
        "LtuStock", "NairuHyst", "LabourIncomeShare", "LabourIncomeShare0", "LabourIncomeTrend",
    };

    /// <summary>Rewrites a current save as a version 2 save (no shadow or labour state, no switch, version number 2).</summary>
    public static string AsVersion2(string json)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        root["Version"] = 2;
        root.Remove("EconomicDepth");
        foreach (var c in root["Countries"]!.AsArray().Append(root["Player"]))      // the player's state is also written under "Player"
            if (c is JsonObject o) foreach (var k in V3CountryFields) o.Remove(k);
        return root.ToJsonString();
    }

    public static JsonObject Shipped(string file)
    {
        using var s = typeof(ShadowCatalog).Assembly.GetManifestResourceStream("data/" + file)!;
        return (JsonObject)JsonNode.Parse(s)!;
    }

    public static List<(string Id, string Archetype)> RosterWithArchetypes() => CountryLoader.LoadEmbedded().Select(d => (d.Id, d.Archetype)).ToList();

    /// <summary>A quiet deterministic game: no random shocks, no events, no advisers.</summary>
    public static Simulation Quiet(string id = "GBR", ulong seed = 1, bool depth = true)
    {
        var s = Simulation.New(id, seed, stochastic: false);
        s.World.Events = false; s.World.Advisors = false; s.World.RecordHistory = false; s.World.EconomicDepth = depth;
        return s;
    }
}

/// <summary>The shadow economy: starting data, tax-base erosion and its drivers.</summary>
public class ShadowEconomyTests
{
    [Fact]
    public void Shipped_file_is_sound_for_every_country()
    {
        Assert.True(ShadowCatalog.Available);
        var roster = DepthFixture.RosterWithArchetypes();
        Assert.Equal(28, roster.Count);
        Assert.Empty(ShadowCatalog.Validate(DepthFixture.Shipped("shadow.json"), roster));
    }

    [Fact]
    public void A_roster_country_missing_from_the_file_is_reported()
    {
        var doc = DepthFixture.Shipped("shadow.json");
        ((JsonObject)doc["countries"]!).Remove("NGA");
        var problems = ShadowCatalog.Validate(doc, DepthFixture.RosterWithArchetypes());
        Assert.Contains(problems, p => p.StartsWith("NGA"));
        // the loader still returns a sensible archetype value rather than failing
        Assert.Equal(0.28, ShadowCatalog.ShareFrom(doc, "NGA", "resource"), 9);
    }

    [Fact]
    public void Starting_shares_follow_the_broad_ordering_of_the_literature()
    {
        double Share(string id) => ShadowCatalog.Share(id, "x");
        var adv = new[] { "USA", "GBR", "DEU", "FRA", "JPN", "AUS", "CAN", "CHE" }.Select(Share).ToArray();
        Assert.All(adv, s => Assert.InRange(s, 0.05, 0.13));
        var poor = new[] { "NGA", "ETH", "EGY", "MEX", "BRA", "TUR" }.Select(Share).ToArray();
        Assert.All(poor, s => Assert.InRange(s, 0.25, 0.60));
        Assert.True(Share("CHE") < Share("USA") && Share("USA") < Share("IND") && Share("IND") < Share("NGA"));
    }

    [Fact]
    public void Every_country_starts_at_its_catalogue_share_and_the_multiplier_is_exactly_one()
    {
        var w = Simulation.New("GBR", 1, false).World;
        Assert.Equal(DepthFixture.Roster.Length, w.Countries.Count);
        foreach (var c in w.Countries)
        {
            Assert.Equal(ShadowCatalog.Share(c.Id, c.Archetype), c.Shadow0);
            Assert.Equal(c.Shadow0, c.Shadow);
            Assert.Equal(1.0, c.ShadowMult);
        }
    }
}
