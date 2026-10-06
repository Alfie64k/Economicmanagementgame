using System;
using Godot;

namespace EconGame.Ui;

/// <summary>Small factory helpers so screens can build layouts in code concisely.</summary>
public static class UI
{
    static FontVariation? _bold;
    public static Font Bold => _bold ??= new FontVariation { BaseFont = ThemeDB.FallbackFont, VariationEmbolden = 0.55f };

    public static Label Lbl(string text, int size = 15, Color? color = null, bool bold = false, HorizontalAlignment align = HorizontalAlignment.Left, bool wrap = false)
    {
        var l = new Label { Text = text, HorizontalAlignment = align };
        l.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(size * Pal.TextScale));
        l.AddThemeColorOverride("font_color", color ?? Pal.Text);
        if (bold) l.AddThemeFontOverride("font", Bold);
        if (wrap) { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(10, 0); }
        l.MouseFilter = Control.MouseFilterEnum.Ignore;
        return l;
    }

    public static Label H1(string t) => Lbl(t, 26, Pal.Text, true);
    public static Label H2(string t) => Lbl(t, 18, Pal.Text, true);
    public static Label Dim(string t, int size = 13, bool wrap = false) => Lbl(t, size, Pal.Dim, false, HorizontalAlignment.Left, wrap);

    public static VBoxContainer VBox(int sep = 8, params Control[] kids)
    {
        var b = new VBoxContainer(); b.AddThemeConstantOverride("separation", sep);
        foreach (var k in kids) b.AddChild(k);
        return b;
    }

    public static HBoxContainer HBox(int sep = 8, params Control[] kids)
    {
        var b = new HBoxContainer(); b.AddThemeConstantOverride("separation", sep);
        foreach (var k in kids) b.AddChild(k);
        return b;
    }

    public static Control Spacer(float w = 0, float h = 0, bool expand = false)
    {
        var c = new Control { CustomMinimumSize = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Ignore };
        if (expand) { c.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; c.SizeFlagsVertical = Control.SizeFlags.ExpandFill; }
        return c;
    }

    public static T Fill<T>(T c, bool h = true, bool v = true) where T : Control
    {
        if (h) c.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (v) c.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        return c;
    }

    public static PanelContainer Card(Control child, Color? bg = null, int pad = 14)
    {
        var p = new PanelContainer();
        var sb = AppTheme.Box(bg ?? Pal.Panel, 10, Pal.Border, 1, pad);
        p.AddThemeStyleboxOverride("panel", sb);
        p.AddChild(child);
        return p;
    }

    public static MarginContainer Margin(Control child, int all = 16)
    {
        var m = new MarginContainer();
        foreach (var s in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) m.AddThemeConstantOverride(s, all);
        m.AddChild(child);
        return m;
    }

    public static ScrollContainer Scroll(Control child)
    {
        var s = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        child.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        s.AddChild(child);
        return Fill(s);
    }

    public static Button Btn(string text, Action? onPress = null, bool accent = false, int minW = 0)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(minW, 0), FocusMode = Control.FocusModeEnum.None };
        if (accent)
        {
            b.AddThemeStyleboxOverride("normal", AppTheme.Box(Pal.Accent.Darkened(0.25f), 7, Pal.Accent, 1, 10));
            b.AddThemeStyleboxOverride("hover", AppTheme.Box(Pal.Accent.Darkened(0.05f), 7, Pal.Accent.Lightened(0.3f), 1, 10));
        }
        if (onPress != null) b.Pressed += onPress;
        return b;
    }

    public static HSeparator Sep() { var s = new HSeparator(); s.AddThemeStyleboxOverride("separator", AppTheme.Box(Pal.Border, 0)); s.AddThemeConstantOverride("separation", 1); return s; }

    public static string Sign(double v, string fmt = "0.0") => (v >= 0 ? "+" : "") + v.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
    public static string Pct(double v, int d = 1) => (v * 100).ToString("F" + d, System.Globalization.CultureInfo.InvariantCulture) + "%";
}
