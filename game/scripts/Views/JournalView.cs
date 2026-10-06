using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.Core.Model;
using Sim.Core.Scoring;
using EconGame.App;
using EconGame.Ui;

namespace EconGame.Views;

/// <summary>A running record of what you did and what happened, with how the economy moved in the year after each action, and a review of any completed year.</summary>
public partial class JournalView : View
{
    public override string Title => "Journal";
    string _filter = "all"; int _reviewEnd; int _limit = 60;
    readonly Dictionary<string, Button> _chips = new();
    readonly VBoxContainer _review = UI.VBox(10), _list = UI.VBox(6);
    readonly OptionButton _years = new() { CustomMinimumSize = new Vector2(180, 0) };
    readonly Label _note = UI.Dim("", 13, true);
    long _sig = -1; bool _syncing;

    public JournalView()
    {
        var page = Page("Journal", "Everything you decided and everything that happened to you, newest first. Next to each action is what the main measures did over the following twelve months: it shows what moved afterwards, not proof of what caused it.");
        var head = UI.HBox(10, UI.H2("Year in review"), _years); page.AddChild(head);
        _years.ItemSelected += i => { if (_syncing) return; _reviewEnd = (int)_years.GetItemId((int)i); _sig = -1; Refresh(); };
        page.AddChild(UI.Card(_review));
        var chips = UI.HBox(6);
        foreach (var (k, label) in new[] { ("all", "Everything"), ("action", "My actions"), ("policy", "Policies"), ("project", "Projects"), ("event", "Events & crises"), ("election", "Elections") })
        {
            string key = k; var chip = UI.Chip(label, key == _filter, () => { _filter = key; _limit = 60; foreach (var kv in _chips) kv.Value.SetPressedNoSignal(kv.Key == key); _sig = -1; Refresh(); });
            _chips[k] = chip; chips.AddChild(chip);
        }
        page.AddChild(UI.H2("Timeline")); page.AddChild(chips); page.AddChild(_note); page.AddChild(_list);
    }

    public override void Refresh()
    {
        if (!Game.Running) return;
        long sig = (long)Game.World.Month * 1000 + _filter.GetHashCode() % 97 + _reviewEnd * 7 + _limit;
        if (sig == _sig) return; _sig = sig;
        var w = Game.World;

        // completed calendar years that have a full twelve months of history
        var ends = new List<int>(); for (int m = 12; m <= w.Month; m += 12) ends.Add(m);
        _syncing = true; _years.Clear();
        foreach (var e in ends.AsEnumerable().Reverse()) _years.AddItem((w.StartYear + (e - 1) / 12).ToString(), e);
        if (ends.Count > 0) { if (_reviewEnd == 0 || !ends.Contains(_reviewEnd)) _reviewEnd = ends[^1]; _years.Select(ends.AsEnumerable().Reverse().ToList().IndexOf(_reviewEnd)); }
        _syncing = false;
        foreach (var ch in _review.GetChildren().ToList()) { _review.RemoveChild(ch); ch.QueueFree(); }
        var r = ends.Count > 0 ? Journal.Review(w, _reviewEnd) : null;
        if (r == null) _review.AddChild(UI.Dim("The first review appears when a full year has been played.", 14, true));
        else _review.AddChild(YearReviewPanel.Build(r, w));

        foreach (var ch in _list.GetChildren().ToList()) { _list.RemoveChild(ch); ch.QueueFree(); }
        var entries = Journal.Build(w);
        var impacts = Journal.Impacts(w, 12, entries).ToDictionary(i => i.Entry, i => i);
        var shown = entries.Where(e => _filter == "all" || e.Kind == _filter || (_filter == "event" && e.Kind is "crisis" or "milestone")).AsEnumerable().Reverse().ToList();
        _note.Text = shown.Count == 0 ? "Nothing here yet. Add an action to the plan and end the turn." : $"{shown.Count} entries" + (shown.Count > _limit ? $", showing the latest {_limit}." : ".");
        foreach (var e in shown.Take(_limit)) _list.AddChild(Row(e, impacts.TryGetValue(e, out var imp) ? imp : null, w));
        if (shown.Count > _limit) _list.AddChild(UI.Btn("Show older entries", () => { _limit += 60; _sig = -1; Refresh(); }, false, 180));
    }

    static Control Row(JournalEntry e, Impact? imp, World w)
    {
        var col = e.Kind switch { "action" => Pal.Accent, "policy" => Pal.Series[2], "project" => Pal.Series[4], "election" => Pal.Warn, "crisis" => Pal.Bad, "milestone" => Pal.Series[6], _ => Pal.Dim };
        var top = UI.HBox(10, UI.Lbl($"{w.StartYear + e.Month / 12}-{e.Month % 12 + 1:D2}", 13, Pal.Dim), Cards.Chip(e.Kind, col));
        var title = UI.Lbl(e.Title, 14, Pal.Text, false, HorizontalAlignment.Left, true); title.SizeFlagsHorizontal = SizeFlags.ExpandFill; top.AddChild(title);
        var box = UI.VBox(3, top);
        if (imp != null)
        {
            var strip = UI.HBox(14);
            strip.AddChild(UI.Lbl(imp.Complete ? "12 months on:" : $"so far ({Math.Max(0, w.Month - e.Month)} months):", 12, Pal.Faint));
            void Chip(string label, double v, bool upGood, bool pp, double flat)
            {
                bool flatV = Math.Abs(v) < flat; var c = flatV ? Pal.Dim : (v > 0) == upGood ? Pal.Good : Pal.Bad;
                string arrow = flatV ? "▬" : v > 0 ? "▲" : "▼";
                strip.AddChild(UI.Lbl($"{label} {arrow} {(v * 100):+0.0;-0.0;0.0}{(pp ? "pp" : "pts")}", 12, c));
            }
            Chip("growth", imp.Growth, true, true, 0.001); Chip("inflation", imp.Inflation, false, true, 0.001); Chip("unemployment", imp.Unemployment, false, true, 0.001);
            Chip("debt", imp.DebtToGdp, false, true, 0.002); Chip("approval", imp.Approval, true, false, 0.005);
            box.AddChild(strip);
        }
        var p = new PanelContainer(); p.AddThemeStyleboxOverride("panel", AppTheme.Box(Pal.PanelAlt, 6, null, 0, 8)); p.AddChild(box);
        return p;
    }
}
