using System;
using System.Collections.Generic;
using Godot;
using EconGame.App;

namespace EconGame.Ui;

/// <summary>
/// A short guided first turn. A card names one thing to do, a pulsing outline marks where, and the step completes when the player does it (open a page, add
/// something to the plan, end the turn). Every step can be skipped and the whole coach dismissed; it appears once in a new sandbox game and can be replayed from Help.
/// </summary>
public partial class TutorialCoach : Control
{
    public sealed record Step(string Title, string Text, string? Target, string Gate);

    public static readonly Step[] Steps =
    {
        new("Welcome", "You are running a national economy. The tiles on the Dashboard are its vital signs. Click one (try Inflation) to see what is driving it, then press Next.", null, "next"),
        new("Open the Budget", "Your main levers are taxes, benefits and departmental budgets. Open the Budget page.", "nav:Budget", "nav:Budget"),
        new("Plan a change", "Drag a tax slider on the Overview, or open a tab such as Income tax. Then press Add to plan. Nothing happens yet: you are only drafting this turn.", null, "plan"),
        new("Review the plan", "The Plan button shows what is staged and what it will cost in political capital. Open it, look, and close it again. When you are ready, move on.", "plan", "next"),
        new("End the turn", "A turn is one month. Press End turn (or Enter). Your plan is applied first, then the economy moves, and the news and advisers update.", "endturn", "turn"),
        new("Try the interest rate", "Open Monetary and drag the target-rate slider: the panel shows the expected effect on inflation, jobs and the currency over 3, 6 and 12 months, before you commit.", "nav:Monetary", "nav:Monetary"),
        new("Listen to the cabinet", "Your advisers disagree on purpose. Open the Cabinet page: each note can carry a suggested move you can add to the plan, preview or snooze.", "nav:Cabinet", "nav:Cabinet"),
        new("Let time run", "Run to lets the clock run to the end of the quarter or year, or to the next election, and stop early when something needs you. Pick one, or press Next to skip.", "runto", "runto"),
        new("You are set", "F2 opens the glossary, F5 quick-saves, F1 shows the keys. Policies, Investment and Trade add depth once you are comfortable. Good luck.", null, "next"),
    };

    readonly Func<string, Control?> _target;
    readonly Action _finished;
    readonly PanelContainer _card = new();
    readonly Label _count, _title, _text;
    readonly Button _next, _skip;
    readonly Highlight _hl = new();
    int _step; int _turnMonth; bool _collapsed; Control? _hidden;
    public int CurrentStep => _step;
    public bool Finished { get; private set; }
    public string CurrentGate => _step < Steps.Length ? Steps[_step].Gate : "";

    public TutorialCoach(Func<string, Control?> target, Action finished)
    {
        _target = target; _finished = finished;
        SetAnchorsPreset(LayoutPreset.FullRect); MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_hl);
        _card.AddThemeStyleboxOverride("panel", AppTheme.Box(Pal.PanelAlt, 10, Pal.Warn, 2, 14));
        _card.CustomMinimumSize = new Vector2(420, 0); _card.MouseFilter = MouseFilterEnum.Stop;
        _count = UI.Lbl("", 12, Pal.Warn, true); _title = UI.Lbl("", 17, Pal.Text, true); _text = UI.Lbl("", 14, Pal.Dim, false, HorizontalAlignment.Left, true);
        _next = UI.Btn("Next ▸", () => Advance(), true, 90); _skip = UI.Btn("Skip step", () => Advance(), false, 100);
        var exit = UI.Btn("Close tutorial", () => Finish(), false, 140);
        var hide = UI.Btn("Hide", () => { _collapsed = !_collapsed; Layout(); }, false, 70);
        _card.AddChild(UI.VBox(6, UI.HBox(8, _count, UI.Spacer(0, 0, true), hide), _title, _text, UI.HBox(8, _next, _skip, UI.Spacer(0, 0, true), exit)));
        AddChild(_card);
        _turnMonth = Game.Running ? Game.World.Month : 0;
        Show(0);
    }

    public override void _Ready() { Resized += Layout; Layout(); }

    void Show(int i)
    {
        _step = i;
        if (i >= Steps.Length) { Finish(); return; }
        var s = Steps[i];
        _count.Text = $"TUTORIAL · STEP {i + 1} OF {Steps.Length}"; _title.Text = s.Title; _text.Text = s.Text;
        _next.Visible = s.Gate == "next"; _skip.Visible = s.Gate != "next";
        _next.Text = i == Steps.Length - 1 ? "Finish" : "Next ▸";
        if (s.Gate == "turn") _turnMonth = Game.World.Month;
        _collapsed = false; Layout();
    }

    void Advance() => Show(_step + 1);

    public void Finish()
    {
        if (Finished) return; Finished = true;
        Settings.TutorialSeen = true; Settings.Save();
        _finished(); QueueFree();
    }

    /// <summary>The game did something the current step may be waiting for: "nav:Page", "plan", "turn" or "runto".</summary>
    public void Notify(string what)
    {
        if (Finished || _step >= Steps.Length) return;
        var g = Steps[_step].Gate;
        if (g == what || (g == "plan" && what == "plan" && Game.Sim != null && Game.Sim.Plan.Count > 0) || (g == "turn" && what == "turn" && Game.World.Month > _turnMonth)) Advance();
    }

    void Layout()
    {
        _card.Visible = true;
        var size = Size; if (size.X <= 0) return;
        _card.Size = new Vector2(_card.CustomMinimumSize.X, 0);
        _card.ResetSize();
        _card.Position = new Vector2(196, Mathf.Max(70, size.Y - _card.Size.Y - 18));
        foreach (var n in new[] { _text }) n.Visible = !_collapsed;
        _title.Visible = true;
        _next.Visible = !_collapsed && Steps[Math.Min(_step, Steps.Length - 1)].Gate == "next";
        _skip.Visible = !_collapsed && Steps[Math.Min(_step, Steps.Length - 1)].Gate != "next";
        _card.ResetSize(); _card.Position = new Vector2(196, Mathf.Max(70, size.Y - _card.Size.Y - 18));
    }

    public override void _Process(double delta)
    {
        if (Finished || _step >= Steps.Length) return;
        var key = Steps[_step].Target; Control? t = key == null ? null : _target(key);
        _hl.Follow(t != null && t.IsVisibleInTree() ? new Rect2(t.GlobalPosition - GlobalPosition, t.Size) : null, delta);
    }

    /// <summary>A pulsing outline around the thing to click. With reduced motion it is a steady, thicker outline.</summary>
    sealed partial class Highlight : Control
    {
        Rect2? _r; double _t;
        public Highlight() { MouseFilter = MouseFilterEnum.Ignore; SetAnchorsPreset(LayoutPreset.FullRect); }
        public void Follow(Rect2? r, double dt) { _r = r; _t += dt; QueueRedraw(); }
        public override void _Draw()
        {
            if (_r is not Rect2 r) return;
            float pulse = Settings.ReduceMotion ? 0.5f : 0.5f + 0.5f * Mathf.Sin((float)_t * 4f);
            var rr = r.Grow(4 + 3 * pulse);
            DrawRect(rr, new Color(Pal.Warn, 0.9f), false, Settings.ReduceMotion ? 4 : 3);
            DrawRect(r.Grow(1), new Color(Pal.Warn, 0.25f * pulse), true);
        }
    }
}
