using System;
using Godot;

namespace EconGame.Ui;

/// <summary>Labelled matrix heat map (e.g. input-output flows).</summary>
public partial class Heatmap : Control
{
    public string[] Rows = Array.Empty<string>(), Cols = Array.Empty<string>();
    public double[,] Values = new double[0, 0];
    public Func<double, string> Format = v => v.ToString("0.00");

    public Heatmap() { CustomMinimumSize = new Vector2(320, 220); MouseFilter = MouseFilterEnum.Pass; }
    public void Set(string[] rows, string[] cols, double[,] v) { Rows = rows; Cols = cols; Values = v; QueueRedraw(); }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion m && Rows.Length > 0)
        {
            float lw = 90, th = 22; float cw = (Size.X - lw) / Cols.Length, ch = (Size.Y - th) / Rows.Length;
            int c = (int)((m.Position.X - lw) / cw), r = (int)((m.Position.Y - th) / ch);
            TooltipText = c >= 0 && c < Cols.Length && r >= 0 && r < Rows.Length ? $"{Rows[r]} → {Cols[c]}: {Format(Values[r, c])}" : "";
        }
    }

    public override void _Draw()
    {
        if (Rows.Length == 0) return;
        var font = ThemeDB.FallbackFont; int fs = Mathf.RoundToInt(11 * Pal.TextScale);
        float lw = 90, th = 22; float cw = (Size.X - lw) / Cols.Length, ch = (Size.Y - th) / Rows.Length;
        double max = 1e-12; foreach (var v in Values) max = Math.Max(max, v);
        for (int c = 0; c < Cols.Length; c++) DrawString(font, new Vector2(lw + c * cw, 14), Cols[c], HorizontalAlignment.Center, cw, fs, Pal.Dim);
        for (int r = 0; r < Rows.Length; r++)
        {
            DrawString(font, new Vector2(0, th + r * ch + ch * 0.62f), Rows[r], HorizontalAlignment.Left, lw - 6, fs, Pal.Dim);
            for (int c = 0; c < Cols.Length; c++)
            {
                var rect = new Rect2(lw + c * cw + 1, th + r * ch + 1, cw - 2, ch - 2);
                DrawRect(rect, Pal.Ramp((float)(Values[r, c] / max)));
                DrawString(font, rect.Position + new Vector2(0, ch * 0.6f), Format(Values[r, c]), HorizontalAlignment.Center, cw - 2, fs, Colors.White);
            }
        }
    }
}
