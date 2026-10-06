using Godot;

namespace EconGame.Ui;

/// <summary>
/// One vocabulary for interactive states: normal, hover, selected (the current page or row), selected + hover
/// (an extra emphasis step), pressed, keyboard focus and disabled. Styles are registered as theme type variations
/// so they follow the theme (text scale, colour-blind palette) instead of being baked into each node.
/// </summary>
public static class StateStyles
{
    public const string NavItem = "NavItem", ListRow = "ListRow", Chip = "Chip", Primary = "PrimaryButton", ColumnHeader = "ColumnHeader";

    // identical content margins in every state, so labels never shift when the state changes
    static StyleBoxFlat Make(Color fill, Color? edge, int edgeW, int barW, int radius, int l, int r, int t, int b)
    {
        var s = new StyleBoxFlat { BgColor = fill };
        s.SetCornerRadiusAll(radius);
        if (edge != null)
        {
            s.BorderColor = edge.Value;
            s.BorderWidthLeft = Mathf.Max(edgeW, barW); s.BorderWidthTop = s.BorderWidthRight = s.BorderWidthBottom = edgeW;
        }
        s.ContentMarginLeft = l; s.ContentMarginRight = r; s.ContentMarginTop = t; s.ContentMarginBottom = b;
        return s;
    }

    static StyleBoxFlat Ring(int radius, int l, int r, int t, int b)
    {
        var s = Make(new Color(0, 0, 0, 0), Pal.FocusRing, 2, 0, radius, l, r, t, b);
        s.DrawCenter = false; return s;
    }

    static readonly Color Clear = new(0, 0, 0, 0);

    /// <summary>Vertical list item: nav entries, table rows. The selected state has a 3px accent bar on its left.</summary>
    public static void Row(Theme t, string variation, int l = 14, int r = 10, int tp = 8, int bt = 8, int radius = 6)
    {
        t.SetTypeVariation(variation, "Button");
        t.SetStylebox("normal", variation, Make(Clear, null, 0, 0, radius, l, r, tp, bt));
        t.SetStylebox("hover", variation, Make(Pal.Hover, Pal.HoverEdge, 1, 0, radius, l, r, tp, bt));
        t.SetStylebox("pressed", variation, Make(Pal.SelFill, Pal.SelBar, 0, 3, radius, l, r, tp, bt));
        t.SetStylebox("hover_pressed", variation, Make(Pal.SelFillHover, Pal.SelBar, 1, 3, radius, l, r, tp, bt));
        t.SetStylebox("disabled", variation, Make(Clear, null, 0, 0, radius, l, r, tp, bt));
        t.SetStylebox("focus", variation, Ring(radius, l, r, tp, bt));
        t.SetColor("font_color", variation, Pal.Dim);
        t.SetColor("font_hover_color", variation, Pal.Text);
        t.SetColor("font_pressed_color", variation, Colors.White);
        t.SetColor("font_hover_pressed_color", variation, Colors.White);
        t.SetColor("font_focus_color", variation, Pal.Text);
        t.SetColor("font_disabled_color", variation, Pal.Faint);
    }

    /// <summary>Toggle chip (filters, metric pickers): the selected state is a filled, outlined pill.</summary>
    public static void ChipStyle(Theme t, string variation)
    {
        t.SetTypeVariation(variation, "Button");
        t.SetStylebox("normal", variation, Make(Pal.PanelAlt, Pal.Border, 1, 0, 7, 14, 14, 8, 8));
        t.SetStylebox("hover", variation, Make(Pal.Hover, Pal.HoverEdge, 1, 0, 7, 14, 14, 8, 8));
        t.SetStylebox("pressed", variation, Make(Pal.SelFill, Pal.SelBar, 2, 0, 7, 14, 14, 8, 8));
        t.SetStylebox("hover_pressed", variation, Make(Pal.SelFillHover, Pal.SelBar, 2, 0, 7, 14, 14, 8, 8));
        t.SetStylebox("disabled", variation, Make(Pal.Panel, Pal.Border, 1, 0, 7, 14, 14, 8, 8));
        t.SetStylebox("focus", variation, Ring(7, 14, 14, 8, 8));
        t.SetColor("font_color", variation, Pal.Dim);
        t.SetColor("font_hover_color", variation, Pal.Text);
        t.SetColor("font_pressed_color", variation, Colors.White);
        t.SetColor("font_hover_pressed_color", variation, Colors.White);
        t.SetColor("font_focus_color", variation, Pal.Text);
    }

