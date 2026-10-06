using System.Text.Json.Nodes;
using Sim.Core.Data;
using Sim.Core.Engine;
using Sim.Core.Model;
using Sim.Core.Policy;
using Xunit;

namespace Sim.Tests;

/// <summary>A small hand-written tax-code document so the engine can be tested independently of the shipped data file.</summary>
public static class FiscalFixture
{
    public const string Json = """
    {
      "version": 1, "note": "fixture",
      "archetypes": {
        "advanced": { "label": "Advanced template", "unit": "me",
          "income": { "allowance": 0.30, "taperStart": 0, "taperRate": 0, "bands": [ {"from":0,"rate":0.20}, {"from":0.9,"rate":0.30}, {"from":3.0,"rate":0.40} ], "index": "earnings" },
          "payroll": { "employee": [ {"from":0.25,"rate":0.08} ], "employer": {"from":0.25,"rate":0.15} },
          "corp": { "main": 0.25, "small": 0.25, "smallLimit": 0, "expensing": 0.3 },
          "vat": { "standard": 0.19, "reduced": 0.07, "categories": { "food":"reduced","energy":"standard","housing":"exempt","transport":"standard","services":"standard","goods":"standard","health_edu":"exempt" } },
          "benefits": { "shares": {"pension":0.40,"unemployment":0.04,"child":0.06,"disability":0.12,"housing":0.06,"meanstest":0.12,"other":0.20},
            "pension": {"level":0.30,"age":65,"index":"neutral"}, "unemployment": {"level":0.15,"months":12}, "child": {"level":0.05,"threshold":0},
            "disability": {"level":0.20}, "housing": {"level":0.15}, "meanstest": {"level":0.20,"taper":0.50,"workAllowance":0.15} } },
        "hub": {}, "emerging": {}, "resource": {}, "developing": {}
      },
      "countries": {
        "GBR": { "label": "United Kingdom 2024/25 (approximate)", "unit": "lcu", "meanEarnings": 35000,
          "income": {"allowance": 12570, "taperStart": 100000, "taperRate": 0.5, "bands": [{"from":0,"rate":0.20},{"from":37700,"rate":0.40},{"from":125140,"rate":0.45}], "index": "frozen"},
          "payroll": {"employee": [{"from":12570,"rate":0.08},{"from":50270,"rate":0.02}], "employer": {"from":9100,"rate":0.138}},
          "corp": {"main":0.25,"small":0.19,"smallLimit":50000,"expensing":0.6},
          "vat": {"standard":0.20,"reduced":0.05,"categories":{"food":"zero","energy":"reduced","housing":"zero","transport":"standard","services":"standard","goods":"standard","health_edu":"exempt"}},
          "benefits": {"shares":{"pension":0.42,"unemployment":0.03,"child":0.05,"disability":0.18,"housing":0.07,"meanstest":0.13,"other":0.12},
            "pension":{"level":11500,"age":66,"index":"triplelock"}, "unemployment":{"level":4700,"months":36}, "child":{"level":1330,"threshold":60000},
            "disability":{"level":7000}, "housing":{"level":5500}, "meanstest":{"level":7000,"taper":0.55,"workAllowance":5000}} }
      }
    }
    """;

    public static JsonObject Doc() => (JsonObject)JsonNode.Parse(Json)!;

    /// <summary>A deterministic game for the UK with (or without) the detailed code attached.</summary>
    public static Simulation Game(bool fiscal, string id = "GBR", bool stochastic = false)
    {
        var s = Simulation.New(id, 1, stochastic, detailedTax: false);
        s.World.Events = false;
        if (fiscal) s.World.Player.Fiscal = TaxCodeCatalog.BuildFrom(Doc(), s.World.Player);
        return s;
    }
}

public class FiscalCodeTests
{
    [Fact]
    public void Normal_distribution_helpers_are_accurate()
    {
        Assert.Equal(0.5, Sim.Core.Util.Maths.NormCdf(0), 7);
        Assert.Equal(0.975, Sim.Core.Util.Maths.NormCdf(1.959964), 5);
        Assert.Equal(1.959964, Sim.Core.Util.Maths.NormInv(0.975), 5);
        for (double p = 0.001; p < 1; p += 0.037) Assert.Equal(p, Sim.Core.Util.Maths.NormCdf(Sim.Core.Util.Maths.NormInv(p)), 5);
    }

