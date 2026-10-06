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

/// <summary>Personal allowance and its taper, the band table, indexation, and what the schedule does to people at different incomes.</summary>
public partial class IncomeTaxPage : BudgetPage
{
    public override string PageName => "Income tax";
    public override string Tip => "The personal allowance, where it is withdrawn, and the rate bands. Thresholds are in the country's own currency at today's prices.";

    readonly BudgetCtx _x;
    readonly ParamRow _allow, _tStart, _tRate, _index;
    readonly Label _taper = UI.Dim("", 13, true), _indexNote = UI.Dim("", 13, true), _bandNote = UI.Dim("", 13, true);
    readonly VBoxContainer _bandBox = UI.VBox(8);
    readonly Button _add, _remove, _reset;
    readonly List<(ParamRow? From, ParamRow Rate)> _band = new();
    readonly LineChart _chart = new() { CustomMinimumSize = new Vector2(300, 290), XIsMonths = false };
    readonly BarList _deciles = new() { Diverging = true, LabelWidth = 120 };
    readonly GridContainer _facts = Facts(4);
    readonly GridContainer _levels = Table("Earnings", "Income tax", "Contributions", "Take-home", "Next £1 keeps");

    public IncomeTaxPage(BudgetCtx x)
    {
        _x = x;
        _allow = x.Row("Inc.Allow"); _tStart = x.Row("Inc.TaperStart"); _tRate = x.Row("Inc.TaperRate"); _index = x.Row("Thr.Index");
        AddChild(Section("Personal allowance", "The slice of income that is not taxed, and the income at which it starts to be withdrawn. Withdrawing it creates a hidden higher marginal rate.",
            _allow, _tStart, _tRate, _taper, UI.Sep(), _index, _indexNote));

        _add = UI.Btn("+ Add a band", AddBand, false, 130); _remove = UI.Btn("− Remove top band", RemoveBand, false, 160); _reset = UI.Btn("Reset bands", UndoBands, false, 120);
        AddChild(Section("Rate bands", "Each band's rate applies to the slice of taxable income (income above the allowance) inside it. Up to eight bands.",
            _bandBox, _bandNote, UI.HBox(8, _add, _remove, _reset)));

        _deciles.Format = v => (v * 100).ToString("+0.0;-0.0;0.0") + "%";
        AddChild(Section("What the schedule does", null,
            UI.Lbl("Marginal and average tax rates by income", 14, Pal.Text, true), _chart,
            UI.Lbl("Take-home pay at selected incomes", 14, Pal.Text, true), _levels,
            UI.Lbl("Change in net income by tenth of earners (draft against today's code)", 14, Pal.Text, true), _deciles, _facts));
    }

    // ---- editing

    void SetBands(List<(double From, double Rate)> b)
    {
        var tmp = new Dictionary<string, double>(); FiscalParams.SetBands(tmp, b); b = FiscalParams.Bands(tmp);
        var cur = FiscalParams.Bands(_x.AfterPlan);
        bool same = b.Count == cur.Count && b.Zip(cur).All(t => Math.Abs(t.First.From - t.Second.From) < 1e-9 && Math.Abs(t.First.Rate - t.Second.Rate) < 1e-9);
        Draft.SetBands(same ? null : b);
    }

    void UndoBands() { Draft.SetBands(null); Game.Unstage("fiscal:" + FiscalParams.BandsKey); }

    void AddBand()
    {
        var b = FiscalParams.Bands(_x.Effective); if (b.Count >= FiscalParams.MaxBands) return;
        var last = b[^1];
        b.Add((Math.Round(last.From + Math.Max(1.0, last.From * 0.6), 2), Math.Min(0.75, last.Rate + 0.05)));
        SetBands(b);
    }

    void RemoveBand()
    {
        var b = FiscalParams.Bands(_x.Effective); if (b.Count <= 1) return;
        b.RemoveAt(b.Count - 1); SetBands(b);
    }

    void EditBand(int i, bool rate, double v)
    {
        var b = FiscalParams.Bands(_x.Effective); if (i >= b.Count) return;
        b[i] = rate ? (b[i].From, v) : (v, b[i].Rate);
        SetBands(b);
    }

