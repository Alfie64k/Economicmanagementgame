using System.Collections.Generic;
using Godot;

namespace EconGame.Ui;

/// <summary>The window chips (2 years, 5 years, all) and the markers toggle for annotated charts. Every bar edits the shared <see cref="ChartPrefs"/>, so they stay in step across pages.</summary>
public partial class ChartRangeBar : HBoxContainer
{
    readonly Dictionary<int, Button> _win = new();
    readonly Button _marks;

    public ChartRangeBar(string? hint = null)
    {
        AddThemeConstantOverride("separation", 6);
        AddChild(UI.Dim("Show", 13));
        foreach (var (months, label) in new[] { (24, "2 years"), (60, "5 years"), (0, "All") })
        {
            int m = months; var b = UI.Chip(label, ChartPrefs.Window == m, () => ChartPrefs.Set(m, ChartPrefs.Markers));
            b.TooltipText = m == 0 ? "The whole game so far" : $"The last {label}";
            _win[m] = b; AddChild(b);
        }
        _marks = UI.Chip("▼▲ Actions and events", ChartPrefs.Markers, () => ChartPrefs.Set(ChartPrefs.Window, !ChartPrefs.Markers));
        _marks.TooltipText = "Mark on the charts what you did (▼) and what happened to you (▲). Hover a marker to read it.";
        AddChild(_marks);
        if (hint != null) AddChild(UI.Dim(hint, 12));
    }

    public override void _EnterTree() { ChartPrefs.Changed += Sync; Sync(); }
    public override void _ExitTree() { ChartPrefs.Changed -= Sync; }
    void Sync()
    {
        foreach (var kv in _win) kv.Value.SetPressedNoSignal(kv.Key == ChartPrefs.Window);
        _marks.SetPressedNoSignal(ChartPrefs.Markers);
    }
}
