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

/// <summary>Employee and employer social contributions: thresholds, rates and the cap.</summary>
public partial class PayrollPage : BudgetPage
{
    public override string PageName => "Payroll";
    public override string Tip => "National insurance and social-security contributions: what workers and employers pay, and where the cap falls.";

    readonly BudgetCtx _x;
    readonly ParamRow _eeFrom, _eeRate, _eeUpper, _eeRate2, _erFrom, _erRate;
    readonly LineChart _chart = new() { CustomMinimumSize = new Vector2(300, 250), XIsMonths = false };
    readonly GridContainer _facts = Facts(4);
    readonly Label _note = UI.Dim("", 13, true);

    public PayrollPage(BudgetCtx x)
    {
        _x = x;
        _eeFrom = x.Row("Pay.EeFrom"); _eeRate = x.Row("Pay.EeRate"); _eeUpper = x.Row("Pay.EeUpper"); _eeRate2 = x.Row("Pay.EeRate2");
        _erFrom = x.Row("Pay.ErFrom"); _erRate = x.Row("Pay.ErRate");
        AddChild(Section("Employee contributions", "Deducted from pay. They fall on workers directly and reduce the reward from working.", _eeFrom, _eeRate, _eeUpper, _eeRate2));
        AddChild(Section("Employer contributions", "Paid on top of wages. They raise the cost of hiring, which pushes up the natural rate of unemployment and, over time, holds back wages.", _erFrom, _erRate));
        AddChild(Section("What it does", null, UI.Lbl("Average contribution rate by earnings", 14, Pal.Text, true), _chart, _note, _facts));
    }

    public override void Sync(BudgetCtx x)
    {
        foreach (var r in new[] { _eeFrom, _eeRate, _eeUpper, _eeRate2, _erFrom, _erRate }) x.Sync(r);
        var c = x.C; var f = x.F; double e = x.Earn;
        var tp = TaxParams.Of(x.Effective, f.Drift); var t0 = TaxParams.Of(f.P, f.Drift);
        int n = 200; double top = 12;
        var X = new double[n]; var ee = new double[n]; var er = new double[n]; var ee0 = new double[n]; var er0 = new double[n];
        for (int i = 0; i < n; i++)
        {
            double m = top * (i + 1) / n; X[i] = m * e;
            ee[i] = tp.Employee(m) / m; er[i] = tp.Employer(m) / m; ee0[i] = t0.Employee(m) / m; er0[i] = t0.Employer(m) / m;
        }
        bool changed = !FiscalDraft.Same(f.P, x.Effective);
        var s = new List<Series>
        {
            new() { Name = "Employee (share of pay)", X = X, Y = ee, Color = Pal.Series[0] },
            new() { Name = "Employer (share of pay)", X = X, Y = er, Color = Pal.Series[1] },
        };
        if (changed) { s.Add(new Series { Name = "Employee today", X = X, Y = ee0, Color = Pal.Faint, Dashed = true }); s.Add(new Series { Name = "Employer today", X = X, Y = er0, Color = Pal.Faint, Dashed = true }); }
        _chart.YMin = 0; _chart.YFormat = v => Fmt.P(v, 0); _chart.XFormat = v => Compact(c.Currency, v); _chart.SetSeries(s);

        bool capped = x.Effective.GetValueOrDefault("Pay.EeUpper") > 0;
        _note.Text = capped ? "Above the upper limit employees pay the second rate; below it the first. Cutting the second rate to zero makes the top earners' contributions a flat sum."
                            : "No upper limit: employee contributions are proportional on every pound above the threshold. Setting a limit makes the system regressive above it unless the second rate is raised.";
        var est = x.Est;
        foreach (var ch in _facts.GetChildren().ToList()) { _facts.RemoveChild(ch); ch.QueueFree(); }
        Fact(_facts, "Contributions raised", Pp(est.TaxGdp[3]) + " of GDP vs now", Tone(est.TaxGdp[3]));
        Fact(_facts, "Average (employee + employer)", Fmt.P(est.New.AvgPay, 1) + " of pay");
        Fact(_facts, "Natural unemployment", (est.NairuDelta * 100).ToString("+0.00;-0.00;0.00") + "pp", Tone(est.NairuDelta, false), "Higher employer costs and lower take-home pay both raise the unemployment rate the economy settles at.");
        Fact(_facts, "Labour supply", (est.LabourDelta * 100).ToString("+0.0;-0.0;0.0") + "%", Tone(est.LabourDelta));
    }
}
