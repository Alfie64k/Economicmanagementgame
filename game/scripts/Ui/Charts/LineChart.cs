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
    /// <summary>Follow the shared window and marker preferences (<see cref="ChartPrefs"/>) and show <see cref="Markers"/>. Only for charts whose x axis is game months.</summary>
    public bool Annotated;
    public List<ChartMarker> Markers = new();
    /// <summary>Charts with the same non-empty group draw a shared crosshair at the month the mouse is over in any of them.</summary>
    public string SyncGroup = "";
    float? _hoverX; double? _syncX;
    static event Action<string, double?>? Synced;

    public LineChart() { CustomMinimumSize = new Vector2(240, 150); MouseFilter = MouseFilterEnum.Pass; }

    public override void _EnterTree() { Synced += OnSynced; ChartPrefs.Changed += OnPrefs; }
    public override void _ExitTree() { Synced -= OnSynced; ChartPrefs.Changed -= OnPrefs; }
    void OnSynced(string group, double? x) { if (SyncGroup.Length > 0 && group == SyncGroup && _hoverX == null) { _syncX = x; QueueRedraw(); } }
    void OnPrefs() { if (Annotated) QueueRedraw(); }

    public void SetSeries(IEnumerable<Series> s) { Items = s.ToList(); QueueRedraw(); }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion m)
        {
            _hoverX = m.Position.X; _syncX = null; QueueRedraw();
            if (SyncGroup.Length > 0 && _plot.Size.X > 0 && XIsMonths) Synced?.Invoke(SyncGroup, _xmin + (m.Position.X - _plot.Position.X) / _plot.Size.X * (_xmax - _xmin));
        }
    }
    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit) { _hoverX = null; QueueRedraw(); if (SyncGroup.Length > 0) Synced?.Invoke(SyncGroup, null); }
    }
    Rect2 _plot; double _xmin, _xmax;
    /// <summary>Screen x of a month on this chart as last drawn, or null if it is outside the plot (used by tests).</summary>
    public float? ScreenXOf(double month) => _plot.Size.X > 0 && month >= _xmin && month <= _xmax ? GlobalPosition.X + _plot.Position.X + (float)((month - _xmin) / (_xmax - _xmin)) * _plot.Size.X : null;
    public Rect2 PlotRect => new(GlobalPosition + _plot.Position, _plot.Size);

    /// <summary>The part of a series at or after <paramref name="x0"/>.</summary>
    static Series Clip(Series s, double x0)
    {
        int i0 = 0; while (i0 < s.X.Length && s.X[i0] < x0) i0++;
        if (i0 == 0) return s;
        T[]? Cut<T>(T[]? a) => a == null ? null : a.Skip(i0).ToArray();
        return new Series { Name = s.Name, Color = s.Color, Dashed = s.Dashed, Width = s.Width, X = s.X.Skip(i0).ToArray(), Y = s.Y.Skip(i0).ToArray(), Lo = Cut(s.Lo), Hi = Cut(s.Hi) };
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
        bool windowed = Annotated && XIsMonths && ChartPrefs.Window > 0 && xmax - xmin > ChartPrefs.Window + 1;
        if (windowed) { xmin = xmax - ChartPrefs.Window; all = all.Select(s => Clip(s, xmin)).Where(s => s.Y.Length > 0).ToList(); }
        if (xmax - xmin < 1e-9) xmax = xmin + 1;
        _plot = plot; _xmin = xmin; _xmax = xmax;
        var marks = Annotated && XIsMonths && ChartPrefs.Markers ? Markers.Where(mk => mk.X >= xmin && mk.X <= xmax).ToList() : new List<ChartMarker>();
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

        foreach (var mk in marks)
        {
            float mx = P(mk.X, ymin).X; var col = new Color(mk.Color, 0.9f);
            DrawLine(new Vector2(mx, plot.Position.Y + 6), new Vector2(mx, plot.End.Y), new Color(mk.Color, 0.28f), 1);
            // triangles point down for what you did and up for what happened to you: the shape carries the meaning without colour
            var tri = mk.Mine ? new[] { new Vector2(mx - 4, plot.Position.Y), new Vector2(mx + 4, plot.Position.Y), new Vector2(mx, plot.Position.Y + 7) }
                              : new[] { new Vector2(mx - 4, plot.Position.Y + 7), new Vector2(mx + 4, plot.Position.Y + 7), new Vector2(mx, plot.Position.Y) };
            DrawColoredPolygon(tri, col);
        }

        double? active = null; float hx = 0; bool local = false;
        if (_hoverX is float lx0 && lx0 >= plot.Position.X && lx0 <= plot.End.X) { hx = lx0; active = xmin + (hx - plot.Position.X) / plot.Size.X * (xmax - xmin); local = true; }
        else if (_syncX is double sx && sx >= xmin && sx <= xmax) { active = sx; hx = P(sx, ymin).X; }
        if (active is double xv)
        {
            DrawLine(new Vector2(hx, plot.Position.Y), new Vector2(hx, plot.End.Y), new Color(1, 1, 1, local ? 0.35f : 0.2f), 1);
            var lines = new List<(string, Color)> { (XIsMonths ? $"{StartYear + (int)(xv / 12)}-{(int)(xv % 12) + 1:D2}" : XFormat(xv), Pal.Dim) };
            foreach (var s in all)
            {
                int idx = 0; double best = double.MaxValue;
                for (int i = 0; i < s.X.Length; i++) { double d = Math.Abs(s.X[i] - xv); if (d < best) { best = d; idx = i; } }
                DrawCircle(P(s.X[idx], s.Y[idx]), local ? 4 : 3, s.Color);
                lines.Add(($"{s.Name}: {YFormat(s.Y[idx])}", s.Color));
            }
            if (local) foreach (var mk in marks.Where(mk => Math.Abs(P(mk.X, ymin).X - hx) <= 6))
                lines.Add(((mk.Mine ? "▼ " : "▲ ") + mk.Label, mk.Color));
            float cap = Mathf.Max(80, size.X - 16);
            for (int i = 0; i < lines.Count; i++)
            {
                var (t, c) = lines[i];
                while (t.Length > 4 && font.GetStringSize(t, HorizontalAlignment.Left, -1, fs).X > cap - 14) t = t[..^2].TrimEnd() + "…";
                lines[i] = (t, c);
            }
            float w = lines.Max(l => font.GetStringSize(l.Item1, HorizontalAlignment.Left, -1, fs).X) + 14, h = lines.Count * (fs + 5) + 8;
            float bx = Mathf.Clamp(hx + 12 + w > size.X ? hx - 12 - w : hx + 12, 0, Mathf.Max(0, size.X - w));
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
