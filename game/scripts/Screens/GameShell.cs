using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.Core.Engine;
using Sim.Core.Events;
using Sim.Core.Model;
using Sim.Core.Policy;
using Sim.Core.Scoring;
using EconGame.App;
using EconGame.Ui;
using EconGame.Views;

namespace EconGame.Screens;

/// <summary>In-game frame: top bar (date, speed, political capital), left navigation, page area and the news/advisor feed.</summary>
public partial class GameShell : Control
{
    readonly List<(string name, Func<View> make)> _pages = new();
    readonly Dictionary<string, View> _cache = new();
    readonly Dictionary<string, Button> _navBtns = new();
    readonly ButtonGroup _navGroup = new();
    View? _current; string _currentName = "";
    Control _pageHost = new();
    Label _date = new(), _country = new(), _pcLabel = new(), _score = new(), _alert = new();
    readonly PcBar _pcBar = new();
    Button _endTurn = new(), _planBtn = new();
    MenuButton _runTo = new();
    Label _auto = new();
    // responsive layout: below ~1500 logical pixels the top bar wraps onto two rows and the news panel starts hidden
    HBoxContainer _barA = new(), _barB = new(), _barTop = new(), _barBottom = new();
    PanelContainer _nav = new(), _feedPanel = new();
    Button _feedToggle = new();
    bool _stacked; bool? _feedWanted;
    string _pauseReason = "";
    int _cabinetSig = -1; int _cabinetCount;
    VBoxContainer? _planBox;
    readonly Dictionary<int, Button> _speedBtns = new();
    VBoxContainer _feed = new();
    string _feedFilter = "all";
    readonly Dictionary<string, Button> _feedChips = new();
    double _acc, _uiAcc; bool _dirty = true; int _feedCount = -1;
    Control? _modal;
    readonly VBoxContainer _toasts = new();
    Label _ticker = new();
    int _lastHintMonth = -1;
    public View? Current => _current;
    public string CurrentName => _currentName;

    public override void _Ready()
    {
        _pages.Add(("Dashboard", () => new DashboardView()));
        _pages.Add(("Budget", () => new BudgetView()));
        _pages.Add(("Monetary", () => new MonetaryView()));
        _pages.Add(("Policies", () => new PoliciesView()));
        _pages.Add(("Investment", () => new InvestmentView()));
        _pages.Add(("Sectors", () => new SectorsView()));
        _pages.Add(("Trade", () => new TradeView()));
        _pages.Add(("Society", () => new SocietyView()));
        _pages.Add(("Forecast", () => new ForecastView()));
        _pages.Add(("World map", () => new WorldMapView()));
        _pages.Add(("Cabinet", () => new CabinetView()));
        _pages.Add(("Rankings", () => new RankingsView()));
        _pages.Add(("Journal", () => new JournalView()));
        _pages.Add(("Report", () => new ReportView()));

        var root = UI.VBox(0); root.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(root);
        _toasts.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop); _toasts.Position = new Vector2(0, 70); _toasts.MouseFilter = MouseFilterEnum.Ignore; _toasts.AddThemeConstantOverride("separation", 6);
        root.AddChild(BuildTopBar());
        var body = UI.HBox(0); body.SizeFlagsVertical = SizeFlags.ExpandFill; root.AddChild(body);
        body.AddChild(BuildNav());
        _pageHost.SizeFlagsHorizontal = SizeFlags.ExpandFill; _pageHost.SizeFlagsVertical = SizeFlags.ExpandFill; _pageHost.ClipContents = true;
        body.AddChild(_pageHost);
        body.AddChild(BuildFeed());

