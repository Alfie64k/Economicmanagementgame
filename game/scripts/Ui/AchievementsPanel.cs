using System;
using System.Linq;
using Godot;
using Sim.Core.Scoring;
using EconGame.App;

namespace EconGame.Ui;

/// <summary>Every achievement with its tier, what earns it and when you earned it.</summary>
public partial class AchievementsPanel : VBoxContainer
{
    public static Color TierColour(Tier t) => t switch { Tier.Gold => Pal.Warn, Tier.Silver => new Color("C0CAD6"), _ => new Color("C98B5B") };

    public AchievementsPanel(Action back, bool modal)
    {
        AddThemeConstantOverride("separation", 10);
        int total = Achievements.All.Count, got = Achievements.All.Count(a => Profile.Has(a.Id));
        AddChild(UI.HBox(12, UI.H1("Achievements"), UI.Lbl($"{got} of {total}", 18, Pal.Accent, true), UI.Spacer(0, 0, true), UI.Btn(modal ? "Close" : "← Back", back, false, 100)));
        AddChild(UI.Dim("Awarded for runs on Normal or Hard difficulty, including scenarios. Sandbox and Easy games are for learning and do not count. Everything stays on this computer.", 13, true));
        var grid = new GridContainer { Columns = 2 }; grid.AddThemeConstantOverride("h_separation", 10); grid.AddThemeConstantOverride("v_separation", 10);
        foreach (var a in Achievements.All.OrderByDescending(a => Profile.Has(a.Id)).ThenBy(a => a.Tier).ThenBy(a => a.Name, StringComparer.Ordinal))
        {
            var award = Profile.Get(a.Id); bool has = award != null;
            var head = UI.HBox(8, UI.Lbl((has ? "★ " : "☆ ") + a.Name, 16, has ? Pal.Text : Pal.Dim, true), Cards.Chip(a.Tier.ToString().ToLower(), TierColour(a.Tier)));
            if (a.AtEnd) head.AddChild(Cards.Chip("at the end of a run", Pal.Faint));
            var body = UI.VBox(4, head, UI.Lbl(a.Description, 13, Pal.Dim, false, HorizontalAlignment.Left, true),
                UI.Lbl(has ? $"Earned {SaveStore.When(award!.When)}" + (award.Country != "" ? $" as {award.Country}" : "") : "Not yet earned", 12, has ? Pal.Good : Pal.Faint));
            var card = UI.Card(body, has ? Pal.PanelAlt : Pal.Panel, 12); card.SizeFlagsHorizontal = SizeFlags.ExpandFill; grid.AddChild(card);
        }
        var sc = UI.Scroll(grid); sc.CustomMinimumSize = new Vector2(0, modal ? 420 : 520); AddChild(sc);
    }
}