    ParamRow BandRow(int i, bool rate)
    {
        var def = FiscalParams.Def(rate ? FiscalParams.BandRate(i) : FiscalParams.BandFrom(i))!;
        var r = new ParamRow(def, rate ? $"Band {i + 1} rate" : $"Band {i + 1} starts at");
        r.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        r.Edited += v => EditBand(i, rate, v);
        r.Undone += UndoBands;
        return r;
    }

    void Rebuild(int n)
    {
        foreach (var ch in _bandBox.GetChildren().ToList()) { _bandBox.RemoveChild(ch); ch.QueueFree(); }
        _band.Clear();
        for (int i = 0; i < n; i++)
        {
            var rate = BandRow(i, true);
            ParamRow? from = i == 0 ? null : BandRow(i, false);
            Control left = from ?? (Control)UI.Dim("Starts where taxable income begins (above the allowance)", 13, true);
            left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _bandBox.AddChild(UI.HBox(18, left, rate));
            _band.Add((from, rate));
        }
    }

    // ---- display

    string TaperNote(BudgetCtx x)
    {
        var tp = TaxParams.Of(x.Effective, x.F.Drift);
        if (tp.TaperRate <= 0 || tp.TaperStart <= 0) return "No taper: the full allowance is available at every income.";
        double end = tp.TaperStart + tp.Allow / tp.TaperRate, e = x.Earn, mid = (tp.TaperStart + end) / 2, h = 1e-4;
        double mr = (tp.Income(mid + h) - tp.Income(mid)) / h;
        return $"The allowance is withdrawn between {Amt(x.C, tp.TaperStart * e)} and {Amt(x.C, end * e)}. Inside that range each extra {Fmt.Symbol(x.C.Currency)}1 earned is taxed at {Fmt.P(mr, 0)}: the band rate plus the allowance lost with it.";
    }

    public override void Sync(BudgetCtx x)
    {
        var c = x.C; var f = x.F;
        x.Sync(_allow); x.Sync(_tStart); x.Sync(_tRate); x.Sync(_index);
        _taper.Text = TaperNote(x);
        _indexNote.Text = (f.StatIndex.Length > 0 ? $"In reality: {f.StatIndex}. " : "") + "The game starts neutral (thresholds follow earnings)."
            + (Math.Abs(f.Drift - 1) > 0.002 ? $" Since the start, thresholds are worth {Fmt.P(f.Drift, 1)} of what earnings-indexing would give: {(f.Drift < 1 ? "fiscal drag is pulling more income into the higher bands" : "they have outpaced earnings")}." : "");

        var eff = FiscalParams.Bands(x.Effective); var live = FiscalParams.Bands(f.P);
        if (eff.Count != _band.Count) Rebuild(eff.Count);
        string tag = Draft.Bands != null ? "draft" : Game.Staged("fiscal:" + FiscalParams.BandsKey) != null ? "in the plan" : "";
        for (int i = 0; i < eff.Count; i++)
        {
            bool isNew = i >= live.Count;
            double lr = isNew ? eff[i].Rate : live[i].Rate, lf = isNew ? eff[i].From : live[i].From;
            _band[i].Rate.Sync(c, eff[i].Rate, lr, isNew ? "new band" : Math.Abs(eff[i].Rate - lr) > 1e-9 ? tag : "");
            _band[i].From?.Sync(c, eff[i].From, lf, isNew ? "new band" : Math.Abs(eff[i].From - lf) > 1e-9 ? tag : "");
        }
        _add.Disabled = eff.Count >= FiscalParams.MaxBands; _remove.Disabled = eff.Count <= 1; _reset.Disabled = tag == "";
        _bandNote.Text = "Bands now: " + FiscalParams.ShowBands(c, eff).Replace("from", "above") + (tag != "" ? $"  ·  {tag}" : "");

        Chart(x); Levels(x); Deciles(x);
    }

