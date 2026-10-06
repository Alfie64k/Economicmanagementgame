using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.Core.Model;
using Sim.Core.Policy;
using EconGame.App;
using EconGame.Ui;

namespace EconGame.Views;

public partial class PoliciesView : View
{
    public override string Title => "Policies";
    readonly VBoxContainer _list = new();
    readonly HBoxContainer _chips = new();
    readonly Label _result = UI.Lbl("", 13, Pal.Dim, false, HorizontalAlignment.Left, true);
    string _cat = "all";
    int _lastSig = -1;

    static readonly Dictionary<string, string> ModText = new()
    {
        ["tfp"] = "productivity growth", ["nairu"] = "structural unemployment", ["fdi"] = "FDI inflows", ["export"] = "exports", ["import"] = "imports",
        ["corruption"] = "corruption (per year)", ["approval"] = "approval", ["gini"] = "inequality", ["invest"] = "private investment", ["savings"] = "saving rate",
        ["inflation"] = "inflation", ["credibility"] = "central-bank credibility", ["risk"] = "sovereign risk premium", ["fx"] = "currency strength", ["participation"] = "labour participation",
        ["fertility"] = "fertility", ["migration"] = "net migration", ["lifeexp"] = "life expectancy (years)", ["renewables"] = "renewables share (per year)", ["emissions"] = "emissions",
        ["unrest"] = "unrest", ["polcap"] = "political capital regeneration", ["revenue"] = "revenue (% GDP)", ["stability"] = "stability",
    };

    public PoliciesView()
    {
        var page = Page("Policies", "Legislation that reshapes the economy over years. Add a bill to this turn's plan; it goes to the vote, and costs its political capital, when you end the turn. It takes time to implement and may be voted down in a democracy.");
        _chips.AddThemeConstantOverride("separation", 6); page.AddChild(_chips);
        page.AddChild(_result);
        _list.AddThemeConstantOverride("separation", 10); page.AddChild(_list);
    }

    public override void Refresh()
    {
        if (!Game.Running) return;
        var c = Game.Player;
        int sig = c.Policies.Count * 1000 + c.Policies.Count(p => p.Active) * 10 + (int)(c.PoliticalCapital / 5) + _cat.GetHashCode() % 97 + Game.PlanVersion * 7919;
        if (sig == _lastSig) return; _lastSig = sig;
        foreach (var ch in _chips.GetChildren().ToList()) ch.QueueFree();
        var cats = new[] { "all" }.Concat(PolicyCatalog.Policies.Select(p => p.Category).Distinct()).ToList();
        foreach (var k in cats) { string kk = k; _chips.AddChild(UI.Chip(char.ToUpper(k[0]) + k[1..], k == _cat, () => { _cat = kk; _lastSig = -1; Refresh(); })); }
        foreach (var ch in _list.GetChildren().ToList()) ch.QueueFree();
        foreach (var def in PolicyCatalog.Policies.Where(p => _cat == "all" || p.Category == _cat).OrderBy(p => p.Category).ThenBy(p => p.Pc)) _list.AddChild(Card(c, def));
    }

    Control Card(CountryState c, PolicyDef def)
    {
        var ap = c.Policies.FirstOrDefault(p => p.Id == def.Id);
        var box = UI.VBox(6);
        string status = ap == null ? "" : ap.Failed ? "Rejected" : ap.Active ? "In force" : $"Takes effect {Game.World.StartYear + ap.ActivationMonth / 12}-{ap.ActivationMonth % 12 + 1:D2}";
        var head = UI.HBox(8, UI.Lbl(def.Name, 17, Pal.Text, true), Cards.Chip(def.Category, Pal.Faint), UI.Spacer(0, 0, true));
        if (status != "") head.AddChild(Cards.Chip(status, ap!.Failed ? Pal.Bad : ap.Active ? Pal.Good : Pal.Warn));
        box.AddChild(head);
        box.AddChild(UI.Dim(def.Desc, 13, true));
        var fx = new List<string>();
        foreach (var kv in def.Mods) fx.Add($"{(kv.Value >= 0 ? "▲" : "▼")} {ModText.GetValueOrDefault(kv.Key, kv.Key)}");
        foreach (var kv in def.Budget) fx.Add($"{(kv.Value >= 0 ? "▲" : "▼")} {kv.Key.ToLower()} budget {(kv.Value >= 0 ? "+" : "")}{kv.Value * 100:0.0}pp GDP");
        foreach (var kv in def.Subsidy) fx.Add($"subsidy: {kv.Key.ToLower()} {kv.Value:P0}");
        if (def.Carbon != null) fx.Add($"carbon price {def.Carbon:0}/t");
        if (def.OneOffRevenue != 0) fx.Add($"one-off {(def.OneOffRevenue > 0 ? "+" : "")}{def.OneOffRevenue * 100:0.0}% GDP");
        box.AddChild(UI.Lbl(string.Join("   ", fx), 12, Pal.Accent, false, HorizontalAlignment.Left, true));
        double pass = c.Gov == "autocracy" ? 0.97 : Math.Clamp(0.55 + 0.6 * (c.Coalition - 0.5) + 0.3 * (c.Approval - 0.4) - 0.003 * def.Pc, 0.2, 0.97);
        var foot = UI.HBox(10, UI.Dim($"Cost {def.Pc * (c.Gov == "autocracy" ? 0.7 : 1):0} political capital · {def.Delay} months to take effect · {UI.Pct(pass, 0)} chance to pass"));
        foot.AddChild(UI.Spacer(0, 0, true));
        string pkey = "policy:" + def.Id; var pending = Game.Staged(pkey);
        var dry = CommandProcessor.Apply(Game.World, Command.Enact(c.Id, def.Id), true);
        if (pending != null)
        {
            foot.AddChild(Cards.Chip(pending.Type == "enact" ? "Proposed: vote at the end of the turn" : "Repeal at the end of the turn", Pal.Warn));
            foot.AddChild(UI.Btn("Withdraw", () => { Game.Unstage(pkey); _result.Text = "↶ Withdrawn from the plan"; }, false, 100));
        }
        else if (ap == null || ap.Failed)
        {
            var b = UI.Btn("Add to plan", () => Act(Command.Enact(c.Id, def.Id)), true, 120); b.Disabled = !dry.Ok; if (!dry.Ok) b.TooltipText = dry.Message; foot.AddChild(b);
        }
        else foot.AddChild(UI.Btn("Plan repeal", () => Act(Command.Repeal(c.Id, def.Id)), false, 120));
        box.AddChild(foot);
        return UI.Card(box);
    }

    void Act(Command cmd) { PlanUi.Stage(cmd, t => _result.Text = t); _lastSig = -1; Refresh(); }
}
