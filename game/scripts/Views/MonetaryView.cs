using System;
using System.Linq;
using Godot;
using Sim.Core.Engine;
using Sim.Core.Model;
using Sim.Core.Policy;
using EconGame.App;
using EconGame.Ui;

namespace EconGame.Views;

public partial class MonetaryView : View
{
    public override string Title => "Monetary";
    readonly HBoxContainer _tiles = new();
    readonly LineChart _rates = new() { Title = "Policy rate, 10-year yield and inflation" };
    readonly LineChart _fx = new() { Title = "Exchange rate (local currency per US$)" };
    readonly Label _status = UI.Lbl("", 14, Pal.Text, false, HorizontalAlignment.Left, true);
    readonly Label _fxStatus = UI.Lbl("", 14, Pal.Text, false, HorizontalAlignment.Left, true);
    readonly Label _result = UI.Dim("", 13, true);
    readonly Label _staged = UI.Lbl("", 13, Pal.Warn, true, HorizontalAlignment.Left, true);
    readonly AppSlider _rate = new();
    readonly OptionButton _regime = new();
    readonly RatePreviewPanel _rp = new();
    bool _init, _touched; int _sig = -1;

    public MonetaryView()
    {
        var page = Page("Monetary policy", "The central bank follows a Taylor-type rule unless you pin the rate. Overriding an independent bank costs political capital and credibility.");
        _tiles.AddThemeConstantOverride("separation", 10); page.AddChild(_tiles);
        var charts = UI.HBox(12); foreach (var c in new LineChart[] { _rates, _fx }) { c.Annotated = true; c.SyncGroup = "mon"; c.SizeFlagsHorizontal = SizeFlags.ExpandFill; c.CustomMinimumSize = new Vector2(300, 220); charts.AddChild(UI.Fill(UI.Card(c, null, 10), true, false)); }
        page.AddChild(new ChartRangeBar());
        page.AddChild(charts);

        var ctl = UI.VBox(8);
        ctl.AddChild(UI.H2("Interest-rate stance"));
        ctl.AddChild(_status);
        _rate.Setup(-0.01, 0.4, 0.0025, 0.03, v => UI.Pct(v, 2)); _rate.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _rate.Changed += v => { _touched = true; _rp.Request(v, Game.Player.PolicyRate); };
        ctl.AddChild(UI.HBox(10, UI.Lbl("Target rate", 14, Pal.Dim), _rate));
        ctl.AddChild(UI.HBox(10,
            UI.Btn("Add rate pin to plan", () => Do(Command.SetRate(Game.Player.Id, true, _rate.Value)), true),
            UI.Btn("Plan a return to the rule", () => Do(Command.SetRate(Game.Player.Id, false, 0)))));
        ctl.AddChild(_staged);
        ctl.AddChild(UI.Dim("The rate moves at most 0.5pp a month toward your target. Real rates drive investment, saving and the exchange rate with a lag.", 13, true));
        page.AddChild(UI.Card(ctl));
        page.AddChild(UI.Card(_rp));

        var fx = UI.VBox(8);
        fx.AddChild(UI.H2("Exchange-rate regime"));
        fx.AddChild(_fxStatus);
        foreach (var r in new[] { "Float", "Managed", "Peg" }) _regime.AddItem(r);
        fx.AddChild(UI.HBox(10, _regime, UI.Btn("Add regime change to plan", () => Do(Command.SetFxRegime(Game.Player.Id, (FxRegime)_regime.Selected))), UI.Spacer(0, 0, true)));
        fx.AddChild(UI.Dim("A peg imports the anchor currency's rates and risks a disorderly break if reserves run out. Floats absorb shocks but pass depreciation into prices.", 13, true));
        fx.AddChild(_result);
        page.AddChild(UI.Card(fx));
    }

    /// <summary>Self-test hook: move the slider as the player would.</summary>
    public void MoveRateForTest(double delta) => _rate.SetValue(_rate.Value + delta, true);
    public RatePreview? PreviewResult => _rp.Last;

    void Do(Command cmd) { PlanUi.Stage(cmd, t => _result.Text = t); Refresh(); }

    public override void _EnterTree() { Game.PlanChanged += OnPlan; }
    public override void _ExitTree() { Game.PlanChanged -= OnPlan; }
    void OnPlan() { if (Game.Running && IsInsideTree()) Refresh(); }

