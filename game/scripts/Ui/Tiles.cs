using System;
using Godot;

namespace EconGame.Ui;

/// <summary>Headline metric tile with a sparkline; hovering shows a "why" explanation supplied by the caller.</summary>
public partial class KpiTile : PanelContainer
{
    readonly Label _title, _value, _delta;
    readonly Spark _spark = new() { CustomMinimumSize = new Vector2(70, 30) };
    public Func<string>? Explain;
    public event Action? Clicked;

    public KpiTile(string title)
    {
        AddThemeStyleboxOverride("panel", AppTheme.Box(Pal.Panel, 10, Pal.Border, 1, 12));
        CustomMinimumSize = new Vector2(150, 0);
        _title = UI.Dim(title, 12); _value = UI.Lbl("-", 24, Pal.Text, true); _delta = UI.Lbl("", 12, Pal.Dim);
        var row = UI.HBox(6, UI.VBox(0, _value, _delta), UI.Spacer(0, 0, true), _spark);
        AddChild(UI.VBox(2, _title, row));
        MouseFilter = MouseFilterEnum.Stop;
        MouseEntered += () => { if (Explain != null) TooltipText = Explain(); };
        GuiInput += e => { if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left) Clicked?.Invoke(); };
    }

    public void Set(string value, string delta, Color? deltaColor, double[] history, Color? sparkColor = null)
    {
        _value.Text = value; _delta.Text = delta; _delta.AddThemeColorOverride("font_color", deltaColor ?? Pal.Dim);
        _spark.Set(history, sparkColor ?? Pal.Accent);
    }
}

/// <summary>Titled card container for view sections.</summary>
public static class Cards
{
    public static PanelContainer Section(string title, Control body, string? sub = null)
    {
        var box = UI.VBox(8);
        box.AddChild(UI.H2(title));
        if (sub != null) box.AddChild(UI.Dim(sub, 13, true));
        box.AddChild(body);
        return UI.Card(box);
    }

    public static Label Chip(string text, Color color)
    {
        var l = UI.Lbl(text, 12, Colors.White, true);
        l.AddThemeStyleboxOverride("normal", AppTheme.Box(color.Darkened(0.3f), 10, null, 0, 5));
        return l;
    }
}
