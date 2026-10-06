using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.Core.Engine;
using Sim.Core.Model;
using Sim.Core.Policy;
using Sim.Core.Regions;
using EconGame.App;
using EconGame.Map;
using EconGame.Ui;

namespace EconGame.Views;

/// <summary>Navigable 2D world map: choropleth overlays, trade flows, event pins, country briefings and regional drill-down.</summary>
public partial class WorldMapView : View
{
    public override string Title => "World map";
    readonly MapCanvas _map2d = new();
    readonly GlobeCanvas _globe = new();
    readonly OptionButton _mode = new();
    IMapSurface _map => _globe.Visible ? _globe : _map2d;
    readonly OptionButton _overlay = new(), _regionMetric = new();
    readonly LineEdit _search = new();
    readonly CheckBox _flows = new() { Text = "Trade flows", ButtonPressed = true }, _events = new() { Text = "Events", ButtonPressed = true };
    readonly VBoxContainer _panel = new(), _bookmarks = new();
    readonly Legend _legend = new();
    CountryState? _sel; string? _regionFocus; bool _regionMode;
    int _sig = -1, _built;
    OverlayDef Ov => OverlayDef.All[Math.Max(0, _overlay.Selected)];

    public WorldMapView()
    {
        MapData.EnsureLoaded();
        var root = UI.VBox(10); root.SizeFlagsVertical = SizeFlags.ExpandFill; AddChild(root);
        root.AddChild(UI.H1("World map"));

        foreach (var o in OverlayDef.All) _overlay.AddItem(o.Title);
        _overlay.Selected = 0; _overlay.ItemSelected += _ => { _sig = -1; Refresh(); };
        _search.PlaceholderText = "Search country…"; _search.CustomMinimumSize = new Vector2(190, 0);
        _search.TextSubmitted += t => GoTo(t);
        foreach (var m in new[] { "GDP per head", "Growth since start", "Unemployment" }) _regionMetric.AddItem(m);
        _regionMetric.ItemSelected += _ => { _sig = -1; Refresh(); };
        _flows.Toggled += _ => { _sig = -1; Refresh(); }; _events.Toggled += _ => { _sig = -1; Refresh(); };
        _bookmarks.AddThemeConstantOverride("separation", 4);
        _mode.AddItem("2D map"); _mode.AddItem("3D globe"); _mode.ItemSelected += i => SetMode((int)i);
        var bar = UI.HBox(10, UI.Lbl("View", 14, Pal.Dim), _mode, UI.Lbl("Overlay", 14, Pal.Dim), _overlay, _search, _flows, _events, UI.Btn("Reset view", () => { _map.Fit(); _regionMode = false; _sig = -1; Refresh(); }), UI.Spacer(0, 0, true));
        root.AddChild(bar);
        var bm = UI.HBox(6); bm.AddChild(UI.Dim("Bookmarks", 12)); var bmBox = new HBoxContainer(); bmBox.AddThemeConstantOverride("separation", 4); _bookmarkRow = bmBox; bm.AddChild(bmBox); root.AddChild(bm);

        var body = UI.HBox(12); body.SizeFlagsVertical = SizeFlags.ExpandFill; root.AddChild(body);
        var mapBox = UI.VBox(6); mapBox.SizeFlagsHorizontal = SizeFlags.ExpandFill; mapBox.SizeFlagsVertical = SizeFlags.ExpandFill;
        var stack = new Control { CustomMinimumSize = new Vector2(500, 380), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, ClipContents = true };
        foreach (Control c in new Control[] { _map2d, _globe }) { c.SetAnchorsPreset(LayoutPreset.FullRect); stack.AddChild(c); }
        _globe.Visible = false;
        mapBox.AddChild(UI.Fill(UI.Card(stack, Pal.Bg, 2), true, true)); _legend.CustomMinimumSize = new Vector2(0, 38); mapBox.AddChild(_legend);
        body.AddChild(mapBox);
        _panel.AddThemeConstantOverride("separation", 8);
        var pc = UI.Card(UI.Scroll(_panel)); pc.CustomMinimumSize = new Vector2(350, 0); pc.SizeFlagsVertical = SizeFlags.ExpandFill; body.AddChild(pc);

        foreach (var surf in new IMapSurface[] { _map2d, _globe })
        {
            surf.HoverText = sh => HoverInfo(sh);
            surf.Clicked += sh => { if (sh != null) Select(sh.Id); };
            surf.RegionClicked += sh => { _regionFocus = sh.Id; _sig = -1; Refresh(); };
        }
    }

