using Sim.Core.Util;

namespace Sim.Core.Model;

public sealed class HistoryPoint
{
    public int Month;
    public double Gdp, GdpUsdBn, Growth, Inflation, Unemployment, DebtToGdp, DeficitToGdp, PolicyRate, Yield10, Fx, Approval, Gini, CaToGdp, EmissionsMt, Stability, Pop;
}

/// <summary>Everything needed to save, load and replay a game.</summary>
public sealed class World
{
    public int Version = 1;
    public ulong Seed;
    public int StartYear = 2024;
    public int Month;                       // months since start
    public string PlayerId = "";
    public bool Stochastic = true;
    public bool RecordHistory = true;
    public List<CountryState> Countries = new();
    public Rng[] CountryRng = Array.Empty<Rng>();
    public Rng WorldRng = new();
    public GlobalState Global = new();
    public double RosterCoverage = 0.85;    // share of world emissions represented by simulated countries
    public double WorldCycle;               // OU deviation in world demand growth
    public Dictionary<string, List<HistoryPoint>> History = new();
    public List<LogEntry> Log = new();

    public int Year => StartYear + Month / 12;
    public int MonthOfYear => Month % 12 + 1;
    public string DateString => $"{Year}-{MonthOfYear:D2}";

    public CountryState? Find(string id) => Countries.FirstOrDefault(c => c.Id == id);
    public CountryState Player => Find(PlayerId) ?? throw new InvalidOperationException("no player country");
}

public sealed class LogEntry
{
    public int Month;
    public string Country = "";
    public string Kind = "";    // news | policy | event | crisis | advisor
    public string Text = "";
}
