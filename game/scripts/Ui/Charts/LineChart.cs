using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace EconGame.Ui;

public sealed class Series
{
    public string Name = "";
    public double[] X = Array.Empty<double>();   // months (or any numeric x)
    public double[] Y = Array.Empty<double>();
    public Color Color = Colors.White;
    public bool Dashed;
    public double[]? Lo, Hi;                       // optional uncertainty band
    public float Width = 2f;
}

/// <summary>Multi-series line chart with fan bands, nice ticks and a hover read-out.</summary>
public partial class LineChart : Control
{
    public List<Series> Items = new();
    public Func<double, string> YFormat = v => v.ToString("0.0");
    public Func<double, string> XFormat = v => v.ToString("0");
    public string Title = "";
    public bool ShowLegend = true;
    public double? YMin, YMax;
    public int StartYear = 2024;
    public bool XIsMonths = true;
    float? _hoverX;

    public LineChart() { CustomMinimumSize = new Vector2(240, 150); MouseFilter = MouseFilterEnum.Pass; }

    public void SetSeries(IEnumerable<Series> s) { Items = s.ToList(); QueueRedraw(); }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion m) { _hoverX = m.Position.X; QueueRedraw(); }
    }
    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit) { _hoverX = null; QueueRedraw(); }
    }

    static double NiceStep(double range, int target)
    {
        if (range <= 0) return 1;
        double raw = range / target, mag = Math.Pow(10, Math.Floor(Math.Log10(raw))), n = raw / mag;
        double nice = n < 1.5 ? 1 : n < 3 ? 2 : n < 7 ? 5 : 10;
        return nice * mag;
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont; int fs = Mathf.RoundToInt(12 * Pal.TextScale);
        var size = Size;
        float left = 52, right = 12, top = Title.Length > 0 ? 26 : 10, bottom = 24 + (ShowLegend && Items.Count > 1 ? 18 : 0);
        var plot = new Rect2(left, top, Mathf.Max(10, size.X - left - right), Mathf.Max(10, size.Y - top - bottom));
        if (Title.Length > 0) DrawString(font, new Vector2(0, 16), Title, HorizontalAlignment.Left, -1, Mathf.RoundToInt(13 * Pal.TextScale), Pal.Dim);

        var all = Items.Where(s => s.Y.Length > 0).ToList();
        if (all.Count == 0) { DrawString(font, plot.Position + new Vector2(10, 24), "No data yet", HorizontalAlignment.Left, -1, fs, Pal.Faint); return; }

        double xmin = all.Min(s => s.X.Min()), xmax = all.Max(s => s.X.Max());
        if (xmax - xmin < 1e-9) xmax = xmin + 1;
        double ymin = all.Min(s => Math.Min(s.Y.Min(), s.Lo?.Min() ?? double.MaxValue)), ymax = all.Max(s => Math.Max(s.Y.Max(), s.Hi?.Max() ?? double.MinValue));
        if (YMin != null) ymin = YMin.Value; if (YMax != null) ymax = YMax.Value;
        if (ymax - ymin < 1e-9) { ymax += 1; ymin -= 1; }
        double pad = (ymax - ymin) * 0.08; if (YMin == null) ymin -= pad; if (YMax == null) ymax += pad;
        double step = NiceStep(ymax - ymin, 4);
        double y0 = Math.Floor(ymin / step) * step;

        Vector2 P(double x, double y) => new(plot.Position.X + (float)((x - xmin) / (xmax - xmin)) * plot.Size.X,
                                              plot.Position.Y + plot.Size.Y - (float)((y - ymin) / (ymax - ymin)) * plot.Size.Y);

        for (double g = y0; g <= ymax + 1e-9; g += step)
        {
            if (g < ymin - 1e-9) continue;
            var a = P(xmin, g);
            DrawLine(a, new Vector2(plot.End.X, a.Y), Math.Abs(g) < 1e-12 ? Pal.Faint : new Color(1, 1, 1, 0.06f), 1);
            DrawString(font, new Vector2(2, a.Y + 4), YFormat(g), HorizontalAlignment.Right, left - 8, fs, Pal.Dim);
        }
        // x ticks
        double xspan = xmax - xmin; int nx = Mathf.Clamp((int)(plot.Size.X / 90), 2, 10);
        double xs = XIsMonths ? Math.Max(12, Math.Ceiling(xspan / 12.0 / nx) * 12) : NiceStep(xspan, nx);
        for (double x = Math.Ceiling(xmin / xs) * xs; x <= xmax + 1e-9; x += xs)
        {
            var p = P(x, ymin);
            DrawLine(new Vector2(p.X, plot.Position.Y), new Vector2(p.X, plot.End.Y), new Color(1, 1, 1, 0.04f), 1);
            DrawString(font, new Vector2(p.X - 20, plot.End.Y + 16), XIsMonths ? (StartYear + (int)(x / 12)).ToString() : XFormat(x), HorizontalAlignment.Center, 40, fs, Pal.Dim);
        }

        foreach (var s in all)
        {
            if (s.Lo != null && s.Hi != null && s.Lo.Length == s.X.Length)
            {
                var poly = new Vector2[s.X.Length * 2];
                for (int i = 0; i < s.X.Length; i++) { poly[i] = P(s.X[i], s.Hi[i]); poly[poly.Length - 1 - i] = P(s.X[i], s.Lo[i]); }
                if (s.X.Length > 1) DrawColoredPolygon(poly, new Color(s.Color, 0.18f));
            }
            var pts = new Vector2[s.X.Length];
            for (int i = 0; i < pts.Length; i++) pts[i] = P(s.X[i], s.Y[i]);
            if (pts.Length > 1)
            {
                if (s.Dashed) for (int i = 0; i + 1 < pts.Length; i += 2) DrawLine(pts[i], pts[i + 1], s.Color, s.Width, true);
                else DrawPolyline(pts, s.Color, s.Width, true);
            }
            else if (pts.Length == 1) DrawCircle(pts[0], 3, s.Color);
        }

        if (_hoverX is float hx && hx >= plot.Position.X && hx <= plot.End.X)
        {
            double xv = xmin + (hx - plot.Position.X) / plot.Size.X * (xmax - xmin);
            DrawLine(new Vector2(hx, plot.Position.Y), new Vector2(hx, plot.End.Y), new Color(1, 1, 1, 0.35f), 1);
            var lines = new List<(string, Color)> { (XIsMonths ? $"{StartYear + (int)(xv / 12)}-{(int)(xv % 12) + 1:D2}" : XFormat(xv), Pal.Dim) };
            foreach (var s in all)
            {
                int idx = 0; double best = double.MaxValue;
                for (int i = 0; i < s.X.Length; i++) { double d = Math.Abs(s.X[i] - xv); if (d < best) { best = d; idx = i; } }
                DrawCircle(P(s.X[idx], s.Y[idx]), 4, s.Color);
                lines.Add(($"{s.Name}: {YFormat(s.Y[idx])}", s.Color));
            }
            float w = lines.Max(l => font.GetStringSize(l.Item1, HorizontalAlignment.Left, -1, fs).X) + 14, h = lines.Count * (fs + 5) + 8;
            float bx = hx + 12 + w > size.X ? hx - 12 - w : hx + 12;
            DrawRect(new Rect2(bx, plot.Position.Y + 4, w, h), new Color(Pal.PanelHi, 0.95f));
            for (int i = 0; i < lines.Count; i++) DrawString(font, new Vector2(bx + 7, plot.Position.Y + 4 + 4 + (i + 1) * (fs + 5) - 4), lines[i].Item1, HorizontalAlignment.Left, -1, fs, lines[i].Item2);
        }

        if (ShowLegend && all.Count > 1)
        {
            float lx = left;
            foreach (var s in all)
            {
                DrawRect(new Rect2(lx, size.Y - 12, 12, 3), s.Color);
                DrawString(font, new Vector2(lx + 16, size.Y - 6), s.Name, HorizontalAlignment.Left, -1, fs, Pal.Dim);
                lx += 28 + font.GetStringSize(s.Name, HorizontalAlignment.Left, -1, fs).X;
            }
        }
    }
}
