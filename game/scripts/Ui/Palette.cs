using Godot;

namespace EconGame.Ui;

/// <summary>Design tokens. Categorical colours follow the Okabe-Ito colour-blind-safe palette.</summary>
public static class Pal
{
    public static bool ColourBlind;           // swaps good/bad onto blue/orange
    public static float TextScale = 1.0f;

    public static readonly Color Bg = new("0E1318");
    public static readonly Color Panel = new("161D25");
    public static readonly Color PanelAlt = new("1C2630");
    public static readonly Color PanelHi = new("243241");
    public static readonly Color Border = new("2C3A49");
    public static readonly Color Text = new("E7EDF3");
    public static readonly Color Dim = new("93A3B4");
    public static readonly Color Faint = new("5E6E7F");
    public static readonly Color Accent = new("4DA3FF");
    public static Color Good => ColourBlind ? new Color("56B4E9") : new Color("3FB68B");
    public static Color Bad => ColourBlind ? new Color("E69F00") : new Color("E5604F");
    public static readonly Color Warn = new("E0B13A");

    // interaction-state tokens (see StateStyles): none depends on the colour-blind palette, and every state also
    // carries a non-colour cue (edge bar, outline, ring, weight)
    public static readonly Color Hover = PanelHi;
    public static readonly Color HoverEdge = Faint;
    public static readonly Color SelFill = Panel.Lerp(Accent, 0.20f);
    public static readonly Color SelFillHover = Panel.Lerp(Accent, 0.32f);
    public static readonly Color SelBar = Accent;
    public static readonly Color Press = Accent.Darkened(0.45f);
    public static readonly Color FocusRing = Text;

    public static readonly Color[] Series =
    {
        new("56B4E9"), new("E69F00"), new("009E73"), new("F0E442"), new("0072B2"), new("D55E00"), new("CC79A7"), new("B4BDC8"),
    };

    public static Color Lerp(Color a, Color b, float t) => a.Lerp(b, Mathf.Clamp(t, 0, 1));

    /// <summary>Diverging colour for a value in [-1, 1]: bad - neutral - good.</summary>
    public static Color Diverge(float v)
    {
        v = Mathf.Clamp(v, -1, 1);
        var mid = new Color("4B6278");
        return v >= 0 ? Lerp(mid, Good, v) : Lerp(mid, Bad, -v);
    }

    /// <summary>Sequential colour ramp (dark blue to bright cyan) for t in [0,1].</summary>
    public static Color Ramp(float t)
    {
        t = Mathf.Clamp(t, 0, 1);
        return t < 0.5f ? Lerp(new Color("263F58"), new Color("2F6FA8"), t * 2) : Lerp(new Color("2F6FA8"), new Color("9BE3FF"), (t - 0.5f) * 2);
    }
}
