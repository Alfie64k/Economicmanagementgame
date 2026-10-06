using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Sim.Core.Policy;
using EconGame.App;
using EconGame.Ui;

namespace EconGame.Views;

public partial class ForecastView : View
{
    public override string Title => "Forecast";
    readonly LineChart _chart = new() { CustomMinimumSize = new Vector2(400, 280) };
    readonly Label _status = UI.Dim("Run a Monte-Carlo forecast of the next five years from today's world, with random shocks.", 13, true);
    readonly HBoxContainer _chips = new();
    System.Collections.Generic.List<FanSeries>? _fan; int _fanMonth; string _metric = "growth";
    readonly PreviewPanel _preview = new();
    int _runId;

    public ForecastView()
    {
        var page = Page("Forecast", "Fan charts show the 10th-90th percentile range across 30 simulated futures. Policy changes drafted on the Budget page can be previewed here.");
        var run = UI.Btn("Run forecast", Run, true, 160); run.SizeFlagsVertical = SizeFlags.ShrinkBegin; _status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        page.AddChild(UI.HBox(10, run, _status));
        _chips.AddThemeConstantOverride("separation", 6); page.AddChild(_chips);
        page.AddChild(UI.Card(_chart));
        page.AddChild(Cards.Section("Draft vs unchanged", UI.VBox(8, UI.Btn("Preview current budget draft", () => _preview.Run(Draft.ToCommands(Game.Player)), false, 260), _preview)));
    }

    public override void Refresh()
    {
        if (!Game.Running) return;
        foreach (var ch in _chips.GetChildren().ToList()) ch.QueueFree();
        foreach (var m in new[] { "growth", "inflation", "unemployment", "debt", "deficit", "approval", "policyRate", "yield" })
        {
            string mm = m; _chips.AddChild(UI.Btn(m == "policyRate" ? "Policy rate" : m == "yield" ? "10y yield" : char.ToUpper(m[0]) + m[1..], () => { _metric = mm; Draw(); }, m == _metric));
        }
        Draw();
    }

    public void RunNow() => Run();

    void Run()
    {
        _status.Text = "Simulating 30 futures…"; int id = ++_runId; var snap = Forecaster.Snapshot(Game.Sim!); _fanMonth = Game.World.Month;
        Task.Run(() => Forecaster.FanChart(snap, 60, 30)).ContinueWith(t => Callable.From(() => { if (id == _runId && IsInsideTree()) { _fan = t.Result; _status.Text = "Forecast complete."; Draw(); } }).CallDeferred());
    }

    void Draw()
    {
        if (_fan == null) { _chart.SetSeries(Array.Empty<Series>()); return; }
        var f = _fan.First(s => s.Metric == _metric);
        double[] x = Enumerable.Range(1, f.P50.Length).Select(i => (double)(_fanMonth + i)).ToArray();
        _chart.StartYear = Game.World.StartYear; _chart.Title = $"{_metric}: median with 10-90% range";
        _chart.YFormat = v => _metric == "gdp" ? v.ToString("0") : (v * 100).ToString("0.0") + "%";
        _chart.SetSeries(new[] { new Series { Name = "Median", X = x, Y = f.P50, Lo = f.P10, Hi = f.P90, Color = Pal.Accent } });
    }
}
