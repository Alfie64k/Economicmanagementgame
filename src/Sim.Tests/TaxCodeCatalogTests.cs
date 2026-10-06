using System.Text.Json.Nodes;
using Sim.Core.Data;
using Sim.Core.Engine;
using Sim.Core.Model;
using Sim.Core.Policy;
using Xunit;

namespace Sim.Tests;

/// <summary>The shipped data file and the code it builds for every country in the roster.</summary>
public class TaxCodeCatalogTests
{
    static readonly string[] Roster = CountryLoader.LoadEmbedded().Select(d => d.Id).ToArray();

    static JsonObject Shipped()
    {
        using var s = typeof(TaxCodeCatalog).Assembly.GetManifestResourceStream("data/taxcodes.json")!;
        return (JsonObject)JsonNode.Parse(s)!;
    }

    [Fact]
    public void Shipped_file_is_sound_for_every_country()
    {
        Assert.True(TaxCodeCatalog.Available);
        var roster = CountryLoader.LoadEmbedded().Select(d => (d.Id, d.Archetype)).ToList();
        Assert.Equal(28, roster.Count);
        Assert.Empty(TaxCodeCatalog.Validate(Shipped(), roster));
    }

    [Fact]
    public void Every_country_starts_on_a_calibrated_code_that_changes_nothing()
    {
        foreach (var id in Roster)
        {
            var s = Simulation.New(id, 1, false); var c = s.World.Player; var f = c.Fiscal;
            Assert.True(f is { Init: true }, id);
            Assert.True(f!.AtStart, id);
            Assert.InRange(Math.Abs(f.Shares.Sum() - 1), 0, 1e-9);
            Assert.InRange(f.Sigma, 0.55, 1.15);
            var bas = TaxCodeEngine.BaseEval(f);
            Assert.InRange(bas.GiniNet, 0.1, 0.65);
            var raw = TaxCodeCatalog.RawCalib(c, bas);
            for (int i = 0; i < 4; i++) Assert.True(raw[i] is >= 0 and < 6, $"{id} calibration {i}: {raw[i]}");
            // the code reproduces the engine's own effective rates at the start
            for (int t = 0; t < Dim.Taxes; t++) Assert.Equal(c.TaxRate0[t], c.TaxRate[t]);
            var cv = TaxCodeEngine.Curve(f, f.P, f.Drift, 6.0, 121);
            Assert.All(cv.TotalMarginal, m => Assert.InRange(m, 0, 1.6));
        }
    }

    [Fact]
    public void Named_countries_carry_their_real_headline_figures()
    {
        double Me(string id, string key) => Simulation.New(id, 1, false).World.Player.Fiscal!.Get(key);
        Assert.Equal(12570.0 / 35000, Me("GBR", "Inc.Allow"), 6);
        Assert.Equal(0.5, Me("GBR", "Inc.TaperRate"), 6);
        Assert.Equal(66, Me("GBR", "Pen.Age"));
        Assert.Equal(0.20, Me("GBR", "Vat.Std"), 6);
        Assert.Equal(67, Me("USA", "Pen.Age"));
        Assert.Equal(0.21, Me("USA", "Corp.Main"), 6);
        Assert.Equal(0.19, Me("DEU", "Vat.Std"), 6);
        Assert.Equal(0.25, Me("FRA", "Corp.Main"), 6);
        Assert.Equal(0.10, Me("JPN", "Vat.Std"), 6);
        Assert.Equal(0.25, Me("CHN", "Corp.Main"), 6);
        Assert.Equal(0.18, Me("IND", "Vat.Std"), 6);
        Assert.Equal(0.34, Me("BRA", "Corp.Main"), 6);
    }

    [Theory]
    [InlineData("GBR")]
    [InlineData("USA")]
    [InlineData("DEU")]
    [InlineData("FRA")]
    [InlineData("IND")]
    public void The_starting_code_is_invisible_to_the_simulation(string id)
    {
        var a = Simulation.New(id, 7, true, detailedTax: false); var b = Simulation.New(id, 7, true);
        Assert.NotNull(b.World.Player.Fiscal);
        for (int i = 0; i < 120; i++) { a.Tick(); b.Tick(); }
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(a.World.Player.TaxRate, b.World.Player.TaxRate);
        Assert.Equal(a.World.Player.Gini, b.World.Player.Gini);
        Assert.Equal(a.World.Player.Approval, b.World.Player.Approval);
    }

    [Fact]
    public void A_zero_income_tax_country_can_introduce_one()
    {
        var s = Simulation.New("ARE", 1, false); s.World.Events = false; var c = s.World.Player; var f = c.Fiscal!;
        double r0 = c.TaxRate[(int)Tax.Income];
        Assert.True(s.Execute(Command.SetBands("ARE", new List<(double, double)> { (0, 0.05), (2.0, 0.10) })).Ok);
        Assert.True(c.TaxRate[(int)Tax.Income] > r0 + 0.01);
        s.Run(12);
        Assert.True(c.Revenue > 0 && c.Debt >= 0);
    }

    [Fact]
    public void Ticking_with_the_code_stays_fast()
    {
        var s = Simulation.New("GBR", 1, false); s.World.Events = false;
        s.Execute(Command.SetFiscal("GBR", "Thr.Index", 1)); s.Execute(Command.SetFiscal("GBR", "Pen.Index", 2));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        s.Run(120);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 2500, $"{sw.ElapsedMilliseconds} ms for 120 ticks with a drifting code");
    }
}