    HBoxContainer _bookmarkRow = new();

    public void SetMode(int m)
    {
        _mode.Selected = m; _globe.Visible = m == 1; _map2d.Visible = m == 0;
        _sig = -1; if (_sel != null && MapData.Find(_sel.Id) is Shape sh) _map.FocusOn(sh); else _map.Fit();
        Refresh();
    }

    public override void _Ready() { if (Game.Running) { _map.Fit(); SelectPlayer(); } }

    public void ShowRegionsDemo() { _regionMode = true; _regionFocus = null; var sh = _sel == null ? null : MapData.Find(_sel.Id); if (sh != null) _map.FocusOn(sh); _sig = -1; Refresh(); }

    void SelectPlayer() { Select(Game.Player.Id, false); }

    string HoverInfo(Shape s)
    {
        var c = Game.World.Find(s.Id);
        if (c == null) return s.Name + "\n(not simulated)";
        var o = Ov;
        return $"{c.Name}\n{o.Title}: {o.Format(o.Value(Game.World, c))}\nGDP {Money.Gbp(c.GdpUsdBn)} · growth {UI.Pct(c.GdpGrowth)}";
    }

    void GoTo(string text)
    {
        var w = Game.World;
        var m = w.Countries.FirstOrDefault(c => c.Name.Equals(text, StringComparison.OrdinalIgnoreCase) || c.Id.Equals(text, StringComparison.OrdinalIgnoreCase))
                ?? w.Countries.FirstOrDefault(c => c.Name.Contains(text, StringComparison.OrdinalIgnoreCase));
        if (m != null) Select(m.Id);
        else { var s = MapData.Countries.FirstOrDefault(x => x.Name.Contains(text, StringComparison.OrdinalIgnoreCase)); if (s != null) { _map.FocusOn(s); _map.Layer.Highlight(s.Id, Pal.Accent); ShowUnsimulated(s); } }
    }

    public void Select(string id, bool focus = true)
    {
        var c = Game.World.Find(id); var shape = MapData.Find(id);
        if (c == null) { _sel = null; if (shape != null) { _map.Layer.Highlight(id, Pal.Faint); ShowUnsimulated(shape); _map.FocusOn(shape); } return; }
        _sel = c; _regionFocus = null; _regionMode = false;
        if (focus && shape != null) _map.FocusOn(shape);
        _sig = -1; Refresh();
    }

