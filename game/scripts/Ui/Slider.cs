using System;
using Godot;

namespace EconGame.Ui;

/// <summary>Themed slider with a baseline marker (current enacted value) so the player sees how far a draft departs from it.</summary>
public partial class AppSlider : Control
{
    [Signal] public delegate void ChangedEventHandler(double value);

    public double Min, Max = 1, Step = 0.01, Value, Baseline = double.NaN;
    public Func<double, string> Format = v => v.ToString("0.00");
    bool _drag, _hover;

    public AppSlider() { CustomMinimumSize = new Vector2(180, 30); FocusMode = FocusModeEnum.All; MouseFilter = MouseFilterEnum.Stop; }

    public void Setup(double min, double max, double step, double value, Func<double, string> fmt)
    {
        Min = min; Max = max; Step = step; Value = Mathf.Clamp(value, min, max); Baseline = Value; Format = fmt; QueueRedraw();
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
        float trackL = 8, trackW = Size.X - 16 - 78;
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            _drag = mb.Pressed; if (mb.Pressed) { GrabFocus(); Set(mb.Position.X); }
        }
        else if (e is InputEventMouseMotion mm) { _hover = true; if (_drag) Set(mm.Position.X); QueueRedraw(); }
        else if (e is InputEventKey k && k.Pressed)
        {
            if (k.Keycode == Key.Left) { SetValue(Value - Step, true); AcceptEvent(); }
            if (k.Keycode == Key.Right) { SetValue(Value + Step, true); AcceptEvent(); }
        }
        void Set(float x) => SetValue(Min + Mathf.Clamp((x - trackL) / trackW, 0, 1) * (Max - Min), true);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit) { _hover = false; QueueRedraw(); }
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont; int fs = Mathf.RoundToInt(13 * Pal.TextScale);
        float trackL = 8, trackW = Size.X - 16 - 78, cy = Size.Y / 2;
        DrawRect(new Rect2(trackL, cy - 3, trackW, 6), Pal.Border);
        float hx = trackL + Frac * trackW;
        DrawRect(new Rect2(trackL, cy - 3, hx - trackL, 6), Pal.Accent.Darkened(0.2f));
        if (!double.IsNaN(Baseline))
        {
            float bx = trackL + (float)((Baseline - Min) / (Max - Min)) * trackW;
            DrawLine(new Vector2(bx, cy - 9), new Vector2(bx, cy + 9), Pal.Dim, 2);
        }
        DrawCircle(new Vector2(hx, cy), _hover || _drag || HasFocus() ? 9 : 7, Pal.Accent);
        bool changed = !double.IsNaN(Baseline) && Math.Abs(Value - Baseline) > Step / 2;
        DrawString(font, new Vector2(Size.X - 72, cy + 5), Format(Value), HorizontalAlignment.Right, 70, fs, changed ? Pal.Warn : Pal.Text);
    }
}
