using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.Core.Engine;
using Sim.Core.Model;
using Sim.Core.Policy;
using Sim.Core.Util;
using EconGame.App;
using EconGame.Ui;

namespace EconGame.Views.BudgetPages;

/// <summary>Pensions and each individual benefit: levels, eligibility, duration, indexation and tapers, with its own spending and GDP effect.</summary>
public partial class WelfarePage : BudgetPage
{
    public override string PageName => "Pensions & welfare";
    public override string Tip => "Each benefit separately: how generous, who qualifies, how long it lasts and how it is uprated. Benefits differ in who receives them and how much of each pound they spend.";

    static readonly string[] Names = { "State pension", "Unemployment benefit", "Child benefit", "Disability benefit", "Housing benefit", "Means-tested support (Universal Credit style)", "Other benefits" };
    static readonly string[][] Keys =
    {
        new[] { "Pen.Level", "Pen.Age", "Pen.Index" }, new[] { "Une.Level", "Une.Months" }, new[] { "Chi.Level", "Chi.Threshold" },
        new[] { "Dis.Level", "Dis.Elig" }, new[] { "Hou.Level" }, new[] { "Mt.Level", "Mt.Taper", "Mt.WorkAllow" }, new[] { "Oth.Scale" },
    };
    static readonly string[] Why =
    {
        "Paid to retirees, who spend most of it but save more than the poor: a mid-sized demand effect. Pension age is the big supply lever: working longer lifts the labour force and cuts spending.",
        "Goes to people with no income, so almost every pound is spent at once: the strongest demand effect, and the best automatic stabiliser. Generous, long-lasting benefits raise the natural rate of unemployment.",
        "Paid to families, a modest demand effect. Withdrawing it above an income threshold saves money but adds a marginal rate there.",
        "Goes mainly to poorer households. Loosening eligibility raises claimant numbers and the cost; tightening it saves money and pushes some people into work.",
        "Targeted on low-income renters and spent almost immediately. Generous support props up rents as well as incomes.",
        "A single award withdrawn as earnings rise. A steep taper makes work barely worth taking at low pay; a shallow one costs more and reaches further up the income scale.",
        "Everything else (carers, bereavement, grants), a catch-all scaled together.",
    };

    readonly BudgetCtx _x;
    readonly StackBar _stack = new();
    readonly Button[] _head = new Button[7];
    readonly VBoxContainer[] _body = new VBoxContainer[7];
    readonly Label[] _info = new Label[7];
    readonly ParamRow[][] _rows = new ParamRow[7][];
    readonly bool[] _open = { true, true, false, false, false, true, false };
    readonly GridContainer _facts = Facts(4);

    public WelfarePage(BudgetCtx x)
    {
        _x = x;
        AddChild(UI.Card(UI.VBox(8, UI.H2("Where the money goes"), _stack,
            UI.Dim("The departmental budget for Pensions & welfare (Departments tab) sets the overall envelope; the settings here add to or take from it, benefit by benefit.", 13, true))));
        for (int k = 0; k < 7; k++)
        {
            int kk = k;
            _head[k] = UI.Btn("", () => { _open[kk] = !_open[kk]; _body[kk].Visible = _open[kk]; Sync(_x); }, false, 0);
            _head[k].Alignment = HorizontalAlignment.Left; _head[k].SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _body[k] = UI.VBox(10); _body[k].Visible = _open[k];
            _info[k] = UI.Dim("", 13, true);
            _rows[k] = Keys[k].Select(key => x.Row(key)).ToArray();
            _body[k].AddChild(UI.Dim(Why[k], 13, true));
            foreach (var r in _rows[k]) _body[k].AddChild(r);
            _body[k].AddChild(_info[k]);
            AddChild(UI.Card(UI.VBox(10, _head[k], _body[k])));
        }
        AddChild(UI.Card(UI.VBox(8, UI.H2("Net effect of the draft"), _facts)));
    }

