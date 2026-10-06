using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.Core.Engine;
using Sim.Core.Policy;
using Sim.Core.Util;
using EconGame.App;
using EconGame.Ui;

namespace EconGame.Views.BudgetPages;

/// <summary>VAT or sales tax: standard and reduced rates and how each category of spending is treated.</summary>
public partial class VatPage : BudgetPage
{
    public override string PageName => "VAT";
    public override string Tip => "The standard and reduced rates, and which kinds of spending are zero-rated, reduced, standard or exempt.";

    readonly BudgetCtx _x;
    readonly ParamRow _std, _red;
    readonly ParamRow[] _cat = new ParamRow[7];
    readonly BarList _inc = new() { LabelWidth = 190, Format = v => Fmt.P(v, 1) };
    readonly GridContainer _facts = Facts(4);
    readonly Label _note = UI.Dim("", 13, true);

    public VatPage(BudgetCtx x)
    {
        _x = x;
        _std = x.Row("Vat.Std"); _red = x.Row("Vat.Red");
        AddChild(Section("Rates", "Where the country has no VAT, this is the average sales or consumption tax on goods and services.", _std, _red));
        var cats = new GridContainer { Columns = 2 }; cats.AddThemeConstantOverride("h_separation", 24); cats.AddThemeConstantOverride("v_separation", 10);
        for (int i = 0; i < 7; i++) { _cat[i] = x.Row(FiscalParams.VatKey(FiscalParams.VatCategories[i])); _cat[i].SizeFlagsHorizontal = SizeFlags.ExpandFill; cats.AddChild(_cat[i]); }
        AddChild(Section("Treatment by category", "Zero-rating food and fuel protects poorer households, who spend more of their income on them, at a heavy cost in revenue. Exempt goods cannot reclaim the tax their suppliers paid.", cats));
        AddChild(Section("Who pays", null, UI.Lbl("VAT paid as a share of income", 14, Pal.Text, true), _inc, _note, _facts));
    }

    public override void Sync(BudgetCtx x)
    {
        x.Sync(_std); x.Sync(_red);
        var f = x.F; var grid = IncomeGrid.For(f.Sigma); double totC = grid.Cons.Sum();
        for (int i = 0; i < 7; i++)
        {
            x.Sync(_cat[i]);
            double share = Enumerable.Range(0, IncomeGrid.N).Sum(j => grid.Cons[j] * grid.VatW[i][j]) / Math.Max(1e-9, totC);
            _cat[i].Hint = $"About {Fmt.P(share, 0)} of household spending falls in this category.";
        }
        var est = x.Est;
        double Share(double[] vat, int a, int b) { double v = 0, y = 0; for (int j = a; j < b; j++) { v += vat[j]; y += grid.M[j]; } return y > 0 ? v / y : 0; }
        int n = IncomeGrid.N / 10;
        _inc.Set(new[]
        {
            new BarItem { Label = "Poorest tenth, draft", Value = Share(est.New.Vat, 0, n), Color = Pal.Series[0] },
            new BarItem { Label = "Poorest tenth, today", Value = Share(est.Cur.Vat, 0, n), Color = Pal.Faint },
            new BarItem { Label = "Middle, draft", Value = Share(est.New.Vat, 4 * n, 6 * n), Color = Pal.Series[1] },
            new BarItem { Label = "Middle, today", Value = Share(est.Cur.Vat, 4 * n, 6 * n), Color = Pal.Faint },
            new BarItem { Label = "Richest tenth, draft", Value = Share(est.New.Vat, 9 * n, 10 * n), Color = Pal.Series[2] },
            new BarItem { Label = "Richest tenth, today", Value = Share(est.Cur.Vat, 9 * n, 10 * n), Color = Pal.Faint },
        });
        double lo = Share(est.New.Vat, 0, n), hi = Share(est.New.Vat, 9 * n, 10 * n);
        _note.Text = lo > hi ? "Regressive: the poorest tenth pay more of their income in VAT than the richest, because they spend almost all of what they earn." : "Progressive after the draft: the richest tenth pay a larger share of income in VAT.";
        foreach (var ch in _facts.GetChildren().ToList()) { _facts.RemoveChild(ch); ch.QueueFree(); }
        Fact(_facts, "Effective VAT rate", $"{Fmt.P(est.New.VatEff, 1)} (now {Fmt.P(est.Cur.VatEff, 1)})", null, "Revenue as a share of all household spending, after exemptions and reduced rates.");
        Fact(_facts, "Revenue vs now", Pp(est.TaxGdp[2]) + " of GDP", Tone(est.TaxGdp[2]));
        Fact(_facts, "Poverty", (est.PovertyDelta * 100).ToString("+0.0;-0.0;0.0") + "pp", Tone(est.PovertyDelta, false));
        Fact(_facts, "Prices", "one-off", null, "Raising the rate lifts consumer prices once; the Monetary page shows the inflation effect.");
    }
}
