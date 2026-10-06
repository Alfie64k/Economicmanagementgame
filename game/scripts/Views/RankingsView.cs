using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.Core.Model;
using Sim.Core.Scoring;
using EconGame.App;
using EconGame.Ui;

namespace EconGame.Views;

/// <summary>League tables across all simulated countries, with a year-ago comparison and a chosen rival.</summary>
public partial class RankingsView : View
{
    public override string Title => "Rankings";
    string _metric = "gdppc";
    readonly Dictionary<string, Button> _chips = new();
    readonly HBoxContainer _tiles = new();
    readonly BarList _bars = new() { LabelWidth = 190, Clickable = true, RowHeight = 25 };
    readonly VBoxContainer _compare = UI.VBox(8);
    readonly Label _hint = UI.Dim("", 13, true);
    int _sig = -1;
    List<RankRow> _table = new();

    public RankingsView()
    {
        var page = Page("Rankings", "How your country compares with the other 27 on each measure, now and a year ago. Click a country to set it as your rival and compare everything side by side.");
        var chips = new HFlowContainer(); chips.AddThemeConstantOverride("h_separation", 6); chips.AddThemeConstantOverride("v_separation", 6);
        foreach (var m in Rankings.Metrics)
        {
            string key = m.Key; var chip = UI.Chip(m.Label, key == _metric, () => { _metric = key; foreach (var kv in _chips) kv.Value.SetPressedNoSignal(kv.Key == key); _sig = -1; Refresh(); });
            chip.TooltipText = m.Tip; _chips[key] = chip; chips.AddChild(chip);
        }
        page.AddChild(chips);
        _tiles.AddThemeConstantOverride("separation", 10); page.AddChild(_tiles);
        _bars.Picked += i => { if (i >= 0 && i < _table.Count && !_table[i].Player) { Game.Rival = Game.Rival == _table[i].Id ? "" : _table[i].Id; _sig = -1; Refresh(); } };
        page.AddChild(UI.Card(UI.VBox(8, _hint, _bars)));
        page.AddChild(Cards.Section("You against your rival", _compare));
    }

    public override void Refresh()
    {
        if (!Game.Running) return;
        int sig = Game.World.Month * 100 + _metric.GetHashCode() % 97 + Game.Rival.GetHashCode() % 89;
        if (sig == _sig) return; _sig = sig;
        var w = Game.World; var m = Rankings.Metric(_metric); int n = w.Countries.Count;
        _table = Rankings.Table(w, _metric)!;
        int? before = w.Month >= 12 ? Rankings.RankOf(w, _metric, w.PlayerId, w.Month - 12) : null;
        var me = _table.First(r => r.Player);

        foreach (var ch in _tiles.GetChildren().ToList()) ch.QueueFree();
        void T(string title, string value, string sub, Color? col = null) { var k = new KpiTile(title) { SizeFlagsHorizontal = SizeFlags.ExpandFill }; k.Set(value, sub, col, Array.Empty<double>()); _tiles.AddChild(k); }
        T("Your rank", $"{me.Rank} of {n}", $"{m.Label}: {m.Format(me.Value)}", me.Rank <= n / 3 ? Pal.Good : me.Rank > 2 * n / 3 ? Pal.Bad : Pal.Dim);
        if (before != null) { int d = before.Value - me.Rank; T("A year ago", $"{before} of {n}", d > 0 ? $"▲ up {d} places" : d < 0 ? $"▼ down {-d} places" : "▬ unchanged", d > 0 ? Pal.Good : d < 0 ? Pal.Bad : Pal.Dim); }
        else T("A year ago", "-", "available after a year");
        var up = me.Rank > 1 ? _table[me.Rank - 2] : null; var down = me.Rank < n ? _table[me.Rank] : null;
        T("Next above", up?.Name ?? "You lead", up != null ? $"{m.Format(up.Value)} ({Gap(m, up.Value - me.Value)} ahead)" : "", Pal.Dim);
        T("Next below", down?.Name ?? "Last place", down != null ? $"{m.Format(down.Value)} ({Gap(m, me.Value - down.Value)} behind)" : "", Pal.Dim);

        _hint.Text = m.Tip + (Game.Rival != "" ? "" : "  Click a country to make it your rival.");
        bool diverge = _table.Any(r => r.Value < 0);
        _bars.Diverging = diverge; _bars.Format = m.Format;
        _bars.Max = null;
        _bars.Set(_table.Select(r => new BarItem
        {
            Label = $"{r.Rank,2}. {r.Name}", Value = r.Value, Text = m.Format(r.Value), Emphasis = r.Player || r.Id == Game.Rival,
            Color = r.Player ? Pal.Accent : r.Id == Game.Rival ? Pal.Warn : Pal.Series[0].Darkened(0.35f),
            Tooltip = r.Player ? "You" : r.Id == Game.Rival ? "Your rival (click to clear)" : "Click to make this country your rival",
        }));
        BuildCompare(w);
    }

    static string Gap(RankMetric m, double d) => m.Format(Math.Abs(d));

    void BuildCompare(World w)
    {
        foreach (var ch in _compare.GetChildren().ToList()) { _compare.RemoveChild(ch); ch.QueueFree(); }
        var rival = Game.Rival != "" ? w.Find(Game.Rival) : null;
        if (rival == null)
        {
            // no rival: show the nearest country by GDP per head as a suggestion
            var t = Rankings.Table(w, "gdppc")!; int i = t.FindIndex(r => r.Player);
            var near = t.Where(r => !r.Player).OrderBy(r => Math.Abs(r.Rank - (i + 1))).First();
            _compare.AddChild(UI.Dim($"No rival chosen. A natural peer is {near.Name}, whose GDP per head is closest to yours.", 13, true));
            _compare.AddChild(UI.Btn($"Make {near.Name} my rival", () => { Game.Rival = near.Id; _sig = -1; Refresh(); }, false, 260));
            return;
        }
        var g = new GridContainer { Columns = 5 }; g.AddThemeConstantOverride("h_separation", 22); g.AddThemeConstantOverride("v_separation", 5);
        foreach (var h in new[] { "", "You", rival.Name, "Your rank", $"{rival.Name} rank" }) g.AddChild(UI.Lbl(h, 12, Pal.Faint, true));
        int ahead = 0, behind = 0;
        foreach (var m in Rankings.Metrics)
        {
            var t = Rankings.Table(w, m.Key)!; var a = t.First(r => r.Player); var b = t.First(r => r.Id == rival.Id);
            bool win = a.Rank < b.Rank, lose = a.Rank > b.Rank; if (win) ahead++; else if (lose) behind++;
            g.AddChild(UI.Lbl(m.Label, 14, Pal.Dim));
            g.AddChild(UI.Lbl(m.Format(a.Value), 14, win ? Pal.Good : lose ? Pal.Bad : Pal.Text, true));
            g.AddChild(UI.Lbl(m.Format(b.Value), 14, Pal.Text));
            g.AddChild(UI.Lbl((win ? "▲ " : lose ? "▼ " : "▬ ") + a.Rank, 14, win ? Pal.Good : lose ? Pal.Bad : Pal.Dim));
            g.AddChild(UI.Lbl(b.Rank.ToString(), 14, Pal.Dim));
        }
        _compare.AddChild(UI.Lbl($"You lead {rival.Name} on {ahead} measures and trail on {behind}.", 15, ahead >= behind ? Pal.Good : Pal.Warn, true));
        _compare.AddChild(g);
        _compare.AddChild(UI.Btn("Clear rival", () => { Game.Rival = ""; _sig = -1; Refresh(); }, false, 130));
    }
}
