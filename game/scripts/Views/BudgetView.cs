using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.Core.Engine;
using Sim.Core.Model;
using Sim.Core.Policy;
using EconGame.App;
using EconGame.Ui;
using EconGame.Views.BudgetPages;
using static EconGame.Views.BudgetPages.BudgetState;

namespace EconGame.Views;

/// <summary>
/// The Budget: a tabbed editor over the whole tax-and-benefit code. Edits are a draft; "Add to plan" stages them for the end of the turn.
/// The footer always shows the combined effect of the plan and the draft, the political capital it needs, and a 5-year preview.
/// </summary>
public partial class BudgetView : View
{
    public override string Title => "Budget";
    readonly BudgetCtx _x = new();
    readonly VBoxContainer _holder = UI.VBox(12);
    readonly HFlowContainer _tabRow = new();
    readonly Label _tip = UI.Dim("", 13, true);
    readonly List<BudgetPage> _pages = new();
    readonly List<Button> _tabs = new();
    readonly Label _summary = UI.Lbl("", 13, Pal.Text, false, HorizontalAlignment.Left, true);
    readonly Label _cost = UI.Lbl("", 13, Pal.Warn, true);
    readonly Label _result = UI.Lbl("", 13, Pal.Dim, false, HorizontalAlignment.Left, true);
    readonly PreviewPanel _preview = new();
    Button _enact = new();
    int _sel; bool _built, _busy;
    Simulation? _builtFor; string _builtCountry = "";

    public BudgetView()
    {
        var root = UI.VBox(10); root.SizeFlagsVertical = SizeFlags.ExpandFill; root.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        root.AddChild(UI.H1("Budget"));
        root.AddChild(UI.Dim("Change taxes, benefits and departmental budgets, see what they do, then add them to this turn's plan. Changes take effect when you end the turn; they cost political capital, and cuts cost more than increases.", 14, true));
        _tabRow.AddThemeConstantOverride("h_separation", 6); _tabRow.AddThemeConstantOverride("v_separation", 6);
        root.AddChild(_tabRow); root.AddChild(_tip);
        var body = UI.VBox(12, _holder, UI.Card(UI.VBox(8, UI.H2("Preview"), _preview)));
        root.AddChild(UI.Scroll(body));

        _enact = UI.Btn("Add to plan", AddToPlan, true, 150);
        var foot = UI.VBox(6, _summary, _cost, UI.HBox(10, _enact, UI.Btn("Preview 5 years", RunPreview, false, 150), UI.Btn("Reset draft", ResetDraft, false, 120)), _result);
        root.AddChild(UI.Card(foot));
        AddChild(root);
    }

    public override void _EnterTree() { Draft.Changed += OnDraft; Game.PlanChanged += OnDraft; }
    public override void _ExitTree() { Draft.Changed -= OnDraft; Game.PlanChanged -= OnDraft; }

    public override void Refresh()
    {
        if (!Game.Running) return;
        if (!_built || !ReferenceEquals(_builtFor, Game.Sim) || _builtCountry != Game.Player.Id) Build();
        Resync();
    }

    void OnDraft()
    {
        if (_busy || !_built || !Game.Running || !ReferenceEquals(_builtFor, Game.Sim) || _builtCountry != Game.Player.Id) return;
        Resync();
    }

    void Build()
    {
        _built = true; _builtFor = Game.Sim; _builtCountry = Game.Player.Id;
        foreach (var ch in _holder.GetChildren().ToList()) { _holder.RemoveChild(ch); ch.QueueFree(); }
        foreach (var ch in _tabRow.GetChildren().ToList()) { _tabRow.RemoveChild(ch); ch.QueueFree(); }
        _pages.Clear(); _tabs.Clear();
        _x.Recompute();
        _pages.Add(new OverviewPage());
        if (_x.HasCode)
        {
            _pages.Add(new IncomeTaxPage(_x)); _pages.Add(new PayrollPage(_x)); _pages.Add(new CorpPage(_x)); _pages.Add(new VatPage(_x)); _pages.Add(new WelfarePage(_x));
        }
        _pages.Add(new DepartmentsPage());
        _sel = Math.Clamp(_sel, 0, _pages.Count - 1);
        for (int i = 0; i < _pages.Count; i++)
        {
            int ii = i;
            var chip = UI.Chip(_pages[i].PageName, i == _sel, () => Select(ii));
            chip.TooltipText = _pages[i].Tip;
            _tabs.Add(chip); _tabRow.AddChild(chip);
            _pages[i].Visible = i == _sel; _holder.AddChild(_pages[i]);
        }
    }

    /// <summary>Show a tab by name (used by the self-test and by links from other pages).</summary>
    public bool ShowTab(string name)
    {
        int i = _pages.FindIndex(p => p.PageName == name); if (i < 0) return false;
        Select(i); return true;
    }

    /// <summary>The first-order estimate for the plan and draft on the current tab (self-test).</summary>
    public FiscalEstimate Estimate => _x.Est;

    public IReadOnlyList<string> TabNames => _pages.Select(p => p.PageName).ToList();

    void Select(int i)
    {
        _sel = i;
        for (int k = 0; k < _pages.Count; k++) { _pages[k].Visible = k == i; _tabs[k].SetPressedNoSignal(k == i); }
        Resync();
    }

