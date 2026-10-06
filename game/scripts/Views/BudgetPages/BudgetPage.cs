using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.Core.Engine;
using Sim.Core.Model;
using Sim.Core.Policy;
using EconGame.App;
using EconGame.Ui;

namespace EconGame.Views.BudgetPages;

/// <summary>
/// What every Budget page needs to know: the tax-and-benefit code in force, the code as it would stand after the staged plan and the unsent draft,
/// and the first-order estimate of that difference. Recomputed whenever the draft, the plan or the month changes.
/// </summary>
public sealed class BudgetCtx
{
    public CountryState C = null!;
    public FiscalCode F = null!;
    /// <summary>Live code with the staged plan applied: the baseline a draft edit is compared with.</summary>
    public Dictionary<string, double> AfterPlan = new();
    /// <summary>Live code with the staged plan and the draft applied.</summary>
    public Dictionary<string, double> Effective = new();
    public FiscalEstimate Est = new();

    public bool HasCode => F != null;

    public void Recompute()
    {
        C = Game.Player; F = C.Fiscal!;
        if (F == null) { AfterPlan = new(); Effective = new(); Est = new(); return; }
        var staged = Game.Sim!.Plan;
        AfterPlan = FiscalDraft.Apply(C, staged.Where(q => q.Type == "fiscal"));
        Effective = Draft.EffectiveCode(C, staged);
        Est = FiscalDraft.Evaluate(C, Effective);
    }

    /// <summary>Nominal mean earnings now, in local currency (one "mean earnings" unit on the charts).</summary>
    public double Earn => FiscalParams.NominalFactor(C, "earn");

    public double Live(string key) => F.Get(key);
    public double Eff(string key) => Effective.TryGetValue(key, out var v) ? v : F.Get(key);
    public string Tag(string key) =>
        Draft.Fiscal.ContainsKey(key) ? "draft" : Game.Staged("fiscal:" + key) != null ? "in the plan" : "";

    /// <summary>The player changed a parameter in the editor.</summary>
    public void Edit(string key, double v)
    {
        double cur = AfterPlan.TryGetValue(key, out var a) ? a : F.Get(key);
        Draft.SetFiscal(key, v, cur);
    }

    public void Undo(string key) { Draft.ClearFiscal(key); Game.Unstage("fiscal:" + key); }

    public ParamRow Row(string key, string? label = null)
    {
        var def = FiscalParams.Def(key) ?? throw new ArgumentException(key);
        var r = new ParamRow(def, label);
        r.Edited += v => Edit(key, v);
        r.Undone += () => Undo(key);
        return r;
    }

    public void Sync(ParamRow r) => r.Sync(C, Eff(r.Def.Key), Live(r.Def.Key), Tag(r.Def.Key));

    /// <summary>Amount in local currency shown as a share of mean earnings is meaningless to players; this gives a plain nominal figure now.</summary>
    public string Nominal(string key, double v) => FiscalParams.Show(C, key, v);
}

/// <summary>A tab of the Budget page.</summary>
public abstract partial class BudgetPage : VBoxContainer
{
    public abstract string PageName { get; }
    public virtual string Tip => "";
    protected BudgetPage() { AddThemeConstantOverride("separation", 12); SizeFlagsHorizontal = SizeFlags.ExpandFill; }
    /// <summary>Update every control from the context. Must not rebuild controls that may have keyboard focus.</summary>
    public abstract void Sync(BudgetCtx ctx);

    protected static PanelContainer Section(string title, string? sub, params Control[] body)
    {
        var box = UI.VBox(10);
        box.AddChild(UI.H2(title));
        if (sub != null) box.AddChild(UI.Dim(sub, 13, true));
        foreach (var b in body) box.AddChild(b);
        return UI.Card(box);
    }

    protected static string Pp(double v, int d = 2) => (v * 100).ToString((v >= 0 ? "+" : "") + "0." + new string('0', d) + ";-0." + new string('0', d)) + "pp";
    /// <summary>Compact money for chart axes: £20k, £1.2m.</summary>
    protected static string Compact(string currency, double v)
    {
        string sym = Sim.Core.Util.Fmt.Symbol(currency);
        double a = Math.Abs(v);
        return a >= 1e6 ? sym + (v / 1e6).ToString("0.#") + "m" : a >= 1e3 ? sym + (v / 1e3).ToString("0") + "k" : sym + v.ToString("0");
    }

