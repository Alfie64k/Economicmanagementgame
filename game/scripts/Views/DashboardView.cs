using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.Core.Model;
using Sim.Core.Scoring;
using EconGame.App;
using EconGame.Ui;

namespace EconGame.Views;

public partial class DashboardView : View
{
    public override string Title => "Dashboard";
    readonly Dictionary<string, KpiTile> _tiles = new();
    readonly LineChart _gdp = new() { Title = "Real GDP (index, start = 100)" };
    readonly LineChart _macro = new() { Title = "Inflation, unemployment and policy rate" };
    readonly LineChart _fiscal = new() { Title = "Public finances (% of GDP)" };
    readonly LineChart _soc = new() { Title = "Politics and society" };
    readonly BarList _why = new() { Diverging = true, LabelWidth = 190 };
    readonly Label _whyHead = UI.Lbl("", 14, Pal.Text, false, HorizontalAlignment.Left, true);
    readonly VBoxContainer _goals = new();
    string _whyMetric = "inflation";
    readonly HBoxContainer _chips = new();

    public DashboardView()
    {
        var page = Page("Dashboard");
        var tiles = new GridContainer { Columns = 4 }; tiles.AddThemeConstantOverride("h_separation", 10); tiles.AddThemeConstantOverride("v_separation", 10);
        foreach (var (key, title) in new[] { ("growth", "Real GDP growth"), ("inflation", "Inflation"), ("unemployment", "Unemployment"), ("rate", "Policy rate"),
                                              ("debt", "Public debt"), ("deficit", "Budget deficit"), ("approval", "Approval"), ("score", "National score") })
        {
            var t = new KpiTile(title) { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            string k = key;
            t.Clicked += () => { if (Explain.Metrics.Contains(k)) { _whyMetric = k; Refresh(); } };
            _tiles[key] = t; tiles.AddChild(t);
        }
        page.AddChild(tiles);

        page.AddChild(new ChartRangeBar());
        var charts = new GridContainer { Columns = 2 }; charts.AddThemeConstantOverride("h_separation", 12); charts.AddThemeConstantOverride("v_separation", 12);
        foreach (var ch in new LineChart[] { _gdp, _macro, _fiscal, _soc }) { ch.Annotated = true; ch.SyncGroup = "dash"; ch.CustomMinimumSize = new Vector2(300, 190); ch.SizeFlagsHorizontal = SizeFlags.ExpandFill; charts.AddChild(UI.Fill(UI.Card(ch, null, 10), true, false)); }
        page.AddChild(charts);

        var whyBox = UI.VBox(8);
        whyBox.AddChild(UI.H2("Why did this change?"));
        _chips.AddThemeConstantOverride("separation", 6); whyBox.AddChild(_chips);
        whyBox.AddChild(_whyHead);
        whyBox.AddChild(_why);
        page.AddChild(UI.Card(whyBox));
        page.AddChild(_goals);
    }

    public override void Refresh()
    {
        if (!Game.Running) return;
        var w = Game.World; var c = Game.Player; var h = Game.History(c.Id);
        double[] H(Func<HistoryPoint, double> f) => h.TakeLast(24).Select(f).ToArray();
        var sc = Scorer.Compute(w, c);
        var up = Pal.Good; var down = Pal.Bad;
        void T(string key, string val, string delta, Color? dc, double[] hist, Color? sc2 = null) => _tiles[key].Set(val, delta, dc, hist, sc2);

        double g0 = h.Count > 4 ? h[^5].Growth : c.GdpGrowth;
        T("growth", UI.Pct(c.GdpGrowth), $"output gap {UI.Pct(c.Gap)}", c.Gap < -0.02 ? down : c.Gap > 0.03 ? Pal.Warn : Pal.Dim, H(p => p.Growth));
        T("inflation", UI.Pct(c.Inflation), $"target {UI.Pct(c.InflTarget, 0)}", Math.Abs(c.Inflation - c.InflTarget) > 0.02 ? down : up, H(p => p.Inflation));
        T("unemployment", UI.Pct(c.Unemp), $"natural {UI.Pct(c.NairU)}", c.Unemp > c.NairU + 0.015 ? down : up, H(p => p.Unemployment));
        T("rate", UI.Pct(c.PolicyRate, 2), c.RateMode == RateMode.Manual ? "manual" : "rule-based", Pal.Dim, H(p => p.PolicyRate));
        T("debt", UI.Pct(c.DebtToGdp, 0), $"yield {UI.Pct(c.Yield10)}", c.DebtToGdp > Math.Max(0.6, c.DebtGdp0 + 0.15) ? down : Pal.Dim, H(p => p.DebtToGdp));
        T("deficit", UI.Pct(c.DeficitToGdp), $"interest {UI.Pct(c.Interest / Math.Max(1e-9, c.GdpNominal))} of GDP", c.DeficitToGdp > 0.05 ? down : Pal.Dim, H(p => p.DeficitToGdp));
        T("approval", UI.Pct(c.Approval, 0), c.NextElectionMonth > 0 ? $"election in {Math.Max(0, c.NextElectionMonth - w.Month)} mo" : "no elections", c.Approval < 0.3 ? down : Pal.Dim, H(p => p.Approval));
        T("score", $"{sc.Grade}  {sc.Total:0}", $"stab {sc.Stability:0} · sust {sc.Sustainability:0}", Pal.Dim, new[] { 50, sc.Total }, Pal.Accent);
        foreach (var kv in _tiles)
        {
            string k = kv.Key;
            kv.Value.Explain = () => Explain.Metrics.Contains(k) ? ExplainText(k) : $"Prosperity {sc.Prosperity:0} · Living {sc.Living:0} · Stability {sc.Stability:0}\nSustainability {sc.Sustainability:0} · Resilience {sc.Resilience:0}";
        }

        double[] x = h.Select(p => (double)p.Month).ToArray();
        double g0v = h.Count > 0 ? h[0].Gdp : 1;
        _gdp.StartYear = _macro.StartYear = _fiscal.StartYear = _soc.StartYear = w.StartYear;
        _gdp.Markers = _macro.Markers = _fiscal.Markers = _soc.Markers = ChartPrefs.For(w);
        var gdpIdx = h.Select(p => 100 * p.Gdp / g0v).ToArray();
        _gdp.YFormat = gdpIdx.Length > 0 && gdpIdx.Max() - gdpIdx.Min() < 4 ? (v => v.ToString("0.0")) : (v => v.ToString("0"));   // a short game's range is a point or two: whole numbers would repeat
        _gdp.SetSeries(new[] { new Series { Name = "GDP", X = x, Y = gdpIdx, Color = Pal.Series[0] } });
        _macro.YFormat = v => v.ToString("0.0") + "%";
        _macro.SetSeries(new[]
        {
            new Series { Name = "Inflation", X = x, Y = h.Select(p => p.Inflation * 100).ToArray(), Color = Pal.Series[1] },
            new Series { Name = "Unemployment", X = x, Y = h.Select(p => p.Unemployment * 100).ToArray(), Color = Pal.Series[2] },
            new Series { Name = "Policy rate", X = x, Y = h.Select(p => p.PolicyRate * 100).ToArray(), Color = Pal.Series[0] },
        });
        _fiscal.YFormat = v => v.ToString("0") + "%";
        _fiscal.SetSeries(new[]
        {
            new Series { Name = "Debt", X = x, Y = h.Select(p => p.DebtToGdp * 100).ToArray(), Color = Pal.Series[5] },
            new Series { Name = "Deficit", X = x, Y = h.Select(p => p.DeficitToGdp * 100).ToArray(), Color = Pal.Series[3] },
        });
        _soc.YFormat = v => v.ToString("0") + "%";
        _soc.SetSeries(new[]
        {
            new Series { Name = "Approval", X = x, Y = h.Select(p => p.Approval * 100).ToArray(), Color = Pal.Series[0] },
            new Series { Name = "Stability", X = x, Y = h.Select(p => p.Stability * 100).ToArray(), Color = Pal.Series[2] },
            new Series { Name = "Gini ×100", X = x, Y = h.Select(p => p.Gini * 100).ToArray(), Color = Pal.Series[6] },
        });

        // why panel
        foreach (var kv in _tiles) kv.Value.Selected = kv.Key == _whyMetric;
        // built once and restyled in place: rebuilding them on every refresh replaced the button under the cursor between press and release
        if (_chips.GetChildCount() == 0)
            foreach (var m in Explain.Metrics)
            {
                string mm = m;
                var b = UI.Chip(char.ToUpper(m[0]) + m[1..], false, () => { _whyMetric = mm; Refresh(); }); b.Name = "chip_" + m;
                _chips.AddChild(b);
            }
        foreach (var ch in _chips.GetChildren()) if (ch is Button cb) cb.SetPressedNoSignal(cb.Name == "chip_" + _whyMetric);
        var ex = Explain.Why(c, _whyMetric);
        _whyHead.Text = ex.Headline;
        double max = Math.Max(0.5, ex.Items.Max(i => Math.Abs(i.Value)));
        _why.Max = max;
        _why.Format = v => UI.Sign(v, "0.0") + "pp";
        _why.Set(ex.Items.Select(i => new BarItem { Label = i.Label, Value = i.Value, Color = IsBad(_whyMetric, i) ? Pal.Bad : Pal.Good, Tooltip = i.Group }));

        // scenario goals
        foreach (var ch in _goals.GetChildren()) ch.QueueFree();
        if (Game.Scenario != null)
        {
            var res = Scenarios.Evaluate(w, Game.Scenario);
            var box = UI.VBox(6);
            box.AddChild(UI.H2(Game.Scenario.Name + " — objectives"));
            foreach (var gs in res.Goals)
                box.AddChild(UI.Lbl($"{(gs.Met ? "✔" : "○")} {gs.Goal.Label}   (now {Fmt(gs.Goal.Metric, gs.Current)})", 14, gs.Met ? Pal.Good : Pal.Dim));
            int left = Game.Scenario.Years * 12 - w.Month;
            box.AddChild(UI.Dim($"{left / 12} years {left % 12} months remaining"));
            _goals.AddChild(UI.Card(box));
        }
    }

    static string Fmt(string metric, double v) => metric is "gdppc" or "emissions" ? v.ToString("0.00") + "×" : metric is "score" ? v.ToString("0") : metric == "default" ? (v > 0.5 ? "yes" : "no") : UI.Pct(v);

    // for inflation/unemployment/deficit/debt, positive contributions are "bad"; for growth/approval positive is good
    static bool IsBad(string metric, DriverItem i) => metric is "growth" or "approval" ? i.Value < 0 : i.Value > 0;

    string ExplainText(string metric)
    {
        var e = Explain.Why(Game.Player, metric);
        return e.Headline + "\n" + string.Join("\n", e.Items.Where(i => Math.Abs(i.Value) >= 0.05).OrderByDescending(i => Math.Abs(i.Value)).Take(5).Select(i => $"  {i.Label}: {i.Value:+0.0;-0.0}pp")) + "\n(click to open the breakdown)";
    }
}
