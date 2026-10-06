using System;
using System.Linq;
using Godot;
using Sim.Core.Model;
using Sim.Core.Scoring;
using EconGame.App;
using EconGame.Ui;

namespace EconGame.Views;

public partial class ReportView : View
{
    public override string Title => "Report";
    readonly Label _grade = UI.Lbl("", 54, Pal.Accent, true);
    readonly Label _sum = UI.Lbl("", 14, Pal.Dim, false, HorizontalAlignment.Left, true);
    readonly BarList _comp = new() { LabelWidth = 160, RowHeight = 30 };
    readonly VBoxContainer _goals = new(), _history = new();

    public ReportView()
    {
        var page = Page("Report card", "Your national scorecard: prosperity, living standards, stability, sustainability and resilience.");
        var top = UI.HBox(24, _grade, UI.VBox(4, _sum)); top.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        page.AddChild(UI.Card(top));
        page.AddChild(Cards.Section("Components (0-100)", _comp));
        page.AddChild(_goals);
        page.AddChild(Cards.Section("Notable events and decisions", _history));
    }

    public override void Refresh()
    {
        if (!Game.Running) return;
        var w = Game.World; var c = Game.Player; var sc = Scorer.Compute(w, c);
        _grade.Text = $"{sc.Grade}";
        _sum.Text = $"Score {sc.Total:0}/100 after {w.Month / 12} years {w.Month % 12} months in charge of {c.Name}. Real GDP per head is {(sc.Detail["gdp_per_head_cagr"]) * 100:+0.0;-0.0}% a year on average.";
        _comp.Max = 100; _comp.Format = v => v.ToString("0");
        Color Col(double v) => v >= 65 ? Pal.Good : v >= 45 ? Pal.Warn : Pal.Bad;
        _comp.Set(new[]
        {
            new BarItem { Label = "Prosperity (25%)", Value = sc.Prosperity, Color = Col(sc.Prosperity), Tooltip = "Real GDP per head growth, blending actual and potential output" },
            new BarItem { Label = "Living standards (25%)", Value = sc.Living, Color = Col(sc.Living), Tooltip = "Real wages, unemployment, inequality, life expectancy" },
            new BarItem { Label = "Stability (20%)", Value = sc.Stability, Color = Col(sc.Stability), Tooltip = "Approval, regime stability, inflation near target, unrest, cycle" },
            new BarItem { Label = "Sustainability (15%)", Value = sc.Sustainability, Color = Col(sc.Sustainability), Tooltip = "Debt, deficit, emissions intensity, renewables" },
            new BarItem { Label = "Resilience (15%)", Value = sc.Resilience, Color = Col(sc.Resilience), Tooltip = "Reserves, external balance, risk premium, diversification" },
        });
        foreach (var ch in _goals.GetChildren().ToList()) ch.QueueFree();
        if (Game.Scenario != null)
        {
            var r = Scenarios.Evaluate(w, Game.Scenario); var box = UI.VBox(6);
            foreach (var g in r.Goals) box.AddChild(UI.Lbl($"{(g.Met ? "✔" : "○")} {g.Goal.Label} (now {Format(g.Goal.Metric, g.Current)})", 14, g.Met ? Pal.Good : Pal.Dim));
            box.AddChild(UI.Dim(r.Summary));
            _goals.AddChild(Cards.Section(Game.Scenario.Name + " — objectives", box));
        }
        foreach (var ch in _history.GetChildren().ToList()) ch.QueueFree();
        foreach (var e in w.EventHistory.Where(e => e.Country == c.Id || e.Country == "WORLD").TakeLast(25).Reverse())
            _history.AddChild(UI.Lbl($"{w.StartYear + e.Month / 12}-{e.Month % 12 + 1:D2}  {e.Name}" + (e.Choice != "" ? $"  →  {e.Choice}" : ""), 13, e.Country == "WORLD" ? Pal.Dim : Pal.Text));
        if (!w.EventHistory.Any()) _history.AddChild(UI.Dim("Nothing yet.", 13));
        int ok = w.CommandLog.Count(l => l.Ok);
        _history.AddChild(UI.Dim($"{ok} policy actions taken, {w.CommandLog.Count - ok} refused.", 12));
    }

    static string Format(string m, double v) => m is "gdppc" or "emissions" ? v.ToString("0.00") + "×" : m == "score" ? v.ToString("0") : m == "default" ? (v > .5 ? "yes" : "no") : UI.Pct(v);
}
