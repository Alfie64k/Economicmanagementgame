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

public partial class BudgetView : View
{
    public override string Title => "Budget";
    readonly VBoxContainer _page;
    readonly Dictionary<Tax, (AppSlider s, Label info)> _taxRows = new();
    readonly Dictionary<BudgetLine, (AppSlider s, Label info)> _lineRows = new();
    readonly StackBar _rev = new(), _spend = new();
    readonly Label _summary = UI.Lbl("", 14, Pal.Text, false, HorizontalAlignment.Left, true);
    readonly Label _cost = UI.Lbl("", 14, Pal.Warn, true);
    readonly Label _result = UI.Lbl("", 13, Pal.Dim, false, HorizontalAlignment.Left, true);
    readonly PreviewPanel _preview = new();
    Button _enact = new();
    bool _built; int _builtMonth = -1;
    readonly HBoxContainer _tiles = new();

    static readonly Dictionary<Tax, string> TaxNames = new()
    {
        [Tax.Income] = "Income tax", [Tax.Corporate] = "Corporation tax", [Tax.Consumption] = "VAT / consumption tax", [Tax.Payroll] = "Payroll & social contributions", [Tax.Tariff] = "Import tariffs",
    };
    static readonly Dictionary<BudgetLine, string> LineNames = new()
    {
        [BudgetLine.Social] = "Pensions & welfare", [BudgetLine.Health] = "Health services", [BudgetLine.Education] = "Education", [BudgetLine.Defence] = "Defence",
        [BudgetLine.Infrastructure] = "Infrastructure", [BudgetLine.RnD] = "Research & development", [BudgetLine.Green] = "Energy transition", [BudgetLine.Housing] = "Housing",
        [BudgetLine.Digital] = "Digital & broadband", [BudgetLine.Admin] = "Administration & other",
    };
    static readonly Dictionary<Tax, string> TaxHelp = new()
    {
        [Tax.Income] = "Raises revenue but cuts disposable income, consumption and approval.",
        [Tax.Corporate] = "Higher rates deter investment and FDI; lower rates cost revenue.",
        [Tax.Consumption] = "Broad base and hard to avoid; lifts prices once and hits poorer households.",
        [Tax.Payroll] = "Raises the natural rate of unemployment as it rises.",
        [Tax.Tariff] = "Protects domestic industry and raises revenue; invites retaliation and cuts trade.",
    };
    static readonly Dictionary<BudgetLine, string> LineHelp = new()
    {
        [BudgetLine.Social] = "Transfers support demand and cut inequality; ageing pushes costs up automatically.",
        [BudgetLine.Health] = "Builds health capital: productivity, life expectancy and approval (slow).",
        [BudgetLine.Education] = "Builds human capital: productivity and lower inequality (very slow).",
        [BudgetLine.Defence] = "Security and stability; little direct growth effect.",
        [BudgetLine.Infrastructure] = "Lifts productivity and FDI appeal; strong demand effect while spending.",
        [BudgetLine.RnD] = "Drives TFP growth, most valuable in already-productive economies.",
        [BudgetLine.Green] = "Accelerates the energy transition and cuts emissions.",
        [BudgetLine.Housing] = "Affordable housing improves approval and eases inequality.",
        [BudgetLine.Digital] = "Boosts services and finance productivity.",
        [BudgetLine.Admin] = "Courts, police, civil service. Cuts raise cash but erode state capacity over time.",
    };

    public BudgetView()
    {
        _page = Page("Budget", "Set tax rates and departmental budgets, preview the consequences, then enact them. Changes cost political capital; cuts cost more than increases.");
        Draft.Changed += OnDraft;
    }

    public override void _ExitTree() { Draft.Changed -= OnDraft; }

    public override void Refresh()
    {
        if (!Game.Running) return;
        var c = Game.Player;
        if (!_built || _builtMonth != Game.World.Month / 12 * 100 + (Draft.Any ? 1 : 0) && !Draft.Any) Build();
        UpdateTiles(); UpdateBars(); UpdateSummary();
    }

    void OnDraft() { if (_built) { UpdateBars(); UpdateSummary(); } }

