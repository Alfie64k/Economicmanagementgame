using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Sim.Core.Model;
using Sim.Core.Policy;
using EconGame.App;

namespace EconGame.Ui;

/// <summary>Runs a "carry on" vs "with these changes" deterministic comparison off the main thread and charts the result.</summary>
public partial class PreviewPanel : VBoxContainer
{
    readonly Label _status = UI.Dim("Preview shows the effect of your draft against carrying on unchanged.", 13, true);
    readonly GridContainer _grid = new() { Columns = 3 };
    public int Months = 60;
    int _runId;

    public PreviewPanel()
    {
        AddThemeConstantOverride("separation", 8);
        AddChild(_status);
        _grid.AddThemeConstantOverride("h_separation", 10); _grid.AddThemeConstantOverride("v_separation", 10);
        AddChild(_grid);
    }

    public void Run(List<Command> cmds)
    {
        if (!Game.Running || cmds.Count == 0) { _status.Text = "Nothing to preview: change something first."; Clear(); return; }
        _status.Text = "Simulating…"; Clear();
        int id = ++_runId; var snap = Forecaster.Snapshot(Game.Sim!); int months = Months; int start = Game.World.StartYear; int m0 = Game.World.Month;
        Task.Run(() => Forecaster.Preview(snap, cmds, months)).ContinueWith(t =>
            Callable.From(() => { if (id == _runId && IsInsideTree()) Show(t.Result, start, m0); }).CallDeferred());
    }

    void Clear() { foreach (var c in _grid.GetChildren()) c.QueueFree(); }

    void Show(List<PreviewSeries> res, int startYear, int m0)
    {
        Clear();
        var x = Enumerable.Range(1, Months).Select(i => (double)(m0 + i)).ToArray();
        var pick = new[] { ("growth", "GDP growth", true), ("inflation", "Inflation", true), ("unemployment", "Unemployment", true), ("debt", "Debt (% GDP)", true), ("deficit", "Deficit (% GDP)", true), ("approval", "Approval", true) };
        var lines = new List<string>();
        foreach (var (key, title, pct) in pick)
        {
            var r = res.First(s => s.Metric == key);
            var ch = new LineChart { Title = title, CustomMinimumSize = new Vector2(220, 150), StartYear = startYear, ShowLegend = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            ch.YFormat = v => (v * 100).ToString("0.0") + "%";
            ch.SetSeries(new[]
            {
                new Series { Name = "Unchanged", X = x, Y = r.Baseline, Color = Pal.Faint, Dashed = true },
                new Series { Name = "With changes", X = x, Y = r.WithPolicy, Color = Pal.Accent },
            });
            _grid.AddChild(UI.Card(ch, null, 8));
            double d = (r.WithPolicy[^1] - r.Baseline[^1]) * 100;
            lines.Add($"{title.Split(' ')[0].ToLower()} {d:+0.0;-0.0}pp");
        }
        var g = res.First(s => s.Metric == "gdp");
        _status.Text = $"After {Months / 12} years vs unchanged: real GDP {(g.WithPolicy[^1] / g.Baseline[^1] - 1) * 100:+0.0;-0.0}%, " + string.Join(", ", lines) + ".";
    }
}