        AddChild(_toasts);
        Game.DecisionPending += ShowDecision;
        Game.Ended += ShowEnd;
        Game.Ticked += MarkDirty; Game.Changed += MarkDirty; Game.PlanChanged += OnPlanChanged; Game.PlanApplied += OnPlanApplied;
        Game.Paused += OnAutoPaused; Game.YearEnded += OnYearEnded;
        Resized += ApplyLayout; Callable.From(ApplyLayout).CallDeferred();
        Game.Speed = Settings.DefaultSpeed == 0 ? 0 : 0; // always start paused so the player can read the briefing
        Navigate("Dashboard");
        UpdateTop(); UpdateFeed(true);
        if (Game.World.Decisions.Count > 0) ShowDecision();
        if (Game.Scenario != null) ShowBriefing();
    }

    public override void _ExitTree() { Game.DecisionPending -= ShowDecision; Game.Ended -= ShowEnd; Game.Ticked -= MarkDirty; Game.Changed -= MarkDirty; Game.PlanChanged -= OnPlanChanged; Game.PlanApplied -= OnPlanApplied; Game.Paused -= OnAutoPaused; Game.YearEnded -= OnYearEnded; }

    // ---------------- layout ----------------
    Control BuildTopBar()
    {
        var bar = new PanelContainer();
        bar.AddThemeStyleboxOverride("panel", AppTheme.Box(Pal.Panel, 0, Pal.Border, 0, 10));
        var h = _barA = UI.HBox(14);
        _country = UI.Lbl(Game.Player.Name, 20, Pal.Text, true);
        _date = UI.Lbl("", 16, Pal.Dim);
        h.AddChild(_country); h.AddChild(_date);
        h.AddChild(UI.Spacer(10, 0));
        var speeds = UI.HBox(4);
        string[] icons = { "II", "1×", "2×", "3×", "4×" };
        for (int i = 0; i < icons.Length; i++)
        {
            int sp = i; var b = UI.Chip(icons[i], false, () => SetSpeed(sp)); b.CustomMinimumSize = new Vector2(44, 0); _speedBtns[i] = b; speeds.AddChild(b);
            b.TooltipText = i == 0 ? "Pause (Space)" : $"Speed {i} (key {i})";
        }
        h.AddChild(speeds);
        _runTo = new MenuButton { Text = "Run to ▾", FocusMode = FocusModeEnum.All, MouseDefaultCursorShape = CursorShape.PointingHand, ThemeTypeVariation = StateStyles.Chip, Flat = false };
        _runTo.TooltipText = "Let the clock run on by itself until a date, stopping early if something needs you.";
        var pop = _runTo.GetPopup();
        pop.AddItem("End of this quarter", 0); pop.AddItem("End of this year", 1); pop.AddItem("Next election", 2); pop.AddItem("One year from now", 3);
        pop.IdPressed += id => RunTo((int)id);
        h.AddChild(_runTo);
        _planBtn = UI.Btn("Plan · empty", ShowPlan, false, 0);
        _endTurn = UI.Btn("End turn ▸", EndTurn, true, 0);
        _endTurn.TooltipText = "Play one month (Enter). Your plan is applied first, then the economy moves.";
        h.AddChild(_planBtn); h.AddChild(_endTurn);
        h = _barB = UI.HBox(14); _barB.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _ticker = UI.Lbl("", 13, Pal.Dim); _ticker.ClipText = true; _ticker.CustomMinimumSize = new Vector2(120, 0); _ticker.SizeFlagsHorizontal = SizeFlags.ExpandFill; h.AddChild(_ticker);
        _auto = Cards.Chip("AUTOPILOT", Pal.Series[2]); _auto.Visible = false; _auto.TooltipText = "The cabinet is running tax, spending and rates. Open the Cabinet page to take control back."; h.AddChild(_auto);
        _alert = UI.Lbl("", 14, Pal.Warn, true); h.AddChild(_alert);
        var pcBox = UI.VBox(2); _pcLabel = UI.Lbl("Political capital", 12, Pal.Dim);
        _pcBar.CustomMinimumSize = new Vector2(160, 10); _pcLabel.ClipText = true; _pcLabel.CustomMinimumSize = new Vector2(160, 0);
        pcBox.AddChild(_pcLabel); pcBox.AddChild(_pcBar); h.AddChild(pcBox);
        _score = UI.Lbl("", 18, Pal.Accent, true); h.AddChild(_score);
        _feedToggle = UI.Chip("News", true, () => { _feedWanted = _feedToggle.ButtonPressed; ApplyLayout(); });
        _feedToggle.TooltipText = "Show or hide the news and advisers panel";
        h.AddChild(_feedToggle);
        h.AddChild(UI.Btn("Menu", ShowMenu));
        _barTop = UI.HBox(14); _barBottom = UI.HBox(14); _barBottom.Visible = false;
        _barTop.AddChild(_barA); _barTop.AddChild(_barB);
        bar.AddChild(UI.VBox(6, _barTop, _barBottom));
        return bar;
    }

    Control BuildNav()
    {
        var nav = _nav = new PanelContainer { CustomMinimumSize = new Vector2(180, 0) };
        nav.AddThemeStyleboxOverride("panel", AppTheme.Box(Pal.Panel, 0, Pal.Border, 0, 8));
        var v = UI.VBox(4);
        foreach (var (name, _) in _pages)
        {
            string n = name;
            var b = new Button { Text = name, Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.All, ThemeTypeVariation = StateStyles.NavItem, ToggleMode = true, ButtonGroup = _navGroup, MouseDefaultCursorShape = CursorShape.PointingHand };
            b.Pressed += () => { if (_currentName != n) Navigate(n); };
            _navBtns[name] = b; v.AddChild(b);
        }
        v.AddChild(UI.Spacer(0, 0, true));
        nav.AddChild(UI.Scroll(v));
        return nav;
    }

    Control BuildFeed()
    {
        var panel = _feedPanel = new PanelContainer { CustomMinimumSize = new Vector2(330, 0) };
        panel.AddThemeStyleboxOverride("panel", AppTheme.Box(Pal.Panel, 0, Pal.Border, 0, 10));
        var v = UI.VBox(8);
        v.AddChild(UI.H2("News & advisers"));
        var chips = UI.HBox(4);
        foreach (var (key, label) in new[] { ("all", "All"), ("advisor", "Advisers"), ("event", "Events"), ("policy", "Policy") })
        {
            string k = key; Button chip = null!;
            chip = UI.Chip(label, k == _feedFilter, () =>
            {
                _feedFilter = k; UpdateFeed(true);
                foreach (var c in _feedChips) c.Value.SetPressedNoSignal(c.Key == k);
            });
            _feedChips[k] = chip; chips.AddChild(chip);
        }
        v.AddChild(chips);
        _feed.AddThemeConstantOverride("separation", 6);
        v.AddChild(UI.Scroll(_feed));
        panel.AddChild(v);
        return panel;
    }

    /// <summary>Wrap the top bar onto two rows and fold the news panel away when the window is narrow (large interface scale or a small screen).</summary>
    void ApplyLayout()
    {
        float w = Size.X; if (w <= 0) return;
        bool stack = w < 1500;
        if (stack != _stacked) { _stacked = stack; _barB.Reparent(stack ? _barBottom : _barTop, false); _barBottom.Visible = stack; }
        bool feed = _feedWanted ?? !stack;
        _feedPanel.Visible = feed; _feedToggle.SetPressedNoSignal(feed);
        _nav.CustomMinimumSize = new Vector2(w < 1300 ? 150 : 180, 0);
    }

    // ---------------- navigation ----------------
    public void Navigate(string name)
    {
        if (_current != null) _pageHost.RemoveChild(_current);
        if (!_cache.TryGetValue(name, out var view)) { view = _pages.First(p => p.name == name).make(); _cache[name] = view; }
        view.SetAnchorsPreset(LayoutPreset.FullRect);
        _pageHost.AddChild(view); _current = view; _currentName = name;
        foreach (var kv in _navBtns)
        {
            bool sel = kv.Key == name;
            kv.Value.SetPressedNoSignal(sel);          // the group does not unpress siblings for programmatic changes
            if (sel) kv.Value.AddThemeFontOverride("font", UI.Bold); else kv.Value.RemoveThemeFontOverride("font");
        }
        view.Refresh();
    }

    void CyclePage(int dir) { int i = _pages.FindIndex(p => p.name == _currentName); Navigate(_pages[(i + dir + _pages.Count) % _pages.Count].name); }

    public void MarkDirty() => _dirty = true;

    /// <summary>Closes any open popup (used by the self-test and when a decision is resolved elsewhere).</summary>
    public void DismissModal() { _modal?.QueueFree(); _modal = null; }

    void SetSpeed(int s)
    {
        if (Game.World.Decisions.Count > 0 || Game.World.GameOver) { UpdateTop(); return; }
        if (s == 0) Game.StopRun();
        if (s != 0) _pauseReason = "";
        Game.Speed = s; UpdateTop();
    }

    void RunTo(int what)
    {
        if (!Game.Running || _modal != null || Game.World.Decisions.Count > 0 || Game.World.GameOver) return;
        var w = Game.World; int target;
        switch (what)
        {
            case 0: target = RunTargets.QuarterEnd(w.Month); break;
            case 1: target = RunTargets.YearEnd(w.Month); break;
            case 2:
                if (RunTargets.NextElection(w) is not int el) { Toast("There is no election to run to.", Pal.Warn); return; }
                target = el; break;
            default: target = w.Month + 12; break;
        }
        _pauseReason = ""; Game.StartRunTo(target); UpdateTop();
    }

    void OnAutoPaused(string why)
    {
        _pauseReason = why; UpdateTop();
        Toast("Paused: " + why, Pal.Warn);
    }

    void OnYearEnded(int month)
    {
        if (!Settings.AnnualReview || Game.SuppressModals || _modal != null || Game.World.Decisions.Count > 0) return;
        var r = Journal.Review(Game.World, month); if (r == null) return;
        ShowYearReview(r);
    }

    public void ShowYearReview(YearReview r)
    {
        var box = YearReviewPanel.Build(r, Game.World, () => { Close(); Navigate("Journal"); }, Close);
        Overlay(box, 760);
    }

    // ---------------- loop ----------------
    public override void _Process(double delta)
    {
        if (!Game.Running) return;
        if (Game.Speed > 0 && _modal == null)
        {
            _acc += delta * Game.MonthsPerSecond[Game.Speed];
            int n = 0;
            while (_acc >= 1 && n < 6) { _acc -= 1; n++; if (!Game.Step()) { _acc = 0; break; } _dirty = true; }
            if (n == 6) _acc = 0;
        }
        _uiAcc += delta;
        if (_dirty && _uiAcc > 0.12) { _uiAcc = 0; _dirty = false; UpdateTop(); UpdateFeed(false); _current?.Refresh(); }
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey k || !k.Pressed || _modal != null) return;
        if (k.Keycode == Key.Space) SetSpeed(Game.Speed == 0 ? Math.Max(1, Settings.DefaultSpeed) : 0);
        else if (k.Keycode >= Key.Key1 && k.Keycode <= Key.Key4) SetSpeed((int)k.Keycode - (int)Key.Key0);
        else if (k.Keycode == Key.Pagedown || (k.Keycode == Key.Tab && k.CtrlPressed && !k.ShiftPressed)) CyclePage(1);
        else if (k.Keycode == Key.Pageup || (k.Keycode == Key.Tab && k.CtrlPressed && k.ShiftPressed)) CyclePage(-1);
        else if ((k.Keycode == Key.Enter || k.Keycode == Key.KpEnter) && !k.AltPressed) EndTurn();
        else if (k.Keycode == Key.Escape) ShowMenu();
        else if (k.Keycode == Key.F1) ShowHelp();
        else if (k.Keycode == Key.F2) ShowGlossary();
    }

    void UpdateTop()
    {
        var w = Game.World; var c = Game.Player;
        _country.Text = c.Name;
        string[] mn = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        _date.Text = $"{mn[w.MonthOfYear - 1]} {w.Year}" + (Game.Scenario != null ? $"  ·  {Math.Max(0, Game.Scenario.Years * 12 - w.Month) / 12}y left" : "");
        foreach (var kv in _speedBtns)
        {
            kv.Value.SetPressedNoSignal(kv.Key == Game.Speed);
        }
        UpdatePlanReadouts();
        var sc = Scorer.Compute(w, c); _score.Text = $"{sc.Grade} {sc.Total:0}";
        _alert.Text = w.Decisions.Count > 0 ? "⚠ Decision required" : Game.Speed == 0 ? (_pauseReason != "" ? "⏸ " + Short(_pauseReason) : "Paused") : Game.RunTo > 0 ? $"▶ to {Game.World.StartYear + (Game.RunTo - 1) / 12}-{(Game.RunTo - 1) % 12 + 1:D2}" : "";
        _alert.TooltipText = _pauseReason;
        _auto.Visible = c.Autopilot;
        UpdateCabinetBadge(w, c);
        var last = w.Log.LastOrDefault(l => (l.Country == w.PlayerId || l.Country == "WORLD") && l.Kind is "event" or "crisis" or "news");
        _ticker.Text = last == null ? "" : "▸ " + last.Text;
        CheckHints(w);
    }

    static string Short(string t) => t.Length <= 46 ? t : t[..45] + "…";

    void UpdateCabinetBadge(World w, CountryState c)
    {
        int sig = w.Month * 1000 + Game.Snoozed.Count;
        if (sig != _cabinetSig)
        {
            _cabinetSig = sig;
            _cabinetCount = Advisors.Generate(w, c).Count(n => n.Severity >= Severity.Warning && !(Game.Snoozed.TryGetValue(c.Id + ":" + n.Key, out var until) && until > w.Month));
        }
        if (_navBtns.TryGetValue("Cabinet", out var b)) b.Text = _cabinetCount > 0 ? $"Cabinet  ·  {_cabinetCount}" : "Cabinet";
    }

    void UpdateFeed(bool force)
    {
        var w = Game.World;
        int count = w.Log.Count * 10 + (_feedFilter.GetHashCode() & 7);
        if (!force && count == _feedCount) return; _feedCount = count;
        foreach (var ch in _feed.GetChildren()) ch.QueueFree();
        var entries = w.Log.Where(l => (l.Country == w.PlayerId || l.Country == "WORLD" || l.Country == "") && (_feedFilter == "all" || l.Kind == _feedFilter || (_feedFilter == "event" && l.Kind == "crisis")))
            .TakeLast(80).Reverse();
        foreach (var e in entries) _feed.AddChild(FeedItem(e));
        if (!entries.Any()) _feed.AddChild(UI.Dim("Nothing to report yet. Advisers speak up every quarter.", 13, true));
    }

    static Control FeedItem(LogEntry e)
    {
        var col = e.Kind switch { "advisor" => Pal.Series[0], "crisis" => Pal.Bad, "event" => Pal.Warn, "policy" => Pal.Series[2], _ => Pal.Dim };
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", AppTheme.Box(Pal.PanelAlt, 6, null, 0, 8));
        var bar = new ColorRect { Color = col, CustomMinimumSize = new Vector2(3, 0) };
        var txt = UI.VBox(2, UI.Lbl($"{Game.World.StartYear + e.Month / 12}-{e.Month % 12 + 1:D2} · {e.Kind}", 11, col), UI.Lbl(e.Text, 13, Pal.Text, false, HorizontalAlignment.Left, true));
        txt.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        p.AddChild(UI.HBox(8, bar, txt));
        return p;
    }

    // ---------------- the turn plan ----------------
    void UpdatePlanReadouts()
    {
        var c = Game.Player; var sim = Game.Sim!; int n = sim.Plan.Count;
        double pend = n > 0 ? sim.PendingPcCost() : 0, bank = c.PoliticalCapital;
        double next = Math.Min(100, Math.Max(0, bank - pend) + SocietyEngine.PcRegen(Game.World, c));
        _pcBar.Set(bank, pend, next);
        _pcLabel.Text = n > 0 ? $"Political capital  {bank:0}/100  ·  plan −{pend:0}  →  {bank - pend:0}" : $"Political capital  {bank:0}/100";
        _pcBar.TooltipText = $"Banked {bank:0} of 100. It changes only when a turn is played: the plan spends {pend:0}, then about +{SocietyEngine.PcRegen(Game.World, c):0.0} regenerates (marker: about {next:0} next turn).";
        _planBtn.Text = n == 0 ? "Plan · empty" : $"Plan · {n}";
        _planBtn.TooltipText = n == 0 ? "Nothing staged. Changes you make are collected here and applied only when you end the turn." : $"{n} staged action{(n == 1 ? "" : "s")}, {pend:0} political capital. Click to review.";
        _endTurn.Text = n == 0 ? "End turn ▸" : $"End turn ▸ ({n})";
    }

    void OnPlanChanged()
    {
        if (!Game.Running) return;
        UpdatePlanReadouts(); MarkDirty();
        if (_modal == null) _planBox = null;
        else if (_planBox != null && IsInstanceValid(_planBox)) BuildPlan();
    }

    void OnPlanApplied(int staged, int failed)
    {
        string msg = failed == 0 ? $"Turn played: {staged} planned action{(staged == 1 ? "" : "s")} applied." : $"Turn played: {staged - failed} of {staged} planned actions applied; {failed} failed (see the news feed).";
        Toast(msg, failed == 0 ? Pal.Accent : Pal.Warn);
    }

    void EndTurn()
    {
        if (!Game.Running || _modal != null || Game.World.Decisions.Count > 0 || Game.World.GameOver) return;
        var bad = Game.Sim!.PlanItems().Where(i => !i.Ok || !i.Affordable).ToList();
        if (bad.Count > 0) { ShowEndTurnConfirm(bad); return; }
        Game.EndTurn(); MarkDirty();
    }

    void ShowEndTurnConfirm(List<Simulation.PlanItem> bad)
    {
        var box = UI.VBox(10);
        box.AddChild(UI.Lbl("Some planned actions will fail", 22, Pal.Warn, true));
        box.AddChild(UI.Dim("The situation has changed since these were staged. Fix the plan, or play the turn and accept the failures.", 13, true));
        foreach (var i in bad) box.AddChild(UI.Lbl($"✘ {i.Label}" + (i.Note != "" ? $" — {i.Note}" : " — not enough political capital left after earlier items"), 13, Pal.Bad, false, HorizontalAlignment.Left, true));
        box.AddChild(UI.HBox(10, UI.Btn("Review plan", () => { Close(); ShowPlan(); }, true), UI.Btn("End turn anyway", () => { Close(); Game.EndTurn(); MarkDirty(); })));
        Overlay(box);
    }

    void ShowPlan()
    {
        if (_modal != null) return;
        _planBox = UI.VBox(10); BuildPlan(); Overlay(_planBox, 780);
    }

    void BuildPlan()
    {
        var box = _planBox; if (box == null) return;
        foreach (var ch in box.GetChildren().ToList()) { box.RemoveChild(ch); ch.QueueFree(); }
        var sim = Game.Sim!; var c = Game.Player; var items = sim.PlanItems();
        double cost = items.Sum(i => i.PcCost), regen = SocietyEngine.PcRegen(Game.World, c);
        box.AddChild(UI.Lbl("Turn plan", 26, Pal.Text, true));
        box.AddChild(UI.Dim("Staged actions are priced now but applied only when you end the turn, in this order. Change your mind freely: nothing has happened yet, and political capital, the news feed and adviser notes stay as they are until then.", 13, true));
        if (items.Count == 0)
        {
            box.AddChild(UI.Lbl("Nothing staged yet.", 16, Pal.Dim));
            box.AddChild(UI.Dim("Use “Add to plan” on the Budget, Monetary, Policies, Investment, Trade and World map pages.", 13, true));
        }
        else
        {
            box.AddChild(UI.Lbl($"{items.Count} action{(items.Count == 1 ? "" : "s")} · {cost:0} of {c.PoliticalCapital:0} political capital · {Math.Max(0, c.PoliticalCapital - cost):0} left after the turn, about {Math.Min(100, Math.Max(0, c.PoliticalCapital - cost) + regen):0} once it regenerates", 14, cost > c.PoliticalCapital ? Pal.Bad : Pal.Accent, true));
            var list = UI.VBox(4);
            for (int n = 0; n < items.Count; n++)
            {
                var it = items[n]; bool good = it.Ok && it.Affordable;
                var row = UI.HBox(10);
                row.AddChild(UI.Lbl($"{n + 1}.", 14, Pal.Dim));
                var lbl = UI.Lbl(it.Label, 14, good ? Pal.Text : Pal.Bad, false, HorizontalAlignment.Left, true); lbl.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(lbl);
                row.AddChild(Cards.Chip(it.PcCost > 0 ? $"−{it.PcCost:0} PC" : "free", it.PcCost > 0 ? Pal.Warn : Pal.Faint));
                if (!good) row.AddChild(Cards.Chip(it.Ok ? "can't afford" : "will fail", Pal.Bad));
                string key = it.Key; var rm = UI.Btn("Remove", () => Game.Unstage(key), false, 90); row.AddChild(rm);
                if (!good && it.Note != "") row.TooltipText = it.Note;
                list.AddChild(UI.Card(row, Pal.PanelAlt, 8));
            }
            var sc = UI.Scroll(list); sc.SizeFlagsVertical = SizeFlags.ShrinkBegin; sc.CustomMinimumSize = new Vector2(0, Math.Min(250, items.Count * 48 + 6)); box.AddChild(sc);
            var prev = new PreviewPanel { Months = 60 };
            box.AddChild(UI.HBox(10, UI.Btn("Preview 5 years", () => prev.Run(sim.Plan.ToList()), false, 170), UI.Dim("Plan versus carrying on unchanged.", 12)));
            box.AddChild(prev);
        }
        var actions = UI.HBox(10);
        actions.AddChild(UI.Btn("End turn ▸", () => { Close(); EndTurn(); }, true, 150));
        var clear = UI.Btn("Clear plan", () => Game.ClearPlan(), false, 130); clear.Disabled = items.Count == 0; actions.AddChild(clear);
        actions.AddChild(UI.Btn("Close", Close, false, 110));
        box.AddChild(actions);
    }

    // ---------------- modals ----------------
    void Close() { _modal?.QueueFree(); _modal = null; MarkDirty(); }

    Control Overlay(Control content, int width = 640)
    {
        var o = new Control(); o.SetAnchorsPreset(LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f) }; dim.SetAnchorsPreset(LayoutPreset.FullRect); o.AddChild(dim);
        var cc = new CenterContainer(); cc.SetAnchorsPreset(LayoutPreset.FullRect); o.AddChild(cc);
        var card = UI.Card(content, Pal.PanelAlt, 22); card.CustomMinimumSize = new Vector2(width, 0); cc.AddChild(card);
        AddChild(o); _modal = o; Game.Speed = 0; UpdateTop();
        return o;
    }

    void ShowDecision()
    {
        _modal?.QueueFree(); _modal = null;
        var w = Game.World; var d = w.Decisions.FirstOrDefault(); if (d == null) return;
        var box = UI.VBox(10);
        box.AddChild(Cards.Chip("DECISION", Pal.Warn));
        box.AddChild(UI.Lbl(d.Title, 26, Pal.Text, true));
        box.AddChild(UI.Lbl(d.Text, 15, Pal.Dim, false, HorizontalAlignment.Left, true));
        box.AddChild(UI.Dim($"If you do not choose within {Math.Max(0, d.Deadline - w.Month)} months, the default option is applied.", 12, true));
        box.AddChild(UI.Sep());
        for (int i = 0; i < d.Labels.Count; i++)
        {
            int idx = i; string cost = d.Costs[i] > 0 ? $"   (costs {UI.Pct(d.Costs[i], 1)} of GDP)" : "";
            var b = UI.Btn(d.Labels[i] + cost, () => { Game.Sim!.Resolve(d.Id, idx); Close(); if (w.Decisions.Count > 0) ShowDecision(); UpdateFeed(true); }, i == d.DefaultChoice);
            b.Alignment = HorizontalAlignment.Left; b.CustomMinimumSize = new Vector2(580, 0); box.AddChild(b);
        }
        Overlay(box);
    }

    public void Toast(string text, Color? accent = null)
    {
        var p = new PanelContainer(); p.AddThemeStyleboxOverride("panel", AppTheme.Box(Pal.PanelHi, 10, accent ?? Pal.Accent, 1, 12));
        var l = UI.Lbl(text, 14, Pal.Text, false, HorizontalAlignment.Left, true); l.CustomMinimumSize = new Vector2(420, 0); p.AddChild(l);
        _toasts.AddChild(p); EconGame.Audio.Sfx.Ok();
        var tw = CreateTween(); tw.TweenInterval(9.0);
        if (!Settings.ReduceMotion) tw.TweenProperty(p, "modulate:a", 0.0, 0.8);
        tw.TweenCallback(Callable.From(() => p.QueueFree()));
    }

    void CheckHints(World w)
    {
        if (Game.Scenario == null || w.Month == _lastHintMonth) return; _lastHintMonth = w.Month;
        foreach (var hnt in Scenarios.HintsAt(Game.Scenario, w.Month))
            if (w.Month > 0 && Game.ShownHints.Add(Game.Scenario.Id + hnt.Month)) Toast("Tip: " + hnt.Text, Pal.Warn);
    }

    void ShowBriefing()
    {
        var sc = Game.Scenario!;
        var box = UI.VBox(10);
        box.AddChild(Cards.Chip(sc.Kind.ToUpper(), Pal.Accent));
        box.AddChild(UI.Lbl(sc.Name, 26, Pal.Text, true));
        box.AddChild(UI.Lbl(sc.Description, 15, Pal.Dim, false, HorizontalAlignment.Left, true));
        box.AddChild(UI.Lbl("Objectives", 16, Pal.Accent, true));
        foreach (var g in sc.Goals) box.AddChild(UI.Lbl("◦ " + g.Label, 14));
        foreach (var hnt in Scenarios.HintsAt(sc, 0)) box.AddChild(UI.Lbl("Tip: " + hnt.Text, 13, Pal.Warn, false, HorizontalAlignment.Left, true));
        box.AddChild(UI.Btn("Begin", Close, true, 140));
        Overlay(box);
    }

    void ShowEnd()
    {
        _modal?.QueueFree(); _modal = null;
        var w = Game.World; var c = Game.Player; var card = Scorer.Compute(w, c);
        var box = UI.VBox(10);
        bool over = w.GameOver;
        string head = over ? "Game over" : Game.Scenario != null ? "Scenario complete" : "Term complete";
        box.AddChild(UI.Lbl(head, 28, over ? Pal.Bad : Pal.Good, true));
        if (over) box.AddChild(UI.Lbl(w.GameOverReason, 16, Pal.Text, false, HorizontalAlignment.Left, true));
        if (Game.Scenario != null)
        {
            var r = Scenarios.Evaluate(w, Game.Scenario);
            box.AddChild(UI.Lbl(r.Summary, 16, r.Won ? Pal.Good : Pal.Warn, true));
            foreach (var g in r.Goals) box.AddChild(UI.Lbl($"{(g.Met ? "✔" : "✘")} {g.Goal.Label}", 14, g.Met ? Pal.Good : Pal.Bad));
        }
        box.AddChild(UI.Lbl($"Final score {card.Total:0} — grade {card.Grade}", 20, Pal.Accent, true));
        box.AddChild(UI.Dim($"Prosperity {card.Prosperity:0} · Living standards {card.Living:0} · Stability {card.Stability:0} · Sustainability {card.Sustainability:0} · Resilience {card.Resilience:0}", 13, true));
        box.AddChild(UI.HBox(10, UI.Btn("View report", () => { Close(); Navigate("Report"); }, true), UI.Btn("View journal", () => { Close(); Navigate("Journal"); }), UI.Btn("Main menu", () => { Game.Quit(); Main.Instance!.ShowMainMenu(); })));
        Overlay(box);
    }

    public void ShowGlossary(string? term = null)
    {
        _modal?.QueueFree(); _modal = null;
        Overlay(new GlossaryPanel(Close, Navigate, term), 980);
    }

    void ShowHelp()
    {
        var box = UI.VBox(8);
        box.AddChild(UI.H1("How to play"));
        foreach (var line in new[]
        {
            "Enter — end turn · Space — pause / resume · 1-4 — game speed · Run to ▾ — let the clock run to a date · Ctrl+Tab or PageUp/PageDown — change page · Tab — move keyboard focus · Esc — menu · F1 — this help · F2 — glossary",
            "Dashboard: click a headline tile to see why it moved. Hover for a quick explanation.",
            "Budget: drag sliders to draft changes, preview five years ahead, then enact. Cuts cost more political capital than rises.",
            "Policies and Investment: reforms and projects take years; the legislature may refuse and projects can overrun.",
            "Trade and World map: deals, tariffs, sanctions and aid ripple through partners. Drag to pan, scroll to zoom, switch to the 3D globe.",
            "Advisers disagree on purpose. Elections (democracies) and coups (autocracies) end your term if you lose public support.",
        }) box.AddChild(UI.Lbl(line, 14, Pal.Dim, false, HorizontalAlignment.Left, true));
        box.AddChild(UI.HBox(10, UI.Btn("Close", Close, true, 120), UI.Btn("Glossary (F2)", () => ShowGlossary(), false, 160)));
        Overlay(box, 720);
    }

    void ShowMenu()
    {
        if (_modal != null) { Close(); return; }
        var box = UI.VBox(10);
        box.AddChild(UI.H1("Game menu"));
        box.AddChild(UI.Btn("Resume", Close, true, 280));
        box.AddChild(UI.Btn("Glossary (F2)", () => ShowGlossary(), false, 280));
        box.AddChild(UI.Btn("Save to slot 1", () => { Game.Save("slot1"); Close(); }, false, 280));
        box.AddChild(UI.Btn("Settings", () => { Close(); Main.Instance!.ShowSettings(() => Main.Instance!.ShowGame()); }, false, 280));
        box.AddChild(UI.Btn("Quit to main menu", () => { Game.Save("auto"); Game.Quit(); Main.Instance!.ShowMainMenu(); }, false, 280));
        Overlay(box, 360);
    }
}
