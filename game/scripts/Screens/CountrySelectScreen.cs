using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.Core.Model;
using Sim.Core.Scoring;
using EconGame.App;
using EconGame.Ui;

namespace EconGame.Screens;

public partial class CountrySelectScreen : Control
{
    List<CountryState> _roster = new();
    DataTable _table = new();
    CountryState? _sel;
    VBoxContainer _detail = new();
    OptionButton _diff = new();
    LineEdit _search = new();
    OptionButton _region = new();
    Button _start = new();
    readonly PanelContainer _spot = new();
    readonly Emblem _emblem = new();
    readonly Label _hiddenNote = UI.Dim("Selected country is hidden by the current filter.", 12);
    readonly VBoxContainer _spotBody = new();

    public override void _Ready()
    {
        _roster = Game.Roster();
        var root = UI.VBox(12); root.SetAnchorsPreset(LayoutPreset.FullRect);
        var margin = UI.Margin(root, 22); margin.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(margin);

        root.AddChild(UI.HBox(12, UI.Btn("← Back", () => Main.Instance!.ShowMainMenu()), UI.H1("Choose your country")));

        var body = UI.HBox(16); body.SizeFlagsVertical = SizeFlags.ExpandFill; root.AddChild(body);

        // ---- left: filters + table ----
        var left = UI.VBox(10); left.SizeFlagsHorizontal = SizeFlags.ExpandFill; left.SizeFlagsStretchRatio = 1.5f; body.AddChild(left);

        // spotlight: the selected country, pinned above the browse list (the full data stays on the right)
        _spot.AddThemeStyleboxOverride("panel", AppTheme.Box(Pal.PanelHi, 12, Pal.Accent, 2, 14));
        _spotBody.AddThemeConstantOverride("separation", 4); _spotBody.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _spot.AddChild(UI.HBox(16, _emblem, _spotBody));
        left.AddChild(_spot);

        _search.PlaceholderText = "Search countries…"; _search.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _search.TextChanged += _ => OnFilterChanged();
        _region.AddItem("All regions");
        foreach (var r in _roster.Select(c => c.Region).Distinct().OrderBy(x => x)) _region.AddItem(r);
        _region.ItemSelected += _ => OnFilterChanged();
        left.AddChild(UI.HBox(8, _search, _region));

        _table.Columns = new List<Column>
        {
            new() { Title = "Country", Width = 0, Text = o => ((CountryState)o).Name, Key = o => ((CountryState)o).Name },
            new() { Title = "GDP (£)", Width = 90, Align = HorizontalAlignment.Right, Text = o => Money.Gbp(((CountryState)o).GdpUsdBn), Key = o => ((CountryState)o).GdpUsdBn },
            new() { Title = "Per head", Width = 80, Align = HorizontalAlignment.Right, Text = o => $"${((CountryState)o).GdpPerCapitaUsd / 1000:0.0}k", Key = o => ((CountryState)o).GdpPerCapitaUsd },
            new() { Title = "Infl", Width = 60, Align = HorizontalAlignment.Right, Text = o => UI.Pct(((CountryState)o).Inflation, 1), Key = o => ((CountryState)o).Inflation,
                    Tint = o => ((CountryState)o).Inflation > 0.08 ? Pal.Bad : null },
            new() { Title = "Debt", Width = 60, Align = HorizontalAlignment.Right, Text = o => UI.Pct(((CountryState)o).DebtToGdp, 0), Key = o => ((CountryState)o).DebtToGdp,
                    Tint = o => ((CountryState)o).DebtToGdp > 1.0 ? Pal.Bad : null },
            new() { Title = "Difficulty", Width = 90, Text = o => Stars(Rating((CountryState)o)), Key = o => Rating((CountryState)o) },
        };
        _table.Filter = o =>
        {
            var c = (CountryState)o;
            if (_region.Selected > 0 && c.Region != _region.GetItemText(_region.Selected)) return false;
            return _search.Text.Length == 0 || c.Name.Contains(_search.Text, StringComparison.OrdinalIgnoreCase) || c.Id.Contains(_search.Text, StringComparison.OrdinalIgnoreCase);
        };
        _table.SizeFlagsVertical = SizeFlags.ExpandFill;
        _table.RowKey = o => ((CountryState)o).Id;
        _table.RowSelected += o => Select((CountryState)o);
        left.AddChild(_table);
        _table.SetRows(_roster.Cast<object>());

        // ---- right: detail ----
        var right = UI.VBox(10); right.SizeFlagsHorizontal = SizeFlags.ExpandFill; right.SizeFlagsStretchRatio = 1.2f; body.AddChild(right);
        _detail.AddThemeConstantOverride("separation", 8);
        var card = UI.Card(UI.Scroll(_detail)); card.SizeFlagsVertical = SizeFlags.ExpandFill; right.AddChild(card);

        foreach (var d in new[] { "Sandbox (no game over)", "Easy", "Normal", "Hard" }) _diff.AddItem(d);
        _diff.Selected = 0;
        _start = UI.Btn("Start as this country →", Start, true, 260); _start.Disabled = true;
        right.AddChild(UI.HBox(10, UI.Lbl("Difficulty", 14, Pal.Dim), _diff, UI.Spacer(0, 0, true), _start));

        _table.Select(_roster.First(c => c.Id == "GBR"));
    }

