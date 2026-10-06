using System.Text.Json;
using Sim.Core.Engine;
using Sim.Core.Events;
using Sim.Core.Model;
using Sim.Core.Util;

namespace Sim.Core.Scoring;

public sealed class GoalDef
{
    public string Label { get; set; } = "";
    public string Metric { get; set; } = "";
    public string Op { get; set; } = ">";
    public double Value { get; set; }
    public int ByMonth { get; set; }
    public double Weight { get; set; } = 1;
}
public sealed class HintDef { public int Month { get; set; } public string Text { get; set; } = ""; }
public sealed class ScenarioEvent { public string Id { get; set; } = ""; public int Month { get; set; } public bool Country { get; set; } = true; }

public sealed class ScenarioDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Country { get; set; } = "";
    public string Difficulty { get; set; } = "Normal";
    public string Description { get; set; } = "";
    public int Years { get; set; }
    public string? Unlock { get; set; }
    public List<GoalDef> Goals { get; set; } = new();
    public List<EffectDef> Setup { get; set; } = new();
    public List<ScenarioEvent> Events { get; set; } = new();
    public List<HintDef> Hints { get; set; } = new();
}

public sealed class ScenarioFile { public List<ScenarioDef> Scenarios { get; set; } = new(); }

public sealed class GoalStatus { public GoalDef Goal = new(); public double Current; public bool Met, Expired; }

public sealed class ScenarioResult
{
    public bool Complete, Won;
    public List<GoalStatus> Goals = new();
    public ScoreCard Score = new();
    public string Summary = "";
}

public static class Scenarios
{
    static ScenarioFile? _f;
    static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };
    public static IReadOnlyList<ScenarioDef> All => (_f ??= Load()).Scenarios;
    public static ScenarioDef? Find(string id) => All.FirstOrDefault(s => s.Id == id);

    static ScenarioFile Load()
    {
        using var s = typeof(Scenarios).Assembly.GetManifestResourceStream("data/scenarios.json") ?? throw new InvalidOperationException("scenarios.json missing");
        return JsonSerializer.Deserialize<ScenarioFile>(s, Opts) ?? throw new InvalidDataException("bad scenarios.json");
    }

    public static void ApplyDifficulty(World w, Difficulty d)
    {
        w.Difficulty = d;
        (w.PcRegenMult, w.EventSeverity, w.EventFrequency, w.GameOverOnLoss) = d switch
        {
            Difficulty.Sandbox => (1.0, 1.0, 1.0, false),
            Difficulty.Easy => (1.3, 0.7, 0.8, false),
            Difficulty.Normal => (1.0, 1.0, 1.0, true),
            _ => (0.8, 1.25, 1.3, true),
        };
    }

    public static Simulation Start(ScenarioDef def, ulong seed = 1)
    {
        var sim = Simulation.New(def.Country, seed, stochastic: true);
        var w = sim.World;
        w.ScenarioId = def.Id;
        ApplyDifficulty(w, Enum.Parse<Difficulty>(def.Difficulty));
        EventEngine.ApplyEffects(w, w.Player, def.Setup, def.Name);
        foreach (var e in def.Events)
            w.Scheduled.Add(new ScheduledEvent { Month = e.Month, EventId = e.Id, Country = e.Country ? def.Country : "" });
        return sim;
    }

    public static double Metric(World w, CountryState c, string name) => name switch
    {
        "infl" => c.Inflation, "unemp" => c.Unemp, "debt" => c.DebtToGdp, "deficit" => c.DeficitToGdp, "approval" => c.Approval,
        "gdppc" => (c.Gdp / c.Pop) / (c.Gdp0 / c.Pop0), "emissions" => c.EmissionsMt / c.EmissionsMt0, "renewables" => c.Renewables,
        "gini" => c.Gini, "score" => Scorer.Compute(w, c).Total, "growth" => c.GdpGrowth, "ca" => c.CaToGdp, "default" => c.InDefault ? 1 : 0,
        "nonenergy" => 1 - c.SectorVa[(int)Sector.Energy] / Math.Max(1e-9, c.SectorVa.Sum()),
        _ => throw new ArgumentException("scenario metric " + name),
    };

    public static ScenarioResult Evaluate(World w, ScenarioDef def)
    {
        var c = w.Player;
        int end = def.Years * 12;
        var r = new ScenarioResult { Score = Scorer.Compute(w, c) };
        foreach (var g in def.Goals)
        {
            double v = Metric(w, c, g.Metric);
            bool met = g.Op == "<" ? v < g.Value : v > g.Value;
            int by = g.ByMonth > 0 ? g.ByMonth : end;
            r.Goals.Add(new GoalStatus { Goal = g, Current = v, Met = met, Expired = w.Month >= by });
        }
        r.Complete = w.GameOver || w.Month >= end;
        double wTot = def.Goals.Sum(g => g.Weight), wMet = r.Goals.Where(g => g.Met).Sum(g => g.Goal.Weight);
        r.Won = !w.GameOver && r.Complete && wMet >= wTot - 1e-9;
        r.Summary = w.GameOver ? $"Game over: {w.GameOverReason}" : r.Complete ? (r.Won ? "Objectives achieved." : $"{r.Goals.Count(g => g.Met)} of {r.Goals.Count} objectives met.") : "In progress.";
        return r;
    }

    /// <summary>Hints that are due at the player's current month (each shown once by the UI).</summary>
    public static IEnumerable<HintDef> HintsAt(ScenarioDef def, int month) => def.Hints.Where(h => h.Month == month);
}
