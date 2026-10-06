using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.Core.Engine;
using Sim.Core.Model;
using Sim.Core.Policy;
using EconGame.App;
using EconGame.Ui;

namespace EconGame.Views;

public partial class InvestmentView : View
{
    public override string Title => "Investment";
    readonly BarList _assets = new() { Diverging = false, LabelWidth = 170 };
    readonly VBoxContainer _pipeline = new(), _catalogue = new(), _subs = new();
    readonly Label _result = UI.Lbl("", 13, Pal.Dim, false, HorizontalAlignment.Left, true);
    int _sig = -1;
    readonly Dictionary<Sector, AppSlider> _subSliders = new();

    static readonly string[] AssetNames = { "Infrastructure", "Education system", "Health system", "R&D base", "Clean energy", "Housing stock", "Digital networks", "Defence capability" };

    public InvestmentView()
    {
        var page = Page("Investment", "Public capital builds slowly and decays without upkeep. Projects add permanent capability but run for years, can overrun, and need maintenance afterwards. Starts, cancellations and subsidy changes join this turn's plan and take effect when you end the turn.");
        _assets.RowHeight = 28;
        page.AddChild(Cards.Section("Public asset quality (100% = starting level)", _assets, "Budget lines move these toward your spending level; completed projects add a lasting boost."));
        page.AddChild(Cards.Section("Pipeline", _pipeline));
        page.AddChild(_result);
        BuildSubsidies();
        page.AddChild(Cards.Section("Industrial strategy: sector subsidies", _subs, "Subsidies (% of sector output) tilt private investment and lift potential output, at fiscal cost."));
        page.AddChild(Cards.Section("Project catalogue", _catalogue));
    }

