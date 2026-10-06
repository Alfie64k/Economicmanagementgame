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

    public static Simulation New(string playerId, ulong seed = 1, bool stochastic = true, IEnumerable<CountryData>? roster = null, int startYear = 2024)
    {
        var data = (roster ?? CountryLoader.LoadEmbedded()).ToList();
        var w = new World { Seed = seed, PlayerId = playerId, Stochastic = stochastic, StartYear = startYear };
        foreach (var d in data) w.Countries.Add(Calibrator.Build(d));
        w.CountryRng = w.Countries.Select(c => new Rng(Rng.Mix(seed, c.Id))).ToArray();
        w.WorldRng = new Rng(Rng.Mix(seed, "world"));
        if (w.Find(playerId) == null) throw new ArgumentException($"Unknown country {playerId}", nameof(playerId));
        Politics.Init(w);
        return new Simulation(w);
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

    /// <summary>Queue a player command; it is applied at the next tick boundary. Returns the dry-run price/validity now.</summary>
    public CommandResult Submit(Command cmd)
    {
        var dry = CommandProcessor.Apply(World, cmd, dryRun: true);
        if (dry.Ok) World.Queue.Add(cmd);
        return dry;
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

    void Record()
    {
        foreach (var c in World.Countries)
        {
            if (!World.History.TryGetValue(c.Id, out var list)) World.History[c.Id] = list = new List<HistoryPoint>();
            list.Add(Snapshot(c, World.Month));
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
    public static Simulation Load(string json) =>
        new(JsonSerializer.Deserialize<World>(json, Json) ?? throw new InvalidDataException("bad save"));

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
