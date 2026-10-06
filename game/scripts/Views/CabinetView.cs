using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.Core.Model;
using Sim.Core.Policy;
using EconGame.App;
using EconGame.Ui;

namespace EconGame.Views;

/// <summary>
/// The cabinet's current advice as cards you can act on: add the suggested move to the plan, preview it against carrying on, or snooze the note.
/// Advice reads the state of the country, which only changes when a turn is played, so the cards change then too.
/// </summary>
public partial class CabinetView : View
{
    public override string Title => "Cabinet";
    readonly VBoxContainer _conflicts = UI.VBox(8), _cards = UI.VBox(10), _snoozed = UI.VBox(6), _auto = UI.VBox(8);
    readonly Label _msg = UI.Lbl("", 13, Pal.Dim, false, HorizontalAlignment.Left, true);
    readonly PreviewPanel _preview = new() { Months = 60 };
    readonly Label _previewTitle = UI.Dim("", 13, true);
    long _sig = -1; int _msgMonth = -1;
    const int SnoozeMonths = 6;

    public CabinetView()
    {
        var page = Page("Cabinet", "Your advisers' current advice. They disagree on purpose: the finance minister tightens, the social minister supports. Each suggestion can be added to this turn's plan or previewed first. Notes change only when a turn is played.");
        page.AddChild(_conflicts); page.AddChild(_msg); page.AddChild(_cards);
        page.AddChild(UI.Card(UI.VBox(8, UI.H2("Preview"), _previewTitle, _preview)));
        page.AddChild(_snoozed);
        page.AddChild(_auto);
    }

    public static string Key(AdvisorNote n) => Game.Player.Id + ":" + n.Key;
    public static bool IsSnoozed(AdvisorNote n) => Game.Snoozed.TryGetValue(Key(n), out var until) && until > Game.World.Month;

    /// <summary>Live notes that are not snoozed, most urgent first.</summary>
    public static List<AdvisorNote> Live() =>
        Advisors.Generate(Game.World, Game.Player).Where(n => !IsSnoozed(n)).OrderByDescending(n => (int)n.Severity).ThenBy(n => n.Advisor, StringComparer.Ordinal).ToList();

    public override void Refresh()
    {
        if (!Game.Running) return;
        long sig = (long)Game.World.Month * 1_000_000 + Game.PlanVersion * 1000 + Game.Snoozed.Count * 10 + (Game.Player.Autopilot ? 1 : 0);
        if (sig == _sig) return; _sig = sig;
        Rebuild();
    }

    void Rebuild()
    {
        if (_msgMonth != Game.World.Month) { _msg.Text = ""; _msgMonth = Game.World.Month; }   // feedback from an earlier turn is stale
        foreach (var box in new[] { _conflicts, _cards, _snoozed, _auto }) foreach (var ch in box.GetChildren().ToList()) { box.RemoveChild(ch); ch.QueueFree(); }
        var all = Advisors.Generate(Game.World, Game.Player);
        var live = all.Where(n => !IsSnoozed(n)).OrderByDescending(n => (int)n.Severity).ThenBy(n => n.Advisor, StringComparer.Ordinal).ToList();

        foreach (var k in Advisors.Conflicts(live))
        {
            var t = UI.VBox(4,
                UI.Lbl("The cabinet is divided", 15, Pal.Warn, true),
                UI.Lbl($"{k.Tighten.Advisor} wants to tighten: “{k.Tighten.Text}”", 13, Pal.Text, false, HorizontalAlignment.Left, true),
                UI.Lbl($"{k.Support.Advisor} wants support: “{k.Support.Text}”", 13, Pal.Text, false, HorizontalAlignment.Left, true),
                UI.Dim("Preview each remedy against carrying on before choosing; you can also do part of both.", 12, true));
            _conflicts.AddChild(UI.Card(t, Pal.PanelAlt, 12));
        }

        if (live.Count == 0) _cards.AddChild(UI.Card(UI.Dim("Nothing needs your attention. The cabinet will speak up when something changes.", 14, true)));
        foreach (var n in live) _cards.AddChild(NoteCard(n));

        var snoozed = all.Where(IsSnoozed).ToList();
        if (snoozed.Count > 0)
        {
            var row = UI.HBox(10, UI.Dim($"{snoozed.Count} snoozed note{(snoozed.Count == 1 ? "" : "s")}: " + string.Join(", ", snoozed.Select(n => n.Advisor).Distinct()) + ".", 13, true),
                UI.Btn("Bring back", () => { foreach (var n in snoozed) Game.Snoozed.Remove(Key(n)); _sig = -1; Refresh(); }, false, 110));
            _snoozed.AddChild(row);
        }

        var c = Game.Player;
        var ap = Command.Autopilot(c.Id, !c.Autopilot);
        _auto.AddChild(UI.Card(UI.VBox(8,
            UI.H2("Cabinet autopilot"),
            UI.Dim(c.Autopilot ? "The cabinet is running tax, spending and interest-rate dials by simple rules. You still decide policies, projects and trade. Take control back at any time."
                               : "Hand the routine dials to the cabinet: it nudges tax, spending and rates by simple rules while you concentrate on reform. It is competent, not clever, and it will not enact policies for you.", 13, true),
            PlanUi.Toggle(c.Autopilot ? "Take control back" : "Hand over to the cabinet", ap, m => _msg.Text = m, false, 230))));
    }

    Control NoteCard(AdvisorNote n)
    {
        var col = n.Severity switch { Severity.Alert => Pal.Bad, Severity.Warning => Pal.Warn, _ => Pal.Accent };
        string sev = n.Severity switch { Severity.Alert => "ALERT", Severity.Warning => "WARNING", _ => "NOTE" };
        var head = UI.HBox(8, UI.Lbl(n.Advisor, 15, Pal.Text, true), Cards.Chip(sev, col));
        if (n.Stance != Stance.None) head.AddChild(Cards.Chip(n.Stance == Stance.Tighten ? "wants to tighten" : "wants support", Pal.Faint));
        var body = UI.VBox(6, head, UI.Lbl(n.Text, 14, Pal.Text, false, HorizontalAlignment.Left, true));

        var actions = UI.HBox(8);
        if (n.Suggestion != null)
        {
            var cmd = n.Suggestion; var dry = CommandProcessor.Apply(Game.World, cmd, dryRun: true);
            body.AddChild(UI.Lbl("Suggested: " + PlanText.Describe(Game.World, cmd) + (dry.Ok && !dry.NoOp ? $"   ·   {dry.PcCost:0} political capital" : ""), 13, Pal.Accent, false, HorizontalAlignment.Left, true));
            actions.AddChild(PlanUi.Toggle("Add to plan", cmd, m => _msg.Text = m, true, 150));
            actions.AddChild(UI.Btn("Preview", () => { _previewTitle.Text = "Plan plus: " + PlanText.Describe(Game.World, cmd) + ", against carrying on."; _preview.Run(Game.Sim!.Merged(new[] { cmd })); }, false, 110));
        }
        actions.AddChild(UI.Btn($"Snooze {SnoozeMonths} months", () => { Game.Snoozed[Key(n)] = Game.World.Month + SnoozeMonths; _sig = -1; Refresh(); }, false, 160));
        body.AddChild(actions);

        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", AppTheme.Box(Pal.Panel, 10, Pal.Border, 1, 12));
        var bar = new ColorRect { Color = col, CustomMinimumSize = new Vector2(4, 0) };
        body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        p.AddChild(UI.HBox(12, bar, body));
        return p;
    }
}