    void Build()
    {
        _built = true; _builtMonth = Game.World.Month / 12 * 100;
        foreach (var ch in _page.GetChildren().Skip(2).ToList()) ch.QueueFree();
        _taxRows.Clear(); _lineRows.Clear();
        var c = Game.Player;

        _tiles.AddThemeConstantOverride("separation", 10); _tiles.GetChildren().ToList().ForEach(n => n.QueueFree());
        _page.AddChild(_tiles);

        var bars = UI.VBox(10, _rev, _spend);
        _page.AddChild(UI.Card(bars));

        var cols = UI.HBox(14); cols.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var taxBox = UI.VBox(10); taxBox.AddChild(UI.H2("Revenue: tax rates")); taxBox.AddChild(UI.Dim("Effective rates on each tax base. Marker shows today's setting.", 13, true));
        foreach (Tax t in Enum.GetValues<Tax>())
        {
            var tt = t; double r0 = c.TaxRate[(int)t];
            var s = new AppSlider(); s.Setup(0, Math.Min(0.9, Math.Max(0.12, c.TaxRate0[(int)t] * 2.2)), 0.0025, r0, v => UI.Pct(v, 1));
            s.Changed += v => Draft.Set(tt, v, c.TaxRate[(int)tt]);
            var info = UI.Dim("", 12, true);
            _taxRows[t] = (s, info);
            var box = UI.VBox(2, UI.HBox(8, UI.Lbl(TaxNames[t], 14, Pal.Text, true)), s, info);
            box.TooltipText = TaxHelp[t];
            taxBox.AddChild(box);
        }
        cols.AddChild(UI.Fill(UI.Card(taxBox), true, false));

        var spendBox = UI.VBox(10); spendBox.AddChild(UI.H2("Spending: departmental budgets")); spendBox.AddChild(UI.Dim("Share of potential GDP. Assets (infrastructure, education…) respond slowly to sustained changes.", 13, true));
        foreach (BudgetLine l in Enum.GetValues<BudgetLine>())
        {
            var ll = l; double s0 = c.Budget[(int)l];
            var s = new AppSlider(); s.Setup(0, Math.Min(0.6, Math.Max(0.05, c.Budget0[(int)l] * 3 + 0.02)), 0.0005, s0, v => UI.Pct(v, 2));
            s.Changed += v => Draft.Set(ll, v, c.Budget[(int)ll]);
            var info = UI.Dim("", 12, true);
            _lineRows[l] = (s, info);
            var box = UI.VBox(2, UI.Lbl(LineNames[l], 14, Pal.Text, true), s, info);
            box.TooltipText = LineHelp[l];
            spendBox.AddChild(box);
        }
        cols.AddChild(UI.Fill(UI.Card(spendBox), true, false));
        _page.AddChild(cols);

        var act = UI.VBox(8);
        act.AddChild(UI.H2("Your draft"));
        act.AddChild(_summary); act.AddChild(_cost);
        _enact = UI.Btn("Enact changes", Enact, true, 160);
        act.AddChild(UI.HBox(10, _enact, UI.Btn("Preview 5 years", () => _preview.Run(Draft.ToCommands(Game.Player)), false, 160), UI.Btn("Reset draft", ResetDraft, false, 130)));
        act.AddChild(_result);
        act.AddChild(_preview);
        _page.AddChild(UI.Card(act));
    }

    public void RunPreview() => _preview.Run(Draft.ToCommands(Game.Player));

    void ResetDraft()
    {
        Draft.Clear(); _built = false; Refresh();
    }

    void Enact()
    {
        var c = Game.Player; var cmds = Draft.ToCommands(c);
        var msgs = new List<string>();
        foreach (var cmd in cmds) { var r = Game.Sim!.Execute(cmd); msgs.Add((r.Ok ? "✔ " : "✘ ") + r.Message); }
        Draft.Clear(); _built = false;
        Game.NotifyChanged();
        Refresh();
        _result.Text = string.Join("\n", msgs);
    }

    void UpdateTiles()
    {
        var c = Game.Player; double y = Math.Max(1e-9, c.GdpNominal);
        foreach (var ch in _tiles.GetChildren().ToList()) ch.QueueFree();
        void Tile(string t, string v, string sub, Color? col = null)
        {
            var k = new KpiTile(t) { SizeFlagsHorizontal = SizeFlags.ExpandFill }; k.Set(v, sub, col, Array.Empty<double>()); _tiles.AddChild(k);
        }
        Tile("Revenue", UI.Pct(c.Revenue / y), Money.Local(c, c.Revenue));
        Tile("Spending", UI.Pct(c.Spending / y), Money.Local(c, c.Spending));
        Tile("Deficit", UI.Pct(c.DeficitToGdp), "primary " + UI.Pct(-c.PrimaryBalance / y), c.DeficitToGdp > 0.05 ? Pal.Bad : Pal.Dim);
        Tile("Interest bill", UI.Pct(c.Interest / y), UI.Pct(c.Revenue > 0 ? c.Interest / c.Revenue : 0, 0) + " of revenue", c.Interest / Math.Max(1, c.Revenue) > 0.15 ? Pal.Bad : Pal.Dim);
        Tile("Public debt", UI.Pct(c.DebtToGdp, 0), Money.Local(c, c.Debt), c.DebtToGdp > Math.Max(0.6, c.DebtGdp0 + 0.15) ? Pal.Bad : Pal.Dim);
    }

