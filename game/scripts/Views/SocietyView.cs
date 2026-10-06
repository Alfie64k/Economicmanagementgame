using System;
using System.Linq;
using Godot;
using Sim.Core.Model;
using Sim.Core.Scoring;
using EconGame.App;
using EconGame.Ui;

namespace EconGame.Views;

public partial class SocietyView : View
{
    public override string Title => "Society";
    readonly HBoxContainer _tiles = new();
    readonly BarList _demo = new() { LabelWidth = 150 }, _approval = new() { Diverging = true, LabelWidth = 170 }, _env = new() { LabelWidth = 170 };
    readonly LineChart _pop = new() { Title = "Population (millions)" };
    readonly LineChart _emis = new() { Title = "Emissions (Mt CO₂)" };
    readonly Label _politics = UI.Lbl("", 14, Pal.Text, false, HorizontalAlignment.Left, true);

    public SocietyView()
    {
        var page = Page("Society & politics", "Demography, public mood, institutions and the environment move slowly and constrain what you can do.");
        _tiles.AddThemeConstantOverride("separation", 10); page.AddChild(_tiles);
        var row = UI.HBox(12);
        row.AddChild(UI.Fill(Cards.Section("Demography", _demo), true, false));
        row.AddChild(UI.Fill(Cards.Section("What is driving approval", _approval, "Percentage-point contributions to your long-run approval target."), true, false));
        page.AddChild(row);
        var row2 = UI.HBox(12);
        foreach (var c in new LineChart[] { _pop, _emis }) { c.CustomMinimumSize = new Vector2(300, 190); c.SizeFlagsHorizontal = SizeFlags.ExpandFill; row2.AddChild(UI.Fill(UI.Card(c, null, 10), true, false)); }
        page.AddChild(row2);
        page.AddChild(Cards.Section("Political system", _politics));
        page.AddChild(Cards.Section("Environment & energy", _env));
    }

    public override void Refresh()
    {
        if (!Game.Running) return;
        var w = Game.World; var c = Game.Player; var h = Game.History(c.Id);
        foreach (var ch in _tiles.GetChildren().ToList()) ch.QueueFree();
        void T(string t, string v, string sub, Color? col = null) { var k = new KpiTile(t) { SizeFlagsHorizontal = SizeFlags.ExpandFill }; k.Set(v, sub, col, Array.Empty<double>()); _tiles.AddChild(k); }
        T("Population", $"{c.Pop:0.0}m", $"{(c.Pop / c.Pop0 - 1) * 100:+0.0;-0.0}% since start");
        T("Life expectancy", $"{c.LifeExp:0.0}", "years"); T("Inequality (Gini)", c.Gini.ToString("0.00"), $"start {c.Gini0:0.00}", c.Gini > c.Gini0 + 0.02 ? Pal.Bad : Pal.Dim);
        T("Unrest", UI.Pct(c.Unrest, 0), $"stability {UI.Pct(c.Stability, 0)}", c.Unrest > 0.4 ? Pal.Bad : Pal.Dim);
        T("Corruption", UI.Pct(c.Corruption, 0), $"democracy {UI.Pct(c.Democracy, 0)}");

        _demo.Max = 1; _demo.Format = v => UI.Pct(v, 0);
        _demo.Set(new[]
        {
            new BarItem { Label = "Children (0-14)", Value = c.Young, Color = Pal.Series[0] }, new BarItem { Label = "Working age (15-64)", Value = c.Working, Color = Pal.Series[2] },
            new BarItem { Label = "Over 65", Value = c.Old, Color = Pal.Series[1], Tooltip = $"was {UI.Pct(c.Old0, 0)} at the start" },
            new BarItem { Label = "Participation", Value = c.Participation, Color = Pal.Series[4] }, new BarItem { Label = "Fertility (÷4)", Value = c.Fertility / 4, Text = c.Fertility.ToString("0.0"), Color = Pal.Series[6] },
        });
        var ex = Explain.Why(c, "approval");
        _approval.Max = Math.Max(5, ex.Items.Max(i => Math.Abs(i.Value))); _approval.Format = v => UI.Sign(v, "0.0") + "pp";
        _approval.Set(ex.Items.Where(i => i.Label != "Baseline mood").Select(i => new BarItem { Label = i.Label, Value = i.Value, Color = i.Value >= 0 ? Pal.Good : Pal.Bad }));

        double[] x = h.Select(p => (double)p.Month).ToArray();
        _pop.StartYear = _emis.StartYear = w.StartYear; _pop.YFormat = v => v.ToString("0.0"); _emis.YFormat = v => v.ToString("0");
        _pop.SetSeries(new[] { new Series { Name = "Population", X = x, Y = h.Select(p => p.Pop).ToArray(), Color = Pal.Series[2] } });
        _emis.SetSeries(new[] { new Series { Name = "Emissions", X = x, Y = h.Select(p => p.EmissionsMt).ToArray(), Color = Pal.Series[5] } });

        string elec = c.NextElectionMonth > 0 ? $"Next election in {Math.Max(0, c.NextElectionMonth - w.Month)} months; win probability ≈ {UI.Pct(1 / (1 + Math.Exp(-10 * (c.Approval - 0.42))), 0)} at current approval." : $"No competitive elections ({c.Gov}); coup risk is highest when stability is below 30% and unrest is high.";
        _politics.Text = $"System: {c.Gov}. Legislative support {UI.Pct(c.Coalition, 0)}. Style of government: {c.Style}.\n{elec}\nPolitical capital regenerates with approval and is spent on every reform ({c.PoliticalCapital:0}/100).";

        _env.Max = 1; _env.Format = v => UI.Pct(v, 0);
        _env.Set(new[]
        {
            new BarItem { Label = "Renewables share", Value = c.Renewables, Color = Pal.Good },
            new BarItem { Label = "Emissions vs start", Value = Math.Min(1.5, c.EmissionsMt / c.EmissionsMt0), Text = UI.Pct(c.EmissionsMt / c.EmissionsMt0, 0), Color = c.EmissionsMt < c.EmissionsMt0 ? Pal.Good : Pal.Bad },
            new BarItem { Label = "Climate damage to output", Value = Math.Min(1, c.ClimateDamage * 10), Text = UI.Pct(c.ClimateDamage, 1), Color = Pal.Bad },
            new BarItem { Label = "Carbon price (÷200)", Value = Math.Min(1, c.CarbonPrice / 200), Text = $"{c.CarbonPrice:0}/t", Color = Pal.Series[0] },
        });
    }
}
