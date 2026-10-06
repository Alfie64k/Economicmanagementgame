using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace EconGame.Ui;

/// <summary>Stacked area chart over a shared x axis (e.g. sector value added over time).</summary>
public partial class StackedChart : Control
{
    public List<Series> Layers = new();
    public Func<double, string> YFormat = v => v.ToString("0");
    public int StartYear = 2024;
    public bool Percent;

    public StackedChart() { CustomMinimumSize = new Vector2(240, 180); MouseFilter = MouseFilterEnum.Ignore; }
    public void Set(IEnumerable<Series> layers) { Layers = layers.ToList(); QueueRedraw(); }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont; int fs = Mathf.RoundToInt(12 * Pal.TextScale);
        if (Layers.Count == 0 || Layers[0].X.Length < 2) { DrawString(font, new Vector2(10, 24), "No data yet", HorizontalAlignment.Left, -1, fs, Pal.Faint); return; }
        int n = Layers[0].X.Length;
        float left = 48, right = 10, top = 8, bottom = 38;
        var plot = new Rect2(left, top, Size.X - left - right, Size.Y - top - bottom);
        var totals = new double[n];
        foreach (var l in Layers) for (int i = 0; i < n; i++) totals[i] += Math.Max(0, l.Y[i]);
        double ymax = Percent ? 1 : totals.Max() * 1.05;
        double xmin = Layers[0].X.Min(), xmax = Layers[0].X.Max();
        Vector2 P(double x, double y) => new(plot.Position.X + (float)((x - xmin) / (xmax - xmin)) * plot.Size.X, plot.End.Y - (float)(y / ymax) * plot.Size.Y);

        var baseLine = new double[n];
        foreach (var l in Layers)
        {
            var poly = new Vector2[n * 2];
            for (int i = 0; i < n; i++)
            {
                double v = Math.Max(0, l.Y[i]) / (Percent ? Math.Max(1e-12, totals[i]) : 1);
                poly[i] = P(l.X[i], baseLine[i] + v); poly[poly.Length - 1 - i] = P(l.X[i], baseLine[i]);
                baseLine[i] += v;
            }
            DrawColoredPolygon(poly, new Color(l.Color, 0.9f));
        }
        for (int g = 0; g <= 4; g++)
        {
            double v = ymax * g / 4; var a = P(xmin, v);
            DrawLine(a, new Vector2(plot.End.X, a.Y), new Color(1, 1, 1, 0.08f), 1);
            DrawString(font, new Vector2(2, a.Y + 4), Percent ? $"{v * 100:0}%" : YFormat(v), HorizontalAlignment.Right, left - 8, fs, Pal.Dim);
        }
        double xs = Math.Max(12, Math.Ceiling((xmax - xmin) / 12.0 / 6) * 12);
        for (double x = Math.Ceiling(xmin / xs) * xs; x <= xmax; x += xs)
            DrawString(font, new Vector2(P(x, 0).X - 20, plot.End.Y + 14), (StartYear + (int)(x / 12)).ToString(), HorizontalAlignment.Center, 40, fs, Pal.Dim);
        float lx = left;
        foreach (var l in Layers)
        {
            DrawRect(new Rect2(lx, Size.Y - 14, 10, 10), l.Color);
            DrawString(font, new Vector2(lx + 14, Size.Y - 5), l.Name, HorizontalAlignment.Left, -1, fs, Pal.Dim);
            lx += 26 + font.GetStringSize(l.Name, HorizontalAlignment.Left, -1, fs).X;
        }
    }
}
