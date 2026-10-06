using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace EconGame.Ui;

public sealed class Seg { public string Label = ""; public double Value; public Color Color = Colors.White; }

/// <summary>A single horizontal stacked bar with in-bar labels where they fit, plus a legend row.</summary>
public partial class StackBar : Control
{
    public List<Seg> Segments = new();
    public Func<double, string> Format = v => v.ToString("0.0");
    public string Caption = "";
    public double? Total;

    public StackBar() { CustomMinimumSize = new Vector2(200, 70); MouseFilter = MouseFilterEnum.Pass; }
    public void Set(IEnumerable<Seg> segs, string caption = "") { Segments = segs.Where(s => s.Value > 1e-9).ToList(); Caption = caption; QueueRedraw(); }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion m)
        {
            double tot = Total ?? Segments.Sum(s => s.Value); float x = 0; TooltipText = "";
            foreach (var s in Segments)
            {
                float w = (float)(s.Value / tot) * Size.X;
                if (m.Position.X >= x && m.Position.X < x + w && m.Position.Y > 18 && m.Position.Y < 46) TooltipText = $"{s.Label}: {Format(s.Value)}";
                x += w;
            }
        }
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont; int fs = Mathf.RoundToInt(12 * Pal.TextScale);
        DrawString(font, new Vector2(0, 12), Caption, HorizontalAlignment.Left, -1, fs, Pal.Dim);
        if (Segments.Count == 0) return;
        double tot = Total ?? Segments.Sum(s => s.Value); float x = 0;
        foreach (var s in Segments)
        {
            float w = (float)(s.Value / tot) * Size.X;
            DrawRect(new Rect2(x, 18, Mathf.Max(1, w - 1), 28), s.Color);
            if (w > 46) DrawString(font, new Vector2(x + 5, 37), Format(s.Value), HorizontalAlignment.Left, w - 8, fs, Colors.White);
            x += w;
        }
        float lx = 0;
        foreach (var s in Segments)
        {
            DrawRect(new Rect2(lx, 56, 9, 9), s.Color);
            DrawString(font, new Vector2(lx + 13, 65), s.Label, HorizontalAlignment.Left, -1, fs - 1, Pal.Dim);
            lx += 24 + font.GetStringSize(s.Label, HorizontalAlignment.Left, -1, fs - 1).X;
            if (lx > Size.X - 60) break;
        }
    }
}
