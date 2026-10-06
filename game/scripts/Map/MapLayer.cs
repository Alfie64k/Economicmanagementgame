using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using EconGame.Ui;

namespace EconGame.Map;

public sealed class Pin { public Vector2 Pos; public Color Color; public string Text = ""; public string Id = ""; }
public sealed class Flow { public Vector2 From, To; public float Width; public Color Color; }

/// <summary>Renders countries (and optional regions, flows and pins) in world coordinates. Used by the 2D map directly and by the globe through a SubViewport.</summary>
public partial class MapLayer : Node2D
{
    sealed class Drawn { public Shape S = null!; public List<Polygon2D> Fills = new(); public List<Line2D> Lines = new(); }
    readonly Dictionary<string, Drawn> _countries = new();
    readonly List<Drawn> _regions = new();
    readonly Node2D _countryRoot = new(), _regionRoot = new(), _hoverRoot = new(), _hiRoot = new();
    string? _hoverId;
    readonly FlowPin _overlay = new();
    float _zoom = 1;
    public static readonly Color Unsimulated = new("1E2833");

    public override void _Ready()
    {
        MapData.EnsureLoaded();
        AddChild(_countryRoot); AddChild(_regionRoot); AddChild(_hoverRoot); AddChild(_hiRoot); AddChild(_overlay);
        foreach (var s in MapData.Countries)
        {
            var d = new Drawn { S = s };
            foreach (var poly in s.Polys)
            {
                var pg = new Polygon2D { Polygon = poly, Color = Unsimulated, Antialiased = true };
                var ln = new Line2D { Points = poly.Append(poly[0]).ToArray(), Width = 0.25f, DefaultColor = new Color(0.30f, 0.38f, 0.47f), Antialiased = true, JointMode = Line2D.LineJointMode.Round };
                _countryRoot.AddChild(pg); _countryRoot.AddChild(ln); d.Fills.Add(pg); d.Lines.Add(ln);
            }
            _countries[s.Id] = d;
        }
    }

    public void SetFill(Func<string, Color?> color)
    {
        foreach (var kv in _countries) { var c = color(kv.Key) ?? Unsimulated; foreach (var p in kv.Value.Fills) p.Color = c; }
    }

    public void SetZoom(float zoom)
    {
        _zoom = zoom;
        float w = 0.9f / zoom;
        foreach (var d in _countries.Values) foreach (var l in d.Lines) l.Width = w;
        foreach (var d in _regions) foreach (var l in d.Lines) l.Width = 0.7f / zoom;
        foreach (var l in _hiRoot.GetChildren().OfType<Line2D>()) l.Width = 2.2f / zoom;
        foreach (var l in _hoverRoot.GetChildren().OfType<Line2D>()) l.Width = 1.6f / zoom;
        _overlay.Zoom = zoom; _overlay.QueueRedraw();
    }

    public void Highlight(string? id, Color color)
    {
        foreach (var c in _hiRoot.GetChildren()) c.QueueFree();
        if (id == null || !_countries.TryGetValue(id, out var d)) return;
        foreach (var poly in d.S.Polys)
            _hiRoot.AddChild(new Line2D { Points = poly.Append(poly[0]).ToArray(), Width = 2.2f / _zoom, DefaultColor = color, Antialiased = true, JointMode = Line2D.LineJointMode.Round });
    }

    /// <summary>Pointer-over outline: lighter and thinner than the selection outline so the two stay distinguishable.</summary>
    public void SetHover(string? id)
    {
        if (id == _hoverId) return;
        _hoverId = id;
        foreach (var c in _hoverRoot.GetChildren()) c.QueueFree();
        if (id == null || !_countries.TryGetValue(id, out var d)) return;
        foreach (var poly in d.S.Polys)
            _hoverRoot.AddChild(new Line2D { Points = poly.Append(poly[0]).ToArray(), Width = 1.6f / _zoom, DefaultColor = new Color(Pal.Text, 0.75f), Antialiased = true, JointMode = Line2D.LineJointMode.Round });
    }

    /// <summary>Show a country's regions on top of the national polygon, coloured by the given function (null = hide regions).</summary>
    public void ShowRegions(string? country, Func<string, Color>? color)
    {
        foreach (var c in _regionRoot.GetChildren()) c.QueueFree();
        _regions.Clear();
        if (country == null || color == null || !MapData.Regions.TryGetValue(country, out var list)) return;
        foreach (var s in list)
        {
            var d = new Drawn { S = s }; var col = color(s.Id);
            foreach (var poly in s.Polys)
            {
                var pg = new Polygon2D { Polygon = poly, Color = col, Antialiased = true };
                var ln = new Line2D { Points = poly.Append(poly[0]).ToArray(), Width = 0.7f / _zoom, DefaultColor = new Color(0, 0, 0, 0.55f), Antialiased = true };
                _regionRoot.AddChild(pg); _regionRoot.AddChild(ln); d.Fills.Add(pg); d.Lines.Add(ln);
            }
            _regions.Add(d);
        }
    }

    public Shape? RegionAt(Vector2 p) => _regions.Select(r => r.S).FirstOrDefault(s => s.Hit(p));
    public bool HasRegions => _regions.Count > 0;

    public void SetOverlay(List<Flow> flows, List<Pin> pins) { _overlay.Flows = flows; _overlay.Pins = pins; _overlay.QueueRedraw(); }
    public Pin? PinAt(Vector2 world, float tolerancePx) => _overlay.Pins.FirstOrDefault(p => p.Pos.DistanceTo(world) * _zoom < tolerancePx);

    partial class FlowPin : Node2D
    {
        public List<Flow> Flows = new(); public List<Pin> Pins = new(); public float Zoom = 1;
        public override void _Draw()
        {
            foreach (var f in Flows)
            {
                var mid = (f.From + f.To) / 2 + new Vector2(0, -f.From.DistanceTo(f.To) * 0.18f);
                var pts = new Vector2[24];
                for (int i = 0; i < pts.Length; i++) { float t = i / (float)(pts.Length - 1); pts[i] = (1 - t) * (1 - t) * f.From + 2 * (1 - t) * t * mid + t * t * f.To; }
                DrawPolyline(pts, f.Color, f.Width / Zoom, true);
                var dir = (pts[^1] - pts[^2]).Normalized(); var n = new Vector2(-dir.Y, dir.X); float a = 5f / Zoom;
                DrawColoredPolygon(new[] { pts[^1], pts[^1] - dir * a * 1.6f + n * a * 0.7f, pts[^1] - dir * a * 1.6f - n * a * 0.7f }, f.Color);
            }
            foreach (var p in Pins)
            {
                DrawCircle(p.Pos, 6f / Zoom, new Color(0, 0, 0, 0.6f));
                DrawCircle(p.Pos, 4.5f / Zoom, p.Color);
                DrawArc(p.Pos, 8f / Zoom, 0, Mathf.Tau, 20, p.Color, 1.2f / Zoom, true);
            }
        }
    }
}