    public override void Refresh()
    {
        if (!Game.Running) return;
        var w = Game.World;
        int sig = w.Month / 3 * 100000 + _overlay.Selected * 1000 + (_flows.ButtonPressed ? 1 : 0) * 100 + (_events.ButtonPressed ? 1 : 0) * 10 + (_regionMode ? 1 : 0) + (_sel?.Id.GetHashCode() ?? 0) % 997 * 7 + (_regionFocus?.GetHashCode() ?? 0) % 31 + _regionMetric.Selected * 13;
        if (sig == _sig) return; _sig = sig;
        if (_built == 0) { _built = 1; _map.Fit(); }
        var o = Ov;
        var byId = w.Countries.ToDictionary(c => c.Id);
        foreach (var L in new[] { _map2d.Layer, _globe.Layer }) L.SetFill(id => byId.TryGetValue(id, out var c) ? o.Colour(o.Value(w, c)) : null);
        _legend.Set(o);
        foreach (var L in new[] { _map2d.Layer, _globe.Layer }) L.Highlight(_sel?.Id, Colors.White);

        var flows = new List<Flow>(); var pins = new List<Pin>();
        var me = Game.Player; var meShape = MapData.Find(me.Id);
        if (_flows.ButtonPressed && meShape != null && w.Trade.W.Length > 0)
        {
            int mi = w.Countries.IndexOf(me);
            foreach (var j in Enumerable.Range(0, w.Countries.Count).Where(j => j != mi).OrderByDescending(j => w.Trade.W[mi][j]).Take(8))
            {
                var ps = MapData.Find(w.Countries[j].Id); if (ps == null) continue;
                flows.Add(new Flow { From = meShape.Centroid, To = ps.Centroid, Width = (float)Math.Clamp(w.Trade.W[mi][j] * 60, 1.2, 7), Color = new Color(Pal.Series[1], 0.75f) });
            }
        }
        if (meShape != null) pins.Add(new Pin { Pos = meShape.Centroid, Color = Colors.White, Text = $"{me.Name} (you)" });
        if (_events.ButtonPressed)
        {
            int k = 0;
            foreach (var e in w.Log.Where(l => w.Month - l.Month <= 12 && l.Country != "WORLD" && l.Country != "" && l.Kind is "event" or "crisis").TakeLast(30))
            {
                var s = MapData.Find(e.Country); if (s == null) continue;
                pins.Add(new Pin { Pos = s.Centroid + new Vector2(0.9f * ((k % 3) - 1), 0.9f * ((k / 3) % 3 - 1)), Color = e.Kind == "crisis" ? Pal.Bad : Pal.Warn, Text = $"{w.Find(e.Country)?.Name ?? e.Country}: {Trim(e.Text)}" });
                k++;
            }
        }
        _map2d.SetOverlay(flows, pins); _globe.SetOverlay(flows, pins);

        // regions
        if (_sel != null && _regionMode && Regions.Has(_sel.Id))
        {
            var stats = Regions.Compute(_sel).ToDictionary(r => r.Def.Id);
            Func<RegionStat, double> val = _regionMetric.Selected switch { 0 => r => r.GdpPerHeadRel, 1 => r => r.GrowthSinceStart, _ => r => r.Unemployment };
            double mn = stats.Values.Min(val), mx = stats.Values.Max(val);
            bool good = _regionMetric.Selected == 2;
            foreach (var L in new[] { _map2d.Layer, _globe.Layer }) L.ShowRegions(_sel.Id, rid => { var v = val(stats[rid]); float t = (float)((v - mn) / Math.Max(1e-9, mx - mn)); return good ? Pal.Diverge(1 - 2 * t) : Pal.Ramp(t); });
        }
        else foreach (var L in new[] { _map2d.Layer, _globe.Layer }) L.ShowRegions(null, null);
        _globe.Refreshed();

        BuildBookmarks();
        BuildPanel();
    }

    static string Trim(string s) => s.Length > 90 ? s[..87] + "…" : s;

    void BuildBookmarks()
    {
        foreach (var ch in _bookmarkRow.GetChildren()) ch.QueueFree();
        foreach (var id in Settings.Bookmarks.OrderBy(x => x))
        {
            var c = Game.World.Find(id); if (c == null) continue; string cid = id;
            _bookmarkRow.AddChild(UI.Btn(c.Name, () => Select(cid)));
        }
        if (Settings.Bookmarks.Count == 0) _bookmarkRow.AddChild(UI.Dim("none yet - star a country in its briefing", 12));
    }

    void ShowUnsimulated(Shape s)
    {
        foreach (var ch in _panel.GetChildren()) ch.QueueFree();
        _panel.AddChild(UI.Lbl(s.Name, 24, Pal.Text, true));
        _panel.AddChild(UI.Dim("Not part of the simulated roster in this version. Shown for reference; trade with the rest of the world is modelled in aggregate.", 13, true));
    }

