using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using EconGame.Ui;

namespace EconGame.Map;

/// <summary>Common surface API so the 2D map and the 3D globe can share one briefing panel.</summary>
public interface IMapSurface
{
    MapLayer Layer { get; }
    Control Node { get; }
    event Action<Shape?>? Hovered;
    event Action<Shape?>? Clicked;
    event Action<Shape>? RegionClicked;
    Func<Shape, string>? HoverText { get; set; }
    void FocusOn(Shape s);
    void Fit();
    void SetOverlay(List<Flow> flows, List<Pin> pins);
    void Refreshed();
}

/// <summary>Pan/zoom/hover/click surface hosting a <see cref="MapLayer"/>.</summary>
public partial class MapCanvas : Control, IMapSurface
{
    public Control Node => this;
    public void SetOverlay(List<Flow> flows, List<Pin> pins) => Layer.SetOverlay(flows, pins);
    public void Refreshed() { }
    public MapLayer Layer { get; } = new();
    public float Zoom = 3f; public Vector2 Offset;
    public event Action<Shape?>? Hovered;
    public event Action<Shape?>? Clicked;
    public event Action<Shape>? RegionClicked;
    public Func<Shape, string>? HoverText { get; set; }
    MapLayer IMapSurface.Layer => Layer;
    Shape? _hover; bool _drag; Vector2 _dragStart, _dragOffset; bool _moved;
    Vector2 _mouse;
    Minimap _mini = new();
    string? _pinText;

    public MapCanvas()
    {
        ClipContents = true; MouseFilter = MouseFilterEnum.Stop; FocusMode = FocusModeEnum.Click;
        AddChild(Layer); AddChild(_mini);
        _mini.Owner2 = this;
    }

    public override void _Ready() { Resized += () => { if (!_userMoved) Fit(); }; }
    bool _fitted, _userMoved;

    public void Fit()
    {
        if (Size.X < 10) return;
        _userMoved = false;
        Zoom = Mathf.Min(Size.X / 362f, Size.Y / 150f);
        Offset = new Vector2(Size.X / 2, Size.Y / 2 + 8 * Zoom);   // world centre (0, -10)
        _fitted = true; Apply();
    }

    float MinZoom => Mathf.Min(Size.X / 362f, Size.Y / 150f) * 0.9f;

    void Apply()
    {
        Zoom = Mathf.Clamp(Zoom, MinZoom, 120f);
        Layer.Position = Offset; Layer.Scale = new Vector2(Zoom, Zoom); Layer.SetZoom(Zoom);
        _mini.QueueRedraw(); QueueRedraw();
    }

    public Vector2 ToWorld(Vector2 screen) => (screen - Offset) / Zoom;

    public void FocusOn(Shape s)
    {
        var b = s.LargestBounds();
        float z = Mathf.Min(Size.X * 0.7f / Mathf.Max(b.Size.X, 2), Size.Y * 0.7f / Mathf.Max(b.Size.Y, 2));
        Zoom = Mathf.Clamp(z, MinZoom, 60f);
        Offset = Size / 2 - (b.Position + b.Size / 2) * Zoom;
        _fitted = true; _userMoved = true; Apply();
    }

    public void CentreOn(Vector2 world, float zoom) { Zoom = zoom; Offset = Size / 2 - world * Zoom; _fitted = true; Apply(); }

