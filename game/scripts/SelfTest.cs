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
            for (int i = 0; i < months; i++) { Game.Step(); foreach (var d in Game.World.Decisions.ToList()) Game.Sim!.Resolve(d.Id, d.DefaultChoice); }
            await Frames(3);
            int n = 3;
            foreach (var page in new[] { "Dashboard", "Budget", "Monetary", "Policies", "Investment", "Sectors", "Trade", "Society", "Forecast", "World map", "Report" })
            {
                foreach (var d in Game.World.Decisions.ToList()) Game.Sim!.Resolve(d.Id, d.DefaultChoice);
                shell.DismissModal();
                shell.Navigate(page); await Frames(6);
                if (page == "Budget")
                {
                    var c = Game.Player;
                    Sim.Core.Model.Tax t = Sim.Core.Model.Tax.Income;
                    EconGame.App.Draft.Set(t, c.TaxRate[(int)t] * 1.1, c.TaxRate[(int)t]);
                    EconGame.App.Draft.Set(Sim.Core.Model.BudgetLine.Infrastructure, c.Budget[(int)Sim.Core.Model.BudgetLine.Infrastructure] + 0.01, c.Budget[(int)Sim.Core.Model.BudgetLine.Infrastructure]);
                    ((EconGame.Views.BudgetView)shell.Current!).RunPreview(); await Frames(120);
                }
                if (page == "Policies") { Game.Player.PoliticalCapital = 100; Game.Player.Coalition = 1; Game.Sim!.Execute(Sim.Core.Model.Command.Enact(Game.Player.Id, "rnd_tax_credits")); Game.Sim.Execute(Sim.Core.Model.Command.Enact(Game.Player.Id, "labour_flex")); await Frames(4); }
                if (page == "Investment") { Game.Player.PoliticalCapital = 100; Game.Sim!.Execute(Sim.Core.Model.Command.StartProject(Game.Player.Id, "broadband")); Game.Sim.Execute(Sim.Core.Model.Command.StartProject(Game.Player.Id, "hsr")); Game.Sim.Execute(Sim.Core.Model.Command.SetSubsidy(Game.Player.Id, Sim.Core.Model.Sector.Manufacturing, 0.03)); for (int k = 0; k < 14; k++) { Game.Step(); foreach (var d in Game.World.Decisions.ToList()) Game.Sim!.Resolve(d.Id, d.DefaultChoice); } await Frames(5); }
                if (page == "World map")
                {
                    var mv = (EconGame.Views.WorldMapView)shell.Current!; shell.DismissModal(); await Frames(10); Shot(shots, "map_world");
                    mv.Select("USA"); await Frames(10); Shot(shots, "map_usa");
                    mv.ShowRegionsDemo(); await Frames(10); Shot(shots, "map_usa_regions");
                    mv.Select("DEU"); await Frames(10);
                    mv.SetMode(1); await Frames(30); Shot(shots, "globe_deu");
                    mv.Select("BRA"); await Frames(30); Shot(shots, "globe_bra");
                    mv.SetMode(0); await Frames(5);
                }
                if (page == "Forecast") { ((EconGame.Views.ForecastView)shell.Current!).RunNow(); await Frames(180); }
                if (page == "Trade") { Game.Sim!.Execute(Sim.Core.Model.Command.Tariff(Game.Player.Id, "DEU", 0.1)); for (int k = 0; k < 12; k++) { Game.Step(); foreach (var d in Game.World.Decisions.ToList()) Game.Sim!.Resolve(d.Id, d.DefaultChoice); } await Frames(6); }
                Shot(shots, $"{n++:00}_{page.Replace(' ', '_').ToLower()}");
            }
            // real-time loop: speed 4 must advance the sim
            shell.DismissModal(); shell.Navigate("Dashboard");
            int m0 = Game.World.Month; Game.Speed = 4;
            for (int i = 0; i < 150 && Game.Speed > 0; i++) { await Frames(1); foreach (var d in Game.World.Decisions.ToList()) { Game.Sim!.Resolve(d.Id, d.DefaultChoice); shell.DismissModal(); Game.Speed = 4; } }
            if (Game.World.Month < m0 + 6) throw new Exception($"speed loop advanced only {Game.World.Month - m0} months");
            // save / load round-trip
            string hash = Game.Sim!.StateHash();
            if (!Game.Save("selftest")) throw new Exception("save failed");
            Game.Speed = 0; Game.Load("selftest");
            if (Game.Sim!.StateHash() != hash) throw new Exception("load changed the state");
            // scenario flow
            var sc = Sim.Core.Scoring.Scenarios.Find("tut_budget")!;
            Game.NewGame(sc.Country, Difficulty.Easy, 5, sc); main.ShowGame(); await Frames(5);
            var shell2 = main.GetChildren().OfType<GameShell>().First(); shell2.DismissModal(); await Frames(3); Shot(shots, "20_scenario_start");
            for (int i = 0; i < sc.Years * 12 + 1 && !Game.World.GameOver; i++) { Game.Step(); foreach (var d in Game.World.Decisions.ToList()) Game.Sim!.Resolve(d.Id, d.DefaultChoice); }
            await Frames(5); Shot(shots, "21_scenario_end");
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