    /// <summary>Accent action button: darker fill and a light border on hover (the old lighter fill dropped text contrast).</summary>
    public static void PrimaryStyle(Theme t, string variation)
    {
        t.SetTypeVariation(variation, "Button");
        var accent = Pal.Accent;
        t.SetStylebox("normal", variation, Make(accent.Darkened(0.35f), accent, 1, 0, 7, 14, 14, 8, 8));
        t.SetStylebox("hover", variation, Make(accent.Darkened(0.28f), accent.Lightened(0.35f), 2, 0, 7, 14, 14, 8, 8));
        t.SetStylebox("pressed", variation, Make(Pal.Press, accent, 1, 0, 7, 14, 14, 8, 8));
        t.SetStylebox("hover_pressed", variation, Make(Pal.Press, accent.Lightened(0.35f), 2, 0, 7, 14, 14, 8, 8));
        t.SetStylebox("disabled", variation, Make(Pal.Panel, Pal.Border, 1, 0, 7, 14, 14, 8, 8));
        t.SetStylebox("focus", variation, Ring(7, 14, 14, 8, 8));
        foreach (var c in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" }) t.SetColor(c, variation, Colors.White);
        t.SetColor("font_disabled_color", variation, Pal.Faint);
    }

    /// <summary>Sortable table header: quiet at rest, tinted on hover.</summary>
    public static void HeaderStyle(Theme t, string variation)
    {
        t.SetTypeVariation(variation, "Button");
        t.SetStylebox("normal", variation, Make(Clear, null, 0, 0, 5, 6, 6, 4, 4));
        t.SetStylebox("hover", variation, Make(Pal.Hover, Pal.HoverEdge, 1, 0, 5, 6, 6, 4, 4));
        t.SetStylebox("pressed", variation, Make(Pal.SelFill, null, 0, 0, 5, 6, 6, 4, 4));
        t.SetStylebox("hover_pressed", variation, Make(Pal.SelFillHover, null, 0, 0, 5, 6, 6, 4, 4));
        t.SetStylebox("focus", variation, Ring(5, 6, 6, 4, 4));
        t.SetColor("font_color", variation, Pal.Dim);
        t.SetColor("font_hover_color", variation, Pal.Text);
        t.SetColor("font_pressed_color", variation, Pal.Text);
        t.SetColor("font_focus_color", variation, Pal.Text);
        t.SetFontSize("font_size", variation, Mathf.RoundToInt(13 * Pal.TextScale));
    }

    /// <summary>Card for tiles and panels that can be hovered or selected.</summary>
    public static StyleBoxFlat Tile(bool hover, bool selected)
    {
        if (selected) return AppTheme.Box(hover ? Pal.SelFillHover : Pal.SelFill, 10, Pal.SelBar, 2, 12);
        if (hover) return AppTheme.Box(Pal.PanelAlt, 10, Pal.HoverEdge, 1, 12);
        return AppTheme.Box(Pal.Panel, 10, Pal.Border, 1, 12);
    }

    public static StyleBoxFlat FocusBox(int radius = 10)
    {
        var s = Make(Clear, Pal.FocusRing, 2, 0, radius, 0, 0, 0, 0); s.DrawCenter = false; return s;
    }
    // ---- check boxes ----
    // A CheckBox is a Button in Godot, so it would inherit the button's filled "pressed" pill; give it quiet styleboxes and drawn icons instead.

    /// <summary>A rounded box, with a tick when <paramref name="on"/>. Drawn with a distance field so it stays crisp at any text scale.</summary>
    static ImageTexture BoxIcon(int n, bool on, Color edge, Color fill, bool dim)
    {
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        float half = n / 2f - 0.5f, rad = n * 0.22f, bw = Mathf.Max(1.5f, n * 0.09f);
        Vector2 a = new(n * 0.25f, n * 0.53f), b = new(n * 0.43f, n * 0.71f), c = new(n * 0.77f, n * 0.30f);
        float thick = Mathf.Max(1.6f, n * 0.13f);
        static float Seg(Vector2 p, Vector2 s, Vector2 e)
        {
            var d = e - s; float t = Mathf.Clamp((p - s).Dot(d) / d.LengthSquared(), 0, 1); return (p - (s + d * t)).Length();
        }
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f); var q = (p - new Vector2(n / 2f, n / 2f)).Abs() - new Vector2(half - rad, half - rad);
                float d = new Vector2(Mathf.Max(q.X, 0), Mathf.Max(q.Y, 0)).Length() + Mathf.Min(Mathf.Max(q.X, q.Y), 0) - rad;
                float inside = Mathf.Clamp(0.5f - d, 0, 1), rim = Mathf.Clamp(d + bw + 0.5f, 0, 1);
                var col = fill.Lerp(edge, rim);
                if (on)
                {
                    float tick = Mathf.Clamp(thick / 2 + 0.5f - Mathf.Min(Seg(p, a, b), Seg(p, b, c)), 0, 1);
                    col = col.Lerp(new Color(1, 1, 1), tick);
                }
                if (dim) col = col.Lerp(Pal.Panel, 0.5f);
                col.A = inside; img.SetPixel(x, y, col);
            }
        return ImageTexture.CreateFromImage(img);
    }

    public static void CheckStyle(Theme t)
    {
        int n = Mathf.RoundToInt(20 * Pal.TextScale), pad = 6;
        StyleBoxFlat Quiet(Color fill, bool ring)
        {
            var sb = ring ? Ring(6, pad, pad, 4, 4) : Make(fill, null, 0, 0, 6, pad, pad, 4, 4); return sb;
        }
        t.SetStylebox("normal", "CheckBox", Quiet(Clear, false)); t.SetStylebox("pressed", "CheckBox", Quiet(Clear, false));
        t.SetStylebox("hover", "CheckBox", Quiet(Pal.Hover, false)); t.SetStylebox("hover_pressed", "CheckBox", Quiet(Pal.Hover, false));
        t.SetStylebox("disabled", "CheckBox", Quiet(Clear, false)); t.SetStylebox("focus", "CheckBox", Quiet(Clear, true));
        foreach (var c in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" }) t.SetColor(c, "CheckBox", Pal.Text);
        t.SetColor("font_disabled_color", "CheckBox", Pal.Faint);
        t.SetConstant("h_separation", "CheckBox", 10);
        t.SetIcon("unchecked", "CheckBox", BoxIcon(n, false, Pal.Faint, Pal.PanelAlt, false));
        t.SetIcon("checked", "CheckBox", BoxIcon(n, true, Pal.Accent, Pal.Accent, false));
        t.SetIcon("unchecked_disabled", "CheckBox", BoxIcon(n, false, Pal.Faint, Pal.PanelAlt, true));
        t.SetIcon("checked_disabled", "CheckBox", BoxIcon(n, true, Pal.Accent, Pal.Accent, true));
    }
}