    void BuildPanel()
    {
        foreach (Control keep in new Control[] { _regionMetric, _result }) keep.GetParent()?.RemoveChild(keep);   // reused widgets must survive the rebuild
        foreach (var ch in _panel.GetChildren()) ch.QueueFree();
        if (_sel == null) { _panel.AddChild(UI.Dim("Click a country to open its briefing.", 14, true)); return; }
        var c = _sel; var w = Game.World; var me = Game.Player; bool mine = c.Id == me.Id;
        _panel.AddChild(UI.Lbl(c.Name, 26, Pal.Text, true));
        var chips = UI.HBox(6, Cards.Chip(c.Region, Pal.Faint), Cards.Chip(c.Archetype, Pal.Accent), Cards.Chip(c.Gov, c.Gov == "democracy" ? Pal.Good : Pal.Warn));
        if (mine) chips.AddChild(Cards.Chip("YOU", Pal.Series[1]));
        _panel.AddChild(chips);
        var star = Settings.Bookmarks.Contains(c.Id);
        _panel.AddChild(UI.Btn(star ? "★ Bookmarked" : "☆ Bookmark", () => { if (!Settings.Bookmarks.Remove(c.Id)) Settings.Bookmarks.Add(c.Id); Settings.Save(); _sig = -1; Refresh(); }));

        var grid = new GridContainer { Columns = 4 }; grid.AddThemeConstantOverride("h_separation", 12); grid.AddThemeConstantOverride("v_separation", 4);
        void S(string k, string v, Color? col = null) { grid.AddChild(UI.Dim(k, 12)); grid.AddChild(UI.Lbl(v, 14, col ?? Pal.Text, true)); }
        S("GDP", Money.Gbp(c.GdpUsdBn)); S("Per head", $"${c.GdpPerCapitaUsd:N0}");
        S("Growth", UI.Pct(c.GdpGrowth), c.GdpGrowth < 0 ? Pal.Bad : null); S("Inflation", UI.Pct(c.Inflation), c.Inflation > 0.07 ? Pal.Bad : null);
        S("Unemployment", UI.Pct(c.Unemp)); S("Policy rate", UI.Pct(c.PolicyRate, 2));
        S("Debt", UI.Pct(c.DebtToGdp, 0), c.DebtToGdp > 1 ? Pal.Bad : null); S("Deficit", UI.Pct(c.DeficitToGdp));
        S("Approval", UI.Pct(c.Approval, 0)); S("Stability", UI.Pct(c.Stability, 0));
        S("Yield", UI.Pct(c.Yield10)); S("Current acct", UI.Pct(c.CaToGdp, 1));
        _panel.AddChild(grid);
        if (c.InDefault) _panel.AddChild(UI.Lbl("⚠ In sovereign default", 14, Pal.Bad, true));

        if (!mine)
        {
            int mi = w.Countries.IndexOf(me), ci = w.Countries.IndexOf(c);
            var mineRel = WorldEngine.Rel(w, me.Id, c.Id); var theirs = WorldEngine.Rel(w, c.Id, me.Id);
            _panel.AddChild(UI.Sep());
            _panel.AddChild(UI.H2("Relations"));
            _panel.AddChild(UI.Dim($"{UI.Pct(w.Trade.W[mi][ci], 1)} of your exports go here. " + (mineRel.Alliance ? "Allied. " : "") + (mineRel.Deal ? "Trade agreement in place. " : "") +
                (mineRel.ExtraTariff > 0 ? $"Your tariff {UI.Pct(mineRel.ExtraTariff, 0)}. " : "") + (theirs.ExtraTariff > 0 ? $"Their tariff {UI.Pct(theirs.ExtraTariff, 0)}. " : "") + (mineRel.Sanction ? "You sanction them. " : "") + (theirs.Sanction ? "They sanction you." : ""), 13, true));
            var row = new HFlowContainer(); row.AddThemeConstantOverride("h_separation", 6); row.AddThemeConstantOverride("v_separation", 6);
            void A(string label, Command cmd, bool accent = false) { var dry = CommandProcessor.Apply(w, cmd, true); var b = UI.Btn($"{label} ({dry.PcCost:0})", () => Do(cmd), accent); b.Disabled = !dry.Ok && dry.PcCost == 0; row.AddChild(b); }
            if (!mineRel.Deal) A("Trade deal", Command.TradeDeal(me.Id, c.Id), true);
            if (!mineRel.Alliance) A("Alliance", Command.Alliance(me.Id, c.Id));
            A(mineRel.Sanction ? "Lift sanctions" : "Sanction", Command.Sanction(me.Id, c.Id, !mineRel.Sanction));
            A("Tariff 10%", Command.Tariff(me.Id, c.Id, mineRel.ExtraTariff > 0 ? 0 : 0.10));
            A("Aid 0.2%", Command.Aid(me.Id, c.Id, 0.002));
            _panel.AddChild(row);
            _panel.AddChild(UI.Dim("Numbers in brackets are political capital.", 11));
            _panel.AddChild(_result);
        }

        if (Regions.Has(c.Id))
        {
            _panel.AddChild(UI.Sep());
            _panel.AddChild(UI.H2("Regions"));
            _panel.AddChild(UI.Dim("Illustrative regional split: output is the national sector mix re-allocated through generated location weights (not statistics).", 12, true));
            var tog = UI.Btn(_regionMode ? "Hide regional map" : "Show regional map", () => { _regionMode = !_regionMode; if (_regionMode) { var sh = MapData.Find(c.Id); if (sh != null) _map.FocusOn(sh); } _sig = -1; Refresh(); }, !_regionMode);
            _panel.AddChild(UI.HBox(8, tog, _regionMetric));
            var stats = Regions.Compute(c).OrderByDescending(r => r.GdpShare).ToList();
            if (_regionFocus != null && stats.FirstOrDefault(r => r.Def.Id == _regionFocus) is RegionStat f)
            {
                _panel.AddChild(UI.Lbl(f.Def.Name, 18, Pal.Accent, true));
                _panel.AddChild(UI.Dim($"{UI.Pct(f.GdpShare, 1)} of GDP · {UI.Pct(f.PopShare, 1)} of people · {f.Pop:0.0}m · output per head {f.GdpPerHeadRel:0.00}× national · growth since start {UI.Pct(f.GrowthSinceStart, 1)} · unemployment {UI.Pct(f.Unemployment, 1)} · specialised in {f.LeadingSector.ToLower()}", 13, true));
            }
            var list = UI.VBox(2);
            foreach (var r in stats.Take(10))
            {
                string rid = r.Def.Id;
                var b = new Button { Text = $"{r.Def.Name}   {UI.Pct(r.GdpShare, 1)}  ·  {r.GdpPerHeadRel:0.00}×  ·  u {UI.Pct(r.Unemployment, 1)}", Flat = true, Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.None };
                b.AddThemeFontSizeOverride("font_size", 12); b.Pressed += () => { _regionFocus = rid; _sig = -1; Refresh(); };
                list.AddChild(b);
            }
            _panel.AddChild(list);
        }

        _panel.AddChild(UI.Sep());
        _panel.AddChild(UI.H2("Recent news"));
        var items = w.Log.Where(l => l.Country == c.Id).TakeLast(6).Reverse().ToList();
        if (items.Count == 0) _panel.AddChild(UI.Dim("Nothing notable.", 12));
        foreach (var e in items) _panel.AddChild(UI.Lbl($"{w.StartYear + e.Month / 12}-{e.Month % 12 + 1:D2} {e.Text}", 12, Pal.Dim, false, HorizontalAlignment.Left, true));
    }

