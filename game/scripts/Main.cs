using Godot;
using EconGame.App;
using EconGame.Screens;
using EconGame.Ui;

namespace EconGame;

public partial class Main : Control
{
    Control? _screen;
    public static Main? Instance;

    public override void _Ready()
    {
        Instance = this;
        Settings.Load(); Diagnostics.Install();
        Theme = AppTheme.Build();
        SetAnchorsPreset(LayoutPreset.FullRect);
        var bg = new ColorRect { Color = Pal.Bg };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);
        Game.Changed += () => { };
        var args = OS.GetCmdlineUserArgs();
        if (System.Array.IndexOf(args, "--selftest") >= 0) { AddChild(new SelfTest(args)); return; }
        ShowMainMenu();
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

    public void ApplyTheme() { Theme = AppTheme.Build(); }
}
