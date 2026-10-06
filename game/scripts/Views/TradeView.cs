using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.Core.Engine;
using Sim.Core.Model;
using Sim.Core.Policy;
using EconGame.App;
using EconGame.Ui;

namespace EconGame.Views;

public partial class TradeView : View
{
    public override string Title => "Trade";
    readonly HBoxContainer _tiles = new();
    readonly DataTable _table = new() { CustomMinimumSize = new Vector2(0, 360) };
    readonly VBoxContainer _actions = new();
    readonly LineChart _ca = new() { Title = "Current account (% of GDP)" };
    readonly LineChart _fx = new() { Title = "Exchange rate" };
    readonly Label _result = UI.Lbl("", 13, Pal.Dim, false, HorizontalAlignment.Left, true);
    readonly Label _world = UI.Lbl("", 13, Pal.Dim, false, HorizontalAlignment.Left, true);
    CountryState? _sel;
    AppSlider _tariff = new(), _aid = new();
    int _sig = -1;

    sealed record Partner(CountryState C, double Weight, Relation Mine, Relation Theirs);

    public TradeView()
    {
        var page = Page("Trade & diplomacy", "Exports follow your partners' demand, your competitiveness and trade policy. Deals open markets; tariffs invite retaliation; sanctions hurt both sides.");
        _tiles.AddThemeConstantOverride("separation", 10); page.AddChild(_tiles);
        var charts = UI.HBox(12); foreach (var c in new LineChart[] { _ca, _fx }) { c.CustomMinimumSize = new Vector2(300, 190); c.SizeFlagsHorizontal = SizeFlags.ExpandFill; charts.AddChild(UI.Fill(UI.Card(c, null, 10), true, false)); }
        page.AddChild(charts);
        page.AddChild(_world);

        _table.Columns = new List<Column>
        {
            new() { Title = "Partner", Width = 0, Text = o => ((Partner)o).C.Name, Key = o => ((Partner)o).C.Name },
            new() { Title = "Share of exports", Width = 120, Align = HorizontalAlignment.Right, Text = o => UI.Pct(((Partner)o).Weight, 1), Key = o => ((Partner)o).Weight },
            new() { Title = "Agreement", Width = 90, Text = o => ((Partner)o).Mine.Alliance ? "Alliance" : ((Partner)o).Mine.Deal ? "Trade deal" : "–", Key = o => ((Partner)o).Mine.Deal ? 1 : 0 },
            new() { Title = "My tariff", Width = 80, Align = HorizontalAlignment.Right, Text = o => UI.Pct(((Partner)o).Mine.ExtraTariff, 0), Key = o => ((Partner)o).Mine.ExtraTariff },
            new() { Title = "Their tariff", Width = 90, Align = HorizontalAlignment.Right, Text = o => UI.Pct(((Partner)o).Theirs.ExtraTariff, 0), Key = o => ((Partner)o).Theirs.ExtraTariff,
                    Tint = o => ((Partner)o).Theirs.ExtraTariff > 0 ? Pal.Bad : null },
            new() { Title = "Sanctions", Width = 80, Text = o => ((Partner)o).Mine.Sanction ? "by me" : ((Partner)o).Theirs.Sanction ? "on me" : "–", Key = o => 0 },
        };
        var tabCard = UI.Card(_table); page.AddChild(tabCard);
        _table.RowKey = o => ((Partner)o).C.Id;
        _table.RowSelected += o => { _sel = ((Partner)o).C; BuildActions(); };
        _actions.AddThemeConstantOverride("separation", 8);
        page.AddChild(UI.Card(_actions));
        page.AddChild(_result);
    }

