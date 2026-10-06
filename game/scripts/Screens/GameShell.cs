using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.Core.Events;
using Sim.Core.Model;
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
    View? _current; string _currentName = "";
    Control _pageHost = new();
    Label _date = new(), _country = new(), _pcLabel = new(), _score = new(), _alert = new();
    ProgressBar _pc = new();
    readonly Dictionary<int, Button> _speedBtns = new();
    VBoxContainer _feed = new();
    string _feedFilter = "all";
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
        Game.Ticked += MarkDirty; Game.Changed += MarkDirty;
        Game.Speed = Settings.DefaultSpeed == 0 ? 0 : 0; // always start paused so the player can read the briefing
        Navigate("Dashboard");
        UpdateTop(); UpdateFeed(true);
        if (Game.World.Decisions.Count > 0) ShowDecision();
        if (Game.Scenario != null) ShowBriefing();
    }

    public override void _ExitTree() { Game.DecisionPending -= ShowDecision; Game.Ended -= ShowEnd; Game.Ticked -= MarkDirty; Game.Changed -= MarkDirty; }

    // ---------------- layout ----------------
    Control BuildTopBar()
    {
        var bar = new PanelContainer();
        bar.AddThemeStyleboxOverride("panel", AppTheme.Box(Pal.Panel, 0, Pal.Border, 0, 10));
        var h = UI.HBox(14);
        _country = UI.Lbl(Game.Player.Name, 20, Pal.Text, true);
        _date = UI.Lbl("", 16, Pal.Dim);
        h.AddChild(_country); h.AddChild(_date);
        h.AddChild(UI.Spacer(10, 0));
        var speeds = UI.HBox(4);
        string[] icons = { "II", "1×", "2×", "3×", "4×" };
        for (int i = 0; i < icons.Length; i++)
        {
            int sp = i; var b = UI.Btn(icons[i], () => SetSpeed(sp), false, 44); _speedBtns[i] = b; speeds.AddChild(b);
            b.TooltipText = i == 0 ? "Pause (Space)" : $"Speed {i} (key {i})";
        }
        h.AddChild(speeds);
        h.AddChild(UI.Btn("+1 month", () => { Game.Speed = 0; Game.Step(); MarkDirty(); }, false, 0));
        _ticker = UI.Lbl("", 13, Pal.Dim); _ticker.ClipText = true; _ticker.CustomMinimumSize = new Vector2(120, 0); _ticker.SizeFlagsHorizontal = SizeFlags.ExpandFill; h.AddChild(_ticker);
        _alert = UI.Lbl("", 14, Pal.Warn, true); h.AddChild(_alert);
        var pcBox = UI.VBox(2); _pcLabel = UI.Lbl("Political capital", 12, Pal.Dim);
        _pc = new ProgressBar { MaxValue = 100, ShowPercentage = false, CustomMinimumSize = new Vector2(160, 10) };
        _pc.AddThemeStyleboxOverride("background", AppTheme.Box(Pal.Border, 5)); _pc.AddThemeStyleboxOverride("fill", AppTheme.Box(Pal.Accent, 5));
        pcBox.AddChild(_pcLabel); pcBox.AddChild(_pc); h.AddChild(pcBox);
        _score = UI.Lbl("", 18, Pal.Accent, true); h.AddChild(_score);
        h.AddChild(UI.Btn("Menu", ShowMenu));
        bar.AddChild(h);
        return bar;
    }

    Control BuildNav()
    {
        var nav = new PanelContainer { CustomMinimumSize = new Vector2(180, 0) };
        nav.AddThemeStyleboxOverride("panel", AppTheme.Box(Pal.Panel, 0, Pal.Border, 0, 8));
        var v = UI.VBox(4);
        foreach (var (name, _) in _pages)
        {
            string n = name;
            var b = new Button { Text = name, Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.None, Flat = true };
            b.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(16 * Pal.TextScale));
            b.Pressed += () => Navigate(n);
            _navBtns[name] = b; v.AddChild(b);
        }
        v.AddChild(UI.Spacer(0, 0, true));
        nav.AddChild(UI.Scroll(v));
        return nav;
    }

    Control BuildFeed()
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(330, 0) };
        panel.AddThemeStyleboxOverride("panel", AppTheme.Box(Pal.Panel, 0, Pal.Border, 0, 10));
        var v = UI.VBox(8);
        v.AddChild(UI.H2("News & advisers"));
        var chips = UI.HBox(4);
        foreach (var (key, label) in new[] { ("all", "All"), ("advisor", "Advisers"), ("event", "Events"), ("policy", "Policy") })
        {
            string k = key; chips.AddChild(UI.Btn(label, () => { _feedFilter = k; UpdateFeed(true); }));
        }
        v.AddChild(chips);
        _feed.AddThemeConstantOverride("separation", 6);
        v.AddChild(UI.Scroll(_feed));
        panel.AddChild(v);
        return panel;
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
            kv.Value.AddThemeStyleboxOverride("normal", AppTheme.Box(sel ? Pal.PanelHi : new Color(0, 0, 0, 0), 7, sel ? Pal.Accent : null, sel ? 1 : 0, 10));
            kv.Value.AddThemeColorOverride("font_color", sel ? Colors.White : Pal.Dim);
        }
        view.Refresh();
    }

    public void MarkDirty() => _dirty = true;

    /// <summary>Closes any open popup (used by the self-test and when a decision is resolved elsewhere).</summary>
    public void DismissModal() { _modal?.QueueFree(); _modal = null; }

    void SetSpeed(int s)
    {
        if (Game.World.Decisions.Count > 0 || Game.World.GameOver) return;
        Game.Speed = s; UpdateTop();
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
        else if (k.Keycode == Key.Tab) { int i = _pages.FindIndex(p => p.name == _currentName); Navigate(_pages[(i + 1) % _pages.Count].name); }
        else if (k.Keycode == Key.Escape) ShowMenu();
        else if (k.Keycode == Key.F1) ShowHelp();
    }

    void UpdateTop()
    {
        var w = Game.World; var c = Game.Player;
        _country.Text = c.Name;
        string[] mn = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        _date.Text = $"{mn[w.MonthOfYear - 1]} {w.Year}" + (Game.Scenario != null ? $"  ·  {Math.Max(0, Game.Scenario.Years * 12 - w.Month) / 12}y left" : "");
        foreach (var kv in _speedBtns)
        {
            bool on = kv.Key == Game.Speed;
            kv.Value.AddThemeStyleboxOverride("normal", AppTheme.Box(on ? Pal.Accent.Darkened(0.3f) : Pal.PanelAlt, 7, on ? Pal.Accent : Pal.Border, 1, 8));
        }
        _pc.Value = c.PoliticalCapital; _pcLabel.Text = $"Political capital  {c.PoliticalCapital:0}/100";
        var sc = Scorer.Compute(w, c); _score.Text = $"{sc.Grade} {sc.Total:0}";
        _alert.Text = w.Decisions.Count > 0 ? "⚠ Decision required" : Game.Speed == 0 ? "Paused" : "";
        var last = w.Log.LastOrDefault(l => (l.Country == w.PlayerId || l.Country == "WORLD") && l.Kind is "event" or "crisis" or "news");
        _ticker.Text = last == null ? "" : "▸ " + last.Text;
        CheckHints(w);
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
        var tw = CreateTween(); tw.TweenInterval(9.0); tw.TweenProperty(p, "modulate:a", 0.0, 0.8); tw.TweenCallback(Callable.From(() => p.QueueFree()));
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
        box.AddChild(UI.HBox(10, UI.Btn("View report", () => { Close(); Navigate("Report"); }, true), UI.Btn("Main menu", () => { Game.Quit(); Main.Instance!.ShowMainMenu(); })));
        Overlay(box);
    }

    void ShowHelp()
    {
        var box = UI.VBox(8);
        box.AddChild(UI.H1("How to play"));
        foreach (var line in new[]
        {
            "Space — pause / resume · 1-4 — game speed · Tab — next page · Esc — menu · F1 — this help",
            "Dashboard: click a headline tile to see why it moved. Hover for a quick explanation.",
            "Budget: drag sliders to draft changes, preview five years ahead, then enact. Cuts cost more political capital than rises.",
            "Policies and Investment: reforms and projects take years; the legislature may refuse and projects can overrun.",
            "Trade and World map: deals, tariffs, sanctions and aid ripple through partners. Drag to pan, scroll to zoom, switch to the 3D globe.",
            "Advisers disagree on purpose. Elections (democracies) and coups (autocracies) end your term if you lose public support.",
        }) box.AddChild(UI.Lbl(line, 14, Pal.Dim, false, HorizontalAlignment.Left, true));
        box.AddChild(UI.Btn("Close", Close, true, 120));
        Overlay(box, 720);
    }

    void ShowMenu()
    {
        if (_modal != null) { Close(); return; }
        var box = UI.VBox(10);
        box.AddChild(UI.H1("Game menu"));
        box.AddChild(UI.Btn("Resume", Close, true, 280));
        box.AddChild(UI.Btn("Save to slot 1", () => { Game.Save("slot1"); Close(); }, false, 280));
        box.AddChild(UI.Btn("Settings", () => { Close(); Main.Instance!.ShowSettings(() => Main.Instance!.ShowGame()); }, false, 280));
        box.AddChild(UI.Btn("Quit to main menu", () => { Game.Save("auto"); Game.Quit(); Main.Instance!.ShowMainMenu(); }, false, 280));
        Overlay(box, 360);
    }
}