    void Chart(BudgetCtx x)
    {
        var f = x.F; var c = x.C; double e = x.Earn;
        var bands = FiscalParams.Bands(x.Effective);
        double end = 0; { var tp = TaxParams.Of(x.Effective, f.Drift); if (tp.TaperRate > 0 && tp.TaperStart > 0) end = tp.TaperStart + tp.Allow / tp.TaperRate; }
        double top = Math.Clamp(Math.Max(6, Math.Max(bands[^1].From * f.Drift + x.Effective.GetValueOrDefault("Inc.Allow") * f.Drift, end) * 1.35), 6, 40);
        var now = TaxCodeEngine.Curve(f, f.P, f.Drift, top); var nw = TaxCodeEngine.Curve(f, x.Effective, f.Drift, top);
        double[] X = nw.Income.Select(m => m * e).ToArray();
        static double[] Cap(double[] a) => a.Select(v => Math.Min(v, 1.2)).ToArray();
        bool changed = !FiscalDraft.Same(f.P, x.Effective);
        var s = new List<Series>
        {
            new() { Name = "Average rate (tax + contributions)", X = X, Y = nw.AvgRate, Color = Pal.Series[2] },
            new() { Name = "Income-tax marginal rate", X = X, Y = Cap(nw.IncomeTaxMarginal), Color = Pal.Series[0] },
            new() { Name = "All-in marginal (tax, contributions, benefit withdrawal)", X = X, Y = Cap(nw.TotalMarginal), Color = Pal.Series[1] },
        };
        if (changed) s.Add(new Series { Name = "All-in marginal today", X = X, Y = Cap(now.TotalMarginal), Color = Pal.Faint, Dashed = true });
        _chart.YFormat = v => Fmt.P(v, 0); _chart.XFormat = v => Compact(c.Currency, v);
        _chart.YMin = 0; _chart.SetSeries(s);
    }

    void Levels(BudgetCtx x)
    {
        var c = x.C; var f = x.F; double e = x.Earn;
        var tp = TaxParams.Of(x.Effective, f.Drift); var bp = BenParams.Of(x.Effective, f.P0, f.PenDrift);
        ClearRows(_levels); double h = 1e-4;
        foreach (double m in new[] { 0.5, 1, 2, 4, 10 })
        {
            double tax = tp.Income(m), ee = tp.Employee(m), award = bp.Award(m);
            double marg = 1 - ((tp.Income(m + h) - tax) / h + (tp.Employee(m + h) - ee) / h + bp.MtMetr(m));
            Row(_levels, (Amt(c, m * e), null), (Amt(c, tax * e) + $"  ({Fmt.P(tax / m, 0)})", null), (Amt(c, ee * e), null),
                (Amt(c, (m - tax - ee + award) * e), null), (Fmt.P(Math.Max(0, marg), 0), marg < 0.4 ? Pal.Bad : marg < 0.55 ? Pal.Warn : Pal.Text));
        }
    }

    void Deciles(BudgetCtx x)
    {
        var est = x.Est;
        _deciles.Set(Enumerable.Range(0, 10).Select(d => new BarItem
        {
            Label = d == 0 ? "Poorest tenth" : d == 9 ? "Richest tenth" : $"Tenth {d + 1}",
            Value = est.DecileNet[d], Color = Tone(est.DecileNet[d]),
        }));
        foreach (var ch in _facts.GetChildren().ToList()) { _facts.RemoveChild(ch); ch.QueueFree(); }
        Fact(_facts, "Income tax raised", $"{Fmt.P(est.New.AvgInc, 1)} avg rate", null, "Average income-tax rate across the whole tax base, after the draft.");
        Fact(_facts, "Revenue vs now", Pp(est.TaxGdp[0]) + " of GDP", Tone(est.TaxGdp[0]), "Annual change in income-tax revenue before behavioural feedback.");
        Fact(_facts, "Inequality (Gini)", (est.GiniDelta * 100).ToString("+0.00;-0.00;0.00") + " pts", Tone(est.GiniDelta, false));
        Fact(_facts, "Labour supply", (est.LabourDelta * 100).ToString("+0.0;-0.0;0.0") + "%", Tone(est.LabourDelta), "Hours and participation respond to the after-tax reward from working.");
    }
}
