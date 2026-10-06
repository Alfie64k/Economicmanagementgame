using System;
using FileAccess = Godot.FileAccess;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace EconGame.Map;

public sealed class Shape
{
    public string Id = "", Name = "";
    public Vector2[][] Polys = Array.Empty<Vector2[]>();
    public Vector2 Centroid;               // (lon, -lat)
    public Rect2 Bounds;
    public bool Hit(Vector2 p)
    {
        if (!Bounds.HasPoint(p)) return false;
        foreach (var poly in Polys) if (Geometry2D.IsPointInPolygon(p, poly)) return true;
        return false;
    }
    public Rect2 LargestBounds()
    {
        Rect2 best = default; float bestA = -1;
        foreach (var poly in Polys)
        {
            var r = BoundsOf(poly); float a = r.Size.X * r.Size.Y;
            if (a > bestA) { bestA = a; best = r; }
        }
        return best;
    }
    public static Rect2 BoundsOf(Vector2[] pts)
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (var p in pts) { x0 = Mathf.Min(x0, p.X); y0 = Mathf.Min(y0, p.Y); x1 = Mathf.Max(x1, p.X); y1 = Mathf.Max(y1, p.Y); }
        return new Rect2(x0, y0, x1 - x0, y1 - y0);
    }
}

/// <summary>Natural Earth country and sub-national polygons (see tools/map_prep). World coordinates are (longitude, -latitude).</summary>
public static class MapData
{
    public static List<Shape> Countries = new();
    public static Dictionary<string, List<Shape>> Regions = new();
    public static bool Loaded => Countries.Count > 0;

    public static void EnsureLoaded()
    {
        if (Loaded) return;
        using var f = FileAccess.Open("res://data/world_map.json", FileAccess.ModeFlags.Read);
        using var doc = JsonDocument.Parse(f.GetAsText());
        foreach (var c in doc.RootElement.GetProperty("countries").EnumerateArray()) Countries.Add(Parse(c));
        foreach (var kv in doc.RootElement.GetProperty("regions").EnumerateObject())
            Regions[kv.Name] = kv.Value.EnumerateArray().Select(Parse).ToList();
    }

    static Shape Parse(JsonElement e)
    {
        var polys = e.GetProperty("polys").EnumerateArray().Select(r => r.EnumerateArray().Select(p => new Vector2((float)p[0].GetDouble(), -(float)p[1].GetDouble())).ToArray()).ToArray();
        var c = e.GetProperty("c");
        var s = new Shape { Id = e.GetProperty("id").GetString()!, Name = e.GetProperty("name").GetString()!, Polys = polys, Centroid = new Vector2((float)c[0].GetDouble(), -(float)c[1].GetDouble()) };
        var all = polys.SelectMany(p => p).ToArray();
        s.Bounds = Shape.BoundsOf(all);
        return s;
    }

    public static Shape? Find(string id) => Countries.FirstOrDefault(c => c.Id == id);

    /// <summary>Smallest country polygon containing the world point (lon, -lat), so enclaves win over their surroundings.</summary>
    public static Shape? CountryAt(Vector2 world)
    {
        Shape? best = null; float bestA = float.MaxValue;
        foreach (var s in Countries)
        {
            if (!s.Bounds.HasPoint(world) || !s.Hit(world)) continue;
            float a = s.Bounds.Size.X * s.Bounds.Size.Y;
            if (a < bestA) { bestA = a; best = s; }
        }
        return best;
    }
}
