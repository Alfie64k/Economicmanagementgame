using Godot;
using EconGame.Ui;

namespace EconGame.App;

public static class Settings
{
    /// <summary>Where settings live. The self-test points it somewhere disposable.</summary>
    public static string Path = "user://settings.cfg";
    public static float TextScale = 1.0f;
    public static bool ColourBlind;
    /// <summary>Near-black ground, white text, stronger borders and a yellow keyboard-focus ring.</summary>
    public static bool HighContrast;
    /// <summary>Scales the whole interface (text, controls and spacing), unlike <see cref="TextScale"/> which scales text only.</summary>
    public static float UiScale = 1.0f;
    /// <summary>No fading notifications: they appear and disappear without animation.</summary>
    public static bool ReduceMotion;
    /// <summary>The first-turn tutorial coach has been shown (or dismissed). New games start it once, until this is set.</summary>
    public static bool TutorialSeen;
    public static bool Autosave = true;
    public static int DefaultSpeed = 1;
    public static bool Fullscreen;
    public static float Volume = 0.7f;
    /// <summary>What stops the clock while the game runs by itself (a <see cref="Sim.Core.Policy.PauseTrigger"/> mask).</summary>
    public static int PauseTriggers = (int)Sim.Core.Policy.PauseTrigger.Default;
    /// <summary>Show the year-in-review card each January.</summary>
    public static bool AnnualReview = true;
    public static System.Collections.Generic.HashSet<string> Bookmarks = new();

    public static void Load()
    {
        var cf = new ConfigFile();
        if (cf.Load(Path) == Error.Ok)
        {
            TextScale = (float)(double)cf.GetValue("ui", "text_scale", 1.0);
            ColourBlind = (bool)cf.GetValue("ui", "colour_blind", false);
            HighContrast = (bool)cf.GetValue("ui", "high_contrast", false);
            UiScale = Mathf.Clamp((float)(double)cf.GetValue("ui", "ui_scale", 1.0), 0.8f, 1.5f);
            ReduceMotion = (bool)cf.GetValue("ui", "reduce_motion", false);
            TutorialSeen = (bool)cf.GetValue("game", "tutorial_seen", false);
            Autosave = (bool)cf.GetValue("game", "autosave", true);
            DefaultSpeed = (int)cf.GetValue("game", "speed", 1);
            Fullscreen = (bool)cf.GetValue("ui", "fullscreen", false);
            Diagnostics.CrashLog = (bool)cf.GetValue("privacy", "crash_log", false);
            Volume = (float)(double)cf.GetValue("audio", "volume", 0.7);
            PauseTriggers = (int)cf.GetValue("game", "pause_triggers", (int)Sim.Core.Policy.PauseTrigger.Default);
            AnnualReview = (bool)cf.GetValue("game", "annual_review", true);
            var b = (string)cf.GetValue("map", "bookmarks", "");
            Bookmarks = new System.Collections.Generic.HashSet<string>(b.Split(',', System.StringSplitOptions.RemoveEmptyEntries));
        }
        Apply();
    }

    public static void Save()
    {
        var cf = new ConfigFile();
        cf.SetValue("ui", "text_scale", (double)TextScale); cf.SetValue("ui", "colour_blind", ColourBlind);
        cf.SetValue("ui", "high_contrast", HighContrast); cf.SetValue("ui", "ui_scale", (double)UiScale); cf.SetValue("ui", "reduce_motion", ReduceMotion); cf.SetValue("game", "tutorial_seen", TutorialSeen);
        cf.SetValue("game", "autosave", Autosave); cf.SetValue("game", "speed", DefaultSpeed);
        cf.SetValue("game", "pause_triggers", PauseTriggers); cf.SetValue("game", "annual_review", AnnualReview);
        cf.SetValue("ui", "fullscreen", Fullscreen); cf.SetValue("privacy", "crash_log", Diagnostics.CrashLog); cf.SetValue("audio", "volume", (double)Volume);
        cf.SetValue("map", "bookmarks", string.Join(",", Bookmarks));
        cf.Save(Path);
    }

    public static void Apply()
    {
        Pal.TextScale = TextScale; Pal.ColourBlind = ColourBlind;
        if (Pal.HighContrast != HighContrast) Pal.Use(HighContrast);
        if (Engine.GetMainLoop() is SceneTree tree && DisplayServer.GetName() != "headless") tree.Root.ContentScaleFactor = UiScale;
        AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(Mathf.Max(0.0001f, Volume)));
        if (DisplayServer.GetName() != "headless") DisplayServer.WindowSetMode(Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
    }
}
