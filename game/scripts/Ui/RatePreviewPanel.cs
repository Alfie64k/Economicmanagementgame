using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.Core.Policy;
using EconGame.App;

namespace EconGame.Ui;

/// <summary>
/// Under the interest-rate slider: what pinning the policy rate at the slider's value does to inflation, unemployment, the output gap and the
/// currency over the next 3 months, 6 months and year, against carrying on. Deterministic and noise-free; it is the policy's own effect, not a forecast.
/// </summary>
public partial class RatePreviewPanel : VBoxContainer
{
    readonly Label _status = UI.Lbl("", 14, Pal.Text, false, HorizontalAlignment.Left, true);
    readonly Label _verdict = UI.Lbl("", 13, Pal.Dim, false, HorizontalAlignment.Left, true);
    readonly Label _caption = UI.Dim("Expected path with noise and random events switched off, so it isolates the rate decision rather than predicting the year. Rates reach prices slowly: expect activity and the currency to move first, inflation later.", 12, true);
    readonly GridContainer _grid = new() { Columns = 4 };
    readonly LineChart _chart = new() { ShowLegend = true, CustomMinimumSize = new Vector2(300, 190), SizeFlagsHorizontal = SizeFlags.ExpandFill };
    readonly HBoxContainer _chips = new();
    readonly Godot.Timer _debounce = new() { OneShot = true, WaitTime = 0.25 };
    readonly AsyncRun<RatePreview> _run = new();
    public RatePreview? Last => _last;
    RatePreview? _last; double _target, _baselineRate; double _pcCost; bool _offBaseline;
    int _startYear, _m0; string _metric = "inflation";

    static readonly (string key, string label)[] ChartMetrics = { ("inflation", "Inflation"), ("unemployment", "Unemployment"), ("gap", "Output gap"), ("policyRate", "Policy rate"), ("fx", "Currency") };

    public RatePreviewPanel()
    {
        AddThemeConstantOverride("separation", 8);
        AddChild(UI.H2("What this rate does over the next year"));
        AddChild(_status);
        _grid.AddThemeConstantOverride("h_separation", 24); _grid.AddThemeConstantOverride("v_separation", 8);
        AddChild(_grid);
        AddChild(_verdict);
        _chips.AddThemeConstantOverride("separation", 6); AddChild(_chips);
        AddChild(UI.Card(_chart, null, 10));
        AddChild(_caption);
        AddChild(_debounce);
        _debounce.Timeout += Compute;
        _status.Text = "Move the slider to preview its effect.";
    }

    /// <summary>Ask for a preview at <paramref name="target"/>; calls within 250 ms of each other collapse into one run.</summary>
    public void Request(double target, double baselineRate, bool immediate = false)
    {
        _target = target; _baselineRate = baselineRate;
        _offBaseline = Math.Abs(target - baselineRate) >= 0.00125;
        if (immediate) { _debounce.Stop(); Compute(); } else _debounce.Start();
        if (_last == null) _status.Text = "Calculating…";
        else if (_offBaseline) _status.Text = $"Recalculating for {UI.Pct(target, 2)}…";
    }

    void Compute()
    {
        if (!Game.Running || !IsInsideTree()) return;
        var snap = Forecaster.Snapshot(Game.Sim!); double target = _target;
        _startYear = Game.World.StartYear; _m0 = Game.World.Month;
        var dry = Sim.Core.Policy.CommandProcessor.Apply(Game.World, Sim.Core.Model.Command.SetRate(Game.Player.Id, true, target), dryRun: true);
        _pcCost = dry.NoOp ? 0 : dry.PcCost;
        _run.Start(this, ct => Forecaster.PreviewRate(snap, target, 12, null, ct), r => { _last = r; Show(r); },
            ex => _status.Text = "The preview could not be computed: " + ex.Message);
    }

