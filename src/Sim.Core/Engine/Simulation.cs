using System.Text.Json;
using Sim.Core.Data;
using Sim.Core.Model;
using Sim.Core.Events;
using Sim.Core.Policy;
using Sim.Core.Util;

namespace Sim.Core.Engine;

/// <summary>Owns a <see cref="World"/> and advances it one month at a time. Deterministic given seed and command log.</summary>
public sealed class Simulation
{
    public const double Dt = 1.0 / 12.0;
    public World World { get; private set; }

    public Simulation(World w) { World = w; }

    public static Simulation New(string playerId, ulong seed = 1, bool stochastic = true, IEnumerable<CountryData>? roster = null, int startYear = 2024, bool detailedTax = true)
    {
        var data = (roster ?? CountryLoader.LoadEmbedded()).ToList();
        var w = new World { Seed = seed, PlayerId = playerId, Stochastic = stochastic, StartYear = startYear };
        foreach (var d in data) w.Countries.Add(Calibrator.Build(d));
        w.CountryRng = w.Countries.Select(c => new Rng(Rng.Mix(seed, c.Id))).ToArray();
        w.WorldRng = new Rng(Rng.Mix(seed, "world"));
        if (w.Find(playerId) == null) throw new ArgumentException($"Unknown country {playerId}", nameof(playerId));
        Politics.Init(w);
        WorldEngine.RebuildTrade(w);
        if (detailedTax) w.Player.Fiscal = TaxCodeCatalog.Build(w.Player);
        var sim = new Simulation(w);
        sim.RecordNow();
        return sim;
    }

    public void Tick()
    {
        var w = World;
        ProcessQueue();
        GlobalEngine.Step(w, Dt);
        WorldEngine.Step(w);
        foreach (var c in w.Countries) PolicyEngine.Step(w, c);
        for (int i = 0; i < w.Countries.Count; i++)
        {
            var c = w.Countries[i];
            if (c.Id != w.PlayerId || c.Autopilot) PolicyAgent.Step(c, w.Global, w.CountryRng[i], w);
        }
        for (int i = 0; i < w.Countries.Count; i++)
            MacroEngine.Step(w.Countries[i], w.Global, Dt, w.CountryRng[i], w.Stochastic, w);
        GlobalEngine.Climate(w, Dt);
        w.Month++;
        if (w.Events) { EventEngine.Step(w); Politics.Step(w); }
        if (w.Month % 3 == 0)
        {
            if (w.RecordHistory) Record();
            if (w.Advisors) Advisors.Post(w, w.Player);
        }
    }

    // ---- the staged turn plan ----
    // A turn is one monthly tick. Player actions are staged in World.Queue, priced and validated by dry-run, and applied in staging order
    // at the very start of the next tick. Until then nothing visible changes: political capital, the news log and adviser notes only move
    // when the turn is played. Execute() remains the immediate path for tests, the CLI, the balance harness and AI governments.

    /// <summary>Identity of a command inside the plan: staging another command with the same key replaces the earlier one.</summary>
    public static string KeyOf(Command c) => c.Type switch
    {
        "tax" or "budget" or "subsidy" or "tradedeal" or "alliance" or "sanction" or "tariff" or "aid" or "fiscal" => c.Type + ":" + c.Id,
        "enact" or "repeal" => "policy:" + c.Id,
        "project" => "project:" + c.Id,
        "cancelproject" => c.Id != "" ? "cancel:" + c.Id : "cancel#" + (int)c.Value,
        _ => c.Type,
    };

    public IReadOnlyList<Command> Plan => World.Queue;

    /// <summary>Stage a player command for the next tick. Returns the dry-run price/validity; <c>Staged</c> says whether the plan changed.</summary>
    public CommandResult Stage(Command cmd)
    {
        var w = World; string key = KeyOf(cmd);
        if (cmd.Country != w.PlayerId) return CommandResult.Fail("Only the player's country can stage commands");

        // enacting what is queued for repeal (or the reverse) cancels the pair instead of racing the vote
        string? opposite = cmd.Type == "repeal" ? "enact" : cmd.Type == "enact" ? "repeal" : null;
        if (opposite != null)
        {
            int oi = w.Queue.FindIndex(q => q.Type == opposite && q.Id == cmd.Id);
            if (oi >= 0)
            {
                w.Queue.RemoveAt(oi);
                return new CommandResult { Ok = true, Unstaged = true, Message = cmd.Type == "repeal" ? "Proposal withdrawn" : "Repeal withdrawn; the policy stays in force" };
            }
        }

        var dry = CommandProcessor.Apply(w, cmd, dryRun: true);
        int i = w.Queue.FindIndex(q => KeyOf(q) == key);
        if (dry.NoOp)
        {
            if (i >= 0) { w.Queue.RemoveAt(i); dry.Unstaged = true; dry.Message = "Back to the current setting; removed from the plan"; }
            return dry;
        }
        if (!dry.Ok) return dry;
        if (cmd.Type == "project" && i < 0 && w.Player.Projects.Count(p => !p.Done) + w.Queue.Count(q => q.Type == "project") >= CommandProcessor.MaxConcurrentProjects)
            return CommandResult.Fail("Delivery capacity reached: the staged starts already fill every slot", dry.PcCost);
        double others = PendingPcCost(key);
        if (others + dry.PcCost > w.Player.PoliticalCapital + 1e-9)
            return CommandResult.Fail($"The plan would need {others + dry.PcCost:0} political capital and you have {w.Player.PoliticalCapital:0}. Remove something first", dry.PcCost);
        if (i >= 0) { w.Queue[i] = cmd; dry.Replaced = true; } else w.Queue.Add(cmd);
        dry.Staged = true;
        return dry;
    }

