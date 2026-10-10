using System;
using Godot;
using Sim.Core.Policy;
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
        var ui = new AppSlider(); ui.Setup(0.8, 1.5, 0.1, Settings.UiScale, v => $"{v:0.0}×");
        ui.Changed += v => { Settings.UiScale = (float)v; Settings.Apply(); };
        ui.TooltipText = "Scales the whole interface: text, controls and spacing. Use Text size to scale text alone.";
        box.AddChild(Row("Interface scale", ui));
        var cb = new CheckBox { Text = "Colour-blind safe palette", ButtonPressed = Settings.ColourBlind };
        cb.Toggled += on => { Settings.ColourBlind = on; Settings.Apply(); };
        box.AddChild(cb);
        var hc = new CheckBox { Text = "High-contrast theme", ButtonPressed = Settings.HighContrast };
        hc.Toggled += on => { Settings.HighContrast = on; Settings.Apply(); Main.Instance!.ApplyTheme(); Main.Instance.ShowSettings(_back); };
        box.AddChild(hc);
        var rm = new CheckBox { Text = "Reduce motion (notifications do not fade)", ButtonPressed = Settings.ReduceMotion };
        rm.Toggled += on => Settings.ReduceMotion = on; box.AddChild(rm);
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
        box.AddChild(UI.H2("When the clock is running, stop for…"));
        box.AddChild(UI.Dim("Applies when you press play or use Run to. A manual End turn never stops anywhere.", 12, true));
        var triggers = new GridContainer { Columns = 2 }; triggers.AddThemeConstantOverride("h_separation", 24); triggers.AddThemeConstantOverride("v_separation", 2);
        foreach (var (flag, text) in new[]
        {
            (PauseTrigger.AdviserAlert, "Adviser alerts"), (PauseTrigger.Recession, "A recession starting"), (PauseTrigger.Election, "Elections three months away"),
            (PauseTrigger.PolicyInForce, "A policy taking effect"), (PauseTrigger.Project, "A project completing"), (PauseTrigger.Imf, "IMF programmes and debt crises"),
            (PauseTrigger.GradeDrop, "Your grade falling"), (PauseTrigger.Inflation, "Inflation far above target"), (PauseTrigger.Event, "Any event in your country"),
        })
        {
            var f = flag; var t = new CheckBox { Text = text, ButtonPressed = ((PauseTrigger)Settings.PauseTriggers & f) != 0 };
            t.Toggled += on => Settings.PauseTriggers = on ? Settings.PauseTriggers | (int)f : Settings.PauseTriggers & ~(int)f;
            triggers.AddChild(t);
        }
        box.AddChild(triggers);
        var coach = new CheckBox { Text = "Guide me through the first turn of a new game", ButtonPressed = !Settings.TutorialSeen };
        coach.Toggled += on => Settings.TutorialSeen = !on; box.AddChild(coach);
        var review = new CheckBox { Text = "Show a year-in-review each January", ButtonPressed = Settings.AnnualReview };
        review.Toggled += on => Settings.AnnualReview = on; box.AddChild(review);
        var crash = new CheckBox { Text = "Keep a local crash log (never sent anywhere)", ButtonPressed = Diagnostics.CrashLog };
        crash.Toggled += on => Diagnostics.CrashLog = on; box.AddChild(crash);
        box.AddChild(UI.HBox(10, UI.Btn("Copy feedback report to clipboard", Diagnostics.CopyReport), UI.Dim("Paste it into an issue or email; contains no personal data.", 12)));
        var card = UI.Card(UI.Scroll(box)); card.SizeFlagsVertical = SizeFlags.ExpandFill; root.AddChild(card);   // scrolls when the window or interface scale leaves too little height
        root.AddChild(UI.Dim("Keyboard: Space pause · 1-4 speed · Ctrl+Tab or PageUp/PageDown change view · Tab moves focus · Esc menu · F1 help · F2 glossary", 13));
    }

    static Control Row(string label, Control c) { var l = UI.Lbl(label, 15, Pal.Dim); l.CustomMinimumSize = new Vector2(180, 0); c.SizeFlagsHorizontal = SizeFlags.ExpandFill; return UI.HBox(12, l, c); }
}