    void Show(RatePreview r)
    {
        var c = Game.Player;
        _status.Text = _offBaseline
            ? $"Pinning the policy rate at {UI.Pct(r.Target, 2)} ({UI.Sign((r.Target - _baselineRate) * 100, "0.00")}pp on today's {UI.Pct(_baselineRate, 2)}), against " + (c.RateMode == Sim.Core.Model.RateMode.Manual ? "keeping your current pin." : "letting the central bank follow its rule.")
            : "No change from today's rate. Showing where the economy is heading if you carry on; move the slider to compare.";
        if (r.Note != "") _status.Text += "\n" + r.Note;

        foreach (var ch in _grid.GetChildren().ToList()) { _grid.RemoveChild(ch); ch.QueueFree(); }
        _grid.AddChild(UI.Dim("", 12));
        foreach (int h in Forecaster.Horizons) _grid.AddChild(UI.Lbl(h == 12 ? "In 1 year" : $"In {h} months", 13, Pal.Accent, true));
        double target = c.InflTarget;
        Row("Inflation", r.S("inflation"), v => UI.Pct(v, 2), (b, w) => Math.Abs(w - target) < Math.Abs(b - target) ? 1 : -1, d => UI.Sign(d * 100, "0.00") + "pp");
        Row("Unemployment", r.S("unemployment"), v => UI.Pct(v, 2), (b, w) => w < b ? 1 : -1, d => UI.Sign(d * 100, "0.00") + "pp");
        Row("Output gap", r.S("gap"), v => UI.Pct(v, 2), (b, w) => Math.Abs(w) < Math.Abs(b) ? 1 : -1, d => UI.Sign(d * 100, "0.00") + "pp");
        var fx = r.S("fx");
        Row("Currency vs US$", fx, v => v >= 100 ? v.ToString("0") : v >= 10 ? v.ToString("0.0") : v.ToString("0.000"), (b, w) => 0,
            (d) => "", fxDelta: true);

        string stats = "";
        if (_offBaseline)
        {
            string with = r.MonthsToTarget is int m ? $"reaches its {UI.Pct(c.InflTarget, 0)} target in about {m} month{(m == 1 ? "" : "s")}" : $"does not reach its {UI.Pct(c.InflTarget, 0)} target within a year";
            string without = r.BaselineMonthsToTarget is int bm ? $"{bm} month{(bm == 1 ? "" : "s")}" : "not within a year";
            stats = $"Inflation {with} (carrying on: {without}). ";
        }
        if (_offBaseline) stats += _pcCost > 0 ? $"Staging this costs {_pcCost:0} political capital, charged when you end the turn." : "No political capital needed.";
        _verdict.Text = stats; _verdict.Visible = stats != "";

        foreach (var ch in _chips.GetChildren().ToList()) { _chips.RemoveChild(ch); ch.QueueFree(); }
        foreach (var (key, label) in ChartMetrics) { string k = key; _chips.AddChild(UI.Chip(label, k == _metric, () => { _metric = k; if (_last != null) Show(_last); })); }
        DrawChart(r);
    }

    void Row(string name, PreviewSeries s, Func<double, string> fmt, Func<double, double, int> goodness, Func<double, string> dfmt, bool fxDelta = false)
    {
        _grid.AddChild(UI.Lbl(name, 14, Pal.Dim));
        foreach (int h in Forecaster.Horizons)
        {
            double w = s.At(h), b = s.BaselineAt(h), d = w - b;
            var box = UI.VBox(0);
            box.AddChild(UI.Lbl(fmt(_offBaseline ? w : b), 17, Pal.Text, true));
            string sub; Color col = Pal.Dim;
            if (!_offBaseline) sub = "carrying on";
            else if (fxDelta) { double str = b / w - 1; sub = (Math.Abs(str) < 0.0005 ? "±0.0%" : UI.Sign(str * 100, "0.0") + "%") + (str > 0.0005 ? " stronger" : str < -0.0005 ? " weaker" : ""); }
            else
            {
                sub = (Math.Abs(d) < 0.00005 ? "±0.00pp" : dfmt(d)) + " vs carrying on";
                int g = Math.Abs(d) < 0.00005 ? 0 : goodness(b, w); col = g > 0 ? Pal.Good : g < 0 ? Pal.Bad : Pal.Dim;
            }
            box.AddChild(UI.Lbl(sub, 12, col));
            _grid.AddChild(box);
        }
    }

    void DrawChart(RatePreview r)
    {
        var s = r.S(_metric);
        double[] x = Enumerable.Range(1, s.Baseline.Length).Select(i => (double)(_m0 + i)).ToArray();
        _chart.StartYear = _startYear;
        _chart.Title = ChartMetrics.First(m => m.key == _metric).label;
        _chart.YFormat = _metric == "fx" ? (v => v >= 100 ? v.ToString("0") : v >= 10 ? v.ToString("0.0") : v.ToString("0.000")) : (v => (v * 100).ToString("0.0") + "%");
        var list = new List<Series> { new() { Name = _offBaseline ? "Carrying on" : "Expected path", X = x, Y = s.Baseline, Color = _offBaseline ? Pal.Faint : Pal.Accent, Dashed = _offBaseline } };
        if (_offBaseline) list.Add(new Series { Name = $"Rate at {UI.Pct(r.Target, 2)}", X = x, Y = s.WithPolicy, Color = Pal.Accent });
        _chart.SetSeries(list);
    }
}
