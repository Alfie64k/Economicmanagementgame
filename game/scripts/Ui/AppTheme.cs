using Godot;

namespace EconGame.Ui;

public static class AppTheme
{
    public static StyleBoxFlat Box(Color bg, int radius = 8, Color? border = null, int bw = 0, int pad = 0)
    {
        var s = new StyleBoxFlat { BgColor = bg };
        s.SetCornerRadiusAll(radius);
        if (border != null && bw > 0) { s.BorderColor = border.Value; s.SetBorderWidthAll(bw); }
        if (pad > 0) { s.ContentMarginLeft = s.ContentMarginRight = s.ContentMarginTop = s.ContentMarginBottom = pad; }
        return s;
    }

    public static Theme Build()
    {
        var t = new Theme { DefaultFontSize = Mathf.RoundToInt(15 * Pal.TextScale) };

        // buttons
        foreach (var name in new[] { "Button", "OptionButton", "MenuButton" })
        {
            t.SetStylebox("normal", name, Pad(Box(Pal.PanelAlt, 7, Pal.Border, 1), 14, 8));
            t.SetStylebox("hover", name, Pad(Box(Pal.PanelHi, 7, Pal.Accent, 1), 14, 8));
            t.SetStylebox("pressed", name, Pad(Box(Pal.Accent.Darkened(0.45f), 7, Pal.Accent, 1), 14, 8));
            t.SetStylebox("disabled", name, Pad(Box(Pal.Panel, 7, Pal.Border, 1), 14, 8));
            t.SetStylebox("focus", name, Pad(Box(new Color(0, 0, 0, 0), 7, Pal.Accent, 2), 14, 8));
            t.SetColor("font_color", name, Pal.Text);
            t.SetColor("font_hover_color", name, Colors.White);
            t.SetColor("font_pressed_color", name, Colors.White);
            t.SetColor("font_disabled_color", name, Pal.Faint);
        }
        t.SetStylebox("normal", "CheckBox", new StyleBoxEmpty());
        t.SetColor("font_color", "CheckBox", Pal.Text);

        // labels / panels
        t.SetColor("font_color", "Label", Pal.Text);
        t.SetStylebox("panel", "PanelContainer", Box(Pal.Panel, 10, Pal.Border, 1, 14));
        t.SetStylebox("panel", "Panel", Box(Pal.Panel, 10, Pal.Border, 1));
        t.SetStylebox("panel", "TooltipPanel", Box(Pal.PanelHi, 8, Pal.Accent, 1, 10));
        t.SetColor("font_color", "TooltipLabel", Pal.Text);

        // line edit
        t.SetStylebox("normal", "LineEdit", Pad(Box(Pal.Bg, 7, Pal.Border, 1), 10, 7));
        t.SetStylebox("focus", "LineEdit", Pad(Box(Pal.Bg, 7, Pal.Accent, 2), 10, 7));
        t.SetColor("font_color", "LineEdit", Pal.Text);
        t.SetColor("caret_color", "LineEdit", Pal.Accent);

        // scrollbars
        foreach (var n in new[] { "VScrollBar", "HScrollBar" })
        {
            t.SetStylebox("scroll", n, Box(new Color(1, 1, 1, 0.04f), 4));
            t.SetStylebox("grabber", n, Box(Pal.Border, 4));
            t.SetStylebox("grabber_highlight", n, Box(Pal.Faint, 4));
            t.SetStylebox("grabber_pressed", n, Box(Pal.Accent, 4));
        }
        t.SetStylebox("panel", "ScrollContainer", new StyleBoxEmpty());
        t.SetStylebox("panel", "PopupMenu", Box(Pal.PanelAlt, 8, Pal.Border, 1, 6));
        t.SetColor("font_color", "PopupMenu", Pal.Text);
        t.SetStylebox("hover", "PopupMenu", Box(Pal.PanelHi, 4));
        return t;
    }

    static StyleBoxFlat Pad(StyleBoxFlat s, int h, int v)
    {
        s.ContentMarginLeft = s.ContentMarginRight = h; s.ContentMarginTop = s.ContentMarginBottom = v; return s;
    }
}
