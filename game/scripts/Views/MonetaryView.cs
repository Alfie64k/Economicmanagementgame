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
    readonly AppSlider _rate = new();
    readonly OptionButton _regime = new();
    bool _init;

    public MonetaryView()
    {
        var page = Page("Monetary policy", "The central bank follows a Taylor-type rule unless you pin the rate. Overriding an independent bank costs political capital and credibility.");
        _tiles.AddThemeConstantOverride("separation", 10); page.AddChild(_tiles);
        var charts = UI.HBox(12); foreach (var c in new LineChart[] { _rates, _fx }) { c.SizeFlagsHorizontal = SizeFlags.ExpandFill; c.CustomMinimumSize = new Vector2(300, 220); charts.AddChild(UI.Fill(UI.Card(c, null, 10), true, false)); }
        page.AddChild(charts);

        var ctl = UI.VBox(8);
        ctl.AddChild(UI.H2("Interest-rate stance"));
        ctl.AddChild(_status);
        _rate.Setup(-0.01, 0.4, 0.0025, 0.03, v => UI.Pct(v, 2)); _rate.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ctl.AddChild(UI.HBox(10, UI.Lbl("Target rate", 14, Pal.Dim), _rate));
        ctl.AddChild(UI.HBox(10,
            UI.Btn("Pin the policy rate", () => Do(Command.SetRate(Game.Player.Id, true, _rate.Value)), true),
            UI.Btn("Return to the rule", () => Do(Command.SetRate(Game.Player.Id, false, 0)))));
        ctl.AddChild(UI.Dim("The rate moves at most 0.5pp a month toward your target. Real rates drive investment, saving and the exchange rate with a lag.", 13, true));
        page.AddChild(UI.Card(ctl));

        var fx = UI.VBox(8);
        fx.AddChild(UI.H2("Exchange-rate regime"));
        fx.AddChild(_fxStatus);
        foreach (var r in new[] { "Float", "Managed", "Peg" }) _regime.AddItem(r);
        fx.AddChild(UI.HBox(10, _regime, UI.Btn("Change regime", () => Do(Command.SetFxRegime(Game.Player.Id, (FxRegime)_regime.Selected))), UI.Spacer(0, 0, true)));
        fx.AddChild(UI.Dim("A peg imports the anchor currency's rates and risks a disorderly break if reserves run out. Floats absorb shocks but pass depreciation into prices.", 13, true));
        fx.AddChild(_result);
        page.AddChild(UI.Card(fx));
    }

    void Do(Command cmd) { var r = Game.Sim!.Execute(cmd); _result.Text = (r.Ok ? "✔ " : "✘ ") + r.Message; Game.NotifyChanged(); Refresh(); }

    public override void Refresh()
    {
        if (!Game.Running) return;
        var c = Game.Player; var w = Game.World; var h = Game.History(c.Id);
        double rule = c.NaturalRate + c.Inflation + MacroEngine.InflationResponse(c.Inflation - c.InflTarget) + Math.Clamp(c.Gap, -0.15, 0.10);
        if (!_init) { _init = true; _rate.Setup(-0.01, 0.4, 0.0025, Math.Round(c.PolicyRate / 0.0025) * 0.0025, v => UI.Pct(v, 2)); _regime.Selected = (int)c.Regime; }
        foreach (var ch in _tiles.GetChildren().ToList()) ch.QueueFree();
        void T(string t, string v, string sub, Color? col = null) { var k = new KpiTile(t) { SizeFlagsHorizontal = SizeFlags.ExpandFill }; k.Set(v, sub, col, Array.Empty<double>()); _tiles.AddChild(k); }
        T("Policy rate", UI.Pct(c.PolicyRate, 2), c.RateMode == RateMode.Manual ? "pinned by you" : "rule-based");
        T("Rule suggests", UI.Pct(rule, 2), $"gap {UI.Pct(c.PolicyRate - rule, 2)}", Math.Abs(c.PolicyRate - rule) > 0.02 ? Pal.Warn : Pal.Dim);
        T("Real rate", UI.Pct(c.RealRate, 2), $"expected inflation {UI.Pct(c.InflExp)}");
        T("10y yield", UI.Pct(c.Yield10, 2), $"risk premium {UI.Pct(c.RiskPremium, 2)}", c.RiskPremium > 0.02 ? Pal.Bad : Pal.Dim);
        T("Credibility", UI.Pct(c.Cred, 0), $"bank independence {UI.Pct(c.CbIndependence, 0)}");
        double x0 = h.Count > 0 ? h[0].Month : 0;
        double[] x = h.Select(p => (double)p.Month).ToArray();
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
        _fxStatus.Text = $"Regime: {c.Regime}. Reserves cover {c.Reserves:0.0} months of imports; real exchange rate {c.Rer:0.00}; depreciation {UI.Pct(c.FxChange, 1)} a year.";
    }
}