    /// <summary>Green or red by sign, neutral when the change is negligible.</summary>
    protected static Color Tone(double v, bool upIsGood = true, double eps = 5e-5) =>
        Math.Abs(v) < eps ? Pal.Dim : (v > 0) == upIsGood ? Pal.Good : Pal.Bad;

    protected static string Amt(CountryState c, double v) => EconGame.App.Money.Amount(c.Currency, v);

    /// <summary>A multi-column read-out of label/value pairs (the estimate lines under a page).</summary>
    protected static GridContainer Facts(int columns = 4)
    {
        var g = new GridContainer { Columns = columns };
        g.AddThemeConstantOverride("h_separation", 18); g.AddThemeConstantOverride("v_separation", 4);
        return g;
    }

    /// <summary>A grid with a header row, for the "what you keep at each income" tables.</summary>
    protected static GridContainer Table(params string[] headers)
    {
        var g = new GridContainer { Columns = headers.Length };
        g.AddThemeConstantOverride("h_separation", 22); g.AddThemeConstantOverride("v_separation", 5);
        foreach (var h in headers) g.AddChild(UI.Lbl(h, 12, Pal.Faint, true));
        return g;
    }

    protected static void Row(GridContainer g, params (string Text, Color? Col)[] cells)
    {
        for (int i = 0; i < cells.Length; i++) g.AddChild(UI.Lbl(cells[i].Text, 13, cells[i].Col ?? (i == 0 ? Pal.Dim : Pal.Text), i == 0 ? false : false));
    }

    /// <summary>Remove the data rows of a <see cref="Table"/>, keeping its header.</summary>
    protected static void ClearRows(GridContainer g)
    {
        foreach (var ch in g.GetChildren().Skip(g.Columns).ToList()) { g.RemoveChild(ch); ch.QueueFree(); }
    }

    protected static void Fact(GridContainer g, string label, string value, Color? col = null, string? tip = null)
    {
        var l = UI.Dim(label, 13); var v = UI.Lbl(value, 13, col ?? Pal.Text, true);
        if (tip != null) { l.MouseFilter = Control.MouseFilterEnum.Stop; l.TooltipText = tip; v.MouseFilter = Control.MouseFilterEnum.Stop; v.TooltipText = tip; }
        g.AddChild(l); g.AddChild(v);
    }
}

/// <summary>What the scalar tax and budget sliders show: the unsent draft, else what is already staged, else the live setting.</summary>
public static class BudgetState
{
    public static double? StagedTax(Tax t) => Game.Staged("tax:" + t)?.Value;
    public static double? StagedLine(BudgetLine l) => Game.Staged("budget:" + l)?.Value;
    public static double TaxNow(CountryState c, Tax t) => StagedTax(t) ?? c.TaxRate[(int)t];
    public static double LineNow(CountryState c, BudgetLine l) => StagedLine(l) ?? c.Budget[(int)l];
    public static double TaxEff(CountryState c, Tax t) => Draft.Taxes.TryGetValue(t, out var d) ? d : TaxNow(c, t);
    public static double LineEff(CountryState c, BudgetLine l) => Draft.Lines.TryGetValue(l, out var d) ? d : LineNow(c, l);

    public static readonly Dictionary<Tax, string> TaxNames = new()
    {
        [Tax.Income] = "Income tax", [Tax.Corporate] = "Corporation tax", [Tax.Consumption] = "VAT / consumption tax", [Tax.Payroll] = "Payroll & social contributions", [Tax.Tariff] = "Import tariffs",
    };
    public static readonly Dictionary<BudgetLine, string> LineNames = new()
    {
        [BudgetLine.Social] = "Pensions & welfare", [BudgetLine.Health] = "Health services", [BudgetLine.Education] = "Education", [BudgetLine.Defence] = "Defence",
        [BudgetLine.Infrastructure] = "Infrastructure", [BudgetLine.RnD] = "Research & development", [BudgetLine.Green] = "Energy transition", [BudgetLine.Housing] = "Housing",
        [BudgetLine.Digital] = "Digital & broadband", [BudgetLine.Admin] = "Administration & other",
    };
}