    [Fact]
    public void Loader_converts_lcu_entries_to_multiples_of_mean_earnings_and_merges_onto_the_template()
    {
        var m = TaxCodeCatalog.Merged(FiscalFixture.Doc(), "GBR", "advanced");
        Assert.Equal(12570.0 / 35000, m["income"]!["allowance"]!.GetValue<double>(), 9);
        var bands = m["income"]!["bands"]!.AsArray();
        Assert.Equal(3, bands.Count);
        Assert.Equal(37700.0 / 35000, bands[1]!["from"]!.GetValue<double>(), 9);
        Assert.Equal("me", m["unit"]!.GetValue<string>());
        // an entry that overrides only some blocks keeps the rest of the template
        var doc = FiscalFixture.Doc();
        ((JsonObject)doc["countries"]!)["DEU"] = JsonNode.Parse("""{"unit":"me","vat":{"standard":0.19}}""");
        var d = TaxCodeCatalog.Merged(doc, "DEU", "advanced");
        Assert.Equal(0.19, d["vat"]!["standard"]!.GetValue<double>(), 9);
        Assert.Equal(0.07, d["vat"]!["reduced"]!.GetValue<double>(), 9);
        Assert.Equal(0.30, d["income"]!["allowance"]!.GetValue<double>(), 9);
        Assert.Empty(TaxCodeCatalog.Validate(doc, new[] { ("GBR", "advanced"), ("DEU", "advanced") }));
    }

    [Fact]
    public void Starting_code_reproduces_the_engine_exactly()
    {
        var a = FiscalFixture.Game(false, stochastic: true); var b = FiscalFixture.Game(true, stochastic: true);
        a.World.Events = b.World.Events = true;
        Assert.NotNull(b.World.Player.Fiscal);
        Assert.True(b.World.Player.Fiscal!.AtStart);
        for (int i = 0; i < 120; i++) { a.Tick(); b.Tick(); }
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(a.World.Player.TaxRate, b.World.Player.TaxRate);
        Assert.Equal(a.World.Player.Gini, b.World.Player.Gini);
        Assert.Equal(a.World.Player.Approval, b.World.Player.Approval);
        Assert.True(b.World.Player.Fiscal!.AtStart);
    }

    [Fact]
    public void Direct_tax_writes_are_absorbed_and_behave_as_before()
    {
        var a = FiscalFixture.Game(false); var b = FiscalFixture.Game(true);
        double r = a.World.Player.TaxRate[(int)Tax.Income];
        Assert.True(a.Execute(Command.SetTax("GBR", Tax.Income, r + 0.02)).Ok);
        Assert.True(b.Execute(Command.SetTax("GBR", Tax.Income, r + 0.02)).Ok);
        a.Run(24); b.Run(24);
        Assert.Equal(a.World.Player.TaxRate[(int)Tax.Income], b.World.Player.TaxRate[(int)Tax.Income], 9);
        Assert.Equal(a.World.Player.Gdp, b.World.Player.Gdp, 6);
        Assert.Equal(a.World.Player.Debt, b.World.Player.Debt, 3);
    }

    [Fact]
    public void Income_tax_has_a_sixty_per_cent_marginal_rate_in_the_allowance_taper()
    {
        var g = FiscalFixture.Game(true); var f = g.World.Player.Fiscal!;
        var cv = TaxCodeEngine.Curve(f, f.P, f.Drift, 5.0, 501);
        int Idx(double me) => (int)Math.Round(me / 5.0 * 500);
        Assert.Equal(0.0, cv.IncomeTaxMarginal[Idx(0.2)], 6);                 // inside the allowance
        Assert.Equal(0.20, cv.IncomeTaxMarginal[Idx(1.0)], 3);                // basic rate
        Assert.Equal(0.40, cv.IncomeTaxMarginal[Idx(2.0)], 3);                // higher rate
        Assert.InRange(cv.IncomeTaxMarginal[Idx(3.2)], 0.595, 0.605);         // 40% plus 0.5 × 40% from losing the allowance
        Assert.Equal(0.45, cv.IncomeTaxMarginal[Idx(4.5)], 3);                // additional rate once the allowance is gone
        Assert.True(cv.TotalMarginal[Idx(0.9)] > 0.5 || cv.TotalMarginal[Idx(0.9)] >= cv.IncomeTaxMarginal[Idx(0.9)]);
    }

