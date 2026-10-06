using Godot;

namespace EconGame.Ui;

/// <summary>Political-capital meter: banked balance, the slice the staged plan will spend (hatched), and a tick for where it lands after next month's regeneration.</summary>
public partial class PcBar : Control
{
    double _banked, _pending, _next;
    public double Max = 100;

    public PcBar() { CustomMinimumSize = new Vector2(180, 12); MouseFilter = MouseFilterEnum.Pass; }

    public void Set(double banked, double pending, double nextAfterRegen)
    {
        if (banked == _banked && pending == _pending && nextAfterRegen == _next) return;
        _banked = banked; _pending = pending; _next = nextAfterRegen; QueueRedraw();
    }

    public override void _Draw()
    {
        float w = Size.X, h = Size.Y;
        DrawStyleBox(AppTheme.Box(Pal.Border, 6), new Rect2(0, 0, w, h));
        float f(double v) => (float)Mathf.Clamp(v / Max, 0, 1) * w;
        double keep = System.Math.Max(0, _banked - _pending);
        if (keep > 0) DrawStyleBox(AppTheme.Box(Pal.Accent, 6), new Rect2(0, 0, Mathf.Max(f(keep), 6), h));
        if (_pending > 0)
        {
            float x0 = f(keep), x1 = f(_banked);
            DrawRect(new Rect2(x0, 0, x1 - x0, h), new Color(Pal.Warn, 0.28f));
            for (float x = x0 - h; x < x1; x += 5) { float a = Mathf.Max(x, x0), b = Mathf.Min(x + h, x1); if (b > a) DrawLine(new Vector2(a, h - (a - x)), new Vector2(b, h - (b - x)), Pal.Warn, 1.4f); }
            DrawRect(new Rect2(x0, 0, x1 - x0, h), Pal.Warn, false, 1);
        }
        if (_pending > 0 || System.Math.Abs(_next - _banked) > 0.05)
            DrawLine(new Vector2(f(_next), -1), new Vector2(f(_next), h + 1), Pal.Text, 2);
    }
}
