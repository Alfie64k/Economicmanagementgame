using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace EconGame.Ui;

public sealed class BarItem
{
    public string Label = "";
    public double Value;
    public Color Color = Colors.White;
    public string? Text;          // custom value text
    public string? Tooltip;
    public bool Emphasis;         // drawn with an outline and bold label (the player's own row, a chosen rival)
}

/// <summary>Horizontal bars; with Diverging, bars extend left (negative) or right (positive) from a centre line.</summary>
public partial class BarList : Control
{
    public List<BarItem> Items = new();
    public bool Diverging;
    public Func<double, string> Format = v => v.ToString("0.0");
    public double? Max;
    public int RowHeight = 24;
    public int LabelWidth = 150;

    /// <summary>Rows can be clicked (set by pages that use the list as a picker).</summary>
    public bool Clickable;
    public event Action<int>? Picked;
    int _hover = -1;

    public BarList() { MouseFilter = MouseFilterEnum.Pass; }

    public void Set(IEnumerable<BarItem> items) { Items = items.ToList(); CustomMinimumSize = new Vector2(200, Items.Count * RowHeight + 4); QueueRedraw(); }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion m)
        {
            int i = (int)(m.Position.Y / RowHeight);
            TooltipText = i >= 0 && i < Items.Count ? Items[i].Tooltip ?? "" : "";
            int h = Clickable && i >= 0 && i < Items.Count ? i : -1;
            if (h != _hover) { _hover = h; MouseDefaultCursorShape = h >= 0 ? CursorShape.PointingHand : CursorShape.Arrow; QueueRedraw(); }
        }
        else if (Clickable && e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            int i = (int)(mb.Position.Y / RowHeight);
            if (i >= 0 && i < Items.Count) Picked?.Invoke(i);
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit && _hover != -1) { _hover = -1; QueueRedraw(); }
    }

    public override void _Draw()
    {
        if (Items.Count == 0) return;
        var font = ThemeDB.FallbackFont; int fs = Mathf.RoundToInt(13 * Pal.TextScale);
        double max = Max ?? Math.Max(1e-9, Items.Max(i => Math.Abs(i.Value)));
        float x0 = LabelWidth, w = Size.X - LabelWidth - 70;
        for (int i = 0; i < Items.Count; i++)
        {
            var it = Items[i]; float y = i * RowHeight;
            if (i == _hover) DrawRect(new Rect2(0, y + 1, Size.X, RowHeight - 2), Pal.Hover);
            if (it.Emphasis) DrawRect(new Rect2(0, y + 1, Size.X, RowHeight - 2), new Color(Pal.Accent, 0.14f));
            DrawString(font, new Vector2(0, y + RowHeight * 0.68f), (it.Emphasis ? "▸ " : "") + it.Label, HorizontalAlignment.Left, LabelWidth - 8, fs, it.Emphasis ? Pal.Text : Pal.Dim);
            float bh = RowHeight - 9;
            DrawRect(new Rect2(x0, y + 4, w, bh), new Color(1, 1, 1, 0.04f));
            if (Diverging)
            {
                float cx = x0 + w / 2; float len = (float)(Math.Abs(it.Value) / max) * w / 2;
                DrawRect(new Rect2(it.Value >= 0 ? cx : cx - len, y + 4, len, bh), it.Color);
                DrawLine(new Vector2(cx, y + 2), new Vector2(cx, y + RowHeight - 2), Pal.Faint, 1);
            }
            else DrawRect(new Rect2(x0, y + 4, (float)(Math.Clamp(it.Value / max, 0, 1)) * w, bh), it.Color);
            DrawString(font, new Vector2(x0 + w + 8, y + RowHeight * 0.68f), it.Text ?? Format(it.Value), HorizontalAlignment.Left, 64, fs, Pal.Text);
        }
    }
}