    public override void Sync(BudgetCtx x)
    {
        var c = x.C; var f = x.F; var est = x.Est; double sSave = 1 - c.SavingsRate;
        var segs = new List<Seg>();
        for (int k = 0; k < 7; k++) segs.Add(new Seg { Label = Names[k], Value = est.BenGdpNew[k], Color = Pal.Series[k % 8] });
        _stack.Format = v => Fmt.P(v, 2); _stack.Set(segs, $"Pensions & welfare — {Fmt.P(est.BenGdpNew.Sum(), 2)} of GDP" + (est.Changed ? $" ({Pp(est.BenSpendGdp.Sum())} vs now)" : ""));

        for (int k = 0; k < 7; k++)
        {
            foreach (var r in _rows[k]) x.Sync(r);
            bool edited = Keys[k].Any(key => x.Tag(key) != "");
            double now = est.BenGdpNow[k], nw = est.BenGdpNew[k], d = est.BenSpendGdp[k];
            _head[k].Text = $"{(_open[k] ? "▾" : "▸")}  {Names[k]}   ·   {Fmt.P(nw, 2)} of GDP" + (Math.Abs(d) > 1e-6 ? $"  ({Pp(d)})" : "") + (edited ? "   ·   changed" : "");

            var lines = new List<string>();
            string levelKey = Keys[k][0];
            if (k is 0 or 2 or 3 or 4)
            {
                double lvl = Math.Max(1e-9, x.Eff(levelKey)) * FiscalParams.NominalFactor(c, levelKey);
                double recip = nw * c.GdpNominal * 1000 / lvl;
                if (k == 0 || k == 2 || k == 3 || k == 4) lines.Add($"About {recip:0.0}m recipients implied by today's spending ({Fmt.P(recip / Math.Max(1e-6, c.Pop), 0)} of the population).");
            }
            if (k == 5)
            {
                var curve = TaxCodeEngine.Curve(f, x.Effective, f.Drift, 2, 41);
                int i = 8; double keep = 1 - curve.TotalMarginal[i];
                lines.Add($"At {Amt(c, curve.Income[i] * x.Earn)} a year a household keeps {Fmt.P(Math.Max(0, keep), 0)} of each extra {Fmt.Symbol(c.Currency)}1 earned after tax, contributions and the withdrawal of this award." + (keep < 0.35 ? " That is a poverty trap." : ""));
            }
            if (k == 1) lines.Add($"Natural unemployment {(est.NairuDelta * 100).ToString("+0.00;-0.00;0.00")}pp from every labour-market lever together; the automatic stabiliser pays out more in a downturn when benefits are generous.");
            if (k == 0) lines.Add($"Labour supply {(est.LabourDelta * 100).ToString("+0.0;-0.0;0.0")}% (pension age and work incentives together).");
            double y1 = d * f.BenMpc[k] * 0.75 * sSave;
            lines.Add($"Spent quickly: first-year demand per extra pound ×{f.BenMpc[k]:0.00} the average." + (Math.Abs(d) > 1e-6 ? $" Effect of this change on first-year GDP: {(y1 * 100).ToString("+0.00;-0.00;0.00")}%." : ""));
            _info[k].Text = string.Join("\n", lines);
        }

        foreach (var ch in _facts.GetChildren().ToList()) { _facts.RemoveChild(ch); ch.QueueFree(); }
        Fact(_facts, "Spending vs now", Pp(est.SpendGdp) + " of GDP", Tone(est.SpendGdp, false));
        Fact(_facts, "First-year GDP (demand)", (est.DemandGdp * 100).ToString("+0.00;-0.00;0.00") + "%", Tone(est.DemandGdp));
        Fact(_facts, "Long-run GDP (supply)", (est.LongRunGdp * 100).ToString("+0.0;-0.0;0.0") + "%", Tone(est.LongRunGdp), "Labour supply and natural unemployment once they have adjusted.");
        Fact(_facts, "Poverty", (est.PovertyDelta * 100).ToString("+0.0;-0.0;0.0") + "pp", Tone(est.PovertyDelta, false), "Share below 60% of median income.");
        Fact(_facts, "Inequality (Gini)", (est.GiniDelta * 100).ToString("+0.00;-0.00;0.00") + " pts", Tone(est.GiniDelta, false));
        Fact(_facts, "Approval", (est.ApprovalDelta * 100).ToString("+0.0;-0.0;0.0") + " pts", Tone(est.ApprovalDelta), "Voters who gain or lose directly, and poverty.");
    }
}
