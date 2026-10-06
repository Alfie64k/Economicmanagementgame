using Sim.Core.Data;
using Sim.Core.Engine;
using Sim.Core.Model;
using Xunit;

namespace Sim.Tests;

public class RosterTests
{
    [Fact]
    public void Roster_loads_and_passes_invariants()
    {
        var roster = CountryLoader.LoadEmbedded();
        Assert.True(roster.Count >= 25);
        var problems = roster.SelectMany(CountryLoader.Validate).ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.Equal(roster.Count, roster.Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    public void Every_country_calibrates_to_a_consistent_state()
    {
        foreach (var d in CountryLoader.LoadEmbedded())
        {
            var c = Calibrator.Build(d);
            double demand = c.Cons + c.InvPriv + c.GovCons + c.GovInv + c.Exports - c.Imports;
            Assert.InRange(demand / c.Gdp, 0.999, 1.001);
            Assert.InRange(c.LabourShare.Sum(), 0.999, 1.001);
            Assert.InRange(c.Young + c.Working + c.Old, 0.999, 1.001);
            Assert.InRange(c.SectorVa.Sum() / c.Gdp, 0.999, 1.001);
            Assert.True(c.K.All(k => k > 0), d.Id);
            Assert.True(c.Budget[(int)BudgetLine.Social] > 0, d.Id);
        }
    }
}

public class EngineTests
{
    static Simulation Det(string id) { var s = Simulation.New(id, 1, stochastic: false); s.World.Events = false; return s; }

    [Fact]
    public void Same_seed_gives_identical_hash_and_different_seed_differs()
    {
        var a = Simulation.New("GBR", 7); var b = Simulation.New("GBR", 7); var c = Simulation.New("GBR", 8);
        a.Run(120); b.Run(120); c.Run(120);
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.NotEqual(a.StateHash(), c.StateHash());
    }

    [Fact]
    public void Save_load_roundtrip_preserves_state_and_future()
    {
        var a = Simulation.New("DEU", 3);
        a.Run(36);
        var b = Simulation.Load(a.Save());
        Assert.Equal(a.StateHash(), b.StateHash());
        a.Run(24); b.Run(24);
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void National_accounts_identity_holds_each_month()
    {
        var sim = Det("USA");
        for (int i = 0; i < 120; i++)
        {
            sim.Tick();
            var c = sim.World.Player;
            double y = c.Cons + c.InvPriv + c.GovCons + c.GovInv + c.Exports - c.Imports;
            if (c.Gap > -0.249 && c.Gap < 0.199) Assert.InRange(c.Gdp / y, 0.9999, 1.0001);
        }
    }

    [Theory]
    [InlineData("USA"), InlineData("GBR"), InlineData("DEU"), InlineData("JPN"), InlineData("AUS"), InlineData("KOR")]
    public void Advanced_economies_stay_near_potential_for_a_decade(string id)
    {
        var sim = Det(id);
        for (int m = 0; m < 120; m++)
        {
            sim.Tick();
            Assert.InRange(sim.World.Player.Gap, -0.08, 0.08);
        }
        var c = sim.World.Player;
        Assert.InRange(c.Inflation, -0.02, 0.07);
        Assert.InRange(c.Unemp, 0.01, 0.12);
    }

    [Fact]
    public void Initial_deficit_matches_data()
    {
        var data = CountryLoader.LoadEmbedded().ToDictionary(d => d.Id);
        var sim = Det("GBR");
        sim.Run(2);
        Assert.InRange(sim.World.Player.DeficitToGdp, data["GBR"].Fiscal.Deficit - 0.012, data["GBR"].Fiscal.Deficit + 0.012);
    }

    [Fact]
    public void Tighter_monetary_policy_lowers_inflation_and_output_with_a_lag()
    {
        var baseSim = Det("USA"); var tight = Det("USA");
        var c = tight.World.Player; c.RateMode = RateMode.Manual; c.ManualRate = c.PolicyRate + 0.04;
        baseSim.Run(36); tight.Run(36);
        Assert.True(tight.World.Player.Gap < baseSim.World.Player.Gap - 0.005, "output gap should fall");
        Assert.True(tight.World.Player.Inflation < baseSim.World.Player.Inflation, "inflation should fall");
        // lag: almost no effect after one month
        var b1 = Det("USA"); var t1 = Det("USA");
        var c1 = t1.World.Player; c1.RateMode = RateMode.Manual; c1.ManualRate = c1.PolicyRate + 0.04;
        b1.Run(1); t1.Run(1);
        Assert.InRange(Math.Abs(t1.World.Player.Gdp / b1.World.Player.Gdp - 1), 0, 0.003);
    }

    [Fact]
    public void Fiscal_stimulus_raises_gdp_and_deficit()
    {
        var baseSim = Det("GBR"); var stim = Det("GBR");
        stim.World.Player.Budget[(int)BudgetLine.Infrastructure] += 0.02;
        baseSim.Run(24); stim.Run(24);
        Assert.True(stim.World.Player.Gdp > baseSim.World.Player.Gdp);
        Assert.True(stim.World.Player.Debt > baseSim.World.Player.Debt);
    }

    [Fact]
    public void Higher_taxes_raise_revenue_but_reduce_consumption()
    {
        var baseSim = Det("FRA"); var taxed = Det("FRA");
        taxed.World.Player.TaxRate[(int)Tax.Income] *= 1.15;
        baseSim.Run(12); taxed.Run(12);
        Assert.True(taxed.World.Player.Revenue > baseSim.World.Player.Revenue);
        Assert.True(taxed.World.Player.Cons < baseSim.World.Player.Cons);
    }

    [Fact]
    public void Education_investment_raises_long_run_potential_output()
    {
        var baseSim = Det("IND"); var edu = Det("IND");
        edu.World.Player.Budget[(int)BudgetLine.Education] *= 1.5;
        baseSim.Run(240); edu.Run(240);
        Assert.True(edu.World.Player.HumanCapital > baseSim.World.Player.HumanCapital);
        Assert.True(edu.World.Player.AssetIdx[(int)Asset.Education] > 1.2);
    }

    [Fact]
    public void Demography_ages_rich_countries_and_stays_normalised()
    {
        var sim = Det("JPN");
        double old0 = sim.World.Player.Old;
        sim.Run(240);
        var c = sim.World.Player;
        Assert.True(c.Old > old0);
        Assert.InRange(c.Young + c.Working + c.Old, 0.9999, 1.0001);
    }

    [Theory]
    [InlineData(11), InlineData(22), InlineData(33)]
    public void Whole_world_is_numerically_stable_for_thirty_years(ulong seed)
    {
        var sim = Simulation.New("GBR", seed);
        sim.World.RecordHistory = false;
        sim.Run(360);
        foreach (var c in sim.World.Countries)
        {
            Assert.True(double.IsFinite(c.Gdp) && c.Gdp > 0, $"{c.Id} gdp");
            Assert.True(double.IsFinite(c.Inflation) && c.Inflation < 5.0, $"{c.Id} inflation");
            Assert.True(double.IsFinite(c.Debt) && c.Debt >= 0, $"{c.Id} debt");
            Assert.True(c.DebtToGdp < 6.0, $"{c.Id} debt/gdp {c.DebtToGdp}");
            Assert.True(c.K.All(k => k > 0 && double.IsFinite(k)), $"{c.Id} capital");
            Assert.InRange(c.Unemp, 0.0, 0.5);
        }
    }

    [Fact]
    public void Io_leontief_inverse_inverts_the_technical_matrix()
    {
        var inv = IoTable.LeontiefInverse(); int n = Dim.Sectors;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                double s = 0;
                for (int k = 0; k < n; k++) s += ((i == k ? 1 : 0) - IoTable.A[i, k]) * inv[k * n + j];
                Assert.InRange(s, (i == j ? 1 : 0) - 1e-9, (i == j ? 1 : 0) + 1e-9);
            }
    }
}
