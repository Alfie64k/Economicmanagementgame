using System;
using System.Collections.Generic;
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
        Game.SaveDir = "user://selftest_saves";   // never touch a player's real saves
        Profile.Path = "user://selftest_profile.cfg"; Profile.Clear();
        Settings.Path = "user://selftest_settings.cfg"; Settings.TutorialSeen = true;
        { var d = DirAccess.Open("user://"); if (d != null && d.DirExists("selftest_saves")) { var sd = DirAccess.Open(Game.SaveDir); foreach (var f in sd.GetFiles()) sd.Remove(f); } }

        try
        {
            await RealMenu(main);
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
                if (page == "Society")
                {
                    foreach (var need in new[] { "Work, wages and the informal economy", "Informal economy", "Union coverage", "Wage-price spiral", "Strike risk", "Long-term unemployed", "Labour share" })
                        if (!HasLabel(shell.Current!, need)) throw new Exception($"Society page is missing '{need}'");
                }
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
            await RealGame(shell, shots);
            await Cabinet(shell, shots);
            await RunControls(shell, shots);
            shell = await Polish(main, shell, shots);
            shell = await Saves(main, shell, shots);
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
            shell = await Tutorial(main, shots);
            shell = await Awards(main, shots);
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

    /// <summary>
    /// A left click as the operating system delivers it: through <c>Input.ParseInputEvent</c>, which also updates the Input singleton
    /// (<c>Input.IsMouseButtonPressed</c>), unlike <c>Viewport.PushInput</c>. Press and release are several frames apart, as with a real hand,
    /// so anything that reacts to the press (focus changes, deferred calls) has run before the release arrives.
    /// </summary>
    async System.Threading.Tasks.Task Click(Vector2 p)
    {
        Input.ParseInputEvent(new InputEventMouseMotion { Position = p, GlobalPosition = p }); await Frames(1);
        foreach (bool down in new[] { true, false })
        {
            Input.ParseInputEvent(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = down, ButtonMask = down ? MouseButtonMask.Left : 0 });
            await Frames(down ? 4 : 2);
        }
    }

    async System.Threading.Tasks.Task Key(Key k)
    {
        foreach (bool down in new[] { true, false }) { Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = down }); await Frames(2); }
    }

    /// <summary>Types text into whatever has keyboard focus, one key press at a time.</summary>
    async System.Threading.Tasks.Task Type(string text)
    {
        foreach (char ch in text)
        {
            var k = (Key)char.ToUpperInvariant(ch);
            foreach (bool down in new[] { true, false }) Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Unicode = ch, Pressed = down });
            await Frames(2);
        }
    }

    /// <summary>A real drag: press, move in steps, release.</summary>
    async System.Threading.Tasks.Task Drag(Vector2 from, Vector2 to)
    {
        Input.ParseInputEvent(new InputEventMouseMotion { Position = from, GlobalPosition = from }); await Frames(2);
        Input.ParseInputEvent(new InputEventMouseButton { Position = from, GlobalPosition = from, ButtonIndex = MouseButton.Left, Pressed = true, ButtonMask = MouseButtonMask.Left }); await Frames(3);
        for (int i = 1; i <= 8; i++)
        {
            var p = from.Lerp(to, i / 8f);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = p, GlobalPosition = p, ButtonMask = MouseButtonMask.Left, Relative = (to - from) / 8f }); await Frames(2);
        }
        Input.ParseInputEvent(new InputEventMouseButton { Position = to, GlobalPosition = to, ButtonIndex = MouseButton.Left, Pressed = false }); await Frames(3);
    }

    /// <summary>Clicks the n-th item of an open popup menu with the mouse (items are laid out evenly down the popup).</summary>
    async System.Threading.Tasks.Task ClickPopupItem(PopupMenu pop, int index)
    {
        if (!pop.Visible) throw new Exception("the popup is not open");
        float h = pop.Size.Y / Math.Max(1, pop.ItemCount);
        await Click(new Vector2(pop.Position.X + pop.Size.X / 2, pop.Position.Y + h * (index + 0.5f))); await Frames(4);
    }

    static string FocusedButton() => Main.Instance!.GetViewport().GuiGetFocusOwner() is BaseButton b ? (b is Button t ? t.Text : b.GetType().Name) : "";

    /// <summary>
    /// The menus, settings and country list driven only by real events: no signal is emitted and no widget method is called. This is the layer
    /// that failed for a player on a release build when a focus handler cancelled every button press.
    /// </summary>
    async System.Threading.Tasks.Task RealMenu(Main main)
    {
        main.ShowMainMenu(); await Frames(4);
        bool Has(string text) => FindButton(main, x => x.Text.StartsWith(text)) != null;
        async System.Threading.Tasks.Task ClickText(string text, string what)
        {
            var b = FindButton(main, x => x.Text.StartsWith(text) && !x.Disabled && x.IsVisibleInTree()) ?? throw new Exception($"no enabled '{text}' button ({what})");
            await Click(b.GlobalPosition + b.Size / 2); await Frames(4);
        }
        foreach (var (label, what) in new[] { ("Scenarios", "the scenarios screen"), ("Achievements", "achievements"), ("Settings", "settings") })
        {
            await ClickText(label, what);
            if (Has("New game") || !Has("← Back")) throw new Exception($"a real click on '{label}' did not open {what}");
            if (FocusedButton() != "") throw new Exception($"button '{FocusedButton()}' kept keyboard focus after a mouse click");
            if (label == "Settings")
            {
                var cb = Find<CheckBox>(main) ?? throw new Exception("settings has no check box");
                bool was = cb.ButtonPressed;
                await Click(cb.GlobalPosition + new Vector2(14, cb.Size.Y / 2)); await Frames(2);
                if (cb.ButtonPressed == was) throw new Exception("a real click did not toggle a settings check box");
                await Click(cb.GlobalPosition + new Vector2(14, cb.Size.Y / 2)); await Frames(2);
                if (cb.ButtonPressed != was) throw new Exception("a second real click did not restore the check box");
            }
            await ClickText("← Back", what);
            if (!Has("New game")) throw new Exception($"a real click on Back did not return to the main menu from {what}");
        }

        // country select: search box, region drop-down (popup), row, start
        await ClickText("New game", "the country list");
        var search = Find<LineEdit>(main) ?? throw new Exception("country list has no search box");
        await Click(search.GlobalPosition + search.Size / 2); await Frames(2);
        await Type("fra");
        if (search.Text != "fra") throw new Exception($"typing into the search box gave '{search.Text}'");
        if (FindButton(main, b => HasLabel(b, "France")) == null) throw new Exception("France is not listed after searching for 'fra'");
        if (FindButton(main, b => HasLabel(b, "Germany")) != null) throw new Exception("Germany is still listed after searching for 'fra'");
        foreach (var _ in "fra") await Key(Godot.Key.Backspace);
        if (search.Text != "") throw new Exception("could not clear the search box");
        var region = Find<OptionButton>(main) ?? throw new Exception("country list has no region drop-down");
        await Click(region.GlobalPosition + region.Size / 2); await Frames(6);
        if (!region.GetPopup().Visible) throw new Exception("a real click did not open the region drop-down");
        await ClickPopupItem(region.GetPopup(), 1);
        if (region.Selected != 1) throw new Exception($"clicking a region in the drop-down gave item {region.Selected}");
        await Click(region.GlobalPosition + region.Size / 2); await Frames(6);
        await ClickPopupItem(region.GetPopup(), 0);
        if (region.Selected != 0) throw new Exception("could not return the region filter to all regions");
        await ClickText("← Back", "the country list");
        if (!Has("New game")) throw new Exception("Back from the country list did not return to the main menu");
    }

    /// <summary>The in-game controls driven only by real events: navigation, speed, menus, chips, the End turn button and a slider drag.</summary>
    async System.Threading.Tasks.Task RealGame(GameShell shell, string shots)
    {
        shell.DismissModal(); Game.ClearPlan(); await Frames(2);
        int savedTriggers = Settings.PauseTriggers; Settings.PauseTriggers = 0;
        foreach (var pg in new[] { "Dashboard", "Budget", "Monetary", "Policies", "Investment", "Sectors", "Trade", "Society", "Forecast", "World map", "Cabinet", "Rankings", "Journal", "Report" })
        {
            var nb = FindButton(shell, b => b.Text.StartsWith(pg) && b.ThemeTypeVariation == EconGame.Ui.StateStyles.NavItem) ?? throw new Exception($"no navigation item for {pg}");
            await Click(nb.GlobalPosition + nb.Size / 2); await Frames(4);
            if (shell.CurrentName != pg) throw new Exception($"a real click on the {pg} navigation item opened '{shell.CurrentName}'");
            if (FocusedButton() != "") throw new Exception($"after clicking {pg} the button '{FocusedButton()}' still has focus, so Space and Enter would press it");
        }

        // speed chips and Space
        async System.Threading.Tasks.Task Chip(string text)
        {
            var b = FindButton(shell, x => x.Text == text && x.IsVisibleInTree()) ?? throw new Exception($"no '{text}' button");
            await Click(b.GlobalPosition + b.Size / 2); await Frames(2);
        }
        await Chip("2×"); if (Game.Speed != 2) throw new Exception($"a real click on 2× set the speed to {Game.Speed}");
        await Chip("II"); if (Game.Speed != 0) throw new Exception("a real click on the pause button did not pause");
        await Chip("1×"); if (Game.Speed != 1) throw new Exception("a real click on 1× did not set the speed");
        await Chip("Dashboard"); await Key(Godot.Key.Space);
        if (Game.Speed != 0) throw new Exception("Space did not pause the clock after a mouse click on a button");
        shell.Navigate("Dashboard"); await Frames(2);

        // the Run to menu: a popup, chosen with the keyboard
        var rt = Find<MenuButton>(shell) ?? throw new Exception("no Run to menu");
        await Click(rt.GlobalPosition + rt.Size / 2); await Frames(6);
        if (!rt.GetPopup().Visible) throw new Exception("a real click on Run to did not open its menu");
        await ClickPopupItem(rt.GetPopup(), 3);   // "One year from now": always a date ahead, unlike the end of a quarter that may be today
        if (Game.Speed == 0) throw new Exception("choosing 'One year from now' did not start the clock");
        await Chip("II"); if (Game.Speed != 0) throw new Exception("could not stop a run-to with the pause button");

        // the news filter chips
        var adv = FindButton(shell, b => b.Text == "Advisers" && b.IsVisibleInTree()) ?? throw new Exception("no Advisers chip");
        await Click(adv.GlobalPosition + adv.Size / 2); await Frames(3);
        if (!adv.ButtonPressed) throw new Exception("a real click on the Advisers chip did not select it");
        await Chip("All");

        // End turn
        shell.DismissModal(); foreach (var d in Game.World.Decisions.ToList()) Game.Sim!.Resolve(d.Id, d.DefaultChoice);
        int m = Game.World.Month;
        var end = FindButton(shell, b => b.Text.StartsWith("End turn")) ?? throw new Exception("no End turn button");
        await Click(end.GlobalPosition + end.Size / 2); await Frames(10);
        if (Game.World.Month != m + 1) throw new Exception($"a real click on End turn moved the month from {m} to {Game.World.Month}");
        shell.DismissModal(); foreach (var d in Game.World.Decisions.ToList()) Game.Sim!.Resolve(d.Id, d.DefaultChoice);

        // a slider, dragged with the mouse
        shell.Navigate("Monetary"); await Frames(6);
        var sl = Find<EconGame.Ui.AppSlider>(shell) ?? throw new Exception("the Monetary page has no slider");
        float trackL = 8, trackW = sl.Size.X - 16 - sl.LabelWidth, cy = sl.GlobalPosition.Y + sl.Size.Y / 2;
        double target = sl.Min + 0.8 * (sl.Max - sl.Min);
        await Drag(new Vector2(sl.GlobalPosition.X + trackL + 0.2f * trackW, cy), new Vector2(sl.GlobalPosition.X + trackL + 0.8f * trackW, cy));
        if (Math.Abs(sl.Value - target) > 0.04 * (sl.Max - sl.Min)) throw new Exception($"dragging the slider gave {sl.Value}, expected about {target}");
        Game.ClearPlan(); await Frames(2);

        // the Menu button opens the game menu and Resume closes it
        var menu = FindButton(shell, b => b.Text == "Menu") ?? throw new Exception("no Menu button");
        await Click(menu.GlobalPosition + menu.Size / 2); await Frames(4);
        if (!HasLabel(shell, "Game menu")) throw new Exception("a real click on Menu did not open the game menu");
        var resume = FindButton(shell, b => b.Text == "Resume") ?? throw new Exception("no Resume button");
        await Click(resume.GlobalPosition + resume.Size / 2); await Frames(4);
        if (HasLabel(shell, "Game menu")) throw new Exception("a real click on Resume did not close the game menu");
        Settings.PauseTriggers = savedTriggers;
        shell.Navigate("Dashboard"); await Frames(3);
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

        // auto-pause is switched off here: on a slow machine the clock could otherwise run a month and stop itself between the two key presses
        int triggers = Settings.PauseTriggers; Settings.PauseTriggers = 0;
        foreach (var d in Game.World.Decisions.ToList()) Game.Sim!.Resolve(d.Id, d.DefaultChoice);   // each run has its own seed: a random decision popup would swallow the key
        shell.DismissModal(); await Frames(2);
        await Key(Godot.Key.Space); await Frames(2);
        if (Game.Speed == 0) throw new Exception($"Space did not start the clock (focus: {GetViewport().GuiGetFocusOwner()?.GetType().Name ?? "none"} '{FocusedButton()}', modal: {shell.HasModal}, default speed {Settings.DefaultSpeed})");
        await Key(Godot.Key.Space); await Frames(2);
        if (Game.Speed != 0) throw new Exception("Space did not pause");
        Settings.PauseTriggers = triggers;
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

    static T? Find<T>(Node root) where T : Node
    {
        if (root is T t) return t;
        foreach (var c in root.GetChildren()) { var r = Find<T>(c); if (r != null) return r; }
        return null;
    }
    static void FindAll<T>(Node root, List<T> into) where T : Node
    {
        if (root is T t) into.Add(t);
        foreach (var c in root.GetChildren()) FindAll(c, into);
    }

    /// <summary>The guided first turn: each step completes when the player does what it asks.</summary>
    async System.Threading.Tasks.Task<GameShell> Tutorial(Main main, string shots)
    {
        // a first-ever sandbox game starts the coach by itself; a scenario does not
        Game.SuppressModals = false; Settings.TutorialSeen = false;
        var scn = Sim.Core.Scoring.Scenarios.Find("tut_budget")!;
        Game.NewGame(scn.Country, Difficulty.Easy, 5, scn); main.ShowGame(); await Frames(6);
        if (main.GetChildren().OfType<GameShell>().First().Coach != null) throw new Exception("the tutorial coach started inside a scenario");
        Game.NewGame("GBR", Difficulty.Easy, 5); main.ShowGame(); await Frames(6);
        Game.SuppressModals = true;
        var shell = main.GetChildren().OfType<GameShell>().First();
        var coach = shell.Coach ?? throw new Exception("the tutorial coach did not start in a new sandbox game");
        void At(int step, string why) { if (coach.CurrentStep != step) throw new Exception($"tutorial: expected step {step + 1} {why}, at step {coach.CurrentStep + 1}"); }
        At(0, "at the start"); Shot(shots, "33_tutorial_step1");
        var next = FindButton(coach, b => b.Text == "Next ▸" && b.IsVisibleInTree()) ?? throw new Exception("no Next button on an informational step");
        await Click(next.GlobalPosition + next.Size / 2); await Frames(4); At(1, "after Next");
        await Frames(10); Shot(shots, "33b_tutorial_highlight");
        var nav = FindButton(shell, b => b.Text == "Budget") ?? throw new Exception("no Budget nav"); await Click(nav.GlobalPosition + nav.Size / 2); await Frames(6); At(2, "after opening Budget");
        Game.Stage(Sim.Core.Model.Command.SetTax(Game.Player.Id, Sim.Core.Model.Tax.Income, Game.Player.TaxRate[(int)Sim.Core.Model.Tax.Income] + 0.01)); await Frames(4); At(3, "after staging a change");
        var next2 = FindButton(coach, b => b.Text == "Next ▸" && b.IsVisibleInTree()) ?? throw new Exception("no Next on the review step");
        await Click(next2.GlobalPosition + next2.Size / 2); await Frames(4); At(4, "after reviewing the plan");
        int m = Game.World.Month; await Key(Godot.Key.Enter); await Frames(8);
        if (Game.World.Month != m + 1) throw new Exception("Enter did not end the turn during the tutorial"); At(5, "after ending the turn");
        shell.Navigate("Monetary"); await Frames(4); At(6, "after opening Monetary");
        shell.Navigate("Cabinet"); await Frames(4); At(7, "after opening the Cabinet");
        Shot(shots, "33c_tutorial_runto");
        var skip = FindButton(coach, b => b.Text == "Skip step" && b.IsVisibleInTree()) ?? throw new Exception("no Skip step button"); await Click(skip.GlobalPosition + skip.Size / 2); await Frames(4); At(8, "after skipping");
        var fin = FindButton(coach, b => b.Text == "Finish" && b.IsVisibleInTree()) ?? throw new Exception("no Finish button on the last step"); await Click(fin.GlobalPosition + fin.Size / 2); await Frames(6);
        if (shell.Coach != null || !Settings.TutorialSeen) throw new Exception("the tutorial did not close and record that it was seen");
        Settings.TutorialSeen = true;
        Game.NewGame("GBR", Difficulty.Easy, 5); main.ShowGame(); await Frames(4);
        return main.GetChildren().OfType<GameShell>().First();
    }

    /// <summary>Achievements: not on sandbox, earned on Normal, remembered in the profile and shown on the screen.</summary>
    async System.Threading.Tasks.Task<GameShell> Awards(Main main, string shots)
    {
        Game.NewGame("DEU", Difficulty.Sandbox, 11); main.ShowGame(); await Frames(4);
        Game.Stage(Sim.Core.Model.Command.SetTax(Game.Player.Id, Sim.Core.Model.Tax.Income, Game.Player.TaxRate[(int)Sim.Core.Model.Tax.Income] + 0.01));
        for (int i = 0; i < 12; i++) { Game.Step(); foreach (var d in Game.World.Decisions.ToList()) Game.Sim!.Resolve(d.Id, d.DefaultChoice); }
        if (Profile.Count != 0) throw new Exception("a sandbox game earned an achievement");

        Game.NewGame("DEU", Difficulty.Normal, 11); main.ShowGame(); await Frames(4);
        var shell = main.GetChildren().OfType<GameShell>().First();
        Game.Stage(Sim.Core.Model.Command.SetTax(Game.Player.Id, Sim.Core.Model.Tax.Income, Game.Player.TaxRate[(int)Sim.Core.Model.Tax.Income] + 0.01));
        EconGame.Ui.AchievementsPanel? panel = null;
        for (int i = 0; i < 12; i++) { Game.Step(); foreach (var d in Game.World.Decisions.ToList()) Game.Sim!.Resolve(d.Id, d.DefaultChoice); }
        await Frames(6); Shot(shots, "32_achievement_toast");
        if (!Profile.Has("first_budget")) throw new Exception("a changed tax on Normal did not earn First budget");
        int n = Profile.Count; Game.CheckAchievements(false);
        if (Profile.Count != n) throw new Exception("an award was granted twice");
        shell.DismissModal();
        main.ShowAchievements(); await Frames(6); Shot(shots, "32b_achievements");
        panel = Find<EconGame.Ui.AchievementsPanel>(main) ?? throw new Exception("the achievements screen did not open");
        main.ShowMainMenu(); await Frames(4);
        if (FindButton(main, b => b.Text.StartsWith("Achievements (")) == null) throw new Exception("no Achievements button on the main menu");
        Game.NewGame("DEU", Difficulty.Normal, 11); main.ShowGame(); await Frames(4);
        return main.GetChildren().OfType<GameShell>().First();
    }

    /// <summary>Rolling autosaves, named slots, quick save and load, the saves list.</summary>
    async System.Threading.Tasks.Task<GameShell> Saves(Main main, GameShell shell, string shots)
    {
        shell.DismissModal(); shell.Navigate("Dashboard"); await Frames(3);
        for (int i = 0; i < 4; i++) { if (!Game.Autosave()) throw new Exception("autosave failed"); await Frames(1); }
        var slots = SaveStore.List().Select(s => s.Slot).ToList();
        foreach (var want in new[] { "auto", "auto1", "auto2" }) if (!slots.Contains(want)) throw new Exception("rolling autosave is missing " + want);
        if (slots.Contains("auto3")) throw new Exception("more than three autosaves are kept");
        if (SaveStore.Sanitise("my plan: 2030!") != "my_plan_2030") throw new Exception("slot names are not sanitised: " + SaveStore.Sanitise("my plan: 2030!"));
        if (!Game.Save("my_plan")) throw new Exception("named save failed");
        var named = SaveStore.Read("my_plan");
        if (!named.HasDetails || named.Country != Game.Player.Name || named.Date.Length == 0) throw new Exception("save details were not written");

        // F5 quick-saves; later progress is lost by F9 after confirming
        string hash0 = Game.Sim!.StateHash(); int month0 = Game.World.Month;
        await Key(Godot.Key.F5); await Frames(4);
        if (!Game.HasSave("quick")) throw new Exception("F5 did not write a quick save");
        for (int i = 0; i < 3; i++) Game.Step();
        await Key(Godot.Key.F9); await Frames(6);
        var yes = FindButton(shell, b => b.Text == "Load it") ?? throw new Exception("F9 did not offer to load the quick save");
        await Click(yes.GlobalPosition + yes.Size / 2); await Frames(10);
        shell = main.GetChildren().OfType<GameShell>().First();
        if (Game.World.Month != month0 || Game.Sim!.StateHash() != hash0) throw new Exception("the quick load did not restore the saved state");

        // the saves list: load and delete from the UI
        shell.ShowSaves(); await Frames(8); Shot(shots, "31_saves");
        var del = FindButton(shell, b => b.Text == "Delete") ?? throw new Exception("the saves list has no Delete button");
        await Click(del.GlobalPosition + del.Size / 2); await Frames(4);
        var sure = FindButton(shell, b => b.Text == "Yes, delete") ?? throw new Exception("deleting did not ask for confirmation");
        int before = SaveStore.List().Count;
        await Click(sure.GlobalPosition + sure.Size / 2); await Frames(4);
        if (SaveStore.List().Count != before - 1) throw new Exception("the save was not deleted");
        shell.DismissModal();

        // main menu: Continue names the newest save, Saves… lists them
        main.ShowMainMenu(); await Frames(6);
        var cont = FindButton(main, b => b.Text.StartsWith("Continue")) ?? throw new Exception("no Continue button");
        if (cont.Disabled || !cont.Text.Contains(Game.Player.Name)) throw new Exception("Continue does not name the newest save: " + cont.Text);
        Shot(shots, "00b_menu_continue");
        main.ShowSaves(); await Frames(6); Shot(shots, "31b_saves_menu");
        var load = FindButton(main, b => b.Text == "Load") ?? throw new Exception("no Load button on the saves screen");
        await Click(load.GlobalPosition + load.Size / 2); await Frames(10);
        if (!Game.Running) throw new Exception("loading from the saves screen did not start a game");
        return main.GetChildren().OfType<GameShell>().First();
    }

    /// <summary>Annotated charts, the glossary and the accessibility settings.</summary>
    async System.Threading.Tasks.Task<GameShell> Polish(Main main, GameShell shell, string shots)
    {
        // annotated charts: markers for what you did and what happened, a shared crosshair, and a time window
        shell.DismissModal(); shell.Navigate("Dashboard"); await Frames(8);
        var marks = EconGame.Ui.ChartPrefs.For(Game.World);
        if (marks.Count == 0) throw new Exception("the journal gave the charts no markers after a game with several actions");
        EconGame.Ui.ChartPrefs.Set(0, true); await Frames(4);
        var charts = new List<EconGame.Ui.LineChart>(); FindAll(shell.Current!, charts);
        var annotated = charts.Where(c => c.Annotated && c.SyncGroup == "dash").ToList();
        if (annotated.Count != 4) throw new Exception($"expected four synchronised dashboard charts, found {annotated.Count}");
        var first = annotated[0]; var mk = marks.First(m => m.Mine);
        if (first.ScreenXOf(mk.X) is not float mx) throw new Exception("a marker month is outside the chart");
        var pr = first.PlotRect; await Hover(new Vector2(mx, pr.Position.Y + pr.Size.Y / 2)); await Frames(4); Shot(shots, "27_chart_marker_hover");
        EconGame.Ui.ChartPrefs.Set(24, true); await Frames(4);
        int lastRecorded = Game.History(Game.Player.Id).Last().Month;   // history is recorded quarterly, so the current month need not be on it
        if (annotated[0].ScreenXOf(lastRecorded - 25) != null) throw new Exception($"the two-year window still shows older months (month {Game.World.Month}, last recorded {lastRecorded})");
        if (annotated[0].ScreenXOf(lastRecorded - 12) == null) throw new Exception("the two-year window dropped months it should still show");
        await Hover(new Vector2(first.PlotRect.Position.X + first.PlotRect.Size.X * 0.6f, first.PlotRect.Position.Y + 40)); await Frames(4); Shot(shots, "27b_chart_two_years_synced");
        await Hover(new Vector2(5, 5)); EconGame.Ui.ChartPrefs.Set(0, true); await Frames(2);

        // glossary: F2 opens it, typing filters it, Escape closes it
        await Key(Godot.Key.F2); await Frames(6);
        var gp = Find<EconGame.Ui.GlossaryPanel>(shell) ?? throw new Exception("F2 did not open the glossary");
        gp.Search("taper"); await Frames(4);
        if (gp.ResultCount == 0 || gp.Selected == "") throw new Exception("glossary search for 'taper' found nothing");
        Shot(shots, "28_glossary");
        await Key(Godot.Key.Escape); await Frames(6);
        if (Find<EconGame.Ui.GlossaryPanel>(shell) != null) throw new Exception("Escape did not close the glossary");

        // accessibility: high contrast recolours the interface, the interface scale changes the content scale factor
        Settings.HighContrast = true; Settings.Apply(); main.ApplyTheme(); main.ShowSettings(() => { }); await Frames(6); Shot(shots, "02c_settings_high_contrast");
        if (EconGame.Ui.Pal.Text != new Color("FFFFFF") || EconGame.Ui.Pal.Bg != new Color("000000")) throw new Exception("high contrast did not change the palette");
        main.ShowGame(); await Frames(8); Shot(shots, "29_dashboard_high_contrast");
        Settings.HighContrast = false; Settings.Apply(); main.ApplyTheme();
        Settings.UiScale = 1.25f; Settings.Apply(); await Frames(4);
        if (DisplayServer.GetName() != "headless" && Math.Abs(GetTree().Root.ContentScaleFactor - 1.25f) > 1e-3) throw new Exception("interface scale did not reach the window");
        main.ShowGame(); await Frames(8); Shot(shots, "29b_dashboard_scale_125");
        // the largest interface scale: the layout must still fit the window (top bar on two rows, news panel folded away)
        Settings.UiScale = 1.5f; Settings.Apply(); main.ShowGame(); await Frames(10);
        var big = main.GetChildren().OfType<GameShell>().First(); var vis = GetViewport().GetVisibleRect().Size;
        foreach (var pg in new[] { "Dashboard", "Budget", "Monetary", "Policies", "Investment", "Sectors", "Trade", "Society", "Forecast", "Cabinet", "Rankings", "Journal", "Report" })
        {
            big.DismissModal(); big.Navigate(pg); await Frames(8);
            var cur = big.Current!; var need = cur.GetCombinedMinimumSize().X;
            if (need > cur.Size.X + 1) throw new Exception($"{pg} needs {need:0}px but only {cur.Size.X:0}px are available at interface scale 1.5");
            var menu = FindButton(big, b => b.Text == "Menu") ?? throw new Exception("no Menu button at scale 1.5");
            var endBtn = FindButton(big, b => b.Text.StartsWith("End turn")) ?? throw new Exception("no End turn button at scale 1.5");
            if (menu.GlobalPosition.X + menu.Size.X > vis.X + 1 || endBtn.GlobalPosition.X + endBtn.Size.X > vis.X + 1)
                throw new Exception($"top bar overflows the window at scale 1.5 on {pg}: menu ends at {menu.GlobalPosition.X + menu.Size.X}, window {vis.X}");
            Shot(shots, "30_scale150_" + pg.ToLower());
        }
        Settings.UiScale = 1.0f; Settings.Apply(); main.ShowGame(); await Frames(6);
        if (EconGame.Ui.Pal.Text == new Color("FFFFFF")) throw new Exception("standard palette not restored");
        return main.GetChildren().OfType<GameShell>().First();
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