    readonly Label _result = UI.Dim("", 12, true);

    void Do(Command cmd)
    {
        var r = Game.Sim!.Execute(cmd); _result.Text = (r.Ok ? "✔ " : "✘ ") + r.Message; _sig = -1; Game.NotifyChanged(); Refresh();
    }

    sealed partial class Legend : Control
    {
        OverlayDef? _o;
        public void Set(OverlayDef o) { _o = o; QueueRedraw(); }
        public override void _Draw()
        {
            if (_o == null) return;
            var font = ThemeDB.FallbackFont; int fs = Mathf.RoundToInt(12 * Pal.TextScale);
            DrawString(font, new Vector2(0, 14), _o.Title, HorizontalAlignment.Left, -1, fs, Pal.Dim);
            float x0 = 190, w = Mathf.Min(360, Size.X - x0 - 120);
            for (int i = 0; i < 60; i++) { double v = _o.Min + (_o.Max - _o.Min) * i / 59; DrawRect(new Rect2(x0 + i * w / 60, 4, w / 60 + 1, 12), _o.Colour(v)); }
            DrawString(font, new Vector2(x0 - 6, 28), _o.Format(_o.Min), HorizontalAlignment.Left, -1, fs, Pal.Dim);
            DrawString(font, new Vector2(x0 + w - 30, 28), _o.Format(_o.Max), HorizontalAlignment.Left, -1, fs, Pal.Dim);
            DrawRect(new Rect2(x0 + w + 20, 4, 12, 12), MapLayer.Unsimulated); DrawString(font, new Vector2(x0 + w + 38, 15), "not simulated", HorizontalAlignment.Left, -1, fs, Pal.Faint);
        }
    }
}