    void OnFilterChanged()
    {
        _table.Refresh();
        UpdateHiddenNote();
    }

    void UpdateHiddenNote() => _hiddenNote.Visible = _sel != null && !_table.View.Any(r => ((CountryState)r).Id == _sel.Id);

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey k || !k.Pressed) return;
        if (k.Keycode == Key.Down || k.Keycode == Key.Up)
        {
            if (_table.Step(k.Keycode == Key.Down ? 1 : -1) is { } next) _table.Select(next);
            GetViewport().SetInputAsHandled();
        }
        else if ((k.Keycode == Key.Enter || k.Keycode == Key.KpEnter) && !_start.Disabled && GetViewport().GuiGetFocusOwner() == null) Start();
    }

    static string Stars(double d) => new string('★', (int)Math.Round(d)) + new string('☆', 5 - (int)Math.Round(d));

    /// <summary>1 (comfortable) to 5 (brutal) based on starting vulnerabilities.</summary>
    public static double Rating(CountryState c)
    {
        double s = 1;
        if (c.DebtToGdp > 0.9) s += 0.5; if (c.DebtToGdp > 1.5) s += 0.5;
        if (c.Inflation > 0.08) s += 0.7; if (c.Inflation > 0.3) s += 0.6;
        if (c.Unemp > 0.1) s += 0.4;
        if (c.Corruption > 0.5) s += 0.5;
        if (c.CaToGdp < -0.04) s += 0.4;
        if (c.Archetype is "developing") s += 0.8;
        if (c.Archetype is "resource") s += 0.4;
        if (c.Old > 0.22) s += 0.4;
        if (c.GdpPerCapitaUsd < 5000) s += 0.4;
        return Math.Clamp(s, 1, 5);
    }

    public static (List<string> strengths, List<string> challenges) Profile(CountryState c)
    {
        var good = new List<string>(); var bad = new List<string>();
        if (c.DebtToGdp > 1.0) bad.Add($"High public debt ({UI.Pct(c.DebtToGdp, 0)} of GDP) limits fiscal room"); else if (c.DebtToGdp < 0.45) good.Add($"Low public debt ({UI.Pct(c.DebtToGdp, 0)} of GDP)");
        if (c.Deficit0Share > 0.06) bad.Add($"Large budget deficit ({UI.Pct(c.Deficit0Share, 1)} of GDP)"); else if (c.Deficit0Share < 0.01) good.Add("Balanced or surplus budget");
        if (c.Inflation > 0.08) bad.Add($"High inflation ({UI.Pct(c.Inflation, 0)}) and weak anchoring"); 
        if (c.Unemp > 0.1) bad.Add($"High unemployment ({UI.Pct(c.Unemp, 1)})");
        if (c.Old > 0.2) bad.Add($"Rapidly ageing population ({UI.Pct(c.Old, 0)} over 65)"); else if (c.Young > 0.3) good.Add("Young population: a demographic dividend if jobs are created");
        if (c.Gini > 0.45) bad.Add($"High inequality (Gini {c.Gini:0.00})");
        if (c.Corruption > 0.5) bad.Add("Weak institutions: leakage in public spending and project overruns");
        else if (c.Corruption < 0.2) good.Add("Strong institutions: spending is effective");
        if (c.CaToGdp < -0.04) bad.Add($"External deficit ({UI.Pct(c.CaToGdp, 1)} of GDP)"); else if (c.CaToGdp > 0.04) good.Add($"External surplus ({UI.Pct(c.CaToGdp, 1)} of GDP)");
        if (c.SectorVa[(int)Sector.Energy] / c.SectorVa.Sum() > 0.15) bad.Add("Commodity dependence: exposed to oil and gas prices");
        if (c.Archetype == "hub") bad.Add("Small, open economy: highly exposed to global shocks"); 
        if (c.Gov == "autocracy") bad.Add("Authoritarian system: reforms pass easily, but coups and unrest can end your rule");
        if (c.Gov == "democracy") bad.Add("Elections: lose them and your government falls");
        if (c.GdpPerCapitaUsd > 40000) good.Add("High-income economy with deep capital markets");
        if (c.Archetype is "developing" or "emerging" && c.GdpPerCapitaUsd < 15000) good.Add("Large catch-up growth potential");
        if (c.CbIndependence > 0.8) good.Add("Credible, independent central bank");
        return (good, bad);
    }

    void Select(CountryState c)
    {
        _sel = c; _start.Disabled = false;
        BuildSpotlight(c);
        UpdateHiddenNote();
        BuildDetail(c);
    }

    void BuildSpotlight(CountryState c)
    {
        _emblem.Set(c.Id);
        foreach (var ch in _spotBody.GetChildren()) { _spotBody.RemoveChild(ch); if (ch != _hiddenNote) ch.QueueFree(); }   // the note is reused
        _spotBody.AddChild(UI.Lbl(c.Name, 28, Pal.Text, true));
        var govCol = c.Gov == "democracy" ? Pal.Good : c.Gov == "autocracy" ? Pal.Bad : Pal.Warn;
        var chips = UI.HBox(6, Cards.Chip(c.Archetype.ToUpper(), Pal.Accent), Cards.Chip(c.Gov.ToUpper(), govCol), Cards.Chip(c.Region.ToUpper(), Pal.Faint), Cards.Chip(c.Currency, Pal.Faint));
        _spotBody.AddChild(chips);
        var stats = new GridContainer { Columns = 4 }; stats.AddThemeConstantOverride("h_separation", 22); stats.AddThemeConstantOverride("v_separation", 0);
        void Stat(string k, string v, Color? col = null) { stats.AddChild(UI.VBox(0, UI.Dim(k, 11), UI.Lbl(v, 17, col ?? Pal.Text, true))); }
        Stat("GDP", Money.Gbp(c.GdpUsdBn)); Stat("Inflation", UI.Pct(c.Inflation, 1), c.Inflation > 0.08 ? Pal.Bad : null);
        Stat("Public debt", UI.Pct(c.DebtToGdp, 0), c.DebtToGdp > 1.0 ? Pal.Bad : null); Stat("Approval", UI.Pct(c.Approval, 0));
        _spotBody.AddChild(stats);
        _spotBody.AddChild(UI.Lbl($"Difficulty {Stars(Rating(c))}", 13, Pal.Warn, true));
        var (good, bad) = Profile(c);
        if (good.Count > 0) _spotBody.AddChild(Brief("▲ " + good[0], Pal.Good));
        if (bad.Count > 0) _spotBody.AddChild(Brief("▼ " + bad[0], Pal.Bad));
        _spotBody.AddChild(_hiddenNote);
    }

    static Label Brief(string text, Color col)
    {
        var l = UI.Lbl(text, 12, col); l.ClipText = true; l.CustomMinimumSize = new Vector2(10, 0); l.SizeFlagsHorizontal = SizeFlags.ExpandFill; return l;
    }

    void BuildDetail(CountryState c)
    {
        foreach (var ch in _detail.GetChildren()) { _detail.RemoveChild(ch); ch.QueueFree(); }
        _detail.AddChild(UI.Lbl(c.Name, 30, Pal.Text, true));
        _detail.AddChild(UI.Dim($"{c.Region} · {c.Archetype} economy · {c.Gov} · {c.Currency}", 14));

        void Group(string title, params (string k, string v)[] rows)
        {
            _detail.AddChild(UI.Lbl(title, 13, Pal.Accent, true));
            var grid = new GridContainer { Columns = 4 }; grid.AddThemeConstantOverride("h_separation", 18); grid.AddThemeConstantOverride("v_separation", 6);
            foreach (var (k, v) in rows) { grid.AddChild(UI.Dim(k)); grid.AddChild(UI.Lbl(v, 15, Pal.Text, true)); }
            _detail.AddChild(grid);
        }
        Group("ECONOMY", ("GDP", Money.Gbp(c.GdpUsdBn)), ("Population", $"{c.Pop:0.#}m"), ("GDP per head", $"${c.GdpPerCapitaUsd:N0}"), ("Real growth trend", UI.Pct(c.GrowthTrend, 1)),
              ("Inflation", UI.Pct(c.Inflation, 1)), ("Unemployment", UI.Pct(c.Unemp, 1)));
        Group("FISCAL AND MONETARY", ("Policy rate", UI.Pct(c.PolicyRate, 2)), ("10y yield", UI.Pct(c.Yield10, 2)), ("Public debt", UI.Pct(c.DebtToGdp, 0)), ("Deficit", UI.Pct(c.Deficit0Share, 1)),
              ("Tax revenue", UI.Pct(SocietyRevenue(c), 0)));
        Group("EXTERNAL AND SOCIETY", ("Current account", UI.Pct(c.CaToGdp, 1)), ("Gini", $"{c.Gini:0.00}"), ("Approval", UI.Pct(c.Approval, 0)), ("Over 65s", UI.Pct(c.Old, 0)));

        var mix = new StackBar { CustomMinimumSize = new Vector2(200, 70) }; mix.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var names = Enum.GetNames<Sector>(); double tot = c.SectorVa.Sum();
        mix.Format = v => UI.Pct(v / Math.Max(1e-9, tot), 0);
        mix.Set(names.Select((n, i) => new Seg { Label = n, Value = c.SectorVa[i], Color = Pal.Series[i % Pal.Series.Length] }), "Economic structure (value added)");
        _detail.AddChild(mix);
        _detail.AddChild(UI.Sep());
        var (good, bad) = Profile(c);
        if (good.Count > 0) { _detail.AddChild(UI.Lbl("Strengths", 15, Pal.Good, true)); foreach (var g in good) _detail.AddChild(UI.Lbl("• " + g, 14, Pal.Text, false, HorizontalAlignment.Left, true)); }
        if (bad.Count > 0) { _detail.AddChild(UI.Lbl("Challenges", 15, Pal.Bad, true)); foreach (var b in bad) _detail.AddChild(UI.Lbl("• " + b, 14, Pal.Text, false, HorizontalAlignment.Left, true)); }
        _detail.AddChild(UI.Sep());
        _detail.AddChild(UI.Lbl($"Difficulty {Stars(Rating(c))}", 14, Pal.Warn, true));
    }

    static double SocietyRevenue(CountryState c) => c.Revenue / Math.Max(1e-9, c.GdpNominal);

    void Start()
    {
        if (_sel == null) return;
        var diff = (Sim.Core.Model.Difficulty)_diff.Selected;
        Game.NewGame(_sel.Id, diff, (ulong)(Time.GetTicksMsec() % 100000 + 1));
        Main.Instance!.ShowGame();
    }
}
