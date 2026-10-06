using Sim.Core.Engine;
using Sim.Core.Model;
using Sim.Core.Policy;

namespace Sim.Core.Scoring;

public sealed class Strategy
{
    public string Name = "";
    public Action<Simulation, int> Yearly = (_, _) => { };
}

public sealed class RunResult
{
    public string Strategy = "", Country = "", Archetype = "";
    public ulong Seed;
    public double Score;
    public ScoreCard Card = new();
    public bool Runaway, GameOver;
    public double GdppcRatio, Inflation, Debt;
}

/// <summary>Scripted-strategy self-play used to catch dominant strategies and runaway states.</summary>
public static class Balance
{
    static void Bud(Simulation s, BudgetLine l, double delta, double cap = 0.6)
    {
        var c = s.World.Player;
        s.Execute(Command.SetBudget(c.Id, l, Math.Min(cap, Math.Max(0, c.Budget[(int)l] + delta))));
    }

    static void Try(Simulation s, Command c) => s.Execute(c);

    public static readonly Strategy[] Strategies =
    {
        new() { Name = "laissez-faire" },
        new()
        {
            Name = "austerity", Yearly = (s, y) =>
            {
                var c = s.World.Player; double ceil = Math.Max(0.6, c.DebtGdp0 * 0.9);
                if (c.DebtToGdp > ceil || c.DeficitToGdp > 0.03)
                {
                    Try(s, Command.SetTax(c.Id, Tax.Consumption, c.TaxRate[(int)Tax.Consumption] * 1.04));
                    Bud(s, BudgetLine.Admin, -c.Budget[(int)BudgetLine.Admin] * 0.04);
                }
            }
        },
        new()
        {
            Name = "stimulus", Yearly = (s, y) =>
            {
                var c = s.World.Player;
                if (y < 8 && c.DebtToGdp < c.DebtGdp0 * 1.3 + 0.2)
                {
                    Bud(s, BudgetLine.Infrastructure, 0.002); Bud(s, BudgetLine.Education, 0.001); Bud(s, BudgetLine.Social, 0.001);
                }
            }
        },
        new()
        {
            Name = "industrial", Yearly = (s, y) =>
            {
                var c = s.World.Player;
                string[] pol = { "fta_network", "industrial_manufacturing", "sez", "rnd_tax_credits", "fdi_incentives" };
                if (y < pol.Length) Try(s, Command.Enact(c.Id, pol[y]));
                string[] proj = { "ports", "broadband", "motorways", "grid", "research_campuses" };
                if (y >= 1 && y % 2 == 1 && y / 2 < proj.Length && c.DebtToGdp < c.DebtGdp0 + 0.4) Try(s, Command.StartProject(c.Id, proj[y / 2]));
            }
        },
        new()
        {
            Name = "green", Yearly = (s, y) =>
            {
                var c = s.World.Player;
                string[] pol = { "carbon_tax_50", "renewables_subsidy", "industrial_green" };
                if (y < pol.Length) Try(s, Command.Enact(c.Id, pol[y]));
                string[] proj = { "wind_solar", "grid", "nuclear_plants" };
                if (y >= 1 && y % 2 == 1 && y / 2 < proj.Length && c.DebtToGdp < c.DebtGdp0 + 0.4) Try(s, Command.StartProject(c.Id, proj[y / 2]));
            }
        },
        new()
        {
            Name = "balanced", Yearly = (s, y) =>
            {
                var c = s.World.Player;
                string[] pol = { "fiscal_rule", "competition_policy", "cb_independence", "apprenticeships" };
                if (y < pol.Length) Try(s, Command.Enact(c.Id, pol[y]));
                if (y < 8) { Bud(s, BudgetLine.Education, 0.0008, c.Budget0[(int)BudgetLine.Education] + 0.012); Bud(s, BudgetLine.RnD, 0.0004, c.Budget0[(int)BudgetLine.RnD] + 0.006); }
                if (c.DeficitToGdp > 0.045 && c.DebtToGdp > c.DebtGdp0) Try(s, Command.SetTax(c.Id, Tax.Consumption, c.TaxRate[(int)Tax.Consumption] * 1.03));
                string[] proj = { "broadband", "schools", "grid", "hospitals" };
                if (y >= 2 && y % 3 == 2 && y / 3 < proj.Length && c.DebtToGdp < c.DebtGdp0 + 0.3) Try(s, Command.StartProject(c.Id, proj[y / 3]));
            }
        },
    };

