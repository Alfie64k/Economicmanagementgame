using System;
using System.Linq;
using Godot;
using Sim.Core.Policy;
using Sim.Core.Util;
using EconGame.App;
using EconGame.Ui;

namespace EconGame.Views.BudgetPages;

/// <summary>Corporation tax: main and small-profits rates, the small-profits limit and capital expensing.</summary>
public partial class CorpPage : BudgetPage
{
    public override string PageName => "Corporation tax";
    public override string Tip => "The rate on company profits and how generously investment is written off. These set the cost of capital that drives investment and foreign direct investment.";

    readonly ParamRow _main, _small, _limit, _exp;
    readonly BarList _bars = new() { LabelWidth = 220, Format = v => Fmt.P(v, 1) };
    readonly GridContainer _facts = Facts(4);
    readonly Label _note = UI.Dim("", 13, true);

    public CorpPage(BudgetCtx x)
    {
        _main = x.Row("Corp.Main"); _small = x.Row("Corp.Small"); _limit = x.Row("Corp.SmallLimit"); _exp = x.Row("Corp.Expensing");
        AddChild(Section("Rates", "A lower small-profits rate on profits below the limit supports small firms but invites firms to stay small.", _main, _small, _limit));
        AddChild(Section("Investment allowances", "Capital expensing lets firms deduct investment immediately instead of over many years. It costs revenue now and lowers the tax on new plant, which raises investment and the attractiveness of the country to foreign firms.", _exp));
        AddChild(Section("What it does", null, UI.Lbl("Tax rate that matters, by measure", 14, Pal.Text, true), _bars, _note, _facts));
    }

    public override void Sync(BudgetCtx x)
    {
        foreach (var r in new[] { _main, _small, _limit, _exp }) x.Sync(r);
        var est = x.Est; var f = x.F;
        _bars.Set(new[]
        {
            new BarItem { Label = "Main statutory rate", Value = x.Eff("Corp.Main"), Color = Pal.Series[0], Tooltip = "The headline rate." },
            new BarItem { Label = "Average rate on profits", Value = est.New.CorpEff, Color = Pal.Series[1], Tooltip = "Blend of the main and small-profits rates over all profits." },
            new BarItem { Label = "New investment, after expensing", Value = est.New.Wedge, Color = Pal.Series[2], Tooltip = "What a firm weighing a new project faces: the average rate reduced by capital expensing." },
            new BarItem { Label = "New investment, today", Value = est.Cur.Wedge, Color = Pal.Faint },
        });
        _note.Text = "Investment responds to the rate on new investment, not the headline rate: full expensing can cut it sharply while the statutory rate stays put.";
        foreach (var ch in _facts.GetChildren().ToList()) { _facts.RemoveChild(ch); ch.QueueFree(); }
        double dCorp = f.Calib[(int)Sim.Core.Model.Tax.Corporate] * (est.New.Wedge - est.Cur.Wedge);
        Fact(_facts, "Revenue vs now", Pp(est.TaxGdp[1]) + " of GDP", Tone(est.TaxGdp[1]), "Annual corporation-tax revenue change before investment responds.");
        Fact(_facts, "Cost of capital", (dCorp * 100).ToString("+0.00;-0.00;0.00") + "pp", Tone(dCorp, false), "Change in the tax wedge on new investment. Lower is better for growth.");
        Fact(_facts, "Year-1 demand effect", (est.DemandGdp * 100).ToString("+0.00;-0.00;0.00") + "%", null, "Cutting company tax does little for demand at first; the gain comes later, through capital.");
        Fact(_facts, "Long-run output", (est.LongRunGdp * 100).ToString("+0.0;-0.0;0.0") + "%", Tone(est.LongRunGdp), "Supply-side effect once investment has built up.");
    }
}
