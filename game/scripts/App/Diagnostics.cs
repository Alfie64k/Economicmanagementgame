using System;
using System.Linq;
using System.Text;
using Godot;
using FileAccess = Godot.FileAccess;

namespace EconGame.App;

/// <summary>Opt-in, local-only diagnostics. Nothing is ever sent anywhere: the report is copied to the clipboard for the player to share if they wish.</summary>
public static class Diagnostics
{
    public static bool CrashLog;       // opt-in; stored in settings
    const string LogPath = "user://crash.log";

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("unhandled", e.ExceptionObject?.ToString() ?? "?");
    }

    /// <summary>Always-on, local-only startup trace (not opt-in: it is written once at launch and only helps diagnose a failed start).</summary>
    public static void Startup(string text)
    {
        try
        {
            const string path = "user://startup.log";
            using var f = FileAccess.Open(path, FileAccess.FileExists(path) ? FileAccess.ModeFlags.ReadWrite : FileAccess.ModeFlags.Write);
            if (f == null) return;
            f.SeekEnd(); f.StoreLine($"[{Time.GetDatetimeStringFromSystem()}] {text}");
        }
        catch { /* logging must never be the thing that breaks startup */ }
    }

    public static void Write(string kind, string text)
    {
        if (!CrashLog) return;
        using var f = FileAccess.Open(LogPath, FileAccess.FileExists(LogPath) ? FileAccess.ModeFlags.ReadWrite : FileAccess.ModeFlags.Write);
        if (f == null) return;
        f.SeekEnd(); f.StoreLine($"[{Time.GetDatetimeStringFromSystem()}] {kind}: {text}");
    }

    public static string Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Economic Management Game feedback report");
        sb.AppendLine($"Godot {Engine.GetVersionInfo()["string"]} · {OS.GetName()} · {DisplayServer.GetName()}");
        if (Game.Running)
        {
            var w = Game.World; var c = Game.Player;
            sb.AppendLine($"Country {c.Id} · difficulty {w.Difficulty} · scenario {(w.ScenarioId == "" ? "-" : w.ScenarioId)} · seed {w.Seed} · month {w.Month} · state hash {Game.Sim!.StateHash()}");
            sb.AppendLine($"GDP growth {c.GdpGrowth:P1} · inflation {c.Inflation:P1} · unemployment {c.Unemp:P1} · debt {c.DebtToGdp:P0} · approval {c.Approval:P0}");
            foreach (var l in w.Log.TakeLast(15)) sb.AppendLine($"  {l.Month}: [{l.Kind}] {l.Text}");
        }
        sb.AppendLine("What happened / what did you expect?");
        return sb.ToString();
    }

    public static void CopyReport() => DisplayServer.ClipboardSet(Report());
}
