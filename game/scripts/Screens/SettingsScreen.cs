using System;
using Godot;
using EconGame.App;
using EconGame.Ui;

namespace EconGame.Screens;

public partial class SettingsScreen : Control
{
    readonly Action _back;
    public SettingsScreen(Action back) { _back = back; }

    public override void _Ready()
    {
        var root = UI.VBox(14); var margin = UI.Margin(root, 22); margin.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(margin);
        root.AddChild(UI.HBox(12, UI.Btn("← Back", () => { Settings.Save(); _back(); }), UI.H1("Settings")));
        var box = UI.VBox(12);
        var scale = new AppSlider(); scale.Setup(0.8, 1.6, 0.1, Settings.TextScale, v => $"{v:0.0}×");
        scale.Changed += v => { Settings.TextScale = (float)v; Settings.Apply(); Main.Instance!.ApplyTheme(); };
        box.AddChild(Row("Text size", scale));
        var cb = new CheckBox { Text = "Colour-blind safe palette", ButtonPressed = Settings.ColourBlind };
        cb.Toggled += on => { Settings.ColourBlind = on; Settings.Apply(); };
        box.AddChild(cb);
        var auto = new CheckBox { Text = "Autosave every year", ButtonPressed = Settings.Autosave };
        auto.Toggled += on => Settings.Autosave = on; box.AddChild(auto);
        var fs = new CheckBox { Text = "Fullscreen", ButtonPressed = Settings.Fullscreen };
        fs.Toggled += on => { Settings.Fullscreen = on; Settings.Apply(); }; box.AddChild(fs);
        var vol = new AppSlider(); vol.Setup(0, 1, 0.05, Settings.Volume, v => $"{v * 100:0}%");
        vol.Changed += v => { Settings.Volume = (float)v; Settings.Apply(); };
        box.AddChild(Row("Volume", vol));
        var speed = new OptionButton(); foreach (var s in new[] { "Paused", "1× (slow)", "2×", "4×", "8× (fast)" }) speed.AddItem(s);
        speed.Selected = Settings.DefaultSpeed; speed.ItemSelected += i => Settings.DefaultSpeed = (int)i;
        box.AddChild(Row("Default game speed", speed));
        root.AddChild(UI.Card(box));
        root.AddChild(UI.Dim("Keyboard: Space pause · 1-4 speed · Tab next view · Esc menu", 13));
    }

    static Control Row(string label, Control c) { var l = UI.Lbl(label, 15, Pal.Dim); l.CustomMinimumSize = new Vector2(180, 0); c.SizeFlagsHorizontal = SizeFlags.ExpandFill; return UI.HBox(12, l, c); }
}