    [Fact]
    public void Raising_the_allowance_cuts_revenue_and_edits_are_priced_and_staged_like_other_commands()
    {
        var g = FiscalFixture.Game(true); var c = g.World.Player; var f = c.Fiscal!;
        double r0 = c.TaxRate[(int)Tax.Income];
        var cmd = Command.SetFiscal("GBR", "Inc.Allow", f.Get("Inc.Allow") * 1.4);
        var dry = CommandProcessor.Apply(g.World, cmd, dryRun: true);
        Assert.True(dry.Ok && dry.PcCost > 0);
        Assert.Equal(r0, c.TaxRate[(int)Tax.Income]);                         // a dry run changes nothing
        var st = g.Stage(cmd);
        Assert.True(st.Staged);
        Assert.Equal(f.Get0("Inc.Allow"), f.Get("Inc.Allow"));               // still staged only
        g.Tick();
        Assert.True(c.TaxRate[(int)Tax.Income] < r0);
        Assert.False(f.AtStart);
        // putting it back is a no-op against the baseline value and brings the rate back exactly
        var back = g.Execute(Command.SetFiscal("GBR", "Inc.Allow", f.Get0("Inc.Allow")));
        Assert.True(back.Ok);
        Assert.Equal(r0, c.TaxRate[(int)Tax.Income], 12);
    }

    [Fact]
    public void Band_tables_are_replaced_atomically_and_normalised()
    {
        var g = FiscalFixture.Game(true); var c = g.World.Player; var f = c.Fiscal!;
        var bands = new List<(double, double)> { (0, 0.19), (1.2, 0.30), (3.0, 0.42), (6.0, 0.50) };
        var r = g.Execute(Command.SetBands("GBR", bands));
        Assert.True(r.Ok, r.Message);
        var now = FiscalParams.Bands(f.P);
        Assert.Equal(4, now.Count);
        Assert.Equal(0.50, now[3].Rate, 9);
        Assert.True(g.Execute(Command.SetBands("GBR", bands)).NoOp);
        Assert.False(g.Execute(new Command { Type = "fiscal", Country = "GBR", Id = FiscalParams.BandsKey, Data = "not,a:table" }).Ok);
        // out-of-order starts are repaired rather than rejected
        g.Execute(Command.SetBands("GBR", new List<(double, double)> { (0, 0.2), (2.0, 0.3), (1.0, 0.4) }));
        var fixedUp = FiscalParams.Bands(f.P);
        Assert.True(fixedUp[1].From < fixedUp[2].From);
    }

    [Fact]
    public void Frozen_thresholds_drag_people_into_higher_bands()
    {
        var plain = FiscalFixture.Game(true); var frozen = FiscalFixture.Game(true);
        Assert.True(frozen.Execute(Command.SetFiscal("GBR", "Thr.Index", 2)).Ok);
        plain.Run(60); frozen.Run(60);
        Assert.True(frozen.World.Player.Fiscal!.Drift < 0.95, $"drift {frozen.World.Player.Fiscal!.Drift}");
        Assert.Equal(1.0, plain.World.Player.Fiscal!.Drift);
        Assert.True(frozen.World.Player.TaxRate[(int)Tax.Income] > plain.World.Player.TaxRate[(int)Tax.Income] * 1.03);
        Assert.True(frozen.World.Player.TaxRate[(int)Tax.Payroll] > plain.World.Player.TaxRate[(int)Tax.Payroll]);
        Assert.True(frozen.World.Player.Debt < plain.World.Player.Debt);
    }

