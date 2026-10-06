using Godot;
using EconGame.App;
using EconGame.Screens;
using EconGame.Ui;

namespace EconGame;

public partial class Main : Control
{
    Control? _screen;
    ColorRect? _ground;
    public static Main? Instance;

    public override void _Ready()
    {
        Instance = this;
        try
        {
            Diagnostics.Startup($"Godot {Engine.GetVersionInfo()["string"]} · {OS.GetName()} · {DisplayServer.GetName()} · {RenderingServer.GetVideoAdapterName()} · {RenderingServer.GetVideoAdapterApiVersion()}");
            Settings.Load(); Diagnostics.Install();
            // keyboard focus ring behaves like :focus-visible: a mouse click never leaves a lingering ring on a button
            GetViewport().GuiFocusChanged += c =>
            {
                if (c is BaseButton && Input.IsMouseButtonPressed(MouseButton.Left)) c.CallDeferred(Control.MethodName.ReleaseFocus);
            };
            Theme = AppTheme.Build();
            SetAnchorsPreset(LayoutPreset.FullRect);
            _ground = new ColorRect { Color = Pal.Bg };
            _ground.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(_ground);
            Game.Changed += () => { };
            var args = OS.GetCmdlineUserArgs();
            if (System.Array.IndexOf(args, "--selftest") >= 0) { AddChild(new SelfTest(args)); return; }
            ShowMainMenu();
            Diagnostics.Startup("main menu shown");
        }
        catch (System.Exception e)
        {
            Diagnostics.Startup("STARTUP FAILED: " + e);
            GD.PrintErr("Startup failed: " + e);
            ShowStartupError(e);
        }
    }

    /// <summary>Plain-control error page so a startup failure is visible instead of the window silently closing.</summary>
    void ShowStartupError(System.Exception e)
    {
        var bg = new ColorRect { Color = new Color(0.08f, 0.1f, 0.12f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);
        var l = new Label { Text = "The game failed to start.\n\n" + e + "\n\nLog: " + ProjectSettings.GlobalizePath("user://startup.log"), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        l.SetAnchorsPreset(LayoutPreset.FullRect);
        l.OffsetLeft = 24; l.OffsetTop = 24; l.OffsetRight = -24; l.OffsetBottom = -24;
        AddChild(l);
    }

    public void Show(Control screen)
    {
        _screen?.QueueFree();
        _screen = screen;
        screen.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(screen);
    }

    public void ShowMainMenu() => Show(new MainMenuScreen());
    public void ShowCountrySelect(Sim.Core.Scoring.ScenarioDef? sc = null) => Show(new CountrySelectScreen());
    public void ShowScenarios() => Show(new ScenarioScreen());
    public void ShowGame() => Show(new GameShell());
    public void ShowSettings(System.Action back) => Show(new SettingsScreen(back));
    public void ShowSaves() => Show(new SavesScreen());
    public void ShowAchievements() => Show(new AchievementsScreen());

    public void ApplyTheme() { Theme = AppTheme.Build(); if (_ground != null) _ground.Color = Pal.Bg; }
}
