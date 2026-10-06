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
        foreach (var k in new[] { "EconomicDepth", "ShadowEconomy", "LabourMarket" }) root.Remove(k);
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

    static readonly Lazy<Simulation> Thirty = new(() =>
    {
        var s = Simulation.New("GBR", 11, stochastic: true);
        s.World.RecordHistory = false; s.World.Advisors = false;
        s.Run(360);
        return s;
    }, true);

    /// <summary>One shared 30-year stochastic world (seed 11, events on, 28 countries), run once for the bounds tests. Read only.</summary>
    public static Simulation ThirtyYears => Thirty.Value;

    /// <summary>Sets the version 3 switches: <paramref name="depth"/> is the master; <paramref name="shadow"/> and <paramref name="labour"/> pick the systems when it is on.</summary>
    public static Simulation Switch(Simulation s, bool depth, bool shadow, bool labour)
    {
        s.World.EconomicDepth = depth; s.World.ShadowEconomy = shadow; s.World.LabourMarket = labour;
        return s;
    }

    /// <summary>
    /// A world with only one country: nobody else moves, so the player's state depends on nothing but its own levers. (It is not a calm economy: with no
    /// trade partners the country sits in a deep slump, which is why shadow-only tests switch the labour block off.)
    /// </summary>
    public static Simulation Solo(string id, ulong seed = 7, bool stochastic = false, bool depth = true, bool shadow = true, bool labour = true)
    {
        var roster = CountryLoader.LoadEmbedded().Where(d => d.Id == id).ToList();
        var s = Simulation.New(id, seed, stochastic, roster);
        if (!stochastic) s.World.Events = false;
        return Switch(s, depth, shadow, labour);
    }

    /// <summary>A quiet deterministic game: no random shocks, no events, no advisers.</summary>
    public static Simulation Quiet(string id = "GBR", ulong seed = 1, bool depth = true, bool shadow = true, bool labour = true)
    {
        var s = Simulation.New(id, seed, stochastic: false);
        s.World.Events = false; s.World.Advisors = false; s.World.RecordHistory = false;
        return Switch(s, depth, shadow, labour);
    }

    /// <summary>
    /// Fourteen economies that never leave their labour-market dead-bands in quiet play (verified over 30 years), as a world of their own: the
    /// calibrated crisis economies (Argentina, Turkey, Ethiopia ...) have genuine slack from the first month, so scarring starts there by design.
    /// </summary>
    public static readonly string[] CalmEconomies = { "GBR", "USA", "DEU", "FRA", "KOR", "CHE", "AUS", "CAN", "NOR", "POL", "CHN", "SAU", "IDN", "VNM" };

    public static Simulation QuietRoster(string[] ids, ulong seed = 1, bool depth = true, bool shadow = true, bool labour = true)
    {
        var roster = CountryLoader.LoadEmbedded().Where(d => ids.Contains(d.Id)).ToList();
        var s = Simulation.New(ids[0], seed, stochastic: false, roster);
        s.World.Events = false; s.World.Advisors = false; s.World.RecordHistory = false;
        return Switch(s, depth, shadow, labour);
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

    // ---------------------------------------------------------------------------------------------------------------------------------
    // neutrality

    [Theory]
    [InlineData("GBR")]
    [InlineData("USA")]
    [InlineData("DEU")]
    [InlineData("FRA")]
    [InlineData("KOR")]
    [InlineData("CHE")]
    public void The_new_dynamics_are_invisible_while_no_lever_has_moved(string id)
    {
        // a country on its own, so nothing but its own levers can move it; the state hash must be bit-identical with the version 3 dynamics on or off
        var on = DepthFixture.Solo(id, depth: true, labour: false); var off = DepthFixture.Solo(id, depth: false);
        for (int i = 0; i < 96; i++)
        {
            on.Tick(); off.Tick();
            Assert.Equal(off.StateHash(), on.StateHash());
        }
        var c = on.World.Player;
        Assert.Equal(c.Shadow0, c.Shadow);
        Assert.Equal(1.0, c.ShadowMult);
        Assert.Equal(off.World.Player.Revenue, c.Revenue);
        Assert.Equal(off.World.Player.Gini, c.Gini);
        Assert.Equal(off.World.Player.Approval, c.Approval);
    }

    [Fact]
    public void The_default_world_stays_within_a_small_stated_tolerance_of_the_classic_economy_for_ten_years()
    {
        // AI governments do change their taxes, enforcement and policies in the default world, so their shadow shares do move; the drift must stay small.
        // Stated tolerance after 120 deterministic months, every country: GDP 0.5%, revenue 3%, debt 6%, unemployment 0.3pp, shadow share 2.6pp.
        var on = DepthFixture.Quiet("GBR", 5, depth: true, labour: false); var off = DepthFixture.Quiet("GBR", 5, depth: false);
        on.Run(120); off.Run(120);
        foreach (var (x, y) in on.World.Countries.Zip(off.World.Countries))
        {
            Assert.InRange(x.Gdp / y.Gdp, 0.995, 1.005);
            Assert.InRange(x.Revenue / y.Revenue, 0.97, 1.03);
            Assert.InRange(x.Debt / Math.Max(1e-9, y.Debt), 0.94, 1.06);
            Assert.InRange(x.Unemp - y.Unemp, -0.003, 0.003);
            Assert.InRange(Math.Abs(x.Shadow - x.Shadow0), 0, 0.026);
        }
        Assert.Equal(1.0, on.World.Player.ShadowMult);              // the player touched nothing
    }

    [Fact]
    public void Every_country_is_exactly_neutral_before_the_first_month_is_played()
    {
        var w = DepthFixture.Quiet("GBR").World;
        foreach (var c in w.Countries)
        {
            Assert.Equal(0.0, ShadowEngine.WedgeDelta(c));
            Assert.Equal(c.Shadow0, ShadowEngine.Target(c));
            double with = 0, without = 0;
            for (int t = 0; t < Dim.Taxes; t++)
            {
                with += FiscalEngine.TaxRevenueReal(c, (Tax)t, c.Gdp, c.Cons, c.Imports);
                without += FiscalEngine.TaxRevenueAt(c, (Tax)t, c.TaxRate[t], c.Gdp, c.Cons, c.Imports, erode: false);
            }
            Assert.Equal(without, with);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------------------
    // direction of effect

    static double Statutory(CountryState c) { double s = 0; for (int t = 0; t < Dim.Taxes; t++) s += FiscalEngine.TaxRevenueAt(c, (Tax)t, c.TaxRate[t], c.Gdp, c.Cons, c.Imports, erode: false); return s; }
    static double Actual(CountryState c) { double s = 0; for (int t = 0; t < Dim.Taxes; t++) s += FiscalEngine.TaxRevenueReal(c, (Tax)t, c.Gdp, c.Cons, c.Imports); return s; }

    [Fact]
    public void A_heavier_tax_wedge_raises_the_shadow_share_and_lowers_revenue_per_unit_of_base()
    {
        var calm = DepthFixture.Quiet("GBR"); var taxed = DepthFixture.Quiet("GBR");
        var t = taxed.World.Player;
        t.TaxRate[(int)Tax.Consumption] *= 1.25; t.TaxRate[(int)Tax.Income] *= 1.20; t.TaxRate[(int)Tax.Payroll] *= 1.15;
        calm.Run(60); taxed.Run(60);
        var c = calm.World.Player;
        Assert.True(ShadowEngine.WedgeDelta(t) > 0.02);
        Assert.Equal(c.Shadow0, c.Shadow);
        Assert.True(t.Shadow > t.Shadow0 + 0.005, $"shadow {t.Shadow:P2} vs start {t.Shadow0:P2}");
        Assert.True(t.ShadowMult < 0.99);
        // what the taxes collect per unit of what the statutes would collect on the same base
        Assert.Equal(1.0, Actual(c) / Statutory(c), 12);
        Assert.True(Actual(t) / Statutory(t) < 0.99);
        // and the higher rates still raise more money in total (concave response, partly eroded), but less than they would without informality
        var noErosion = DepthFixture.Quiet("GBR", depth: false);
        noErosion.World.Player.TaxRate[(int)Tax.Consumption] *= 1.25; noErosion.World.Player.TaxRate[(int)Tax.Income] *= 1.20; noErosion.World.Player.TaxRate[(int)Tax.Payroll] *= 1.15;
        noErosion.Run(60);
        Assert.True(t.Revenue < noErosion.World.Player.Revenue);
        Assert.True(t.Revenue > calm.World.Player.Revenue * 0.98);
    }

    [Fact]
    public void A_lighter_wedge_lowers_it()
    {
        var calm = DepthFixture.Quiet("DEU"); var cut = DepthFixture.Quiet("DEU");
        cut.World.Player.TaxRate[(int)Tax.Payroll] *= 0.80; cut.World.Player.TaxRate[(int)Tax.Consumption] *= 0.85;
        calm.Run(60); cut.Run(60);
        Assert.True(cut.World.Player.Shadow < calm.World.Player.Shadow - 0.003);
        Assert.True(cut.World.Player.ShadowMult > 1.0);
    }

    [Fact]
    public void More_enforcement_spending_lowers_it_and_less_raises_it()
    {
        var calm = DepthFixture.Quiet("MEX"); var strict = DepthFixture.Quiet("MEX"); var lax = DepthFixture.Quiet("MEX");
        strict.World.Player.Budget[(int)BudgetLine.Admin] *= 1.8; lax.World.Player.Budget[(int)BudgetLine.Admin] *= 0.5;
        calm.Run(60); strict.Run(60); lax.Run(60);
        double s0 = calm.World.Player.Shadow0;
        Assert.Equal(s0, calm.World.Player.Shadow);
        Assert.True(strict.World.Player.Shadow < s0 - 0.004, $"{strict.World.Player.Shadow:P2}");
        Assert.True(lax.World.Player.Shadow > s0 + 0.004, $"{lax.World.Player.Shadow:P2}");
        Assert.True(strict.World.Player.ShadowMult > 1.0 && lax.World.Player.ShadowMult < 1.0);
        Assert.True(strict.World.Player.ShadowDrivers[3] < 0 && lax.World.Player.ShadowDrivers[3] > 0);
    }

    [Fact]
    public void More_corruption_raises_it_and_the_drivers_add_up_to_the_target()
    {
        var calm = DepthFixture.Quiet("BRA"); var dirty = DepthFixture.Quiet("BRA");
        dirty.World.Player.Corruption += 0.15;
        calm.Run(60); dirty.Run(60);
        Assert.True(dirty.World.Player.Shadow > calm.World.Player.Shadow + 0.01);
        var d = dirty.World.Player.ShadowDrivers;
        Assert.True(d[2] > 0.04);
        Assert.Equal(ShadowEngine.Target(dirty.World.Player), d.Sum(), 12);
        Assert.Equal(dirty.World.Player.Shadow0, d[0]);
    }

    [Fact]
    public void The_share_moves_slowly_over_about_three_years_and_never_leaves_its_bounds()
    {
        var s = DepthFixture.Quiet("MEX"); var c = s.World.Player;
        c.TaxRate[(int)Tax.Consumption] *= 1.5; c.TaxRate[(int)Tax.Income] *= 1.5;      // a big, permanent shock
        double target = ShadowEngine.Target(c);
        Assert.True(target > c.Shadow0 + 0.015, $"target {target:P2} vs start {c.Shadow0:P2}");
        s.Run(12);
        double after1 = c.Shadow - c.Shadow0, gap = target - c.Shadow0;
        Assert.InRange(after1 / gap, 0.15, 0.40);                  // roughly 1 - exp(-1/3) = 28% of the way after a year
        s.Run(240);
        var (lo, hi) = ShadowEngine.Bounds(c);
        Assert.InRange(c.Shadow, lo, hi);
        Assert.InRange((c.Shadow - c.Shadow0) / gap, 0.9, 1.1);
    }

    [Fact]
    public void A_compliance_drive_shrinks_the_informal_economy()
    {
        var calm = DepthFixture.Quiet("GBR"); var drive = DepthFixture.Quiet("GBR");
        var c = drive.World.Player; c.PoliticalCapital = 100; c.Coalition = 1; c.Approval = 0.9;
        Assert.True(drive.Execute(Command.Enact("GBR", "tax_compliance")).Ok);
        calm.Run(84); drive.Run(84);
        Assert.True(c.Mod("shadow") < -0.05);
        Assert.True(c.Shadow < c.Shadow0 - 0.004, $"{c.Shadow:P2} vs {c.Shadow0:P2}");
        Assert.True(c.Revenue > calm.World.Player.Revenue);
    }

    [Fact]
    public void The_erosion_is_exactly_the_stated_multiplier_on_every_tax_but_tariffs()
    {
        var s = DepthFixture.Quiet("MEX"); var c = s.World.Player;
        double burden0 = SocietyEngine.TaxBurden(c);
        c.Shadow = c.Shadow0 + 0.05;
        double m = (1 - c.Shadow) / (1 - c.Shadow0);
        Assert.Equal(m, c.ShadowMult, 12);
        foreach (var t in new[] { Tax.Income, Tax.Corporate, Tax.Consumption, Tax.Payroll })
            Assert.Equal(m, FiscalEngine.TaxRevenueReal(c, t, c.Gdp, c.Cons, c.Imports) / FiscalEngine.TaxRevenueAt(c, t, c.TaxRate[(int)t], c.Gdp, c.Cons, c.Imports, erode: false), 12);
        Assert.Equal(FiscalEngine.TaxRevenueAt(c, Tax.Tariff, c.TaxRate[(int)Tax.Tariff], c.Gdp, c.Cons, c.Imports, erode: false),
                     FiscalEngine.TaxRevenueReal(c, Tax.Tariff, c.Gdp, c.Cons, c.Imports));
        // voters feel the statutory burden, not the part evaded
        Assert.Equal(burden0, SocietyEngine.TaxBurden(c), 12);
    }

    [Fact]
    public void A_larger_informal_economy_slightly_widens_inequality_and_dents_approval()
    {
        var calm = DepthFixture.Quiet("MEX"); var informal = DepthFixture.Quiet("MEX");
        informal.World.Player.Shadow += 0.08;
        calm.Run(36); informal.Run(36);
        var a = calm.World.Player; var b = informal.World.Player;
        Assert.True(b.Gini > a.Gini && b.Gini - a.Gini < 0.01, $"{b.Gini - a.Gini}");
        Assert.True(b.Approval < a.Approval && a.Approval - b.Approval < 0.02, $"{a.Approval - b.Approval}");
    }

    [Fact]
    public void The_finance_minister_notices_when_informality_grows()
    {
        var s = DepthFixture.Quiet("MEX"); var c = s.World.Player;
        Assert.DoesNotContain(Advisors.Generate(s.World, c), n => n.Key == "shadow" || n.Key == "shadowfall");
        c.TaxRate[(int)Tax.Consumption] *= 1.6; c.Shadow = c.Shadow0 + 0.03; ShadowEngine.Target(c);
        var note = Assert.Single(Advisors.Generate(s.World, c), n => n.Key == "shadow");
        Assert.Contains("informal economy", note.Text);
        Assert.Contains("taxes", note.Text);
    }

    [Fact]
    public void Ai_countries_run_the_same_code_with_no_detailed_tax_code()
    {
        var s = DepthFixture.Quiet("GBR"); var fra = s.World.Find("FRA")!;
        Assert.Null(fra.Fiscal);
        fra.TaxRate[(int)Tax.Consumption] *= 1.3; fra.TaxRate[(int)Tax.Payroll] *= 1.2;
        s.Run(48);
        Assert.True(fra.Shadow > fra.Shadow0 + 0.004, $"{fra.Shadow:P2}");
        Assert.True(fra.ShadowMult < 1.0);
    }

    // ---------------------------------------------------------------------------------------------------------------------------------
    // determinism, persistence, bounds

    [Fact]
    public void A_mid_run_save_resumes_exactly_and_the_same_seed_repeats()
    {
        Simulation Play(bool resume = false)
        {
            var s = Simulation.New("FRA", 9, true);
            s.World.RecordHistory = false;
            s.Run(10);
            s.Execute(Command.SetTax("FRA", Tax.Consumption, s.World.Player.TaxRate[(int)Tax.Consumption] * 1.15));
            s.World.Player.Budget[(int)BudgetLine.Admin] *= 0.7;
            s.Run(20);
            if (resume) s = Simulation.Load(s.Save());     // saved at month 30, mid-adjustment
            s.Run(40);
            return s;
        }
        var a = Play(); var b = Play(); var c = Play(resume: true);
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(a.StateHash(), c.StateHash());
        Assert.True(a.World.Player.Shadow > a.World.Player.Shadow0);
        foreach (var (x, y) in a.World.Countries.Zip(c.World.Countries)) { Assert.Equal(x.Shadow, y.Shadow); Assert.Equal(x.ShadowDrivers, y.ShadowDrivers); }
    }

    [Fact]
    public void Shares_stay_inside_their_bounds_with_no_nan_for_all_28_countries_over_30_years()
    {
        var w = DepthFixture.ThirtyYears.World;
        Assert.Equal(28, w.Countries.Count);
        foreach (var c in w.Countries)
        {
            var (lo, hi) = ShadowEngine.Bounds(c);
            Assert.InRange(c.Shadow, lo - 1e-12, hi + 1e-12);
            Assert.InRange(c.Shadow, 0.0, 1.0);
            Assert.True(double.IsFinite(c.ShadowMult) && c.ShadowMult is > 0.5 and < 1.6, $"{c.Id} mult {c.ShadowMult}");
            Assert.All(c.ShadowDrivers, d => Assert.True(double.IsFinite(d), c.Id));
            Assert.True(double.IsFinite(c.Revenue) && c.Revenue > 0, c.Id);
        }
    }
}
