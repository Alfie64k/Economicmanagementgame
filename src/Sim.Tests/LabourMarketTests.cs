using System.Text.Json.Nodes;
using Sim.Core.Data;
using Sim.Core.Engine;
using Sim.Core.Events;
using Sim.Core.Model;
using Sim.Core.Policy;
using Sim.Core.Scoring;
using Xunit;

namespace Sim.Tests;

/// <summary>Wage bargaining, the wage-price spiral, hysteresis, strike risk and the labour share.</summary>
public class LabourMarketTests
{
    static void AssertAtRest(CountryState c)
    {
        Assert.Equal(0.0, c.WageGap); Assert.Equal(0.0, c.WagePremium); Assert.Equal(0.0, c.WageSpiral); Assert.Equal(0.0, c.StrikeRisk);
        Assert.Equal(0.0, c.LtuStock); Assert.Equal(0.0, c.NairuHyst);
        Assert.Equal(c.LabourIncomeShare0, c.LabourIncomeShare); Assert.Equal(c.UnionCoverage0, c.UnionCoverage);
    }

    // ---------------------------------------------------------------------------------------------------------------------------------
    // data and starting state

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
            AssertAtRest(c);
            Assert.Equal(c.UnionCoverage * c.UnionStrength, c.BargainingPower);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------------------
    // neutrality

    [Fact]
    public void The_labour_block_is_bit_invisible_in_a_world_of_calm_economies()
    {
        // fourteen economies that never leave the dead-bands: the state hash must be identical with the labour dynamics on or off, month after month
        var on = DepthFixture.QuietRoster(DepthFixture.CalmEconomies, 1, depth: true, shadow: false, labour: true);
        var off = DepthFixture.QuietRoster(DepthFixture.CalmEconomies, 1, depth: false);
        Assert.Equal(14, on.World.Countries.Count);
        for (int i = 0; i < 120; i++)
        {
            on.Tick(); off.Tick();
            Assert.Equal(off.StateHash(), on.StateHash());
        }
        Assert.All(on.World.Countries, AssertAtRest);
    }

    [Fact]
    public void The_calm_economies_stay_exactly_at_rest_for_ten_years_in_the_default_world()
    {
        var s = DepthFixture.Quiet("GBR", 5);
        s.Run(120);
        foreach (var c in s.World.Countries.Where(c => DepthFixture.CalmEconomies.Contains(c.Id)))
            AssertAtRest(c);
    }

    [Fact]
    public void Switched_off_it_is_always_at_rest_even_in_a_country_in_deep_slump()
    {
        foreach (var s in new[] { DepthFixture.Quiet("ARG", depth: false), DepthFixture.Quiet("ARG", labour: false) })
        {
            s.Run(60);
            AssertAtRest(s.World.Player);
        }
        var live = DepthFixture.Quiet("ARG"); live.Run(60);
        Assert.True(live.World.Player.NairuHyst > 0.001 && live.World.Player.WageGap != 0, "the same country is not at rest with the dynamics on");
    }

    // The tolerances below are stated, not tuned to pass. Fourteen calm economies move only through trade with the others; six economies whose
    // calibrated baseline sits in a deep, permanent output gap (Argentina, Turkey, Ethiopia, Egypt, Nigeria, Singapore) accumulate long-term unemployment
    // from the first month, by design, and drift further; the rest are in between.
    static readonly string[] Slumped = { "ARG", "TUR", "ETH", "EGY", "NGA", "SGP" };

