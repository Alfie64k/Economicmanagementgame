using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using EconGame.Ui;

namespace EconGame.Map;

/// <summary>
/// Rotatable 3D globe. The 2D <see cref="MapLayer"/> is rendered into a 2048x1024 texture (equirectangular) that wraps a sphere,
/// so overlays, regions and highlights behave exactly as on the flat map. Drag to rotate, wheel to zoom, click to pick a country.
/// </summary>
public partial class GlobeCanvas : Control, IMapSurface
{
    public MapLayer Layer { get; } = new();
    public Control Node => this;
    public event Action<Shape?>? Hovered;
    public event Action<Shape?>? Clicked;
    public event Action<Shape>? RegionClicked;
    public Func<Shape, string>? HoverText { get; set; }

    SubViewport _tex = new(), _scene = new();
    Node3D _globe = new();
    Camera3D _cam = new();
    MultiMeshInstance3D _flowDots = new(), _pinDots = new();
    float _yaw, _pitch, _dist = 3.2f;
    bool _drag, _moved; Vector2 _dragStart, _mouse; float _yaw0, _pitch0;
    Shape? _hover; List<Pin> _pins = new(); string? _pinText;

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit) { _hover = null; _pinText = null; _hud.Set(null, _mouse); }
    }
    readonly MapTip _hud = new();
    const int TexW = 4096, TexH = 2048;

    public override void _Ready()
    {
        ClipContents = true; MouseFilter = MouseFilterEnum.Stop;

        // --- 2D map texture ---
        _tex = new SubViewport { Size = new Vector2I(TexW, TexH), RenderTargetUpdateMode = SubViewport.UpdateMode.Always, TransparentBg = false, Disable3D = true };
        AddChild(_tex);
        _tex.AddChild(new ColorRect { Color = new Color("0C2A44"), Size = new Vector2(TexW, TexH) });
        _tex.AddChild(new Graticule { Scale = new Vector2(TexW / 360f, TexW / 360f), Position = new Vector2(TexW / 2f, TexH / 2f) });
        Layer.Position = new Vector2(TexW / 2f, TexH / 2f); Layer.Scale = new Vector2(TexW / 360f, TexW / 360f);
        _tex.AddChild(Layer); Layer.SetZoom(TexW / 360f);

        // --- 3D scene ---
        var cont = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
        cont.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(cont);
        _scene = new SubViewport { OwnWorld3D = true, TransparentBg = false, HandleInputLocally = false };
        cont.AddChild(_scene);
        var env = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color("05080D"), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White };
        _scene.AddChild(new WorldEnvironment { Environment = env });
        _cam = new Camera3D { Fov = 32, Position = new Vector3(0, 0, _dist), Current = true };
        _scene.AddChild(_cam);
        _scene.AddChild(_globe);

        var sphere = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 96, Rings = 48 };
        var sh = new Shader { Code = @"shader_type spatial; render_mode unshaded;
uniform sampler2D map : source_color, filter_linear_mipmap, repeat_enable;
void fragment(){ vec3 c = texture(map, UV).rgb; float l = clamp(dot(normalize(NORMAL), normalize(vec3(0.35,0.45,0.82))), 0.0, 1.0); ALBEDO = c * (0.50 + 0.62 * l); }" };
        var mat = new ShaderMaterial { Shader = sh }; mat.SetShaderParameter("map", _tex.GetTexture());
        _globe.AddChild(new MeshInstance3D { Mesh = sphere, MaterialOverride = mat });

        var atm = new Shader { Code = @"shader_type spatial; render_mode unshaded, cull_front, blend_add, depth_draw_never;
void fragment(){ float f = pow(1.0 - abs(dot(normalize(NORMAL), normalize(VIEW))), 2.2); ALBEDO = vec3(0.25,0.55,1.0) * f * 1.4; ALPHA = clamp(f, 0.0, 1.0); }" };
        _scene.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 1.08f, Height = 2.16f, RadialSegments = 64, Rings = 32 }, MaterialOverride = new ShaderMaterial { Shader = atm } });

        _globe.AddChild(_flowDots); _globe.AddChild(_pinDots);
        AddChild(_hud);

        Resized += () => { };
        FocusLonLat(0, 30);
    }

    // ---- geometry helpers ----
    public static Vector3 LonLatToVec(float lon, float lat)
    {
        float phi = (lon + 180f) / 360f * Mathf.Tau, la = Mathf.DegToRad(lat);
        return new Vector3(Mathf.Sin(phi) * Mathf.Cos(la), Mathf.Sin(la), Mathf.Cos(phi) * Mathf.Cos(la));
    }

    public static Vector2 VecToWorld(Vector3 v)
    {
        float lat = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(v.Y, -1, 1)));
        float phi = Mathf.Atan2(v.X, v.Z); if (phi < 0) phi += Mathf.Tau;
        return new Vector2(phi / Mathf.Tau * 360f - 180f, -lat);
    }

    void ApplyRotation() { _globe.Basis = new Basis(Vector3.Right, _pitch) * new Basis(Vector3.Up, _yaw); _cam.Position = new Vector3(0, 0, _dist); }

    public void FocusLonLat(float lon, float lat)
    {
        var t = LonLatToVec(lon, lat);
        _yaw = -Mathf.Atan2(t.X, t.Z);
        float r = Mathf.Sqrt(t.X * t.X + t.Z * t.Z);
        _pitch = Mathf.Atan2(t.Y, r);
        ApplyRotation();
    }

    public void Fit() { _dist = 3.2f; FocusLonLat(0, 25); }

    public void FocusOn(Shape s)
    {
        var b = s.LargestBounds(); float size = Mathf.Max(b.Size.X, b.Size.Y);
        _dist = Mathf.Clamp(2.55f + size / 70f, 2.5f, 3.4f);
        var c = b.Position + b.Size / 2; FocusLonLat(c.X, -c.Y);
    }

    Vector2? Pick(Vector2 screen)
    {
        var o = _cam.ProjectRayOrigin(screen - Vector2.Zero); var d = _cam.ProjectRayNormal(screen);
        // _scene size = our size (stretch), so screen coordinates match
        float b = o.Dot(d), c = o.Dot(o) - 1f, disc = b * b - c;
        if (disc < 0) return null;
        float t = -b - Mathf.Sqrt(disc); if (t < 0) return null;
        var p = o + d * t; var local = _globe.Basis.Inverse() * p;
        return VecToWorld(local.Normalized());
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown && !mb.Pressed) return;   // a wheel notch is a press and a release; act once
            if (mb.ButtonIndex == MouseButton.WheelUp) { _dist = Mathf.Clamp(_dist * 0.9f, 1.4f, 6f); ApplyRotation(); AcceptEvent(); }
            else if (mb.ButtonIndex == MouseButton.WheelDown) { _dist = Mathf.Clamp(_dist / 0.9f, 1.4f, 6f); ApplyRotation(); AcceptEvent(); }
            else if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed) { _drag = true; _moved = false; _dragStart = mb.Position; _yaw0 = _yaw; _pitch0 = _pitch; }
                else
                {
                    _drag = false;
                    if (!_moved && Pick(mb.Position) is Vector2 w)
                    {
                        if (Layer.HasRegions && Layer.RegionAt(w) is Shape rs) RegionClicked?.Invoke(rs); else Clicked?.Invoke(MapData.CountryAt(w));
                    }
                }
            }
        }
        else if (e is InputEventMouseMotion mm)
        {
            _mouse = mm.Position;
            if (_drag && (mm.Position - _dragStart).Length() > 3)
            {
                _moved = true; float k = 0.0055f * (_dist / 3.2f + 0.3f);
                _yaw = _yaw0 + (mm.Position.X - _dragStart.X) * k; _pitch = Mathf.Clamp(_pitch0 + (mm.Position.Y - _dragStart.Y) * k, -1.4f, 1.4f);
                ApplyRotation();
            }
            else if (!_drag)
            {
                _pinText = null; Shape? s = null;
                if (Pick(mm.Position) is Vector2 w)
                {
                    var pin = _pins.FirstOrDefault(p => p.Pos.DistanceTo(w) < 2.5f); _pinText = pin?.Text;
                    if (pin == null) s = MapData.CountryAt(w);
                }
                if (!ReferenceEquals(s, _hover)) { _hover = s; Hovered?.Invoke(s); }
                _hud.Set(_pinText ?? (s != null ? HoverText?.Invoke(s) ?? s.Name : null), _mouse);
            }
        }
    }

    public void SetOverlay(List<Flow> flows, List<Pin> pins)
    {
        _pins = pins;
        BuildDots(_flowDots, flows.SelectMany(f => Arc(f)).ToList(), 0.0042f);
        BuildDots(_pinDots, pins.Select(p => (LonLatToVec(p.Pos.X, -p.Pos.Y) * 1.012f, p.Color, 1.0f)).ToList(), 0.012f);
    }

    static IEnumerable<(Vector3, Color, float)> Arc(Flow f)
    {
        var a = LonLatToVec(f.From.X, -f.From.Y); var b = LonLatToVec(f.To.X, -f.To.Y);
        float ang = a.AngleTo(b); int n = Mathf.Clamp((int)(ang * 40), 8, 60); float w = Mathf.Clamp(f.Width / 3.5f, 0.5f, 1.6f);
        for (int i = 1; i < n; i++)
        {
            float t = i / (float)n; var p = a.Slerp(b, t) * (1.008f + 0.16f * Mathf.Sin(Mathf.Pi * t) * Mathf.Min(1f, ang / 1.5f));
            yield return (p, f.Color, w);
        }
    }

    static void BuildDots(MultiMeshInstance3D inst, List<(Vector3 p, Color c, float s)> dots, float radius)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new SphereMesh { Radius = radius, Height = radius * 2, RadialSegments = 8, Rings = 4 }, InstanceCount = dots.Count };
        for (int i = 0; i < dots.Count; i++)
        {
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * dots[i].s), dots[i].p));
            mm.SetInstanceColor(i, dots[i].c);
        }
        inst.Multimesh = mm;
        inst.MaterialOverride ??= new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true };
    }

    public void Refreshed() { _tex.RenderTargetUpdateMode = SubViewport.UpdateMode.Once; }

    partial class Graticule : Node2D
    {
        public override void _Draw()
        {
            for (int lon = -180; lon <= 180; lon += 30) DrawLine(new Vector2(lon, -90), new Vector2(lon, 90), new Color(1, 1, 1, 0.07f), 0.18f);
            for (int lat = -60; lat <= 60; lat += 30) DrawLine(new Vector2(-180, -lat), new Vector2(180, -lat), new Color(1, 1, 1, 0.07f), 0.18f);
        }
    }
}
