using System.Collections.Generic;
using System.Linq;
using Godot;
using EconGame.Map;

namespace EconGame.Ui;

/// <summary>Flag-like identity mark for a country: its Natural Earth silhouette, or a coloured badge with the code when no shape is available.</summary>
public partial class Emblem : Control
{
    readonly Control _holder = new() { MouseFilter = MouseFilterEnum.Ignore };
    string _code = ""; Color _tint = Pal.Accent; bool _fallback;

    public Emblem() { CustomMinimumSize = new Vector2(120, 90); MouseFilter = MouseFilterEnum.Ignore; ClipContents = true; AddChild(_holder); }

    public void Set(string id)
    {
        foreach (var c in _holder.GetChildren()) { _holder.RemoveChild(c); c.QueueFree(); }
        _code = id; _tint = Pal.Series[(int)((uint)id.GetHashCode() % Pal.Series.Length)];
        MapData.EnsureLoaded();
        var shape = MapData.Find(id);
        _fallback = shape == null || shape.Polys.Length == 0;
        if (!_fallback)
        {
            // keep the main landmass and nearby islands; distant overseas territories would shrink the silhouette
            var main = shape!.LargestBounds();
            var near = main.Grow(Mathf.Max(main.Size.X, main.Size.Y) * 0.6f);
            var polys = shape.Polys.Where(p => p.Length >= 3 && near.Intersects(Shape.BoundsOf(p))).ToList();
            var all = polys.SelectMany(p => p).ToArray();
            var b = Shape.BoundsOf(all);
            float s = Mathf.Min((Size.X - 12) / Mathf.Max(b.Size.X, 0.5f), (Size.Y - 12) / Mathf.Max(b.Size.Y, 0.5f));
            var origin = Size / 2 - (b.Position + b.Size / 2) * s;
            foreach (var poly in polys)
            {
                var pts = poly.Select(p => origin + p * s).ToArray();
                _holder.AddChild(new Polygon2D { Polygon = pts, Color = Pal.Accent.Darkened(0.35f) });
                var line = new Line2D { Points = pts.Append(pts[0]).ToArray(), Width = 1.2f, DefaultColor = Pal.Accent.Lightened(0.2f), Antialiased = true };
                _holder.AddChild(line);
            }
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawStyleBox(AppTheme.Box(new Color(0.04f, 0.07f, 0.1f), 10, Pal.Border, 1), new Rect2(Vector2.Zero, Size));
        if (_fallback)
        {
            DrawCircle(Size / 2, Mathf.Min(Size.X, Size.Y) * 0.36f, _tint.Darkened(0.3f));
            var font = ThemeDB.FallbackFont; int fs = Mathf.RoundToInt(26 * Pal.TextScale);
            DrawString(font, new Vector2(0, Size.Y / 2 + fs * 0.35f), _code, HorizontalAlignment.Center, Size.X, fs, Colors.White);
        }
    }
}
