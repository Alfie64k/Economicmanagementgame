using System;
using System.Linq;
using Godot;

namespace EconGame.Ui;

public partial class Spark : Control
{
    public double[] Values = Array.Empty<double>();
    public Color Color = Pal.Accent;
    public Spark() { CustomMinimumSize = new Vector2(80, 28); MouseFilter = MouseFilterEnum.Ignore; }
    public void Set(double[] v, Color? c = null) { Values = v; if (c != null) Color = c.Value; QueueRedraw(); }

    public override void _Draw()
    {
        if (Values.Length < 2) return;
        double mn = Values.Min(), mx = Values.Max(); if (mx - mn < 1e-12) { mx += 1; mn -= 1; }
        var pts = new Vector2[Values.Length];
        for (int i = 0; i < pts.Length; i++)
            pts[i] = new Vector2(i / (float)(pts.Length - 1) * Size.X, Size.Y - 2 - (float)((Values[i] - mn) / (mx - mn)) * (Size.Y - 4));
        DrawPolyline(pts, Color, 1.6f, true);
        DrawCircle(pts[^1], 2.5f, Color);
    }
}