    [Theory]
    [InlineData(false)]       // labour block alone
    [InlineData(true)]        // everything in version 3 together: the default world
    public void The_default_world_stays_within_stated_tolerances_of_the_classic_economy_for_ten_years(bool withShadow)
    {
        var on = DepthFixture.Quiet("GBR", 5, depth: true, shadow: withShadow, labour: true); var off = DepthFixture.Quiet("GBR", 5, depth: false);
        on.Run(120); off.Run(120);
        foreach (var (x, y) in on.World.Countries.Zip(off.World.Countries))
        {
            double gdp = x.Gdp / y.Gdp, rev = x.Revenue / y.Revenue, debt = x.Debt / Math.Max(1e-9, y.Debt), du = x.Unemp - y.Unemp, di = x.Inflation - y.Inflation;
            void Within(string what, double v, double lo, double hi) => Assert.True(v >= lo && v <= hi, $"{x.Id} {what} {v:F5} outside [{lo}, {hi}]");
            Within("natural-rate scar", x.NairuHyst, 0.0, LabourMarketEngine.MaxScar);
            if (Slumped.Contains(x.Id))
            {
                // scarring costs up to MaxScar of natural unemployment (and the output that goes with it); inflation differs through the wage block on a 40%-a-year base
                Within("GDP ratio", gdp, 0.96, 1.01); Within("revenue ratio", rev, 0.87, 1.03); Within("debt ratio", debt, 0.92, 1.08);
                Within("unemployment difference", du, -0.002, LabourMarketEngine.MaxScar + 0.01); Within("inflation difference", di, -0.025, 0.01);
            }
            else if (DepthFixture.CalmEconomies.Contains(x.Id))
            {
                // moved only by trade with the others (and, with the shadow economy on, by what its own AI-governed neighbours do)
                Within("GDP ratio", gdp, 0.998, 1.002); Within("revenue ratio", rev, 0.99, 1.01); Within("debt ratio", debt, 0.99, 1.01);
                Within("unemployment difference", du, -0.001, 0.001); Within("inflation difference", di, -0.001, 0.001);
            }
            else
            {
                Within("GDP ratio", gdp, 0.995, 1.005); Within("revenue ratio", rev, 0.97, 1.03); Within("debt ratio", debt, 0.94, 1.06);
                Within("unemployment difference", du, -0.005, 0.005); Within("inflation difference", di, -0.001, 0.001);
            }
        }
        if (!withShadow) Assert.All(on.World.Countries, c => Assert.Equal(c.Shadow0, c.Shadow));
    }

    /// <summary>Numbers recorded from the version 2 engine (the commit before the shadow economy and labour dynamics) with the same code paths: 72 months, a country plus all 27 AI neighbours.</summary>
    static readonly (string Id, bool Stochastic, ulong Seed, double Gdp, double Debt, double Price, double Rate, double Unemp, double Approval)[] Version2 =
    {
        ("GBR", false, 1, 2897.026332074605, 3606.356331047893, 1.1340999131383003, 0.027747934147881778, 0.044945163417027874, 0.28436764500846934),
        ("USA", false, 1, 30660.48583380195, 46653.00668524152, 1.132548295574434, 0.03134208204867766, 0.038641794743412575, 0.412443620878065),
        ("DEU", false, 1, 4223.514610767912, 3531.2223024927066, 1.1184548472973619, 0.020034146481806247, 0.03651782866225269, 0.21624079338669555),
        ("ARG", false, 1, 292156.6341673342, 1683941.1775049812, 16.889383882492172, 0.5028576055169562, 0.17877702434064374, 0.044285404526318704),
        ("GBR", true, 7, 2939.2293829777927, 3602.2707032984754, 1.1257166724992114, 0.03240168505817975, 0.040280847457539244, 0.32180639176538756),
        ("TUR", true, 7, 28009.804187827656, 86896.72044342627, 7.190840959785437, 0.23081733009807714, 0.15898321274883684, 0.03121136009876005),
        ("FRA", true, 7, 3002.1709690025173, 4085.1164009315094, 1.117436212026424, 0.017679610826633236, 0.07551544976381304, 0.39857690167285886),
    };

