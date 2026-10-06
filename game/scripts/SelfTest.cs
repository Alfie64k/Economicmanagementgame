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
        Game.SuppressModals = true;   // the year-in-review modal would block the scripted walk; it is exercised explicitly below

        try
        {
            await UiFlow(main, shots);
            main.ShowMainMenu(); await Frames(3); Shot(shots, "00_menu");
            main.ShowCountrySelect(); await Frames(4); Shot(shots, "01_select");
            main.ShowScenarios(); await Frames(3); Shot(shots, "02_scenarios");
            main.ShowSettings(() => { }); await Frames(4); Shot(shots, "02b_settings");
            Game.NewGame(country, Difficulty.Sandbox, 7);
            main.ShowGame(); await Frames(3);
            var shell = (GameShell)main.GetChildren().OfType<GameShell>().First();
            for (int i = 0; i < months; i++) { Game.Step(); foreach (var d in Game.World.Decisions.ToList()) Game.Sim!.Resolve(d.Id, d.DefaultChoice); }
            await Frames(3);
            int n = 3;
            foreach (var page in new[] { "Dashboard", "Budget", "Monetary", "Policies", "Investment", "Sectors", "Trade", "Society", "Forecast", "World map", "Cabinet", "Rankings", "Journal", "Report" })
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
                    var bv = (EconGame.Views.BudgetView)shell.Current!;
                    bv.RunPreview(); await Frames(120);
                    EconGame.App.Draft.Clear(); await Frames(2);
                    if (c.Fiscal != null)
                    {
                        var f = c.Fiscal;
                        foreach (var tab in new[] { "Overview", "Income tax", "Payroll", "Corporation tax", "VAT", "Pensions & welfare", "Departments" })
                        {
                            if (!bv.ShowTab(tab)) throw new Exception("Budget has no tab '" + tab + "'");
                            await Frames(4);
                            Shot(shots, "04t_budget_" + tab.Replace(' ', '_').Replace("&", "and").ToLower());
                        }
                        // a taper edit on the income-tax tab: a cut to the allowance and a steeper taper raise revenue, and the deciles respond
                        bv.ShowTab("Income tax"); await Frames(4);
                        EconGame.App.Draft.SetFiscal("Inc.Allow", f.Get("Inc.Allow") * 0.9, f.Get("Inc.Allow"));
                        EconGame.App.Draft.SetFiscal("Inc.TaperRate", Math.Min(0.9, f.Get("Inc.TaperRate") + 0.25), f.Get("Inc.TaperRate"));
                        await Frames(4);
                        if (!(bv.Estimate.Changed && bv.Estimate.TaxGdp[0] > 0)) throw new Exception("cutting the allowance should raise income-tax revenue in the estimate");
                        if (!(bv.Estimate.DecileNet.Min() < 0)) throw new Exception("cutting the allowance should leave some tenth worse off");
                        Shot(shots, "04a_budget_income_tax");
                        EconGame.App.Draft.Clear();
                        // a more generous unemployment benefit costs money and lifts first-year demand
                        bv.ShowTab("Pensions & welfare"); await Frames(4);
                        EconGame.App.Draft.SetFiscal("Une.Level", f.Get("Une.Level") * 1.3, f.Get("Une.Level")); await Frames(4);
                        if (!(bv.Estimate.BenSpendGdp[1] > 0 && bv.Estimate.DemandGdp > 0)) throw new Exception("a higher jobseeker benefit should cost more and lift demand");
                        // staged, it joins the plan and nothing changes until the turn is played
                        double before = f.Get("Une.Level");
                        foreach (var cmd in EconGame.App.Draft.ToCommands(c)) Game.Stage(cmd);
                        EconGame.App.Draft.Clear(); await Frames(4);
                        if (!Game.Sim!.Plan.Any(q => q.Type == "fiscal")) throw new Exception("the fiscal change was not staged");
                        if (Math.Abs(f.Get("Une.Level") - before) > 1e-12) throw new Exception("a staged fiscal change must not apply before the turn is played");
                        Shot(shots, "04b_budget_welfare");
                        Game.ClearPlan(); await Frames(2);
                        bv.ShowTab("Overview"); await Frames(3);
                    }
                }
                if (page == "Monetary")
                {
                    var mo = (EconGame.Views.MonetaryView)shell.Current!; await Frames(60);
                    mo.MoveRateForTest(0.02); await Frames(90);
                    var rp = mo.PreviewResult ?? throw new Exception("the rate preview produced no result");
                    if (!(Math.Abs(rp.Target - (Game.Player.PolicyRate + 0.02)) < 0.003)) throw new Exception("the rate preview is not for the slider value");
                    if (!(rp.S("inflation").DeltaAt(12) < 0)) throw new Exception("a rate hike should lower inflation within a year in the preview");
                    var msc = mo.GetChildren().OfType<ScrollContainer>().First(); msc.ScrollVertical = 700; await Frames(4); Shot(shots, "05b_monetary_forecast"); msc.ScrollVertical = 0; await Frames(2);
                }
                if (page == "Policies") { Game.Player.PoliticalCapital = 100; Game.Player.Coalition = 1; Game.Sim!.Execute(Sim.Core.Model.Command.Enact(Game.Player.Id, "rnd_tax_credits")); Game.Sim.Execute(Sim.Core.Model.Command.Enact(Game.Player.Id, "labour_flex")); await Frames(4); }
                if (page == "Investment") { Game.Player.PoliticalCapital = 100; Game.Sim!.Execute(Sim.Core.Model.Command.StartProject(Game.Player.Id, "broadband")); Game.Sim.Execute(Sim.Core.Model.Command.StartProject(Game.Player.Id, "hsr")); Game.Sim.Execute(Sim.Core.Model.Command.SetSubsidy(Game.Player.Id, Sim.Core.Model.Sector.Manufacturing, 0.03)); for (int k = 0; k < 14; k++) { Game.Step(); foreach (var d in Game.World.Decisions.ToList()) Game.Sim!.Resolve(d.Id, d.DefaultChoice); } await Frames(5); }
                if (page == "World map")
                {
                    var mv = (EconGame.Views.WorldMapView)shell.Current!; shell.DismissModal(); await Frames(10); Shot(shots, "map_world");
                    mv.Select("USA"); await Frames(10); Shot(shots, "map_usa");
                    if (mv.ScreenPosOf("USA") is Vector2 up) { await Hover(up + new Vector2(60, 40)); await Frames(3); Shot(shots, "map_usa_tooltip"); await Hover(new Vector2(5, 5)); }
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
            await Cabinet(shell, shots);
            await RunControls(shell, shots);
            // real-time loop: speed 4 must advance the sim
            shell.DismissModal(); shell.Navigate("Dashboard");
            int savedTriggers = Settings.PauseTriggers; Settings.PauseTriggers = 0;
            int m0 = Game.World.Month; Game.Speed = 4;
            for (int i = 0; i < 150 && Game.Speed > 0; i++) { await Frames(1); foreach (var d in Game.World.Decisions.ToList()) { Game.Sim!.Resolve(d.Id, d.DefaultChoice); shell.DismissModal(); Game.Speed = 4; } }
            Settings.PauseTriggers = savedTriggers;
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

    static Button? FindButton(Node root, Func<Button, bool> pred)
    {
        if (root is Button b && pred(b)) return b;
        foreach (var c in root.GetChildren()) { var r = FindButton(c, pred); if (r != null) return r; }
        return null;
    }
    static bool HasLabel(Node n, string text) => n is Label l ? l.Text == text : n.GetChildren().Any(c => HasLabel(c, text));

    async System.Threading.Tasks.Task Hover(Vector2 p)
    {
        GetViewport().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true); await Frames(3);
    }

    async System.Threading.Tasks.Task Click(Vector2 p)
    {
        foreach (bool down in new[] { true, false })
        {
            var e = new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = down, ButtonMask = down ? MouseButtonMask.Left : 0 };
            GetViewport().PushInput(e, true); await Frames(2);
        }
    }

    async System.Threading.Tasks.Task Key(Key k)
    {
        foreach (bool down in new[] { true, false }) { GetViewport().PushInput(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = down }, false); await Frames(2); }
    }

    /// <summary>Drives the real UI with synthetic mouse and keyboard events: menu, country pick, start, shortcuts, map click.</summary>
    async System.Threading.Tasks.Task UiFlow(Main main, string shots)
    {
        main.ShowMainMenu(); await Frames(4);
        var play = FindButton(main, b => b.Text.StartsWith("New game")) ?? throw new Exception("no New game button");
        await Click(play.GlobalPosition + play.Size / 2); await Frames(6);
        var row = FindButton(main, b => HasLabel(b, "Germany")) ?? throw new Exception("country list has no Germany row");
        await Click(row.GlobalPosition + row.Size / 2); await Frames(4);
        if (!row.ButtonPressed || row.Flat) throw new Exception("selected country row is not in the selected state");
        var hov = FindButton(main, b => HasLabel(b, "France")) ?? throw new Exception("country list has no France row");
        await Hover(hov.GlobalPosition + hov.Size / 2); Shot(shots, "01b_select_hover");
        var start = FindButton(main, b => b.Text.StartsWith("Start as")) ?? throw new Exception("no Start button");
        if (start.Disabled) throw new Exception("Start disabled after picking a country");
        await Click(start.GlobalPosition + start.Size / 2); await Frames(8);
        if (!Game.Running || Game.Player.Id != "DEU") throw new Exception("UI flow did not start Germany");
        var shell = main.GetChildren().OfType<GameShell>().First();
        await Key(Godot.Key.Pagedown); await Frames(3);
        if (shell.CurrentName != "Budget") throw new Exception("PageDown did not advance the page (" + shell.CurrentName + ")");
        var budgetNav = FindButton(shell, b => b.Text == "Budget") ?? throw new Exception("no Budget nav button");
        if (!budgetNav.ButtonPressed || budgetNav.Flat) throw new Exception("current page is not shown as selected in the navigation");
        var dashNav = FindButton(shell, b => b.Text == "Dashboard")!;
        if (dashNav.ButtonPressed) throw new Exception("previous page is still shown as selected");
        await Hover(dashNav.GlobalPosition + dashNav.Size / 2); await Frames(3); Shot(shots, "03b_nav_hover");
        await Hover(budgetNav.GlobalPosition + budgetNav.Size / 2); await Frames(3); Shot(shots, "03c_nav_selected_hover");
        await Hover(new Vector2(900, 700)); await Frames(2);

        // staged turn: nothing visible changes until the turn is played
        var pl = Game.Player; double pc0 = pl.PoliticalCapital, tax0 = pl.TaxRate[(int)Sim.Core.Model.Tax.Income]; int log0 = Game.World.Log.Count, month0 = Game.World.Month;
        var st = Game.Stage(Sim.Core.Model.Command.SetTax(pl.Id, Sim.Core.Model.Tax.Income, tax0 + 0.01));
        if (!st.Staged) throw new Exception("staging a tax change failed: " + st.Message);
        await Frames(20);
        if (pl.PoliticalCapital != pc0 || pl.TaxRate[(int)Sim.Core.Model.Tax.Income] != tax0 || Game.World.Log.Count != log0 || Game.World.Month != month0) throw new Exception("staging changed the world before the turn was played");
        var planBtn = FindButton(shell, b => b.Text.StartsWith("Plan ·")) ?? throw new Exception("no Plan button");
        if (!planBtn.Text.Contains("1")) throw new Exception("Plan button does not show the staged action: " + planBtn.Text);
        Shot(shots, "14_plan_staged");
        await Click(planBtn.GlobalPosition + planBtn.Size / 2); await Frames(8); Shot(shots, "14b_plan_tray");
        if (FindButton(main, b => b.Text == "Clear plan") == null) throw new Exception("plan tray did not open");
        var closeBtn = FindButton(main, b => b.Text == "Close") ?? throw new Exception("no Close button in the plan tray");
        await Click(closeBtn.GlobalPosition + closeBtn.Size / 2); await Frames(6);
        await Key(Godot.Key.Enter); await Frames(10);
        if (Game.World.Month != month0 + 1) throw new Exception("Enter did not end the turn");
        if (Game.Sim!.Plan.Count != 0) throw new Exception("plan not cleared after the turn");
        if (!(Game.Player.TaxRate[(int)Sim.Core.Model.Tax.Income] > tax0)) throw new Exception("planned tax change was not applied by the turn");
        if (!(Game.Player.PoliticalCapital != pc0)) throw new Exception("political capital did not move when the turn was played");
        Shot(shots, "14c_turn_played");

        await Key(Godot.Key.Space); await Frames(2);
        if (Game.Speed == 0) throw new Exception("Space did not start the clock");
        await Key(Godot.Key.Space); await Frames(2);
        if (Game.Speed != 0) throw new Exception("Space did not pause");
        var nav = FindButton(shell, b => b.Text == "World map") ?? throw new Exception("no World map nav button");
        await Click(nav.GlobalPosition + nav.Size / 2); await Frames(10);
        var mv = (EconGame.Views.WorldMapView)shell.Current!;
        await Frames(10);
        var pos = mv.ScreenPosOf("BRA") ?? throw new Exception("map position missing");
        await Click(pos); await Frames(6);
        if (mv.SelectedId != "BRA") throw new Exception($"clicking Brazil on the map selected {mv.SelectedId}");
        GD.Print("UIFLOW OK");
        Game.Quit();
    }

    /// <summary>Cabinet cards: acting on a suggestion stages it, snoozing hides the note, the rival chosen on Rankings is remembered.</summary>
    async System.Threading.Tasks.Task Cabinet(GameShell shell, string shots)
    {
        shell.DismissModal(); Game.ClearPlan(); Game.Snoozed.Clear();
        shell.Navigate("Cabinet"); await Frames(6);
        var cv = (EconGame.Views.CabinetView)shell.Current!;
        var notes = EconGame.Views.CabinetView.Live();
        GD.Print($"cabinet: {notes.Count} live notes, {notes.Count(n => n.Suggestion != null)} with a suggested move");
        var withMove = notes.FirstOrDefault(n => n.Suggestion != null && Sim.Core.Policy.CommandProcessor.Apply(Game.World, n.Suggestion, dryRun: true).Ok);
        if (withMove != null)
        {
            var add = FindButton(cv, b => b.Text.StartsWith("Add to plan") && !b.Disabled) ?? throw new Exception("a note with a suggestion has no Add to plan button");
            await Click(add.GlobalPosition + add.Size / 2); await Frames(6);
            if (Game.Sim!.Plan.Count == 0) throw new Exception("Add to plan did not stage the suggested move");
            if (FindButton(cv, b => b.Text.Contains("in plan")) == null) throw new Exception("the card did not show the move as in the plan");
            Game.ClearPlan(); await Frames(4);
        }
        if (notes.Count > 0)
        {
            Shot(shots, "22_cabinet");
            var sn = FindButton(cv, b => b.Text.StartsWith("Snooze")) ?? throw new Exception("no Snooze button");
            await Click(sn.GlobalPosition + sn.Size / 2); await Frames(6);
            if (Game.Snoozed.Count != 1) throw new Exception("Snooze did not record the note");
            if (EconGame.Views.CabinetView.Live().Count != notes.Count - 1) throw new Exception("the snoozed note is still live");
            Shot(shots, "22b_cabinet_snoozed");
            Game.Snoozed.Clear();
        }
        // a rival chosen on the Rankings page survives navigation
        shell.Navigate("Rankings"); await Frames(6);
        Game.Rival = Game.Player.Id == "DEU" ? "FRA" : "DEU"; shell.Navigate("Dashboard"); await Frames(3); shell.Navigate("Rankings"); await Frames(8);
        if (Game.Rival == "") throw new Exception("the rival was lost on navigation");
        Shot(shots, "23_rankings_rival");
        Game.Rival = "";
        shell.Navigate("Journal"); await Frames(8); Shot(shots, "24_journal");
    }

    /// <summary>Run to a date, auto-pause, and the year-in-review modal.</summary>
    async System.Threading.Tasks.Task RunControls(GameShell shell, string shots)
    {
        shell.DismissModal(); shell.Navigate("Dashboard"); await Frames(3);
        Game.ClearPlan(); foreach (var d in Game.World.Decisions.ToList()) Game.Sim!.Resolve(d.Id, d.DefaultChoice);

        // run to the end of the year: the clock must stop on the date, or earlier with a stated reason
        string? why = null; void On(string s) => why = s; Game.Paused += On;
        int target = Sim.Core.Policy.RunTargets.YearEnd(Game.World.Month);
        Settings.PauseTriggers = (int)Sim.Core.Policy.PauseTrigger.Default;
        Game.StartRunTo(target);
        if (Game.Speed == 0) throw new Exception("Run to did not start the clock");
        for (int i = 0; i < 400 && Game.Speed > 0; i++)
        {
            await Frames(1);
            foreach (var d in Game.World.Decisions.ToList()) { Game.Sim!.Resolve(d.Id, d.DefaultChoice); shell.DismissModal(); }
            if (Game.Speed == 0 && Game.World.Month < target && why == null) break;   // stopped by a decision: not under test here
        }
        Game.Paused -= On;
        if (Game.Speed != 0) throw new Exception("the clock never stopped on the run-to date");
        if (Game.World.Month > target) throw new Exception($"run to overshot: month {Game.World.Month}, target {target}");
        if (Game.World.Month < target && string.IsNullOrEmpty(why)) throw new Exception("the clock stopped early without a reason");
        GD.Print($"run to month {target}: stopped at {Game.World.Month}" + (why != null ? " — " + why : ""));
        await Frames(4); Shot(shots, "25_run_to_stopped");

        // the year-in-review panel, built from the journal, opens as a modal and offers the journal
        while (Game.World.Month < 24 && !Game.World.GameOver) { Game.Step(); foreach (var d in Game.World.Decisions.ToList()) Game.Sim!.Resolve(d.Id, d.DefaultChoice); }
        int end = Game.World.Month / 12 * 12;
        var review = Sim.Core.Scoring.Journal.Review(Game.World, end) ?? throw new Exception("no year review for a completed year");
        shell.DismissModal(); shell.ShowYearReview(review); await Frames(8); Shot(shots, "26_year_review");
        var jb = FindButton(shell, b => b.Text == "Open the journal");
        if (jb == null) throw new Exception("the year review offers no way into the journal");
        await Click(jb.GlobalPosition + jb.Size / 2); await Frames(6);
        if (shell.CurrentName != "Journal") throw new Exception("the journal button went to " + shell.CurrentName);
        shell.Navigate("Dashboard"); await Frames(3);
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
