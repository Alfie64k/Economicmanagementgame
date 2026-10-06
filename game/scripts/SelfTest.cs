using System;
using System.IO;
using System.Linq;
using Godot;
using Sim.Core.Model;
using EconGame.App;
using EconGame.Screens;

namespace EconGame;

/// <summary>Headless-friendly smoke test: boots a game, walks through every page and saves screenshots. Run with: godot --path game -- --selftest [--shots=dir] [--country=GBR].</summary>
public partial class SelfTest : Node
{
    readonly string[] _args;
    public SelfTest(string[] args) { _args = args; }

    string Arg(string key, string def) => _args.FirstOrDefault(a => a.StartsWith(key + "="))?.Split('=')[1] ?? def;

    public override async void _Ready()
    {
        string shots = Arg("--shots", "");
        string country = Arg("--country", "GBR");
        int months = int.Parse(Arg("--months", "30"));
        var main = Main.Instance!;
        try
        {
            main.ShowMainMenu(); await Frames(3); Shot(shots, "00_menu");
            main.ShowCountrySelect(); await Frames(4); Shot(shots, "01_select");
            main.ShowScenarios(); await Frames(3); Shot(shots, "02_scenarios");
            Game.NewGame(country, Difficulty.Sandbox, 7);
            main.ShowGame(); await Frames(3);
            var shell = (GameShell)main.GetChildren().OfType<GameShell>().First();
            for (int i = 0; i < months; i++) Game.Step();
            await Frames(3);
            int n = 3;
            foreach (var page in new[] { "Dashboard", "Budget", "Monetary", "Policies", "Investment", "Sectors", "Trade", "Society", "Forecast", "World map", "Report" })
            {
                shell.Navigate(page); await Frames(6); Shot(shots, $"{n++:00}_{page.Replace(' ', '_').ToLower()}");
            }
            GD.Print("SELFTEST OK");
        }
        catch (Exception ex) { GD.PrintErr("SELFTEST FAILED: " + ex); }
        GetTree().Quit();
    }

    async System.Threading.Tasks.Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }

    void Shot(string dir, string name)
    {
        if (dir.Length == 0 || DisplayServer.GetName() == "headless") return;
        Directory.CreateDirectory(dir);
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(Path.Combine(dir, name + ".png"));
    }
}