    [Fact]
    public void With_the_switch_off_the_economy_is_the_version_2_economy()
    {
        foreach (var r in Version2)
        {
            var s = Simulation.New(r.Id, r.Seed, r.Stochastic);
            s.World.EconomicDepth = false;
            if (!r.Stochastic) s.World.Events = false;
            s.Run(72);
            var c = s.World.Player;
            // bit-identical on the machine that recorded them; the relative tolerance only absorbs platform differences in the maths library
            void Same(double want, double got, string what) => Assert.True(Math.Abs(got - want) <= 1e-7 * Math.Abs(want), $"{r.Id} {(r.Stochastic ? "stochastic" : "deterministic")} {what}: {got:R} vs {want:R}");
            Same(r.Gdp, c.Gdp, "GDP"); Same(r.Debt, c.Debt, "debt"); Same(r.Price, c.PriceLevel, "price level"); Same(r.Rate, c.PolicyRate, "policy rate");
            Same(r.Unemp, c.Unemp, "unemployment"); Same(r.Approval, c.Approval, "approval");
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------------------
    // direction of effect

    sealed class Shocked
    {
        public double PeakInflation, PeakPremium, PeakSpiral, PeakGap, PeakStrike;
        public int HalfLifeMonths;                         // from the premium's peak until it has halved
        public double PremiumLater, InflationLater;
        public CountryState C = null!;
    }

    /// <summary>The same country, the same price shock (a cost-push of +6 points a year for 18 months from month 25), with a chosen union coverage and central-bank credibility.</summary>
    static Shocked PriceShock(double coverage, double credibility, double strength = 0.6, double size = 0.06, int duration = 18, int months = 156)
    {
        var s = DepthFixture.Quiet("GBR", 7);
        var c = s.World.Player;
        c.UnionCoverage0 = c.UnionCoverage = coverage; c.UnionStrength = strength; c.Cred = credibility;
        var r = new Shocked { C = c };
        var premium = new List<double>();
        for (int m = 1; m <= months; m++)
        {
            if (m == 25) c.ModTarget["inflation"] = size;
            if (m == 25 + duration) c.ModTarget["inflation"] = 0;
            s.Tick();
            premium.Add(c.WagePremium);
            r.PeakInflation = Math.Max(r.PeakInflation, c.Inflation); r.PeakPremium = Math.Max(r.PeakPremium, c.WagePremium); r.PeakSpiral = Math.Max(r.PeakSpiral, c.WageSpiral);
            r.PeakGap = Math.Max(r.PeakGap, c.WageGap); r.PeakStrike = Math.Max(r.PeakStrike, c.StrikeRisk);
        }
        int peak = premium.IndexOf(premium.Max());
        r.HalfLifeMonths = premium.Skip(peak).TakeWhile(p => p > 0.5 * premium[peak]).Count();
        r.PremiumLater = c.WagePremium; r.InflationLater = c.Inflation;
        return r;
    }

    [Fact]
    public void A_price_shock_sets_off_a_bigger_and_longer_wage_price_spiral_where_unions_are_strong()
    {
        var weak = PriceShock(0.1, 0.6); var strong = PriceShock(0.9, 0.6);
        Assert.True(strong.PeakPremium > 3 * weak.PeakPremium, $"{strong.PeakPremium:P2} vs {weak.PeakPremium:P2}");
        Assert.True(strong.PeakSpiral > 3 * weak.PeakSpiral);
        Assert.True(strong.PeakInflation > weak.PeakInflation + 0.001, $"{strong.PeakInflation:P2} vs {weak.PeakInflation:P2}");
        Assert.True(strong.HalfLifeMonths > weak.HalfLifeMonths, $"half-lives {strong.HalfLifeMonths} vs {weak.HalfLifeMonths} months");
        // and the extra inflation outlasts the shock itself
        Assert.True(strong.InflationLater >= weak.InflationLater);
        // the push is a few tenths of a point, not a runaway: wage premia stay inside their bounds and the spiral is only a fraction of the shock
        Assert.InRange(strong.PeakPremium, 0.004, LabourMarketEngine.PremiumMax);
        Assert.True(strong.PeakSpiral < 0.4 * 0.06);
    }

    [Fact]
    public void A_credible_central_bank_damps_the_spiral()
    {
        var loose = PriceShock(0.9, 0.25); var firm = PriceShock(0.9, 0.9);
        Assert.True(firm.PeakPremium < 0.6 * loose.PeakPremium, $"{firm.PeakPremium:P2} vs {loose.PeakPremium:P2}");
        Assert.True(firm.PeakSpiral < 0.5 * loose.PeakSpiral);
        Assert.True(firm.PeakInflation < loose.PeakInflation - 0.005, $"{firm.PeakInflation:P2} vs {loose.PeakInflation:P2}");
    }

    [Fact]
    public void The_spiral_dies_out_once_the_shock_has_passed()
    {
        var r = PriceShock(0.9, 0.25);
        Assert.InRange(Math.Abs(r.PremiumLater), 0.0, 0.001);
        Assert.InRange(Math.Abs(r.C.WageSpiral), 0.0, 0.001);
        Assert.InRange(Math.Abs(r.C.WageGap), 0.0, 0.004);
        Assert.InRange(r.InflationLater, 0.0, 0.035);        // back near target
    }

    [Fact]
    public void Real_wages_fall_behind_in_a_price_shock_and_strike_risk_follows_bargaining_power()
    {
        var weak = PriceShock(0.1, 0.6); var strong = PriceShock(0.9, 0.6);
        Assert.True(weak.PeakGap > 0.01 && strong.PeakGap > 0.01, "real wages fall behind in both");
        Assert.True(weak.PeakGap > strong.PeakGap, "workers with bargaining power are protected better");
        Assert.True(strong.PeakStrike > 0.05, $"{strong.PeakStrike}");
        Assert.True(strong.PeakStrike > 3 * weak.PeakStrike, $"{strong.PeakStrike} vs {weak.PeakStrike}");
        Assert.InRange(strong.PeakStrike, 0.0, 1.0);
        // a worker whose employer fully trusts the bank gets no cover from indexation: more real-wage loss, hence more unrest, at the same strength
        Assert.True(PriceShock(0.9, 0.95).PeakStrike >= strong.PeakStrike);
    }

    [Fact]
    public void Strike_risk_feeds_the_strike_event_and_a_quiet_economy_leaves_its_probability_untouched()
    {
        var def = EventCatalog.Find("strike_wave")!;
        var pm = Assert.Single(def.ProbMods, m => m.Metric == "strike");
        Assert.Equal(0.0, pm.Ref); Assert.True(pm.Slope > 0);
        var s = DepthFixture.Quiet("GBR"); var c = s.World.Player; var g = s.World.Global;
        Assert.Equal(0.0, EventEngine.Metric(c, g, "strike"));
        Assert.Equal(1.0, 1 + pm.Slope * (EventEngine.Metric(c, g, "strike") - pm.Ref));
        c.StrikeRisk = 0.25;
        Assert.Equal(0.25, EventEngine.Metric(c, g, "strike"));
        Assert.True(1 + pm.Slope * (EventEngine.Metric(c, g, "strike") - pm.Ref) > 1.5);
        Assert.Equal(c.Shadow, EventEngine.Metric(c, g, "shadow"));
    }

    [Fact]
    public void Conceding_a_pay_settlement_makes_good_part_of_the_real_wage_shortfall()
    {
        var s = DepthFixture.Quiet("GBR"); var w = s.World; var c = w.Player;
        c.WageGap = 0.03; double rw = c.RealWageIdx;
        EventEngine.ApplyEffects(w, c, new[] { new EffectDef { Kind = "state", Key = "wagecatchup", Value = 0.6 } }, "test");
        Assert.Equal(0.012, c.WageGap, 12);
        Assert.Equal(rw * Math.Exp(0.6 * 0.03), c.RealWageIdx, 12);
        // workers who are not behind get nothing
        c.WageGap = -0.01; rw = c.RealWageIdx;
        EventEngine.ApplyEffects(w, c, new[] { new EffectDef { Kind = "state", Key = "wagecatchup", Value = 0.6 } }, "test");
        Assert.Equal(-0.01, c.WageGap); Assert.Equal(rw, c.RealWageIdx);
        // and the shipped event offers it as the "concede" choice
        var concede = EventCatalog.Find("strike_wave")!.Choices[0];
        Assert.Contains(concede.Effects, e => e.Kind == "state" && e.Key == "wagecatchup" && e.Value > 0);
    }

    sealed class SlumpRun
    {
        public double PeakHysteresis, NairuBefore, NairuAtPeak;
        public double HystAfter5y, HystAfter12y;
        public Simulation Sim = null!;
    }

    /// <summary>A policy-made slump (policy rate held at 20% for three years from month 13) in one country, then recovery.</summary>
    static SlumpRun Slump(double coverage, bool depth = true)
    {
        var s = DepthFixture.Quiet("GBR", 7, depth);
        var c = s.World.Player;
        c.UnionCoverage0 = c.UnionCoverage = coverage; c.UnionStrength = 0.6;
        var r = new SlumpRun { Sim = s };
        for (int m = 1; m <= 12 * 16; m++)
        {
            if (m == 13) { c.RateMode = RateMode.Manual; c.ManualRate = 0.20; }
            if (m == 13 + 36) c.RateMode = RateMode.Auto;
            if (m == 13) r.NairuBefore = c.NairU;
            s.Tick();
            if (c.NairuHyst > r.PeakHysteresis) { r.PeakHysteresis = c.NairuHyst; r.NairuAtPeak = c.NairU; }
            if (m == 13 + 36 + 60) r.HystAfter5y = c.NairuHyst;
            if (m == 13 + 36 + 144) r.HystAfter12y = c.NairuHyst;
        }
        return r;
    }

    [Fact]
    public void A_long_slump_scars_the_labour_market_and_the_scar_heals_slowly()
    {
        var r = Slump(0.9);
        Assert.True(r.PeakHysteresis > 0.006, $"{r.PeakHysteresis:P2}");
        Assert.True(r.PeakHysteresis <= LabourMarketEngine.MaxScar);
        Assert.True(r.NairuAtPeak > r.NairuBefore + 0.005, $"natural rate {r.NairuBefore:P2} -> {r.NairuAtPeak:P2}");
        Assert.True(r.HystAfter5y > 0.2 * r.PeakHysteresis && r.HystAfter5y < 0.8 * r.PeakHysteresis, $"five years on: {r.HystAfter5y:P3} of a {r.PeakHysteresis:P3} peak");
        Assert.True(r.HystAfter12y < r.HystAfter5y);
        // the same slump without the scarring leaves the natural rate where policy left it
        var classic = Slump(0.9, depth: false);
        Assert.Equal(0.0, classic.PeakHysteresis);
        Assert.True(r.Sim.World.Player.Unemp >= classic.Sim.World.Player.Unemp - 1e-9);
    }

    [Fact]
    public void Scarring_is_worse_where_insiders_are_protected()
    {
        var weak = Slump(0.1); var strong = Slump(0.9);
        Assert.True(strong.PeakHysteresis > 1.3 * weak.PeakHysteresis, $"{strong.PeakHysteresis:P2} vs {weak.PeakHysteresis:P2}");
        Assert.True(weak.PeakHysteresis > 0);
    }

    [Fact]
    public void A_brief_dip_leaves_no_scar()
    {
        var s = DepthFixture.Quiet("GBR", 7);
        var c = s.World.Player;
        s.Run(12); c.RateMode = RateMode.Manual; c.ManualRate = 0.07; s.Run(6); c.RateMode = RateMode.Auto; s.Run(60);
        Assert.Equal(0.0, c.NairuHyst);
    }

    [Fact]
    public void The_labour_share_follows_bargaining_power_and_slumps_with_the_output_gap()
    {
        // stronger unions (a policy modifier on coverage) lift the structural share, weaker ones lower it; its starting value is the reference
        var up = DepthFixture.Quiet("GBR", 7); var down = DepthFixture.Quiet("GBR", 7); var calm = DepthFixture.Quiet("GBR", 7);
        up.World.Player.ModTarget["bargaining"] = 0.30; down.World.Player.ModTarget["bargaining"] = -0.15;
        up.Run(120); down.Run(120); calm.Run(120);
        var u = up.World.Player; var d = down.World.Player; var k = calm.World.Player;
        Assert.Equal(k.LabourIncomeShare0, k.LabourIncomeShare);
        Assert.True(u.UnionCoverage > u.UnionCoverage0 + 0.2 && d.UnionCoverage < d.UnionCoverage0 - 0.1);
        Assert.True(u.LabourIncomeShare > u.LabourIncomeShare0 + 0.01, $"{u.LabourIncomeShare:F4} vs {u.LabourIncomeShare0:F4}");
        Assert.True(d.LabourIncomeShare < d.LabourIncomeShare0 - 0.003, $"{d.LabourIncomeShare:F4} vs {d.LabourIncomeShare0:F4}");
        // a larger labour share means a slightly lower Gini and slightly more household income
        Assert.True(u.Gini < k.Gini && k.Gini - u.Gini < 0.02, $"{u.Gini} vs {k.Gini}");
        Assert.True(u.Cons > k.Cons * 0.999);

        // a deep slump pulls it below the start and it comes back
        var r = Slump(0.5);
        var trough = double.MaxValue;
        var s = DepthFixture.Quiet("GBR", 7); var c = s.World.Player;
        c.UnionCoverage0 = c.UnionCoverage = 0.5; c.UnionStrength = 0.6;
        for (int m = 1; m <= 12 * 16; m++)
        {
            if (m == 13) { c.RateMode = RateMode.Manual; c.ManualRate = 0.20; }
            if (m == 13 + 36) c.RateMode = RateMode.Auto;
            s.Tick();
            trough = Math.Min(trough, c.LabourIncomeShare);
        }
        Assert.True(trough < c.LabourIncomeShare0 - 0.003, $"trough {trough:F4} vs start {c.LabourIncomeShare0:F4}");
        Assert.InRange(c.LabourIncomeShare, c.LabourIncomeShare0 - 0.004, c.LabourIncomeShare0 + 0.004);
        Assert.True(r.PeakHysteresis > 0);
    }

    [Fact]
    public void Ai_countries_use_the_same_labour_path_without_a_detailed_tax_code()
    {
        var s = DepthFixture.Quiet("GBR"); var fra = s.World.Find("FRA")!;
        Assert.Null(fra.Fiscal);
        fra.ModTarget["inflation"] = 0.06;
        s.Run(48);
        Assert.True(fra.WagePremium > 0.002 && fra.WageGap != 0, $"premium {fra.WagePremium:P3}");
        Assert.Equal(0.0, s.World.Player.WagePremium);           // the player's own economy is untouched by FRA's price shock in this window
    }

    [Fact]
    public void Union_policies_move_bargaining_power_and_the_advisers_notice_the_symptoms()
    {
        var s = DepthFixture.Quiet("GBR"); var c = s.World.Player;
        double b0 = c.BargainingPower;
        c.PoliticalCapital = 100; c.Coalition = 1; c.Approval = 0.9;
        Assert.True(s.Execute(Command.Enact("GBR", "union_rights")).Ok);
        s.Run(60);
        Assert.True(c.Mod("bargaining") > 0.05);
        Assert.True(c.BargainingPower > b0 * 1.1, $"{c.BargainingPower} vs {b0}");

        var q = DepthFixture.Quiet("GBR"); var qc = q.World.Player;
        Assert.DoesNotContain(Advisors.Generate(q.World, qc), n => n.Key is "spiral" or "strikes" or "scarring");
        qc.WageSpiral = 0.01; qc.StrikeRisk = 0.3; qc.WageGap = 0.04; qc.NairuHyst = 0.012;
        var keys = Advisors.Generate(q.World, qc).Select(n => n.Key).ToList();
        Assert.Contains("spiral", keys); Assert.Contains("strikes", keys); Assert.Contains("scarring", keys);
    }

    [Fact]
    public void The_explanations_show_the_new_drivers()
    {
        var r = PriceShock(0.9, 0.25, months: 40);
        var infl = Explain.Why(r.C, "inflation");
        var spiral = Assert.Single(infl.Items, i => i.Label == "Wage-price spiral");
        Assert.Equal(r.C.WageSpiral * 100, spiral.Value, 12); Assert.True(spiral.Value > 0);
        var unemp = Explain.Why(DepthFixture.Quiet("GBR").World.Player, "unemployment");
        Assert.Single(unemp.Items, i => i.Label.StartsWith("Long-term unemployment"));
    }

    // ---------------------------------------------------------------------------------------------------------------------------------
    // migration, persistence, determinism, bounds

    [Fact]
    public void Version_2_saves_load_with_the_new_state_at_its_starting_values_and_without_a_jump()
    {
        // a game as version 2 would have played it: no shadow-economy or labour-market dynamics
        var a = DepthFixture.Quiet("GBR", 3, depth: false); a.Run(30);
        var v2 = DepthFixture.AsVersion2(a.Save());
        Assert.DoesNotContain("\"Shadow\"", v2);
        Assert.DoesNotContain("EconomicDepth", v2);
        var b = Simulation.Load(v2);
        Assert.Equal(World.CurrentVersion, b.World.Version);
        Assert.Equal(3, b.World.Version);
        Assert.True(b.World.EconomicDepth && b.World.ShadowEconomy && b.World.LabourMarket);
        foreach (var c in b.World.Countries)
        {
            Assert.Equal(ShadowCatalog.Share(c.Id, c.Archetype), c.Shadow0); Assert.Equal(c.Shadow0, c.Shadow);
            var p = LabourCatalog.For(c.Id, c.Archetype);
            Assert.Equal(p.Coverage, c.UnionCoverage); Assert.Equal(p.LabourShare, c.LabourIncomeShare);
            AssertAtRest(c);
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
    public void A_version_2_save_taken_in_the_middle_of_a_slump_starts_with_a_clean_slate_and_then_scars_normally()
    {
        var a = DepthFixture.Quiet("GBR", 7, depth: false);
        a.Run(12); a.World.Player.RateMode = RateMode.Manual; a.World.Player.ManualRate = 0.20; a.Run(30);
        var b = Simulation.Load(DepthFixture.AsVersion2(a.Save()));
        var c = b.World.Player;
        Assert.Equal(0.0, c.LtuStock); Assert.Equal(0.0, c.NairuHyst);
        b.Run(30);
        Assert.True(c.LtuStock > 0 && c.NairuHyst > 0, "the stock builds up from here");
        Assert.True(double.IsFinite(c.NairU) && c.NairU < 0.45);
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

    [Fact]
    public void A_mid_run_save_resumes_exactly_through_a_wage_shock_and_the_same_seed_repeats()
    {
        Simulation Play(bool resume = false)
        {
            var s = Simulation.New("FRA", 9, true);
            s.World.RecordHistory = false;
            s.Run(10);
            s.World.Player.ModTarget["inflation"] = 0.06;            // a cost-push shock the unions will respond to
            s.Run(30);
            s.World.Player.ModTarget["inflation"] = 0.0;
            s.Run(6);
            if (resume) s = Simulation.Load(s.Save());                // saved with a live wage premium, real-wage gap and strike risk
            s.Run(40);
            return s;
        }
        var a = Play(); var b = Play(); var c = Play(resume: true);
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(a.StateHash(), c.StateHash());
        foreach (var (x, y) in a.World.Countries.Zip(c.World.Countries))
        {
            Assert.Equal(x.WagePremium, y.WagePremium); Assert.Equal(x.WageGap, y.WageGap); Assert.Equal(x.LtuStock, y.LtuStock); Assert.Equal(x.StrikeRisk, y.StrikeRisk);
        }
        Assert.NotEqual(0.0, a.World.Player.WageGap);
    }

    [Fact]
    public void Labour_state_stays_inside_its_bounds_with_no_nan_for_all_28_countries_over_30_years()
    {
        var w = DepthFixture.ThirtyYears.World;
        Assert.Equal(28, w.Countries.Count);
        foreach (var c in w.Countries)
        {
            string id = c.Id;
            Assert.InRange(c.UnionCoverage, 0.0, 0.99); Assert.InRange(c.UnionStrength, 0.0, 1.0);
            Assert.InRange(c.LabourIncomeShare, 0.15, 0.85); Assert.InRange(c.LabourIncomeTrend, 0.0, 1.0);
            Assert.InRange(c.StrikeRisk, 0.0, 1.0);
            Assert.InRange(c.WagePremium, LabourMarketEngine.PremiumMin, LabourMarketEngine.PremiumMax);
            Assert.InRange(c.WageGap, LabourMarketEngine.GapMin, LabourMarketEngine.GapMax);
            Assert.InRange(c.WageSpiral, LabourMarketEngine.PassThrough * LabourMarketEngine.PremiumMin, LabourMarketEngine.PassThrough * LabourMarketEngine.PremiumMax);
            Assert.InRange(c.LtuStock, 0.0, LabourMarketEngine.MaxStock);
            Assert.InRange(c.NairuHyst, 0.0, LabourMarketEngine.MaxScar);
            Assert.InRange(c.NairU, 0.01, 0.45); Assert.InRange(c.Unemp, 0.0, 0.5);
            foreach (var v in new[] { c.WageGap, c.WagePremium, c.WageSpiral, c.StrikeRisk, c.LtuStock, c.NairuHyst, c.LabourIncomeShare, c.LabourIncomeTrend, c.RealWageIdx, c.NairU, c.Unemp, c.Inflation })
                Assert.True(double.IsFinite(v), id);
            Assert.True(c.RealWageIdx > 0.05 && c.RealWageIdx < 20, $"{id} real wage index {c.RealWageIdx}");
        }
    }
}
