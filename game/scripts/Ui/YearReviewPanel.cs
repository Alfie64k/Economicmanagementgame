using System;
using System.Linq;
using Godot;
using Sim.Core.Model;
using Sim.Core.Scoring;
using EconGame.App;

namespace EconGame.Ui;

/// <summary>The "year in review" card: how the twelve months went on the headline measures, what you did and what happened to you.</summary>
public static class YearReviewPanel
{
    public static VBoxContainer Build(YearReview r, World w, Action? openJournal = null, Action? close = null)
    {
        var box = UI.VBox(10);
        box.AddChild(UI.Lbl($"{r.Year} in review", 26, Pal.Text, true));
        box.AddChild(UI.Lbl(r.Headline, 16, Pal.Accent, true));

        var g = new GridContainer { Columns = 4 }; g.AddThemeConstantOverride("h_separation", 22); g.AddThemeConstantOverride("v_separation", 5);
        foreach (var h in new[] { "", "Start of year", "End of year", "Change" }) g.AddChild(UI.Lbl(h, 12, Pal.Faint, true));
        foreach (var m in r.Metrics)
        {
            var col = m.Verdict > 0 ? Pal.Good : m.Verdict < 0 ? Pal.Bad : Pal.Dim;
            string arrow = m.Verdict > 0 ? "▲ " : m.Verdict < 0 ? "▼ " : "▬ ";       // a shape as well as a colour
            g.AddChild(UI.Lbl(m.Label, 14, Pal.Text));
            g.AddChild(UI.Lbl(m.Start, 14, Pal.Dim)); g.AddChild(UI.Lbl(m.End, 14, Pal.Text));
            g.AddChild(UI.Lbl(arrow + m.Change, 14, col, true));
        }
        box.AddChild(g);

        string Ord(int n) => n + (n % 100 is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
        if (r.GdpPcRankStart != null && r.GdpPcRankEnd != null)
        {
            int d = r.GdpPcRankStart.Value - r.GdpPcRankEnd.Value;
            box.AddChild(UI.Lbl($"GDP per head: {Ord(r.GdpPcRankEnd.Value)} of {w.Countries.Count}" + (d > 0 ? $" (up {d})" : d < 0 ? $" (down {-d})" : " (unchanged)")
                + (r.GdpRankEnd != null && r.GdpRankStart != null ? $"   ·   total GDP: {Ord(r.GdpRankEnd.Value)}" + (r.GdpRankStart > r.GdpRankEnd ? $" (up {r.GdpRankStart - r.GdpRankEnd})" : r.GdpRankStart < r.GdpRankEnd ? $" (down {r.GdpRankEnd - r.GdpRankStart})" : "") : ""),
                14, d > 0 ? Pal.Good : d < 0 ? Pal.Bad : Pal.Dim));
        }

        if (r.Actions.Count > 0)
        {
            box.AddChild(UI.Lbl("What you did", 15, Pal.Accent, true));
            foreach (var a in r.Actions.Take(7)) box.AddChild(UI.Lbl("◦ " + a.Title, 13, Pal.Text, false, HorizontalAlignment.Left, true));
            if (r.Actions.Count > 7) box.AddChild(UI.Dim($"…and {r.Actions.Count - 7} more in the journal.", 12));
        }
        else box.AddChild(UI.Dim("You made no changes this year. Sometimes that is the right call.", 13, true));
        if (r.Happenings.Count > 0)
        {
            box.AddChild(UI.Lbl("What happened", 15, Pal.Accent, true));
            foreach (var a in r.Happenings.Take(6)) box.AddChild(UI.Lbl("◦ " + a.Title, 13, Pal.Dim, false, HorizontalAlignment.Left, true));
        }
        if (openJournal != null || close != null)
        {
            var row = UI.HBox(10);
            if (close != null) row.AddChild(UI.Btn("Continue", close, true, 140));
            if (openJournal != null) row.AddChild(UI.Btn("Open the journal", openJournal, false, 170));
            box.AddChild(row);
        }
        return box;
    }
}