    public override void Refresh()
    {
        if (!Game.Running) return;
        var w = Game.World; var c = Game.Player; var h = Game.History(c.Id);
        foreach (var ch in _tiles.GetChildren().ToList()) ch.QueueFree();
        void T(string t, string v, string sub, Color? col = null) { var k = new KpiTile(t) { SizeFlagsHorizontal = SizeFlags.ExpandFill }; k.Set(v, sub, col, Array.Empty<double>()); _tiles.AddChild(k); }
        T("Exports", UI.Pct(c.Exports / c.Gdp, 0), "of GDP"); T("Imports", UI.Pct(c.Imports / c.Gdp, 0), "of GDP");
        T("Current account", UI.Pct(c.CaToGdp, 1), $"reserves {c.Reserves:0.0} months", c.CaToGdp < -0.05 ? Pal.Bad : Pal.Dim);
        T("External demand", (c.ExtDemandIdx * 100).ToString("0") + "", "index vs trend", c.ExtDemandIdx < 0.97 ? Pal.Bad : Pal.Dim);
        T("Real exchange rate", c.Rer.ToString("0.00"), c.Rer > 1.1 ? "overvalued" : c.Rer < 0.9 ? "undervalued" : "balanced");
        double[] x = h.Select(p => (double)p.Month).ToArray();
        _ca.StartYear = _fx.StartYear = w.StartYear; _ca.YFormat = v => v.ToString("0.0") + "%";
        _ca.SetSeries(new[] { new Series { Name = "Current account", X = x, Y = h.Select(p => p.CaToGdp * 100).ToArray(), Color = Pal.Series[2] } });
        _fx.YFormat = v => v >= 100 ? v.ToString("0") : v >= 10 ? v.ToString("0.0") : v.ToString("0.000");
        _fx.SetSeries(new[] { new Series { Name = "Local per US$", X = x, Y = h.Select(p => p.Fx).ToArray(), Color = Pal.Series[4] } });
        var g = w.Global;
        _world.Text = $"World: oil {g.OilIdx * 100:0}, food {g.FoodIdx * 100:0}, USD rate {UI.Pct(g.WorldRate, 2)}, risk appetite {g.RiskAppetite:0.00}, global temperature +{g.TempAnomaly - 0.0:0.00}°C, carbon-price floor {g.GlobalCarbonPrice:0}/t.";

        int sig = w.Month / 3 * 100 + w.Relations.Count + (_sel?.Id.GetHashCode() ?? 0) % 7 + Game.PlanVersion * 7919;
        if (sig == _sig) return; _sig = sig;
        int me = w.Countries.IndexOf(c);
        var rows = new List<object>();
        for (int j = 0; j < w.Countries.Count; j++)
            if (j != me) rows.Add(new Partner(w.Countries[j], w.Trade.W.Length > me ? w.Trade.W[me][j] : 0, WorldEngine.Rel(w, c.Id, w.Countries[j].Id), WorldEngine.Rel(w, w.Countries[j].Id, c.Id)));
        _table.SetRows(rows.OrderByDescending(r => ((Partner)r).Weight));
        if (_sel == null) { var first = (Partner)rows.OrderByDescending(r => ((Partner)r).Weight).First(); _sel = first.C; }
        var current = rows.Cast<Partner>().FirstOrDefault(r => r.C.Id == _sel.Id);
        if (current != null) _table.SelectQuiet(current);
        BuildActions();
    }

    void BuildActions()
    {
        foreach (var ch in _actions.GetChildren().ToList()) ch.QueueFree();
        if (_sel == null) return;
        var w = Game.World; var me = Game.Player; var p = _sel;
        var mine = WorldEngine.Rel(w, me.Id, p.Id); var theirs = WorldEngine.Rel(w, p.Id, me.Id);
        _actions.AddChild(UI.H2($"Relations with {p.Name}"));
        _actions.AddChild(UI.Dim($"{p.Region} · {p.Gov} · GDP {Money.Gbp(p.GdpUsdBn)} · {Game.World.Trade.W[w.Countries.IndexOf(me)][w.Countries.IndexOf(p)] * 100:0.0}% of your exports", 13));
        var row = UI.HBox(8);
        void A(string label, Command cmd, bool accent = false) => row.AddChild(PlanUi.Toggle(label, cmd, t => _result.Text = t, accent));
        if (!mine.Deal) A("Propose trade agreement", Command.TradeDeal(me.Id, p.Id), true);
        if (!mine.Alliance) A("Propose alliance", Command.Alliance(me.Id, p.Id));
        A(mine.Sanction ? "Lift sanctions" : "Impose sanctions", Command.Sanction(me.Id, p.Id, !mine.Sanction));
        _actions.AddChild(row);

        var stagedTariff = Game.Staged("tariff:" + p.Id); var stagedAid = Game.Staged("aid:" + p.Id);
        _tariff = new AppSlider { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _tariff.Setup(0, 0.4, 0.01, mine.ExtraTariff, v => UI.Pct(v, 0)); if (stagedTariff != null) _tariff.SetValue(stagedTariff.Value);
        _actions.AddChild(UI.HBox(10, UI.Lbl("Extra tariff on their goods", 14, Pal.Dim), _tariff, UI.Btn(stagedTariff != null ? "Update tariff in plan" : "Add tariff to plan", () => Do(Command.Tariff(me.Id, p.Id, _tariff.Value)))));
        _aid = new AppSlider { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _aid.Setup(0.0005, 0.02, 0.0005, stagedAid?.Value ?? 0.002, v => UI.Pct(v, 2));
        _actions.AddChild(UI.HBox(10, UI.Lbl("Aid (share of your GDP)", 14, Pal.Dim), _aid, UI.Btn(stagedAid != null ? "Update aid in plan" : "Add aid to plan", () => Do(Command.Aid(me.Id, p.Id, _aid.Value)))));
        if (stagedTariff != null) _actions.AddChild(UI.Btn("Withdraw tariff from plan", () => Game.Unstage("tariff:" + p.Id)));
        if (stagedAid != null) _actions.AddChild(UI.Btn("Withdraw aid from plan", () => Game.Unstage("aid:" + p.Id)));
        _actions.AddChild(UI.Dim("PC = political capital, charged when the turn is played. AI governments retaliate against tariffs; deals need the partner's consent.", 12, true));
    }

    void Do(Command cmd) { PlanUi.Stage(cmd, t => _result.Text = t); _sig = -1; Refresh(); }
}
