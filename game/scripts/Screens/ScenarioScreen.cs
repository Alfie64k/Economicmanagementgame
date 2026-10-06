using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.Core.Scoring;
using EconGame.App;
using EconGame.Ui;

namespace EconGame.Screens;

/// <summary>Campaign and scenario picker: tutorials first, then historical scenarios and long challenges.</summary>
public partial class ScenarioScreen : Control
{
    public override void _Ready()
    {
        var root = UI.VBox(12); var margin = UI.Margin(root, 22); margin.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(margin);
        root.AddChild(UI.HBox(12, UI.Btn("← Back", () => Main.Instance!.ShowMainMenu()), UI.H1("Scenarios & campaign")));
        var list = UI.VBox(10);
        string[] kinds = { "tutorial", "historical", "challenge" };
        string[] titles = { "Tutorials", "Historical scenarios", "Challenges" };
        for (int k = 0; k < kinds.Length; k++)
        {
            list.AddChild(UI.Lbl(titles[k], 20, Pal.Accent, true));
            foreach (var sc in Scenarios.All.Where(s => s.Kind == kinds[k])) list.AddChild(Card(sc));
        }
        root.AddChild(UI.Scroll(list));
    }

    Control Card(ScenarioDef sc)
    {
        var body = UI.VBox(6);
        var head = UI.HBox(10, UI.Lbl(sc.Name, 18, Pal.Text, true), UI.Spacer(0, 0, true),
            Cards.Chip(sc.Difficulty, sc.Difficulty == "Hard" ? Pal.Bad : sc.Difficulty == "Easy" ? Pal.Good : Pal.Accent),
            Cards.Chip($"{sc.Years} years", Pal.Faint));
        body.AddChild(head);
        body.AddChild(UI.Dim(sc.Description, 14, true));
        foreach (var g in sc.Goals) body.AddChild(UI.Lbl("◦ " + g.Label, 13, Pal.Dim));
        var start = UI.Btn("Play", () => { Game.NewGame(sc.Country, System.Enum.Parse<Sim.Core.Model.Difficulty>(sc.Difficulty), (ulong)(Time.GetTicksMsec() % 100000 + 1), sc); Main.Instance!.ShowGame(); }, true, 110);
        body.AddChild(UI.HBox(8, UI.Dim($"Country: {sc.Country}"), UI.Spacer(0, 0, true), start));
        return UI.Card(body);
    }
}
