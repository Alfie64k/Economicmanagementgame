using System;
using FileAccess = Godot.FileAccess;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.Core.Engine;
using Sim.Core.Model;
using Sim.Core.Scoring;

namespace EconGame.App;

/// <summary>Holds the running simulation and bridges it to the UI. Everything the player does goes through Sim.Core commands.</summary>
public static class Game
{
    public static Simulation? Sim;
    public static ScenarioDef? Scenario;
    public static int Speed = 1;                       // 0 paused, 1..4 speed steps
    public static bool Running => Sim != null;
    public static World World => Sim!.World;
    public static CountryState Player => Sim!.World.Player;
    public static event Action? Ticked, Changed, Ended, DecisionPending;
    static int _autosaveMonth = -1;
    public static readonly HashSet<string> ShownHints = new();

    public static readonly double[] MonthsPerSecond = { 0, 1.2, 3, 8, 24 };

    public static void NewGame(string country, Difficulty diff, ulong seed, ScenarioDef? scenario = null)
    {
        ShownHints.Clear();
        Scenario = scenario;
        Sim = scenario != null ? Scenarios.Start(scenario, seed) : Simulation.New(country, seed, true);
        if (scenario == null) Scenarios.ApplyDifficulty(Sim.World, diff);
        Speed = 0; _autosaveMonth = -1;
        Money.Track(Sim.World);
        Changed?.Invoke();
    }

    public static string SaveDir => "user://saves";

    public static bool Save(string slot)
    {
        if (Sim == null) return false;
        DirAccess.MakeDirRecursiveAbsolute(SaveDir);
        using var f = FileAccess.Open($"{SaveDir}/{slot}.json", FileAccess.ModeFlags.Write);
        if (f == null) return false;
        f.StoreLine(Scenario?.Id ?? "-");
        f.StoreString(Sim.Save());
        return true;
    }

    public static bool Load(string slot)
    {
        var path = $"{SaveDir}/{slot}.json";
        if (!FileAccess.FileExists(path)) return false;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        string sid = f.GetLine(); string json = f.GetAsText();
        Sim = Simulation.Load(json);
        Scenario = sid == "-" ? null : Scenarios.Find(sid);
        Speed = 0; ShownHints.Clear();
        Money.Track(Sim.World);
        Changed?.Invoke();
        return true;
    }

    public static bool HasSave(string slot) => FileAccess.FileExists($"{SaveDir}/{slot}.json");

    /// <summary>Advance one month unless the game is over or a decision is waiting. Returns false if it could not advance.</summary>
    public static bool Step()
    {
        if (Sim == null || Sim.World.GameOver || Sim.World.Decisions.Count > 0) return false;
        Sim.Tick();
        if (Settings.Autosave && Sim.World.Month % 12 == 0 && Sim.World.Month != _autosaveMonth) { _autosaveMonth = Sim.World.Month; Save("auto"); }
        Ticked?.Invoke();
        if (Sim.World.Decisions.Count > 0) { Speed = 0; DecisionPending?.Invoke(); }
        if (Sim.World.GameOver) { Speed = 0; Ended?.Invoke(); }
        else if (Scenario != null && Sim.World.Month >= Scenario.Years * 12) { Speed = 0; Ended?.Invoke(); }
        return true;
    }

    public static void NotifyChanged() => Changed?.Invoke();

    public static void Quit() { Sim = null; Scenario = null; Speed = 0; }

    public static List<HistoryPoint> History(string id) => Sim != null && Sim.World.History.TryGetValue(id, out var h) ? h : new List<HistoryPoint>();

    /// <summary>All countries in the roster with their start data for the selection screen (a throw-away world at month 0).</summary>
    public static List<CountryState> Roster()
    {
        var sim = Simulation.New("GBR", 1, false);
        sim.World.Events = false;
        Money.Track(sim.World);
        return sim.World.Countries;
    }
}