    public override void Refresh()
    {
        if (!Game.Running) return;
        var c = Game.Player;
        _assets.Max = 1.8; _assets.Format = v => UI.Pct(v, 0);
        _assets.Set(Enum.GetValues<Asset>().Select(a => new BarItem
        {
            Label = AssetNames[(int)a], Value = c.AssetIdx[(int)a], Text = UI.Pct(c.AssetIdx[(int)a], 0),
            Color = c.AssetIdx[(int)a] >= 1 ? Pal.Good : Pal.Bad, Tooltip = $"Boost from projects: +{c.AssetBoost[(int)a] * 100:0}pp",
        }));

        int sig = c.Projects.Count * 100 + (int)c.Projects.Sum(p => p.Elapsed) + (int)(c.PoliticalCapital / 4) + Game.PlanVersion * 7919;
        SyncSubsidies(c);
        if (sig == _sig) return; _sig = sig;
        foreach (var ch in _pipeline.GetChildren().ToList()) ch.QueueFree();
        var live = c.Projects.Where(p => !p.Done).ToList();
        if (live.Count == 0) _pipeline.AddChild(UI.Dim($"No projects under construction ({CommandProcessor.MaxConcurrentProjects} can run at once). Start one below.", 13, true));
        for (int i = 0; i < live.Count; i++)
        {
            var p = live[i]; int idx = i; var def = PolicyCatalog.Project(p.Id);
            double planned = p.Total, frac = Math.Clamp(p.Elapsed / p.ActualMonths, 0, 1);
            var bar = new ProgressBar { MaxValue = 1, Value = frac, ShowPercentage = false, CustomMinimumSize = new Vector2(240, 12), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            bar.AddThemeStyleboxOverride("background", AppTheme.Box(Pal.Border, 5)); bar.AddThemeStyleboxOverride("fill", AppTheme.Box(Pal.Accent, 5));
            string flag = p.Spent > planned * frac * 1.2 ? "  ⚠ over budget" : p.Elapsed > p.PlannedMonths ? "  ⚠ behind schedule" : "";
            var cancel = Command.CancelProject(c.Id, p.Id); string ckey = Simulation.KeyOf(cancel);
            Button cb = Game.Staged(ckey) != null ? UI.Btn("Withdraw cancellation", () => { Game.Unstage(ckey); _result.Text = "↶ Withdrawn from the plan"; }) : UI.Btn("Plan cancellation", () => Act(cancel));
            var row = UI.VBox(3, UI.HBox(12, UI.Lbl(def?.Name ?? p.Id, 15, Pal.Text, true), bar, cb), UI.Dim($"{p.Elapsed:0}/{p.PlannedMonths} months · spent {Money.Local(c, p.Spent * c.PriceLevel)} of {Money.Local(c, planned * c.PriceLevel)} planned{flag}" + (Game.Staged(ckey) != null ? "  · cancelling at the end of the turn" : ""), 12, true));
            _pipeline.AddChild(row);
        }
        foreach (var q in Game.Sim!.Plan.Where(q => q.Type == "project"))
        {
            var sd = PolicyCatalog.Project(q.Id); string qk = Simulation.KeyOf(q);
            _pipeline.AddChild(UI.HBox(12, UI.Lbl(sd?.Name ?? q.Id, 15, Pal.Text, true), Cards.Chip("starts at the end of the turn", Pal.Warn), UI.Spacer(0, 0, true), UI.Btn("Withdraw", () => { Game.Unstage(qk); _result.Text = "↶ Withdrawn from the plan"; })));
        }
        if (c.Projects.Any(p => p.Done)) _pipeline.AddChild(UI.Dim($"Completed: {c.Projects.Count(p => p.Done)} · upkeep {UI.Pct(c.MaintFlow / Math.Max(1e-9, c.Gdp), 2)} of GDP a year", 12));

        foreach (var ch in _catalogue.GetChildren().ToList()) ch.QueueFree();
        foreach (var def in PolicyCatalog.Projects)
        {
            var d = def; var dry = CommandProcessor.Apply(Game.World, Command.StartProject(c.Id, d.Id), true);
            var b = PlanUi.Toggle("Start", Command.StartProject(c.Id, d.Id), t => _result.Text = t, true, 130); b.Disabled |= !dry.Ok && Game.Staged("project:" + d.Id) == null; if (!dry.Ok) b.TooltipText = dry.Message;
            var info = UI.VBox(2, UI.Lbl(d.Name, 15, Pal.Text, true), UI.Dim(d.Desc, 12, true),
                UI.Dim($"{d.Asset} +{d.Bonus * 100:0}pp · {UI.Pct(d.Cost, 1)} of GDP over {d.Months / 12.0:0.#} years · upkeep {UI.Pct(d.Maint, 0)}/yr · {dry.PcCost:0} political capital", 12));
            info.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _catalogue.AddChild(UI.Card(UI.HBox(12, info, b), Pal.PanelAlt, 10));
        }

    }

    // sliders are built once: they stage on every change (replace-by-key, so dragging never piles up), and are re-synced from the plan
    void BuildSubsidies()
    {
        var grid = new GridContainer { Columns = 2 }; grid.AddThemeConstantOverride("h_separation", 20); grid.AddThemeConstantOverride("v_separation", 8);
        foreach (Sector s in Enum.GetValues<Sector>())
        {
            if (s == Sector.Public) continue;
            var ss = s; var sl = new AppSlider { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            sl.Setup(0, 0.15, 0.005, 0, v => UI.Pct(v, 1));
            sl.Changed += v => { var r = PlanUi.Stage(Command.SetSubsidy(Game.Player.Id, ss, v), t => _result.Text = t); if (!r.Ok) sl.SetValue(SubNow(Game.Player, ss)); };
            _subSliders[s] = sl;
            grid.AddChild(UI.Lbl(s.ToString(), 14, Pal.Dim)); grid.AddChild(sl);
        }
        _subs.AddChild(grid);
    }

    static double SubNow(CountryState c, Sector s) => Game.Staged("subsidy:" + s)?.Value ?? c.SectorSubsidy[(int)s];

    void SyncSubsidies(CountryState c)
    {
        foreach (var kv in _subSliders) { kv.Value.Baseline = c.SectorSubsidy[(int)kv.Key]; kv.Value.SetValue(SubNow(c, kv.Key)); }
    }

    void Act(Command cmd) { PlanUi.Stage(cmd, t => _result.Text = t); _sig = -1; Refresh(); }
}
