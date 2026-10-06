using System.Text.Json;
using Sim.Core.Data;
using Sim.Core.Model;
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
        return new Simulation(w);
    }

    public void Tick()
    {
        var w = World;
        GlobalEngine.Step(w, Dt);
        for (int i = 0; i < w.Countries.Count; i++)
        {
            var c = w.Countries[i];
            if (c.Id != w.PlayerId || c.Autopilot) PolicyAgent.Step(c, w.Global, w.CountryRng[i]);
        }
        for (int i = 0; i < w.Countries.Count; i++)
            MacroEngine.Step(w.Countries[i], w.Global, Dt, w.CountryRng[i], w.Stochastic);
        GlobalEngine.Climate(w, Dt);
        w.Month++;
        if (w.RecordHistory && w.Month % 3 == 0) Record();
    }

    public void Run(int months) { for (int i = 0; i < months; i++) Tick(); }

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