    [Fact]
    public void Corporation_tax_cut_lowers_revenue_now_and_raises_investment()
    {
        var a = FiscalFixture.Game(true); var b = FiscalFixture.Game(true);
        Assert.True(b.Execute(Command.SetFiscal("GBR", "Corp.Main", 0.19)).Ok);
        Assert.True(b.World.Player.TaxRate[(int)Tax.Corporate] < a.World.Player.TaxRate[(int)Tax.Corporate]);
        a.Run(36); b.Run(36);
        Assert.True(b.World.Player.InvPriv > a.World.Player.InvPriv * 1.01);
        Assert.True(b.World.Player.Revenue < a.World.Player.Revenue);
    }

    [Fact]
    public void Vat_category_treatment_moves_the_effective_rate_and_prices()
    {
        var a = FiscalFixture.Game(true); var b = FiscalFixture.Game(true);
        double r0 = a.World.Player.TaxRate[(int)Tax.Consumption];
        Assert.True(b.Execute(Command.SetFiscal("GBR", "Vat.Food", 2)).Ok);          // put VAT on food
        double r1 = b.World.Player.TaxRate[(int)Tax.Consumption];
        Assert.True(r1 > r0 * 1.04);
        a.Run(12); b.Run(12);
        Assert.True(b.World.Player.PriceLevel > a.World.Player.PriceLevel);
    }

    [Fact]
    public void Raising_the_pension_age_saves_money_and_raises_labour_supply_but_costs_approval()
    {
        var a = FiscalFixture.Game(true); var b = FiscalFixture.Game(true);
        Assert.True(b.Execute(Command.SetFiscal("GBR", "Pen.Age", 68)).Ok);
        a.Run(48); b.Run(48);
        var fa = a.World.Player.Fiscal!; var fb = b.World.Player.Fiscal!;
        Assert.True(fb.BenRatio[(int)Ben.Pension] < 0.88);                              // two extra years ≈ -14% of pension spending
        Assert.True(fb.Mix < fa.Mix);
        Assert.True(b.World.Player.Potential > a.World.Player.Potential);
        Assert.True(b.World.Player.Approval < a.World.Player.Approval);
        Assert.True(b.World.Player.Spending < a.World.Player.Spending);
    }

    [Fact]
    public void Generous_unemployment_benefit_raises_the_natural_rate_but_props_up_consumption()
    {
        var a = FiscalFixture.Game(true); var b = FiscalFixture.Game(true);
        var f = b.World.Player.Fiscal!;
        Assert.True(b.Execute(Command.SetFiscal("GBR", "Une.Level", f.Get("Une.Level") * 1.8)).Ok);
        a.Run(36); b.Run(36);
        Assert.True(b.World.Player.NairU > a.World.Player.NairU + 0.002);
        var ca = a.World.Player; var cb = b.World.Player;
        Assert.True(b.World.Player.Fiscal!.Mix > 1.0);
        Assert.True(FiscalEngine.SocialReal(cb) / cb.Potential > FiscalEngine.SocialReal(ca) / ca.Potential);
    }

    [Fact]
    public void A_steeper_means_test_taper_lowers_the_take_up_of_work_and_cuts_spending()
    {
        var a = FiscalFixture.Game(true); var b = FiscalFixture.Game(true);
        Assert.True(b.Execute(Command.SetFiscal("GBR", "Mt.Taper", 0.80)).Ok);
        var fb = b.World.Player.Fiscal!;
        Assert.True(fb.BenRatio[(int)Ben.MeansTested] < 1.0);
        Assert.True(fb.LabourTarget < 1.0);
        var cv = TaxCodeEngine.Curve(fb, fb.P, fb.Drift, 3.0, 301);
        Assert.True(cv.TotalMarginal[(int)(0.25 / 3.0 * 300)] >= 0.79, "withdrawal rate shows in the marginal-rate curve");
    }

