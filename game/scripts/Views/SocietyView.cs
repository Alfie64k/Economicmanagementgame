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
    readonly GridContainer _work = new() { Columns = 3 };
    readonly BarList _shadow = new() { Diverging = true, LabelWidth = 170 };
    readonly Label _workText = UI.Lbl("", 13, Pal.Dim, false, HorizontalAlignment.Left, true);

    public SocietyView()
    {
        var page = Page("Society & politics", "Demography, public mood, institutions and the environment move slowly and constrain what you can do.");
        _tiles.AddThemeConstantOverride("separation", 10); page.AddChild(_tiles);
        var row = UI.HBox(12);
        row.AddChild(UI.Fill(Cards.Section("Demography", _demo), true, false));
        row.AddChild(UI.Fill(Cards.Section("What is driving approval", _approval, "Percentage-point contributions to your long-run approval target."), true, false));
        page.AddChild(row);
        _work.AddThemeConstantOverride("h_separation", 10); _work.AddThemeConstantOverride("v_separation", 10);
        page.AddChild(Cards.Section("Work, wages and the informal economy", UI.VBox(10, _work, UI.Lbl("What is moving the informal economy (points of activity, against the start)", 13, Pal.Accent, true), _shadow, _workText),
            "How bargaining turns price shocks into wage claims, how long spells out of work scar the labour market, and how much activity escapes tax."));
        var row2 = UI.HBox(12);
        foreach (var c in new LineChart[] { _pop, _emis }) { c.Annotated = true; c.SyncGroup = "soc"; c.CustomMinimumSize = new Vector2(300, 190); c.SizeFlagsHorizontal = SizeFlags.ExpandFill; row2.AddChild(UI.Fill(UI.Card(c, null, 10), true, false)); }
        page.AddChild(new ChartRangeBar());
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
        _pop.Markers = _emis.Markers = ChartPrefs.For(w);
        _pop.StartYear = _emis.StartYear = w.StartYear; _pop.YFormat = v => v.ToString("0.0"); _emis.YFormat = v => v.ToString("0");
        _pop.SetSeries(new[] { new Series { Name = "Population", X = x, Y = h.Select(p => p.Pop).ToArray(), Color = Pal.Series[2] } });
        _emis.SetSeries(new[] { new Series { Name = "Emissions", X = x, Y = h.Select(p => p.EmissionsMt).ToArray(), Color = Pal.Series[5] } });

        string elec = c.NextElectionMonth > 0 ? $"Next election in {Math.Max(0, c.NextElectionMonth - w.Month)} months; win probability ≈ {UI.Pct(1 / (1 + Math.Exp(-10 * (c.Approval - 0.42))), 0)} at current approval." : $"No competitive elections ({c.Gov}); coup risk is highest when stability is below 30% and unrest is high.";
        _politics.Text = $"System: {c.Gov}. Legislative support {UI.Pct(c.Coalition, 0)}. Style of government: {c.Style}.\n{elec}\nPolitical capital regenerates with approval and is spent on every reform ({c.PoliticalCapital:0}/100).";

        BuildWork(c);

        _env.Max = 1; _env.Format = v => UI.Pct(v, 0);
        _env.Set(new[]
        {
            new BarItem { Label = "Renewables share", Value = c.Renewables, Color = Pal.Good },
            new BarItem { Label = "Emissions vs start", Value = Math.Min(1.5, c.EmissionsMt / c.EmissionsMt0), Text = UI.Pct(c.EmissionsMt / c.EmissionsMt0, 0), Color = c.EmissionsMt < c.EmissionsMt0 ? Pal.Good : Pal.Bad },
            new BarItem { Label = "Climate damage to output", Value = Math.Min(1, c.ClimateDamage * 10), Text = UI.Pct(c.ClimateDamage, 1), Color = Pal.Bad },
            new BarItem { Label = "Carbon price (÷200)", Value = Math.Min(1, c.CarbonPrice / 200), Text = $"{c.CarbonPrice:0}/t", Color = Pal.Series[0] },
        });
    }
    void BuildWork(CountryState c)
    {
        foreach (var ch in _work.GetChildren().ToList()) { _work.RemoveChild(ch); ch.QueueFree(); }
        void T(string t, string v, string sub, Color? col, string tip)
        {
            var k = new KpiTile(t) { CustomMinimumSize = new Vector2(190, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = tip };
            k.Set(v, sub, col, Array.Empty<double>()); _work.AddChild(k);
        }
        double lost = 1 - c.ShadowMult;
        T("Informal economy", UI.Pct(c.Shadow, 1), $"start {UI.Pct(c.Shadow0, 1)}; tax base {(c.ShadowMult - 1) * 100:+0.0;-0.0;0.0}%",
            c.Shadow > c.Shadow0 + 0.015 ? Pal.Bad : c.Shadow < c.Shadow0 - 0.015 ? Pal.Good : Pal.Dim, "Share of activity that goes unreported and untaxed. It rises with the tax wedge and corruption and falls with administration spending and compliance policies.");
        T("Union coverage", UI.Pct(c.UnionCoverage, 0), $"bargaining power {UI.Pct(c.BargainingPower, 0)}", Pal.Dim,
            "Share of workers covered by collective agreements. Bargaining power is coverage times how strong the unions are: it sets how fast price shocks reach pay and how big a spiral is.");
        T("Wage-price spiral", $"{c.WageSpiral * 100:+0.0;-0.0;0.0}pp", $"wage premium {c.WagePremium * 100:+0.0;-0.0;0.0}%", c.WageSpiral > 0.004 ? Pal.Bad : Pal.Dim,
            "Inflation added by bargained pay settlements after a price shock. It is larger where unions are strong and smaller where the central bank is credible.");
        T("Strike risk", UI.Pct(c.StrikeRisk, 0), c.WageGap > 0.01 ? $"real pay {c.WageGap * 100:0.0}% behind" : "pay is keeping up", c.StrikeRisk > 0.2 ? Pal.Bad : c.StrikeRisk > 0.05 ? Pal.Warn : Pal.Dim,
            "Chance that workers walk out, rising with the real-pay shortfall and bargaining power. It feeds the strike-wave event.");
        T("Long-term unemployed", UI.Pct(c.LtuStock, 1), c.NairuHyst > 0.001 ? $"natural rate +{c.NairuHyst * 100:0.0}pp" : "no scarring", c.NairuHyst > 0.005 ? Pal.Bad : Pal.Dim,
            "People out of work for so long that their skills and attachment fade. They drain away only slowly and lift the natural rate of unemployment meanwhile (hysteresis).");
        T("Labour share", UI.Pct(c.LabourIncomeShare, 1), $"start {UI.Pct(c.LabourIncomeShare0, 1)}", Pal.Dim,
            "Share of national income paid as wages. Follows bargaining power and the output gap and is squeezed when real pay falls behind.");

        var d = c.ShadowDrivers;
        _shadow.Max = Math.Max(0.05, new[] { Math.Abs(d[1]), Math.Abs(d[2]), Math.Abs(d[3]), Math.Abs(d[4]) }.Max() * 1.5);
        _shadow.Format = v => $"{v * 100:+0.0;-0.0;0.0}pp";
        _shadow.Set(new[]
        {
            new BarItem { Label = "Tax burden", Value = d[1], Color = d[1] > 0 ? Pal.Bad : Pal.Good, Tooltip = "Change in the tax wedge since the start" },
            new BarItem { Label = "Corruption", Value = d[2], Color = d[2] > 0 ? Pal.Bad : Pal.Good, Tooltip = "Corruption above or below its starting level" },
            new BarItem { Label = "Enforcement", Value = d[3], Color = d[3] > 0 ? Pal.Bad : Pal.Good, Tooltip = "Administration spending against its starting share" },
            new BarItem { Label = "Policy", Value = d[4], Color = d[4] > 0 ? Pal.Bad : Pal.Good, Tooltip = "Compliance drives and amnesties, and the limits on how far it can move" },
        });
        double target = d[0] + d[1] + d[2] + d[3] + d[4];
        _workText.Text = $"The informal economy is heading for {UI.Pct(target, 1)} of activity with today's taxes, corruption and enforcement"
            + (Math.Abs(c.Shadow - target) < 0.002 ? "; it is there." : $" (it moves about a third of the way each year).")
            + (lost > 0.001 ? $" It currently costs {UI.Pct(lost, 1)} of the tax base." : c.ShadowMult > 1.001 ? $" Better compliance has widened the tax base by {UI.Pct(c.ShadowMult - 1, 1)}." : "");
    }
}
