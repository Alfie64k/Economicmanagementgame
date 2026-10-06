using System;
using Godot;

namespace EconGame.Ui;

/// <summary>Themed slider with a baseline marker (current enacted value) so the player sees how far a draft departs from it.</summary>
public partial class AppSlider : Control
{
    [Signal] public delegate void ChangedEventHandler(double value);

    public double Min, Max = 1, Step = 0.01, Value, Baseline = double.NaN;
    public Func<double, string> Format = v => v.ToString("0.00");
    public float LabelWidth = 78;
    bool _drag, _hover, _mouseFocus;

    public AppSlider() { CustomMinimumSize = new Vector2(180, 30); FocusMode = FocusModeEnum.All; MouseFilter = MouseFilterEnum.Stop; }

    public void Setup(double min, double max, double step, double value, Func<double, string> fmt)
    {
        Min = min; Max = max; Step = step; Value = Mathf.Clamp(value, min, max); Baseline = Value; Format = fmt; QueueRedraw();
    }

    /// <summary>Show a value without snapping it to the step grid (for values that did not come from this slider).</summary>
    public void SetExact(double v)
    {
        v = Mathf.Clamp(v, Min, Max);
        if (Math.Abs(v - Value) < 1e-12) return;
        Value = v; QueueRedraw();
    }

    public void SetValue(double v, bool notify = false)
    {
        v = Math.Round(Mathf.Clamp(v, Min, Max) / Step) * Step;
        if (Math.Abs(v - Value) < 1e-12) return;
        Value = v; QueueRedraw();
        if (notify) EmitSignal(SignalName.Changed, Value);
    }

    float Frac => Max > Min ? (float)((Value - Min) / (Max - Min)) : 0;

    public override void _GuiInput(InputEvent e)
    {
        float trackL = 8, trackW = Size.X - 16 - LabelWidth;
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            _drag = mb.Pressed; if (mb.Pressed) { _mouseFocus = true; GrabFocus(); Set(mb.Position.X); }
        }
        else if (e is InputEventMouseMotion mm) { if (_drag) Set(mm.Position.X); }
        else if (e is InputEventKey k && k.Pressed)
        {
            _mouseFocus = false;
            double mult = k.ShiftPressed ? 10 : 1;
            if (k.Keycode is Key.Left or Key.Down) { SetValue(Value - Step * mult, true); AcceptEvent(); }
            else if (k.Keycode is Key.Right or Key.Up) { SetValue(Value + Step * mult, true); AcceptEvent(); }
            else if (k.Keycode == Key.Pagedown) { SetValue(Value - Step * 10, true); AcceptEvent(); }
            else if (k.Keycode == Key.Pageup) { SetValue(Value + Step * 10, true); AcceptEvent(); }
            else if (k.Keycode == Key.Home) { SetValue(Min, true); AcceptEvent(); }
            else if (k.Keycode == Key.End) { SetValue(Max, true); AcceptEvent(); }
        }
        void Set(float x) => SetValue(Min + Mathf.Clamp((x - trackL) / trackW, 0, 1) * (Max - Min), true);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseEnter) { _hover = true; QueueRedraw(); }
        else if (what == NotificationMouseExit) { _hover = false; QueueRedraw(); }
        else if (what == NotificationFocusEnter || what == NotificationFocusExit) { if (what == NotificationFocusExit) _mouseFocus = false; QueueRedraw(); }
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont; int fs = Mathf.RoundToInt(13 * Pal.TextScale);
        float trackL = 8, trackW = Size.X - 16 - LabelWidth, cy = Size.Y / 2;
        DrawRect(new Rect2(trackL, cy - 3, trackW, 6), Pal.Border);
        float hx = trackL + Frac * trackW;
        DrawRect(new Rect2(trackL, cy - 3, hx - trackL, 6), Pal.Accent.Darkened(0.2f));
        if (!double.IsNaN(Baseline))
        {
            float bx = trackL + (float)((Baseline - Min) / (Max - Min)) * trackW;
            DrawLine(new Vector2(bx, cy - 9), new Vector2(bx, cy + 9), Pal.Dim, 2);
        }
        bool focus = HasFocus() && !_mouseFocus;
        DrawCircle(new Vector2(hx, cy), _drag ? 10 : _hover ? 9 : 7, _drag ? Pal.Accent.Lightened(0.2f) : Pal.Accent);
        if (_hover && !_drag) DrawArc(new Vector2(hx, cy), 9, 0, Mathf.Tau, 24, new Color(Pal.Text, 0.6f), 1);
        if (focus) { DrawRect(new Rect2(trackL - 3, cy - 8, trackW + 6, 16), Pal.FocusRing, false, 2); }
        bool changed = !double.IsNaN(Baseline) && Math.Abs(Value - Baseline) > Step / 2;
        DrawString(font, new Vector2(Size.X - LabelWidth + 6, cy + 5), Format(Value), HorizontalAlignment.Right, LabelWidth - 8, fs, changed ? Pal.Warn : Pal.Text);
    }
}