    public static RunResult Run(Strategy st, string country, ulong seed, int years)
    {
        var sim = Simulation.New(country, seed, true);
        var w = sim.World; w.RecordHistory = false; w.Advisors = false;
        Scenarios.ApplyDifficulty(w, Difficulty.Sandbox);
        var c = w.Player;
        for (int y = 0; y < years; y++)
        {
            st.Yearly(sim, y);
            for (int m = 0; m < 12; m++)
            {
                sim.Tick();
                foreach (var d in w.Decisions.ToList()) sim.Resolve(d.Id, d.DefaultChoice);
            }
        }
        var card = Scorer.Compute(w, c);
        bool runaway = !double.IsFinite(c.Gdp) || c.Inflation > 1.5 || c.DebtToGdp > 5 || c.Gdp <= 0;
        return new RunResult
        {
            Strategy = st.Name, Country = country, Archetype = c.Archetype, Seed = seed, Score = card.Total, Card = card, Runaway = runaway, GameOver = w.GameOver,
            GdppcRatio = (c.Gdp / c.Pop) / (c.Gdp0 / c.Pop0), Inflation = c.Inflation, Debt = c.DebtToGdp,
        };
    }

    public static List<RunResult> Matrix(IEnumerable<string> countries, int seeds, int years, IEnumerable<Strategy>? strategies = null)
    {
        var res = new List<RunResult>();
        foreach (var country in countries)
            foreach (var st in strategies ?? Strategies)
                for (ulong s = 1; s <= (ulong)seeds; s++)
                    res.Add(Run(st, country, s, years));
        return res;
    }

    public sealed class Report
    {
        public Dictionary<string, Dictionary<string, double>> MeanByArchetype = new();   // archetype -> strategy -> mean score
        public Dictionary<string, string> BestByArchetype = new();
        public Dictionary<string, double> Overall = new();
        public double RunawayRate;
        public List<string> Findings = new();
    }

    public static Report Analyse(List<RunResult> r)
    {
        var rep = new Report { RunawayRate = r.Count(x => x.Runaway) / (double)Math.Max(1, r.Count) };
        foreach (var g in r.GroupBy(x => x.Archetype))
        {
            var d = g.GroupBy(x => x.Strategy).ToDictionary(k => k.Key, k => k.Average(x => x.Score));
            rep.MeanByArchetype[g.Key] = d;
            rep.BestByArchetype[g.Key] = d.OrderByDescending(kv => kv.Value).First().Key;
        }
        rep.Overall = r.GroupBy(x => x.Strategy).ToDictionary(k => k.Key, k => k.Average(x => x.Score));

        // dominance: a strategy beating every other by >= 5 points in at least 80% of archetypes
        foreach (var st in rep.Overall.Keys)
        {
            int dom = rep.MeanByArchetype.Count(a => a.Value.All(kv => kv.Key == st || a.Value[st] - kv.Value >= 5));
            if (dom >= 0.8 * rep.MeanByArchetype.Count) rep.Findings.Add($"DOMINANT: '{st}' beats all rivals by 5+ points in {dom}/{rep.MeanByArchetype.Count} archetypes");
        }
        if (rep.BestByArchetype.Values.Distinct().Count() < 2) rep.Findings.Add("NO VARIETY: the same strategy is best everywhere");
        foreach (var kv in rep.MeanByArchetype)
            if (kv.Value["laissez-faire"] >= kv.Value.Max(x => x.Value) - 1.0) rep.Findings.Add($"PASSIVE WINS: doing nothing is within 1 point of the best in '{kv.Key}'");
        if (rep.RunawayRate > 0.03) rep.Findings.Add($"RUNAWAY: {rep.RunawayRate:P1} of runs ended in a runaway state");
        return rep;
    }
}
