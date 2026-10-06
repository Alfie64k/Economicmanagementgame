using System;
using System.Collections.Generic;
using Godot;
using Sim.Core.Model;
using EconGame.App;
using EconGame.Ui;
using static EconGame.Views.BudgetPages.BudgetState;

namespace EconGame.Views.BudgetPages;

/// <summary>Departmental budgets as a share of potential GDP.</summary>
public partial class DepartmentsPage : BudgetPage
{
    public override string PageName => "Departments";
    public override string Tip => "Spending by department, as a share of potential GDP. Assets such as infrastructure and education respond slowly to sustained changes.";
    readonly Dictionary<BudgetLine, (AppSlider s, Label info)> _rows = new();
    static readonly Dictionary<BudgetLine, string> LineHelp = new()
    {
        [BudgetLine.Social] = "The total for every benefit and pension. Use the Pensions & welfare tab to change individual benefits; ageing pushes this up automatically.",
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

    public DepartmentsPage()
    {
        var c = Game.Player;
        var box = UI.VBox(10);
        box.AddChild(UI.H2("Spending: departmental budgets"));
        box.AddChild(UI.Dim("Share of potential GDP. Marker shows today's setting; cuts cost more political capital than increases.", 13, true));
        foreach (BudgetLine l in Enum.GetValues<BudgetLine>())
        {
            var ll = l;
            var s = new AppSlider(); s.Setup(0, Math.Min(0.6, Math.Max(0.05, c.Budget0[(int)l] * 3 + 0.02)), 0.0005, c.Budget[(int)l], v => UI.Pct(v, 2));
            s.Changed += v => Draft.Set(ll, v, LineNow(Game.Player, ll));
            var info = UI.Dim("", 12, true);
            _rows[l] = (s, info);
            var b = UI.VBox(2, UI.Lbl(LineNames[l], 14, Pal.Text, true), s, info);
            b.TooltipText = LineHelp[l];
            box.AddChild(b);
        }
        AddChild(UI.Card(box));
    }

    public override void Sync(BudgetCtx ctx)
    {
        var c = Game.Player; double P = c.PriceLevel;
        foreach (var kv in _rows)
        {
            var l = kv.Key; var (s, info) = kv.Value;
            s.Baseline = c.Budget[(int)l]; s.SetExact(LineEff(c, l));
            double share = LineEff(c, l);
            info.Text = $"{Money.Local(c, share * c.Potential * P)} a year" + (Math.Abs(share - c.Budget[(int)l]) > 1e-6 ? $"  ({(share >= c.Budget[(int)l] ? "+" : "")}{(share - c.Budget[(int)l]) * 100:0.00}pp of GDP)" : "")
                        + (l == BudgetLine.Social ? "  · rises with ageing" : "") + (Draft.Lines.ContainsKey(l) ? "  · draft" : StagedLine(l) != null ? "  · in the plan" : "");
        }
    }
}