    void UpdateBars()
    {
        var c = Game.Player; double gdp = c.Gdp, P = c.PriceLevel;
        var taxSegs = new List<Seg>();
        int i = 0;
        foreach (Tax t in Enum.GetValues<Tax>())
        {
            double rate = Draft.Taxes.TryGetValue(t, out var d) ? d : c.TaxRate[(int)t];
            double rev = FiscalEngine.TaxRevenueAt(c, t, rate, gdp, c.Cons, c.Imports) / gdp;
            double rev0 = FiscalEngine.TaxRevenueAt(c, t, c.TaxRate[(int)t], gdp, c.Cons, c.Imports) / gdp;
            taxSegs.Add(new Seg { Label = TaxNames[t], Value = rev, Color = Pal.Series[i % 8] }); i++;
            if (_taxRows.TryGetValue(t, out var row))
                row.info.Text = $"raises {UI.Pct(rev, 1)} of GDP" + (Math.Abs(rev - rev0) > 1e-5 ? $"  ({(rev >= rev0 ? "+" : "")}{(rev - rev0) * 100:0.00}pp vs now)" : "");
        }
        double other = c.OtherRevShare + c.Mod("revenue") + c.ResourceRev0Share * Game.World.Global.OilIdx;
        if (other > 0.001) taxSegs.Add(new Seg { Label = "Resource & other", Value = other, Color = Pal.Series[7] });
        _rev.Format = v => UI.Pct(v, 1); _rev.Set(taxSegs, $"Revenue — {UI.Pct(taxSegs.Sum(s => s.Value), 1)} of GDP");

        var lineSegs = new List<Seg>(); i = 0;
        foreach (BudgetLine l in Enum.GetValues<BudgetLine>())
        {
            double share = Draft.Lines.TryGetValue(l, out var d) ? d : c.Budget[(int)l];
            double mult = l == BudgetLine.Social ? Math.Pow(c.Old / Math.Max(1e-6, c.Old0), 0.4) : 1;
            lineSegs.Add(new Seg { Label = LineNames[l], Value = share * mult * c.Potential / gdp, Color = Pal.Series[i % 8] }); i++;
            if (_lineRows.TryGetValue(l, out var row))
            {
                double now = c.Budget[(int)l] * c.Potential * P;
                row.info.Text = $"{Money.Local(c, share * c.Potential * P)} a year" + (Math.Abs(share - c.Budget[(int)l]) > 1e-6 ? $"  ({(share >= c.Budget[(int)l] ? "+" : "")}{(share - c.Budget[(int)l]) * 100:0.00}pp of GDP)" : "") + (l == BudgetLine.Social ? "  · rises with ageing" : "");
            }
        }
        lineSegs.Add(new Seg { Label = "Debt interest", Value = c.Interest / Math.Max(1e-9, c.GdpNominal), Color = Pal.Bad });
        _spend.Format = v => UI.Pct(v, 1); _spend.Set(lineSegs, $"Spending — {UI.Pct(lineSegs.Sum(s => s.Value), 1)} of GDP");
        _rev.Total = Math.Max(taxSegs.Sum(s => s.Value), lineSegs.Sum(s => s.Value)); _spend.Total = _rev.Total;
    }

    void UpdateSummary()
    {
        var c = Game.Player;
        var cmds = Draft.ToCommands(c);
        if (cmds.Count == 0) { _summary.Text = "No changes drafted. Move a slider to experiment."; _cost.Text = ""; _enact.Disabled = true; return; }
        double gdp = c.Gdp;
        double dRev = 0, dSpend = 0;
        foreach (var kv in Draft.Taxes) dRev += (FiscalEngine.TaxRevenueAt(c, kv.Key, kv.Value, gdp, c.Cons, c.Imports) - FiscalEngine.TaxRevenueAt(c, kv.Key, c.TaxRate[(int)kv.Key], gdp, c.Cons, c.Imports)) / gdp;
        foreach (var kv in Draft.Lines) dSpend += (kv.Value - c.Budget[(int)kv.Key]) * c.Potential / gdp;
        double dDef = dSpend - dRev;
        _summary.Text = $"{cmds.Count} change{(cmds.Count > 1 ? "s" : "")}: revenue {dRev * 100:+0.00;-0.00}pp, spending {dSpend * 100:+0.00;-0.00}pp → deficit {dDef * 100:+0.00;-0.00}pp of GDP before economic feedback.";
        double pc = Draft.PoliticalCost(Game.World, cmds);
        bool ok = pc <= c.PoliticalCapital;
        _cost.Text = $"Political capital: {pc:0} needed, {c.PoliticalCapital:0} available" + (ok ? "" : "  — not enough; trim the draft");
        _cost.AddThemeColorOverride("font_color", ok ? Pal.Warn : Pal.Bad);
        _enact.Disabled = !ok;
    }
}
