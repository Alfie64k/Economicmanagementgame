using Godot;

namespace EconGame.Ui;

/// <summary>Design tokens. Categorical colours follow the Okabe-Ito colour-blind-safe palette.</summary>
public static class Pal
{
    public static bool ColourBlind;           // swaps good/bad onto blue/orange
    public static bool HighContrast;          // near-black ground, white text, strong borders
    public static float TextScale = 1.0f;

    // plain static fields (not properties): colours are read thousands of times a frame, so they are assigned once by Use()
    public static Color Bg, Panel, PanelAlt, PanelHi, Border, Text, Dim, Faint, Accent, Warn;
    public static Color Good => ColourBlind ? new Color("56B4E9") : HighContrast ? new Color("4DFFB4") : new Color("3FB68B");
    public static Color Bad => ColourBlind ? new Color("E69F00") : HighContrast ? new Color("FF7B6B") : new Color("E5604F");

    // interaction-state tokens (see StateStyles): none depends on the colour-blind palette, and every state also
    // carries a non-colour cue (edge bar, outline, ring, weight)
    public static Color Hover, HoverEdge, SelFill, SelFillHover, SelBar, Press, FocusRing;

    static Pal() { Use(false); }

    /// <summary>Switch between the standard and high-contrast palettes. Callers rebuild the theme and the current screen afterwards.</summary>
    public static void Use(bool highContrast)
    {
        HighContrast = highContrast;
        if (highContrast)
        {
            Bg = new("000000"); Panel = new("0A0A0A"); PanelAlt = new("151515"); PanelHi = new("2A2A2A"); Border = new("9A9A9A");
            Text = new("FFFFFF"); Dim = new("E0E0E0"); Faint = new("B0B0B0"); Accent = new("66CCFF"); Warn = new("FFD23F");
        }
        else
        {
            Bg = new("0E1318"); Panel = new("161D25"); PanelAlt = new("1C2630"); PanelHi = new("243241"); Border = new("2C3A49");
            Text = new("E7EDF3"); Dim = new("93A3B4"); Faint = new("5E6E7F"); Accent = new("4DA3FF"); Warn = new("E0B13A");
        }
        Hover = PanelHi; HoverEdge = highContrast ? Text : Faint;
        SelFill = Panel.Lerp(Accent, highContrast ? 0.35f : 0.20f); SelFillHover = Panel.Lerp(Accent, highContrast ? 0.50f : 0.32f);
        SelBar = Accent; Press = Accent.Darkened(0.45f); FocusRing = highContrast ? new Color("FFD23F") : Text;
    }

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