    [Fact]
    public void Who_receives_the_money_matters_for_demand_and_inequality()
    {
        var sim = FiscalFixture.Game(true); var c = sim.World.Player; var f = c.Fiscal!;
        // equal-cost cuts (0.2% of GDP) to different strands: the strand the poor rely on takes more out of consumption
        double Cons(Ben k)
        {
            var g = FiscalFixture.Game(true); var fc = g.World.Player.Fiscal!;
            string key = k switch { Ben.Pension => "Pen.Level", Ben.Unemployment => "Une.Level", Ben.Housing => "Hou.Level", Ben.Disability => "Dis.Level", Ben.Child => "Chi.Level", _ => "Mt.Level" };
            // find the cut that saves exactly 0.2% of GDP, whatever the strand's size and shape
            double lo = 0, hi = 0.99, cut = 0.5;
            for (int it = 0; it < 40; it++)
            {
                cut = 0.5 * (lo + hi);
                var p = new Dictionary<string, double>(fc.P) { [key] = fc.Get(key) * (1 - cut) };
                double saving = -FiscalDraft.Evaluate(g.World.Player, p).SpendGdp;
                if (saving < 0.002) lo = cut; else hi = cut;
            }
            Assert.True(g.Execute(Command.SetFiscal("GBR", key, fc.Get(key) * (1 - cut))).Ok);
            g.Run(2); var h = FiscalFixture.Game(true); h.Run(2);
            return (g.World.Player.Cons - h.World.Player.Cons) / h.World.Player.Cons;
        }
        double une = Cons(Ben.Unemployment), mt = Cons(Ben.MeansTested), hou = Cons(Ben.Housing), pen = Cons(Ben.Pension);
        // the strands the poor rely on cut demand at least a quarter more than pensions do (pensioners save more of each pound)
        Assert.True(une < pen * 1.25 && mt < pen * 1.25 && hou < pen * 1.25, $"unemployment {une:P3}, means-tested {mt:P3}, housing {hou:P3}, pension {pen:P3}");
        Assert.True(une < -0.0005);
        // inequality: cutting the means-tested award and housing support widens the Gini; a higher top rate narrows it
        var gb = FiscalFixture.Game(true); var gc = FiscalFixture.Game(true);
        gb.Execute(Command.SetFiscal("GBR", "Hou.Level", 0.02)); gc.Execute(Command.SetBands("GBR", new List<(double, double)> { (0, 0.20), (1.077, 0.40), (3.575, 0.55) }));
        Assert.True(gb.World.Player.Fiscal!.GiniDelta > 0);
        Assert.True(gc.World.Player.Fiscal!.GiniDelta < 0);
    }

    [Fact]
    public void Draft_estimate_matches_what_happens_once_the_change_is_made()
    {
        var g = FiscalFixture.Game(true); var c = g.World.Player; var f = c.Fiscal!;
        var cmd = Command.SetFiscal("GBR", "Inc.Allow", f.Get("Inc.Allow") * 1.5);
        var est = FiscalDraft.Evaluate(c, FiscalDraft.Apply(c, new[] { cmd }));
        Assert.True(est.Changed && est.RevenueGdp < 0 && est.PcCost > 0);
        double rev0 = TaxRevenueReal(c);
        g.Execute(cmd);
        double rev1 = TaxRevenueReal(c);
        Assert.Equal(est.RevenueGdp, (rev1 - rev0) / c.Gdp, 6);
        Assert.True(Math.Abs(est.DecileNet[0]) < 1e-9);                         // the poorest earn below the allowance and gain nothing from it
        Assert.True(est.DecileNet[4] > est.DecileNet[9] && est.DecileNet[9] > 0);   // the middle gains proportionally more than the top
        Assert.False(FiscalDraft.Evaluate(c, c.Fiscal!.P).Changed);
    }

    static double TaxRevenueReal(CountryState c)
    {
        double s = 0; for (int t = 0; t < Dim.Taxes; t++) s += FiscalEngine.TaxRevenueReal(c, (Tax)t, c.Gdp, c.Cons, c.Imports); return s;
    }

