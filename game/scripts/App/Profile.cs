using System.Collections.Generic;
using Godot;

namespace EconGame.App;

/// <summary>What the player has earned across games: achievements and a few counters. Kept in its own small file, separate from saves, and never sent anywhere.</summary>
public static class Profile
{
    /// <summary>Where the profile lives. The self-test points it somewhere disposable.</summary>
    public static string Path { get; set; } = "user://profile.cfg";
    public sealed record Award(long When, string Country);
    static Dictionary<string, Award> _earned = new();
    static bool _loaded;

    static void EnsureLoaded() { if (!_loaded) Load(); }

    public static void Load()
    {
        _loaded = true; _earned = new();
        var cf = new ConfigFile();
        if (cf.Load(Path) != Error.Ok) return;
        foreach (var key in cf.GetSectionKeys("achievements"))
        {
            var parts = ((string)cf.GetValue("achievements", key, "")).Split('|', 2);
            _earned[key] = new Award(long.TryParse(parts[0], out var t) ? t : 0, parts.Length > 1 ? parts[1] : "");
        }
    }

    static void Save()
    {
        var cf = new ConfigFile();
        foreach (var (id, a) in _earned) cf.SetValue("achievements", id, $"{a.When}|{a.Country}");
        cf.Save(Path);
    }

    public static bool Has(string id) { EnsureLoaded(); return _earned.ContainsKey(id); }
    public static Award? Get(string id) { EnsureLoaded(); return _earned.GetValueOrDefault(id); }
    public static int Count { get { EnsureLoaded(); return _earned.Count; } }

    /// <summary>Record an award. Returns true only the first time, so the caller knows to celebrate.</summary>
    public static bool Unlock(string id, string country)
    {
        EnsureLoaded();
        if (_earned.ContainsKey(id)) return false;
        _earned[id] = new Award((long)Time.GetUnixTimeFromSystem(), country); Save();
        return true;
    }

    /// <summary>Forget everything (used by the self-test and the reset button).</summary>
    public static void Clear() { _loaded = true; _earned = new(); Save(); }
}
