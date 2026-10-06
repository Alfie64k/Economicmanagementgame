using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FileAccess = Godot.FileAccess;

namespace EconGame.App;

/// <summary>What a save slot shows in the list, read from a small sidecar file so listing never has to parse a whole world.</summary>
public sealed class SaveInfo
{
    public string Slot = "", Country = "", Date = "", Grade = "", Scenario = "";
    public long SavedAt;          // unix seconds
    public bool HasDetails;
    public bool IsAuto => Slot == "auto" || Slot.StartsWith("auto") && Slot.Length > 4 && char.IsDigit(Slot[4]);
    public string Label => Slot switch { "auto" => "Autosave (latest)", "auto1" => "Autosave (previous)", "auto2" => "Autosave (oldest)", "quick" => "Quick save", _ => Slot.Replace('_', ' ') };
}

/// <summary>Save slots on disk: manual and named slots, three rolling autosaves, one quick-save slot. Pure file handling; the game state itself is written by <see cref="Game.Save"/>.</summary>
public static class SaveStore
{
    public const int AutosaveSlots = 3;     // auto (newest), auto1, auto2
    static string Dir => Game.SaveDir;

    /// <summary>A slot name safe to use as a file name: letters, digits, spaces, hyphens and underscores, up to 32 characters. Empty when nothing usable is left.</summary>
    public static string Sanitise(string name)
    {
        var s = new string((name ?? "").Where(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_').ToArray()).Trim().Replace(' ', '_');
        return s.Length > 32 ? s[..32] : s;
    }

    /// <summary>Make room for a new autosave: auto → auto1 → auto2, dropping the oldest.</summary>
    public static void RotateAutosaves()
    {
        for (int i = AutosaveSlots - 1; i >= 1; i--)
        {
            string from = i == 1 ? "auto" : "auto" + (i - 1), to = "auto" + i;
            Move(from, to);
        }
    }

    static void Move(string from, string to)
    {
        var dir = DirAccess.Open(Dir); if (dir == null) return;
        foreach (var ext in new[] { "json", "meta" })
        {
            if (!dir.FileExists($"{from}.{ext}")) { if (dir.FileExists($"{to}.{ext}")) dir.Remove($"{to}.{ext}"); continue; }
            if (dir.FileExists($"{to}.{ext}")) dir.Remove($"{to}.{ext}");
            dir.Rename($"{from}.{ext}", $"{to}.{ext}");
        }
    }

    public static void WriteMeta(string slot, string country, string date, string grade, string scenario)
    {
        var d = new Godot.Collections.Dictionary { ["country"] = country, ["date"] = date, ["grade"] = grade, ["scenario"] = scenario, ["saved"] = (long)Time.GetUnixTimeFromSystem() };
        using var f = FileAccess.Open($"{Dir}/{slot}.meta", FileAccess.ModeFlags.Write);
        f?.StoreString(Json.Stringify(d));
    }

    public static SaveInfo Read(string slot)
    {
        var info = new SaveInfo { Slot = slot };
        string meta = $"{Dir}/{slot}.meta";
        if (FileAccess.FileExists(meta))
        {
            using var f = FileAccess.Open(meta, FileAccess.ModeFlags.Read);
            var parsed = Json.ParseString(f.GetAsText());
            if (parsed.VariantType == Variant.Type.Dictionary)
            {
                var d = parsed.AsGodotDictionary();
                string Str(string k) => d.ContainsKey(k) ? d[k].AsString() : "";
                info.Country = Str("country"); info.Date = Str("date"); info.Grade = Str("grade"); info.Scenario = Str("scenario");
                info.SavedAt = d.ContainsKey("saved") ? d["saved"].AsInt64() : 0; info.HasDetails = true;
            }
        }
        if (info.SavedAt == 0) info.SavedAt = (long)FileAccess.GetModifiedTime($"{Dir}/{slot}.json");
        return info;
    }

    /// <summary>Every save on disk, newest first.</summary>
    public static List<SaveInfo> List()
    {
        var res = new List<SaveInfo>();
        var dir = DirAccess.Open(Dir); if (dir == null) return res;
        foreach (var file in dir.GetFiles()) if (file.EndsWith(".json")) res.Add(Read(file[..^5]));
        return res.OrderByDescending(s => s.SavedAt).ThenBy(s => s.Slot, StringComparer.Ordinal).ToList();
    }

    public static SaveInfo? Newest() => List().FirstOrDefault();

    public static bool Delete(string slot)
    {
        var dir = DirAccess.Open(Dir); if (dir == null) return false;
        bool ok = false;
        foreach (var ext in new[] { "json", "meta", "tmp" }) if (dir.FileExists($"{slot}.{ext}")) ok |= dir.Remove($"{slot}.{ext}") == Error.Ok;
        return ok;
    }

    public static string When(long unix)
    {
        if (unix <= 0) return "";
        var t = Time.GetDatetimeDictFromUnixTime(unix + (long)Time.GetTimeZoneFromSystem()["bias"].AsInt32() * 60);
        string[] mn = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        return $"{(int)t["day"]} {mn[(int)t["month"] - 1]} {(int)t["year"]}, {(int)t["hour"]:00}:{(int)t["minute"]:00}";
    }
}
