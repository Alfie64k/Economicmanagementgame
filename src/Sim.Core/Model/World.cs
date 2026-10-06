using Sim.Core.Util;

namespace Sim.Core.Model;

public sealed class HistoryPoint
{
    public int Month;
    public double[] SectorVa = Array.Empty<double>();
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
    public bool Advisors = true;
    public bool Events = true;
    public List<CountryState> Countries = new();
    public Rng[] CountryRng = Array.Empty<Rng>();
    public Rng WorldRng = new();
    public GlobalState Global = new();
    public double RosterCoverage = 0.85;    // share of world emissions represented by simulated countries
    public double WorldCycle;               // OU deviation in world demand growth
    public Dictionary<string, List<HistoryPoint>> History = new();
    public List<LogEntry> Log = new();
    public List<LoggedCommand> CommandLog = new();
    public Dictionary<string, int> AdvisorLast = new();
    public TradeMatrix Trade = new();
    public List<ScheduledEvent> Scheduled = new();
    public List<PendingDecision> Decisions = new();
    public List<EventRecord> EventHistory = new();
    public int NextDecisionId = 1;
    public Difficulty Difficulty = Difficulty.Normal;
    public double PcRegenMult = 1.0, EventSeverity = 1.0, EventFrequency = 1.0;
    public string ScenarioId = "";
    public bool GameOver, GameOverOnLoss = false;
    public string GameOverReason = "";
    public Dictionary<string, Relation> Relations = new();   // keyed "A>B" (directional)
    public List<Command> Queue = new();      // the staged turn plan: applied, in order, at the start of the next tick

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

public sealed class LoggedCommand
{
    public int Month;
    public Command Cmd = new();
    public bool Ok;
    public string Message = "";
}

/// <summary>Serialisable player action. All state changes by the player go through these so games can be replayed from seed + log.</summary>
public sealed class Command
{
    public string Type = "";
    public string Country = "";
    public string Id = "";
    public double Value, Value2;

    public static Command SetTax(string country, Tax t, double rate) => new() { Type = "tax", Country = country, Id = t.ToString(), Value = rate };
    public static Command SetBudget(string country, BudgetLine l, double share) => new() { Type = "budget", Country = country, Id = l.ToString(), Value = share };
    public static Command SetRate(string country, bool manual, double rate) => new() { Type = "rate", Country = country, Id = manual ? "Manual" : "Auto", Value = rate };
    public static Command SetMinWage(string country, double ratio) => new() { Type = "minwage", Country = country, Value = ratio };
    public static Command SetFxRegime(string country, FxRegime r) => new() { Type = "fxregime", Country = country, Id = r.ToString() };
    public static Command Enact(string country, string policyId) => new() { Type = "enact", Country = country, Id = policyId };
    public static Command Repeal(string country, string policyId) => new() { Type = "repeal", Country = country, Id = policyId };
    public static Command StartProject(string country, string projectId, double scale = 1.0) => new() { Type = "project", Country = country, Id = projectId, Value = scale };
    public static Command CancelProject(string country, int index) => new() { Type = "cancelproject", Country = country, Value = index };
    public static Command CancelProject(string country, string projectId) => new() { Type = "cancelproject", Country = country, Id = projectId };
    public static Command SetSubsidy(string country, Sector s, double share) => new() { Type = "subsidy", Country = country, Id = s.ToString(), Value = share };
    public static Command SetCarbon(string country, double price) => new() { Type = "carbon", Country = country, Value = price };
    public static Command TradeDeal(string country, string partner) => new() { Type = "tradedeal", Country = country, Id = partner };
    public static Command Tariff(string country, string partner, double extra) => new() { Type = "tariff", Country = country, Id = partner, Value = extra };
    public static Command Sanction(string country, string target, bool on) => new() { Type = "sanction", Country = country, Id = target, Value = on ? 1 : 0 };
    public static Command Alliance(string country, string partner) => new() { Type = "alliance", Country = country, Id = partner };
    public static Command Aid(string country, string recipient, double shareOfGdp) => new() { Type = "aid", Country = country, Id = recipient, Value = shareOfGdp };
    public static Command Autopilot(string country, bool on) => new() { Type = "autopilot", Country = country, Id = on ? "on" : "off" };
}

/// <summary>Bilateral trade intensity weights (gravity model). W[i][j] = share of i's exports sold to j; the remainder goes to the rest of the world.</summary>
public sealed class TradeMatrix
{
    public double[][] W = Array.Empty<double[]>();
    public double[] RowShare = Array.Empty<double>();
    public double[] CrisisScore = Array.Empty<double>();
}

/// <summary>Directional relation of country A toward country B. Deals and alliances are written to both directions.</summary>
public sealed class Relation
{
    public bool Deal, Alliance, Sanction;
    public int DealStart;
    public double ExtraTariff;   // additional tariff A levies on B's goods
}

public enum Difficulty { Sandbox, Easy, Normal, Hard }

public sealed class ScheduledEvent { public int Month; public string EventId = "", Country = ""; }

public sealed class PendingDecision
{
    public int Id, Month, Deadline;
    public string Country = "", EventId = "", Title = "", Text = "";
    public List<string> Labels = new();
    public List<double> Costs = new();
    public int DefaultChoice;
}

public sealed class EventRecord { public int Month; public string Country = "", EventId = "", Name = "", Choice = ""; }
