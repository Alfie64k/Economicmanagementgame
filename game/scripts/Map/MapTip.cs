using System.Linq;
using Godot;
using EconGame.Ui;

namespace EconGame.Map;

/// <summary>
/// Hover tooltip for the map surfaces. It is a separate full-rect control that must stay the LAST child of its
/// canvas: Godot paints a canvas item's own draw commands first and then its children in tree order, so a tooltip drawn
/// in the canvas' own _Draw ends up behind the country polygons and the minimap.
/// </summary>
public partial class MapTip : Control
{
    string? _text; Vector2 _mouse;

    public MapTip() { MouseFilter = MouseFilterEnum.Ignore; SetAnchorsPreset(LayoutPreset.FullRect); }

    public void Set(string? text, Vector2 mouse)
    {
        if (text == _text && (text == null || mouse == _mouse)) return;
        _text = text; _mouse = mouse; QueueRedraw();
    }

    public override void _Draw()
    {
        if (_text == null) return;
        var font = ThemeDB.FallbackFont; int fs = Mathf.RoundToInt(13 * Pal.TextScale);
        var lines = _text.Split('\n'); float w = lines.Max(l => font.GetStringSize(l, HorizontalAlignment.Left, -1, fs).X) + 16, h = lines.Length * (fs + 4) + 10;
        var pos = _mouse + new Vector2(16, 16); if (pos.X + w > Size.X) pos.X = _mouse.X - w - 12; if (pos.Y + h > Size.Y) pos.Y = _mouse.Y - h - 12;
        pos.X = Mathf.Max(2, pos.X); pos.Y = Mathf.Max(2, pos.Y);
        DrawRect(new Rect2(pos, new Vector2(w, h)), new Color(Pal.PanelHi, 0.97f));
        DrawRect(new Rect2(pos, new Vector2(w, h)), Pal.Accent, false, 1);
        for (int i = 0; i < lines.Length; i++) DrawString(font, pos + new Vector2(8, 5 + (i + 1) * (fs + 4) - 4), lines[i], HorizontalAlignment.Left, -1, fs, i == 0 ? Pal.Text : Pal.Dim);
    }
}
