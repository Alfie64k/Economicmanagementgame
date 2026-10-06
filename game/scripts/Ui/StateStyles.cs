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
}