    [Fact]
    public void Saves_round_trip_and_old_saves_gain_a_code_without_jumps()
    {
        var g = FiscalFixture.Game(true);
        g.Execute(Command.SetFiscal("GBR", "Corp.Main", 0.22)); g.Run(6);
        var copy = Simulation.Load(g.Save());
        Assert.Equal(g.StateHash(), copy.StateHash());
        Assert.NotNull(copy.World.Player.Fiscal);
        g.Run(18); copy.Run(18);
        Assert.Equal(g.StateHash(), copy.StateHash());
        Assert.Equal(g.World.Player.TaxRate, copy.World.Player.TaxRate);

        // a version-1 save has no code: loading builds the default one and keeps the tax rates the player had
        var old = FiscalFixture.Game(false); old.Execute(Command.SetTax("GBR", Tax.Income, old.World.Player.TaxRate[(int)Tax.Income] + 0.015)); old.Run(6);
        old.World.Version = 1;
        double rate = old.World.Player.TaxRate[(int)Tax.Income];
        var json = old.Save();
        Assert.Equal(rate, Simulation.Load(json).World.Player.TaxRate[(int)Tax.Income]);   // identical on load whatever the catalogue holds
    }

    static void StagePlan(Simulation g)
    {
        g.World.Player.PoliticalCapital = 100;
        g.Stage(Command.SetFiscal("GBR", "Inc.Allow", g.World.Player.Fiscal!.Get("Inc.Allow") * 1.1));
        g.Stage(Command.SetBands("GBR", new[] { (0.0, 0.20), (0.9, 0.32), (3.0, 0.42), (6.0, 0.45) }));
        g.Stage(Command.SetFiscal("GBR", "Une.Level", g.World.Player.Fiscal.Get("Une.Level") * 1.2));
        g.Stage(Command.SetFiscal("GBR", "Pen.Age", 67));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_staged_fiscal_plan_is_deterministic_across_saves_and_fresh_runs(bool stochastic)
    {
        var a = FiscalFixture.Game(true, stochastic: stochastic); a.Run(4);
        StagePlan(a);
        Assert.Equal(4, a.Plan.Count);
        var before = a.World.Player.Fiscal!.Get("Une.Level");
        var b = Simulation.Load(a.Save());                          // saved with the plan still queued
        Assert.Equal(4, b.Plan.Count);
        Assert.Equal(before, a.World.Player.Fiscal.Get("Une.Level"));   // nothing applied before the turn is played
        a.Run(20); b.Run(20);
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(4, FiscalParams.Bands(a.World.Player.Fiscal.P).Count);
        Assert.Equal(67, a.World.Player.Fiscal.Get("Pen.Age"));

        var c = FiscalFixture.Game(true, stochastic: stochastic); c.Run(4);     // the same actions in a fresh run
        StagePlan(c); c.Run(20);
        Assert.Equal(a.StateHash(), c.StateHash());
    }

    [Fact]
    public void Staged_fiscal_edits_replace_by_key_and_the_band_table_is_one_entry()
    {
        var g = FiscalFixture.Game(true); g.World.Player.PoliticalCapital = 100;
        g.Stage(Command.SetFiscal("GBR", "Corp.Main", 0.20));
        var r = g.Stage(Command.SetFiscal("GBR", "Corp.Main", 0.18));
        Assert.True(r.Replaced); Assert.Single(g.Plan);
        g.Stage(Command.SetBands("GBR", new[] { (0.0, 0.19), (1.0, 0.35) }));
        g.Stage(Command.SetBands("GBR", new[] { (0.0, 0.19), (1.2, 0.35), (4.0, 0.45) }));
        Assert.Equal(2, g.Plan.Count);
        Assert.Equal(3, FiscalDraft.Apply(g.World.Player, g.Plan).Keys.Count(k => k.StartsWith("Inc.B") && k.EndsWith(".Rate")));
        Assert.True(g.Unstage("fiscal:" + FiscalParams.BandsKey));
        Assert.Single(g.Plan);
    }

    [Fact]
    public void A_legacy_slider_move_and_a_structural_edit_in_the_same_turn_both_land()
    {
        var g = FiscalFixture.Game(true); var c = g.World.Player; g.World.Player.PoliticalCapital = 100;
        double t0 = c.TaxRate[(int)Tax.Income];
        g.Stage(Command.SetTax("GBR", Tax.Income, t0 + 0.01));
        g.Stage(Command.SetFiscal("GBR", "Inc.Allow", c.Fiscal!.Get("Inc.Allow") * 0.8));
        g.Tick();
        Assert.True(c.TaxRate[(int)Tax.Income] > t0 + 0.01 + 1e-4, "a lower allowance adds to the slider's rise");
    }
}