    void Resync()
    {
        if (!_built || !Game.Running) return;
        _x.Recompute();
        _tip.Text = _pages[_sel].Tip;
        _pages[_sel].Sync(_x);
        UpdateSummary();
    }

    public void RunPreview() => _preview.Run(Game.Sim!.Merged(Draft.ToCommands(Game.Player)));

    void ResetDraft() { Draft.Clear(); _result.Text = ""; }

    void AddToPlan()
    {
        var c = Game.Player; var msgs = new List<string>();
        _busy = true;
        foreach (var cmd in Draft.ToCommands(c))
        {
            var r = Game.Stage(cmd);
            msgs.Add(PlanUi.Feedback(r, cmd));
            if (!(r.Ok || r.NoOp)) continue;
            if (cmd.Type == "tax") Draft.Taxes.Remove(Enum.Parse<Tax>(cmd.Id));
            else if (cmd.Type == "budget") Draft.Lines.Remove(Enum.Parse<BudgetLine>(cmd.Id));
            else if (cmd.Id == FiscalParams.BandsKey) Draft.Bands = null;
            else Draft.Fiscal.Remove(cmd.Id);
        }
        _busy = false;
        _result.Text = string.Join("\n", msgs) + (Game.Sim!.Plan.Count > 0 ? "\nNothing changes until you end the turn." : "");
        Resync();
    }

    void UpdateSummary()
    {
        _result.Visible = _result.Text.Length > 0;
        var c = Game.Player; var sim = Game.Sim!; var est = _x.Est;
        var cmds = Draft.ToCommands(c); int staged = sim.Plan.Count;
        // the combined effect of the plan and the draft against today's live settings, before economic feedback
        double gdp = c.Gdp, dRev = 0, dSpend = 0; int nLegacy = 0;
        foreach (Tax t in Enum.GetValues<Tax>())
        {
            double eff = TaxEff(c, t); if (Math.Abs(eff - c.TaxRate[(int)t]) < 1e-9) continue; nLegacy++;
            dRev += (FiscalEngine.TaxRevenueAt(c, t, eff, gdp, c.Cons, c.Imports) - FiscalEngine.TaxRevenueAt(c, t, c.TaxRate[(int)t], gdp, c.Cons, c.Imports)) / gdp;
        }
        foreach (BudgetLine l in Enum.GetValues<BudgetLine>())
        {
            double eff = LineEff(c, l); if (Math.Abs(eff - c.Budget[(int)l]) < 1e-9) continue; nLegacy++;
            dSpend += (eff - c.Budget[(int)l]) * c.Potential / gdp;
        }
        bool code = _x.HasCode && est.Changed;
        if (code) { dRev += est.RevenueGdp; dSpend += est.SpendGdp; }
        _enact.Disabled = cmds.Count == 0;
        if (nLegacy == 0 && !code)
        {
            _summary.Text = staged == 0 ? "No changes yet. Edit a tax or benefit on any tab, then add it to the plan." : $"{staged} action{(staged == 1 ? "" : "s")} in the plan (see the Plan button). Nothing on these tabs differs from today's settings.";
            _cost.Text = ""; return;
        }
        double dDef = dSpend - dRev;
        string draftNote = cmds.Count > 0 ? $"{cmds.Count} unsent draft change{(cmds.Count > 1 ? "s" : "")}" : "";
        string planNote = sim.Plan.Any(q => q.Type is "tax" or "budget" or "fiscal") ? "changes already in the plan" : "";
        string econ = code ? $" Output {(est.DemandGdp * 100).ToString("+0.00;-0.00;0.00")}% in year one, {(est.LongRunGdp * 100).ToString("+0.0;-0.0;0.0")}% in the long run; Gini {(est.GiniDelta * 100).ToString("+0.00;-0.00;0.00")}, poverty {(est.PovertyDelta * 100).ToString("+0.0;-0.0;0.0")}pp, natural unemployment {(est.NairuDelta * 100).ToString("+0.00;-0.00;0.00")}pp." : "";
        _summary.Text = $"{string.Join(" + ", new[] { planNote, draftNote }.Where(x => x != ""))}: revenue {dRev * 100:+0.00;-0.00}pp, spending {dSpend * 100:+0.00;-0.00}pp → deficit {dDef * 100:+0.00;-0.00}pp of GDP before economic feedback.{econ}";
        double draftCost = Draft.PoliticalCost(Game.World, cmds);
        var keys = cmds.Select(Simulation.KeyOf).ToHashSet();
        double others = sim.Plan.Where(q => !keys.Contains(Simulation.KeyOf(q))).Sum(q => CommandProcessor.Apply(Game.World, q, true).PcCost);
        double total = draftCost + others; bool ok = total <= c.PoliticalCapital + 1e-9;
        _cost.Text = $"Political capital: {total:0} needed this turn (plan and draft), {c.PoliticalCapital:0} banked" + (ok ? "" : "  — not enough; trim the draft or the plan");
        _cost.AddThemeColorOverride("font_color", ok ? Pal.Warn : Pal.Bad);
        _enact.Disabled = cmds.Count == 0 || !ok;
    }
}