    public override void Refresh()
    {
        if (!Game.Running) return;
        var c = Game.Player; var w = Game.World; var h = Game.History(c.Id);
        double rule = c.NaturalRate + c.Inflation + MacroEngine.InflationResponse(c.Inflation - c.InflTarget) + Math.Clamp(c.Gap, -0.15, 0.10);
        double snapped = Math.Round(c.PolicyRate / 0.0025) * 0.0025;
        if (!_init)
        {
            _init = true; double max = Math.Max(0.4, Math.Ceiling(Math.Max(c.PolicyRate, rule) * 1.5 / 0.05) * 0.05);   // high-rate countries need a longer scale
            _rate.Setup(-0.01, max, 0.0025, Game.Staged("rate") is { Id: "Manual" } sr0 ? sr0.Value : snapped, v => UI.Pct(v, 2)); _regime.Selected = (int)c.Regime;
        }
        _rate.Baseline = c.PolicyRate;
        int sig = w.Month * 8 + (int)c.RateMode * 4 + (int)c.Regime;
        if (sig != _sig)
        {
            bool first = _sig < 0; _sig = sig;
            if (!_touched && Game.Staged("rate") == null) _rate.SetValue(snapped);   // follow the live rate until the player takes hold of the slider
            _rp.Request(_rate.Value, c.PolicyRate, first);
        }
        foreach (var ch in _tiles.GetChildren().ToList()) ch.QueueFree();
        void T(string t, string v, string sub, Color? col = null) { var k = new KpiTile(t) { SizeFlagsHorizontal = SizeFlags.ExpandFill }; k.Set(v, sub, col, Array.Empty<double>()); _tiles.AddChild(k); }
        T("Policy rate", UI.Pct(c.PolicyRate, 2), c.RateMode == RateMode.Manual ? "pinned by you" : "rule-based");
        T("Rule suggests", UI.Pct(rule, 2), $"gap {UI.Pct(c.PolicyRate - rule, 2)}", Math.Abs(c.PolicyRate - rule) > 0.02 ? Pal.Warn : Pal.Dim);
        T("Real rate", UI.Pct(c.RealRate, 2), $"expected inflation {UI.Pct(c.InflExp)}");
        T("10y yield", UI.Pct(c.Yield10, 2), $"risk premium {UI.Pct(c.RiskPremium, 2)}", c.RiskPremium > 0.02 ? Pal.Bad : Pal.Dim);
        T("Credibility", UI.Pct(c.Cred, 0), $"bank independence {UI.Pct(c.CbIndependence, 0)}");
        double x0 = h.Count > 0 ? h[0].Month : 0;
        double[] x = h.Select(p => (double)p.Month).ToArray();
        _rates.Markers = _fx.Markers = ChartPrefs.For(w);
        _rates.StartYear = _fx.StartYear = w.StartYear; _rates.YFormat = v => v.ToString("0.0") + "%";
        _rates.SetSeries(new[]
        {
            new Series { Name = "Policy rate", X = x, Y = h.Select(p => p.PolicyRate * 100).ToArray(), Color = Pal.Series[0] },
            new Series { Name = "10y yield", X = x, Y = h.Select(p => p.Yield10 * 100).ToArray(), Color = Pal.Series[2] },
            new Series { Name = "Inflation", X = x, Y = h.Select(p => p.Inflation * 100).ToArray(), Color = Pal.Series[1] },
        });
        _fx.YFormat = v => v >= 100 ? v.ToString("0") : v >= 10 ? v.ToString("0.0") : v.ToString("0.000");
        _fx.SetSeries(new[] { new Series { Name = "FX", X = x, Y = h.Select(p => p.Fx).ToArray(), Color = Pal.Series[4] } });
        _status.Text = c.RateMode == RateMode.Manual
            ? $"You are pinning the rate toward {UI.Pct(c.ManualRate, 2)}; the rule would set {UI.Pct(rule, 2)}."
            : $"The central bank is following its rule (target {UI.Pct(c.InflTarget, 0)}). Natural real rate {UI.Pct(c.NaturalRate, 1)}.";
        var sr = Game.Staged("rate"); var sf = Game.Staged("fxregime");
        _staged.Text = (sr == null ? "" : sr.Id == "Manual" ? $"In the plan: pin the policy rate at {UI.Pct(sr.Value, 2)} from the end of this turn." : "In the plan: return the central bank to its rule.")
            + (sr != null && sf != null ? "\n" : "") + (sf == null ? "" : $"In the plan: switch the exchange-rate regime to {sf.Id}.");
        _staged.Visible = _staged.Text != "";
        _fxStatus.Text = $"Regime: {c.Regime}. Reserves cover {c.Reserves:0.0} months of imports; real exchange rate {c.Rer:0.00}; depreciation {UI.Pct(c.FxChange, 1)} a year.";
    }
}
