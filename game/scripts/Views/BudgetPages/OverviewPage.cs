using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.Core.Engine;
using Sim.Core.Model;
using EconGame.App;
using EconGame.Ui;
using static EconGame.Views.BudgetPages.BudgetState;

namespace EconGame.Views.BudgetPages;

/// <summary>The public finances at a glance, with one slider per tax for quick, broad-brush changes.</summary>
public partial class OverviewPage : BudgetPage
{
    public override string PageName => "Overview";
    public override string Tip => "Revenue and spending at a glance, and one slider per tax. Use the other tabs to change how each tax or benefit is built.";
    readonly HBoxContainer _tiles = new();
    readonly StackBar _rev = new(), _spend = new();
    readonly Dictionary<Tax, (AppSlider s, Label info)> _taxRows = new();
    static readonly Dictionary<Tax, string> TaxHelp = new()
    {
        [Tax.Income] = "Raises revenue but cuts disposable income, consumption and approval.",
        [Tax.Corporate] = "Higher rates deter investment and FDI; lower rates cost revenue.",
        [Tax.Consumption] = "Broad base and hard to avoid; lifts prices once and hits poorer households.",
        [Tax.Payroll] = "Raises the natural rate of unemployment as it rises.",
        [Tax.Tariff] = "Protects domestic industry and raises revenue; invites retaliation and cuts trade.",
    };

    public OverviewPage()
    {
        _tiles.AddThemeConstantOverride("separation", 10);
        AddChild(_tiles);
        AddChild(UI.Card(UI.VBox(10, _rev, _spend)));

        var c = Game.Player;
        var taxBox = UI.VBox(10);
        taxBox.AddChild(UI.H2("Quick control: effective tax rates"));
        taxBox.AddChild(UI.Dim("One slider per tax moves its average rate across the whole base, on top of the structure set in the detailed tabs. Marker shows today's setting.", 13, true));
        foreach (Tax t in Enum.GetValues<Tax>())
        {
            var tt = t;
            var s = new AppSlider(); s.Setup(0, Math.Min(0.9, Math.Max(0.12, c.TaxRate0[(int)t] * 2.2)), 0.0025, c.TaxRate[(int)t], v => UI.Pct(v, 1));
            s.Changed += v => Draft.Set(tt, v, TaxNow(Game.Player, tt));
            var info = UI.Dim("", 12, true);
            _taxRows[t] = (s, info);
            var box = UI.VBox(2, UI.Lbl(TaxNames[t], 14, Pal.Text, true), s, info);
            box.TooltipText = TaxHelp[t];
            taxBox.AddChild(box);
        }
        AddChild(UI.Card(taxBox));
    }

    public override void Sync(BudgetCtx ctx)
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

        double gdp = c.Gdp;
        var taxSegs = new List<Seg>(); int i = 0;
        foreach (Tax t in Enum.GetValues<Tax>())
        {
            double rate = TaxEff(c, t);
            double rev = FiscalEngine.TaxRevenueAt(c, t, rate, gdp, c.Cons, c.Imports) / gdp;
            double rev0 = FiscalEngine.TaxRevenueAt(c, t, c.TaxRate[(int)t], gdp, c.Cons, c.Imports) / gdp;
            taxSegs.Add(new Seg { Label = TaxNames[t], Value = rev, Color = Pal.Series[i % 8] }); i++;
            if (_taxRows.TryGetValue(t, out var row))
            {
                row.s.Baseline = c.TaxRate[(int)t]; row.s.SetExact(TaxEff(c, t));
                row.info.Text = $"raises {UI.Pct(rev, 1)} of GDP" + (Math.Abs(rev - rev0) > 1e-5 ? $"  ({(rev >= rev0 ? "+" : "")}{(rev - rev0) * 100:0.00}pp vs now)" : "")
                                + (Draft.Taxes.ContainsKey(t) ? "  · draft" : StagedTax(t) != null ? "  · in the plan" : "");
            }
        }
        double other = c.OtherRevShare + c.Mod("revenue") + c.ResourceRev0Share * Game.World.Global.OilIdx;
        if (other > 0.001) taxSegs.Add(new Seg { Label = "Resource & other", Value = other, Color = Pal.Series[7] });
        _rev.Format = v => UI.Pct(v, 1); _rev.Set(taxSegs, $"Revenue — {UI.Pct(taxSegs.Sum(s => s.Value), 1)} of GDP");

        var lineSegs = new List<Seg>(); i = 0;
        foreach (BudgetLine l in Enum.GetValues<BudgetLine>())
        {
            double share = LineEff(c, l);
            double mult = l == BudgetLine.Social ? Math.Pow(c.Old / Math.Max(1e-6, c.Old0), 0.4) * TaxCodeEngine.BenefitMix(c) : 1;
            lineSegs.Add(new Seg { Label = LineNames[l], Value = share * mult * c.Potential / gdp, Color = Pal.Series[i % 8] }); i++;
        }
        lineSegs.Add(new Seg { Label = "Debt interest", Value = c.Interest / Math.Max(1e-9, c.GdpNominal), Color = Pal.Bad });
        _spend.Format = v => UI.Pct(v, 1); _spend.Set(lineSegs, $"Spending — {UI.Pct(lineSegs.Sum(s => s.Value), 1)} of GDP");
        _rev.Total = Math.Max(taxSegs.Sum(s => s.Value), lineSegs.Sum(s => s.Value)); _spend.Total = _rev.Total;
    }
}
