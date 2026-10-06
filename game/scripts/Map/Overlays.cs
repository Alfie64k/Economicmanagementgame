using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.Core.Model;
using EconGame.Ui;

namespace EconGame.Map;

public sealed class OverlayDef
{
    public string Key = "", Title = "";
    public Func<World, CountryState, double> Value = (_, _) => 0;
    public Func<double, string> Format = v => v.ToString("0.0");
    public double Min, Max, Mid;
    public bool Good;               // use red-green scale around Mid
    public bool HigherIsBetter = true;

    public Color Colour(double v)
    {
        if (!Good) return Pal.Ramp((float)((v - Min) / (Max - Min)));
        double half = Math.Max(Math.Abs(Max - Mid), Math.Abs(Mid - Min));
        double s = (v - Mid) / half * (HigherIsBetter ? 1 : -1);
        return Pal.Diverge((float)s);
    }

    public static readonly OverlayDef[] All =
    {
        new() { Key = "growth", Title = "GDP growth", Value = (w, c) => c.GdpGrowth, Format = v => UI.Pct(v), Min = -0.03, Max = 0.07, Mid = 0.02, Good = true },
        new() { Key = "inflation", Title = "Inflation", Value = (w, c) => c.Inflation, Format = v => UI.Pct(v), Min = 0, Max = 0.12, Mid = 0.03, Good = true, HigherIsBetter = false },
        new() { Key = "unemployment", Title = "Unemployment", Value = (w, c) => c.Unemp, Format = v => UI.Pct(v), Min = 0.02, Max = 0.2, Mid = 0.06, Good = true, HigherIsBetter = false },
        new() { Key = "debt", Title = "Public debt / GDP", Value = (w, c) => c.DebtToGdp, Format = v => UI.Pct(v, 0), Min = 0.2, Max = 1.6, Mid = 0.8, Good = true, HigherIsBetter = false },
        new() { Key = "deficit", Title = "Budget deficit / GDP", Value = (w, c) => c.DeficitToGdp, Format = v => UI.Pct(v), Min = -0.02, Max = 0.1, Mid = 0.035, Good = true, HigherIsBetter = false },
        new() { Key = "approval", Title = "Government approval", Value = (w, c) => c.Approval, Format = v => UI.Pct(v, 0), Min = 0.15, Max = 0.75, Mid = 0.45, Good = true },
        new() { Key = "unrest", Title = "Unrest", Value = (w, c) => c.Unrest, Format = v => UI.Pct(v, 0), Min = 0, Max = 0.7, Mid = 0.2, Good = true, HigherIsBetter = false },
        new() { Key = "gdppc", Title = "GDP per head", Value = (w, c) => Math.Log10(Math.Max(300, c.GdpPerCapitaUsd)), Format = v => $"${Math.Pow(10, v):N0}", Min = 2.7, Max = 4.9 },
        new() { Key = "ca", Title = "Current account / GDP", Value = (w, c) => c.CaToGdp, Format = v => UI.Pct(v), Min = -0.08, Max = 0.12, Mid = 0.0, Good = true },
        new() { Key = "yield", Title = "10-year yield", Value = (w, c) => c.Yield10, Format = v => UI.Pct(v), Min = 0.0, Max = 0.2, Mid = 0.05, Good = true, HigherIsBetter = false },
        new() { Key = "emissions", Title = "CO₂ per head (t)", Value = (w, c) => c.EmissionsMt / Math.Max(1e-9, c.Pop), Format = v => v.ToString("0.0"), Min = 0, Max = 20, Mid = 8, Good = true, HigherIsBetter = false },
        new() { Key = "trade", Title = "Share of my exports", Value = (w, c) =>
            {
                int me = w.Countries.FindIndex(x => x.Id == w.PlayerId), j = w.Countries.IndexOf(c);
                return me >= 0 && me != j && w.Trade.W.Length > me ? w.Trade.W[me][j] : 0;
            }, Format = v => UI.Pct(v, 1), Min = 0, Max = 0.15 },
    };
}
