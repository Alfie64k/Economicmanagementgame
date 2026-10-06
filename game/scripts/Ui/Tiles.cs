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
    bool _hover, _selected;

    /// <summary>Marks the tile as the active choice (for example the metric the "why" panel explains).</summary>
    public bool Selected { get => _selected; set { if (_selected == value) return; _selected = value; ApplyStyle(); } }

    void ApplyStyle() => AddThemeStyleboxOverride("panel", StateStyles.Tile(_hover, _selected));

    public override void _Draw()
    {
        if (HasFocus()) DrawStyleBox(StateStyles.FocusBox(), new Rect2(Vector2.Zero, Size));
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventKey k && k.Pressed && !k.Echo && (k.Keycode == Key.Enter || k.Keycode == Key.KpEnter || k.Keycode == Key.Space)) { Clicked?.Invoke(); AcceptEvent(); }
    }

    public KpiTile(string title)
    {
        ApplyStyle();
        FocusMode = FocusModeEnum.All; MouseDefaultCursorShape = CursorShape.PointingHand;
        FocusEntered += QueueRedraw; FocusExited += QueueRedraw;
        CustomMinimumSize = new Vector2(150, 0);
        _title = UI.Dim(title, 12); _value = UI.Lbl("-", 24, Pal.Text, true); _delta = UI.Lbl("", 12, Pal.Dim);
        var row = UI.HBox(6, UI.VBox(0, _value, _delta), UI.Spacer(0, 0, true), _spark);
        AddChild(UI.VBox(2, _title, row));
        MouseFilter = MouseFilterEnum.Stop;
        MouseEntered += () => { _hover = true; ApplyStyle(); if (Explain != null) TooltipText = Explain(); };
        MouseExited += () => { _hover = false; ApplyStyle(); };
        GuiInput += e => { if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left) Clicked?.Invoke(); };
    }

    public void Set(string value, string delta, Color? deltaColor, double[] history, Color? sparkColor = null)
    {
        _value.Text = value; _delta.Text = delta; _delta.AddThemeColorOverride("font_color", deltaColor ?? Pal.Dim);
        _spark.Visible = history.Length > 1;          // tiles without a series should not reserve the sparkline's width
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
