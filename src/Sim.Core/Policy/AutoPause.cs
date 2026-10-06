using Sim.Core.Model;
using Sim.Core.Scoring;

namespace Sim.Core.Policy;

/// <summary>Things worth stopping the clock for while the game runs on its own.</summary>
[Flags]
public enum PauseTrigger
{
    None = 0,
    AdviserAlert = 1,       // an adviser raises an alert-level warning
    Recession = 2,          // output starts shrinking below potential
    Election = 4,           // an election is three months away, and its result
    PolicyInForce = 8,      // an enacted policy takes effect
    Project = 16,           // a project completes
    Imf = 32,               // IMF programme, default or market-access crisis
    GradeDrop = 64,         // the scorecard grade falls
    Inflation = 128,        // inflation runs 4 points above target
    Event = 256,            // any event touching your country
    Default = AdviserAlert | Recession | Election | PolicyInForce | Project | Imf | GradeDrop,
}

/// <summary>
/// Watches the player's country between ticks and says why the clock should stop. It holds only its own bookkeeping (a position in the log and a few remembered
/// flags), never world state, so it can be recreated after a load. Deterministic: the same game raises the same pauses.
/// </summary>
public sealed class PauseWatcher
{
    public PauseTrigger Enabled = PauseTrigger.Default;
    int _log; string _grade = ""; bool _recession, _inflation; int _electionWarned = -1;
    static readonly string[] Grades = { "F", "D", "C", "B", "A", "S" };

    public PauseWatcher(World w, PauseTrigger enabled = PauseTrigger.Default) { Enabled = enabled; Reset(w); }

    /// <summary>Forget the past: start watching from the present moment of <paramref name="w"/>.</summary>
    public void Reset(World w)
    {
        var c = w.Player; _log = w.Log.Count;
        _grade = Scorer.Compute(w, c).Grade; _recession = InRecession(c); _inflation = Hot(c); _electionWarned = -1;
    }

    static bool InRecession(CountryState c) => c.GdpGrowth < -0.002 && c.Gap < -0.01;
    static bool Hot(CountryState c) => c.Inflation > c.InflTarget + 0.04;

    /// <summary>The reason to pause after the tick that just ran, or null. Call once per tick.</summary>
    public string? Check(World w)
    {
        var c = w.Player; string? reason = null;
        for (; _log < w.Log.Count; _log++)
        {
            var e = w.Log[_log];
            if (e.Country != c.Id && e.Country != "WORLD") continue;
            reason ??= FromLog(e, c);
        }
        bool rec = InRecession(c);
        if (rec && !_recession && Has(PauseTrigger.Recession)) reason ??= $"Recession: output is shrinking ({c.GdpGrowth * 100:+0.0;-0.0}% a year) and below potential.";
        _recession = rec;
        bool hot = Hot(c);
        if (hot && !_inflation && Has(PauseTrigger.Inflation)) reason ??= $"Inflation has reached {c.Inflation * 100:0.0}%, {(c.Inflation - c.InflTarget) * 100:0.0} points over target.";
        _inflation = hot;
        string g = Scorer.Compute(w, c).Grade;
        if (Array.IndexOf(Grades, g) < Array.IndexOf(Grades, _grade) && Has(PauseTrigger.GradeDrop)) reason ??= $"Your grade has fallen from {_grade} to {g}.";
        _grade = g;
        if (c.NextElectionMonth > 0 && c.NextElectionMonth - w.Month == 3 && _electionWarned != c.NextElectionMonth)
        {
            _electionWarned = c.NextElectionMonth;
            if (Has(PauseTrigger.Election)) reason ??= $"An election is three months away; approval is {c.Approval * 100:0}%.";
        }
        return reason;
    }

    bool Has(PauseTrigger t) => (Enabled & t) != 0;

    string? FromLog(LogEntry e, CountryState c)
    {
        switch (e.Kind)
        {
            case "advisor" when e.Sev >= 2 && Has(PauseTrigger.AdviserAlert): return "Adviser alert: " + e.Text;
            case "policy" when e.Text.EndsWith("is now in force.") && Has(PauseTrigger.PolicyInForce): return e.Text;
            case "policy" when e.Text.StartsWith("Project completed") && Has(PauseTrigger.Project): return e.Text;
            case "news" when e.Country == c.Id && (e.Text.StartsWith("You win") || e.Text.StartsWith("You lose")) && Has(PauseTrigger.Election): return e.Text;
            case "crisis" when e.Country == c.Id && Has(PauseTrigger.Imf): return e.Text;
            case "event" when e.Country == c.Id && Has(PauseTrigger.Event): return e.Text;
        }
        return null;
    }
}

/// <summary>Dates the clock can run to.</summary>
public static class RunTargets
{
    public static int QuarterEnd(int month) => (month / 3 + 1) * 3;
    public static int YearEnd(int month) => (month / 12 + 1) * 12;
    /// <summary>The month of the next election, or null where there is none.</summary>
    public static int? NextElection(World w) => w.Player.NextElectionMonth > w.Month ? w.Player.NextElectionMonth : null;
}