    /// <summary>Compatibility alias for <see cref="Stage"/>.</summary>
    public CommandResult Submit(Command cmd) => Stage(cmd);

    public bool Unstage(string key)
    {
        int i = World.Queue.FindIndex(q => KeyOf(q) == key);
        if (i < 0) return false;
        World.Queue.RemoveAt(i); return true;
    }

    public void ClearPlan() => World.Queue.Clear();

    public Command? Staged(string key) => World.Queue.FirstOrDefault(q => KeyOf(q) == key);

    /// <summary>Political capital the plan will spend when the turn is played (priced one command at a time against the current state).</summary>
    public double PendingPcCost(string? exceptKey = null)
    {
        double sum = 0;
        foreach (var q in World.Queue)
        {
            if (exceptKey != null && KeyOf(q) == exceptKey) continue;
            sum += CommandProcessor.Apply(World, q, dryRun: true).PcCost;
        }
        return sum;
    }

    public sealed class PlanItem
    {
        public Command Cmd = new(); public string Key = "", Label = "";
        public double PcCost; public bool Ok = true, Affordable = true; public string Note = "";
    }

    /// <summary>The plan in execution order with its price, whether each item is still valid, and whether the capital stretches that far.</summary>
    public List<PlanItem> PlanItems()
    {
        var res = new List<PlanItem>(); double running = 0, have = World.Player.PoliticalCapital;
        foreach (var q in World.Queue)
        {
            var dry = CommandProcessor.Apply(World, q, dryRun: true);
            running += dry.PcCost;
            res.Add(new PlanItem { Cmd = q, Key = KeyOf(q), Label = PlanText.Describe(World, q), PcCost = dry.PcCost, Ok = dry.Ok && !dry.NoOp, Affordable = running <= have + 1e-9, Note = dry.Ok && !dry.NoOp ? "" : dry.Message });
        }
        return res;
    }

    /// <summary>The plan with <paramref name="extra"/> layered on top (replace-by-key), for previews that include both the plan and an unstaged draft.</summary>
    public List<Command> Merged(IEnumerable<Command> extra)
    {
        var list = World.Queue.ToList();
        foreach (var e in extra)
        {
            int i = list.FindIndex(q => KeyOf(q) == KeyOf(e));
            if (i >= 0) list[i] = e; else list.Add(e);
        }
        return list;
    }

    /// <summary>Apply a command immediately (used by UI for instant feedback and by tests).</summary>
    public CommandResult Execute(Command cmd)
    {
        var r = CommandProcessor.Apply(World, cmd);
        World.CommandLog.Add(new LoggedCommand { Month = World.Month, Cmd = cmd, Ok = r.Ok, Message = r.Message });
        World.Log.Add(new LogEntry { Month = World.Month, Country = cmd.Country, Kind = "policy", Text = r.Message });
        return r;
    }

    void ProcessQueue()
    {
        if (World.Queue.Count == 0) return;
        var q = World.Queue.ToList(); World.Queue.Clear();
        foreach (var cmd in q) Execute(cmd);
    }

    /// <summary>Resolve a pending player decision popup.</summary>
    public bool Resolve(int decisionId, int choice) => EventEngine.ResolveDecision(World, decisionId, choice);

    public void Run(int months) { for (int i = 0; i < months && !World.GameOver; i++) Tick(); }

    public void RecordNow() => Record();

    void Record()
    {
        foreach (var c in World.Countries)
        {
            if (!World.History.TryGetValue(c.Id, out var list)) World.History[c.Id] = list = new List<HistoryPoint>();
            var hp = Snapshot(c, World.Month);
            if (c.Id == World.PlayerId) hp.SectorVa = (double[])c.SectorVa.Clone();
            list.Add(hp);
        }
    }

    public static HistoryPoint Snapshot(CountryState c, int month) => new()
    {
        Month = month, Gdp = c.Gdp, GdpUsdBn = c.GdpUsdBn, Growth = c.GdpGrowth, Inflation = c.Inflation, Unemployment = c.Unemp,
        DebtToGdp = c.DebtToGdp, DeficitToGdp = c.DeficitToGdp, PolicyRate = c.PolicyRate, Yield10 = c.Yield10, Fx = c.Fx,
        Approval = c.Approval, Gini = c.Gini, CaToGdp = c.CaToGdp, EmissionsMt = c.EmissionsMt, Stability = c.Stability, Pop = c.Pop,
    };

    // ---- persistence ----
    static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = false, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };

    public string Save() => JsonSerializer.Serialize(World, Json);
    public static Simulation Load(string json)
    {
        var w = JsonSerializer.Deserialize<World>(json, Json) ?? throw new InvalidDataException("bad save");
        Migrations.Upgrade(w);
        return new Simulation(w);
    }

    /// <summary>Stable fingerprint of key state for determinism / regression tests.</summary>
    public string StateHash()
    {
        ulong h = 1469598103934665603UL;
        void Mixd(double v) { h ^= BitConverter.DoubleToUInt64Bits(v); h *= 1099511628211UL; }
        foreach (var c in World.Countries)
        {
            Mixd(c.Gdp); Mixd(c.Debt); Mixd(c.PriceLevel); Mixd(c.PolicyRate); Mixd(c.Fx); Mixd(c.Unemp); Mixd(c.Approval); Mixd(c.Pop);
            foreach (var k in c.K) Mixd(k);
        }
        Mixd(World.Global.OilIdx);
        return h.ToString("x16");
    }
}