    public Shape? CountryAt(Vector2 world) => MapData.CountryAt(world);

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown)
            {
                float f = mb.ButtonIndex == MouseButton.WheelUp ? 1.18f : 1 / 1.18f;
                _userMoved = true; var w = ToWorld(mb.Position); Zoom = Mathf.Clamp(Zoom * f, MinZoom, 120f); Offset = mb.Position - w * Zoom; Apply(); AcceptEvent();
            }
            else if (mb.ButtonIndex == MouseButton.Left || mb.ButtonIndex == MouseButton.Middle)
            {
                if (mb.Pressed) { _drag = true; _moved = false; _dragStart = mb.Position; _dragOffset = Offset; }
                else
                {
                    _drag = false;
                    if (!_moved && mb.ButtonIndex == MouseButton.Left)
                    {
                        var w = ToWorld(mb.Position);
                        if (Layer.HasRegions && Layer.RegionAt(w) is Shape rs) RegionClicked?.Invoke(rs);
                        else Clicked?.Invoke(CountryAt(w));
                    }
                }
            }
        }
        else if (e is InputEventMouseMotion mm)
        {
            _mouse = mm.Position;
            if (_drag && (mm.Position - _dragStart).Length() > 3) { _moved = true; _userMoved = true; Offset = _dragOffset + (mm.Position - _dragStart); Apply(); }
            else if (!_drag)
            {
                var w = ToWorld(mm.Position);
                var pin = Layer.PinAt(w, 10);
                _pinText = pin?.Text;
                var s = pin == null ? CountryAt(w) : null;
                if (!ReferenceEquals(s, _hover)) { _hover = s; Hovered?.Invoke(s); }
                QueueRedraw();
            }
        }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("0A1017"));
        // graticule
        for (int lon = -180; lon <= 180; lon += 30) DrawLine(Offset + new Vector2(lon, -90) * Zoom, Offset + new Vector2(lon, 90) * Zoom, new Color(1, 1, 1, 0.04f), 1);
        for (int lat = -60; lat <= 60; lat += 30) DrawLine(Offset + new Vector2(-180, -lat) * Zoom, Offset + new Vector2(180, -lat) * Zoom, new Color(1, 1, 1, 0.04f), 1);
        string? txt = _pinText ?? (_hover != null ? HoverText?.Invoke(_hover) ?? _hover.Name : null);
        if (txt != null)
        {
            var font = ThemeDB.FallbackFont; int fs = Mathf.RoundToInt(13 * Pal.TextScale);
            var lines = txt.Split('\n'); float w = lines.Max(l => font.GetStringSize(l, HorizontalAlignment.Left, -1, fs).X) + 16, h = lines.Length * (fs + 4) + 10;
            var pos = _mouse + new Vector2(16, 16); if (pos.X + w > Size.X) pos.X = _mouse.X - w - 12; if (pos.Y + h > Size.Y) pos.Y = _mouse.Y - h - 12;
            DrawRect(new Rect2(pos, new Vector2(w, h)), new Color(Pal.PanelHi, 0.96f));
            DrawRect(new Rect2(pos, new Vector2(w, h)), Pal.Accent, false, 1);
            for (int i = 0; i < lines.Length; i++) DrawString(font, pos + new Vector2(8, 5 + (i + 1) * (fs + 4) - 4), lines[i], HorizontalAlignment.Left, -1, fs, i == 0 ? Pal.Text : Pal.Dim);
        }
    }

    partial class Minimap : Control
    {
        public MapCanvas Owner2 = null!;
        public Minimap() { CustomMinimumSize = new Vector2(200, 100); Size = new Vector2(200, 100); MouseFilter = MouseFilterEnum.Ignore; }
        public override void _Ready() { SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft); Position = new Vector2(12, Owner2.Size.Y - 112); Owner2.Resized += () => Position = new Vector2(12, Owner2.Size.Y - 112); }
        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.04f, 0.07f, 0.1f, 0.85f)); DrawRect(new Rect2(Vector2.Zero, Size), Pal.Border, false, 1);
            float sx = Size.X / 360f, sy = Size.Y / 150f; float s = Mathf.Min(sx, sy); var o = new Vector2(Size.X / 2, Size.Y / 2 + 8 * s);
            foreach (var c in MapData.Countries) foreach (var poly in c.Polys) { if (poly.Length < 3) continue; DrawPolyline(poly.Select(p => o + p * s).ToArray(), new Color(0.45f, 0.55f, 0.65f), 0.7f); }
            var tl = Owner2.ToWorld(Vector2.Zero); var br = Owner2.ToWorld(Owner2.Size);
            var r = new Rect2(o + tl * s, (br - tl) * s); r = r.Intersection(new Rect2(Vector2.Zero, Size));
            DrawRect(r, Pal.Accent, false, 1.5f);
        }
    }
}
