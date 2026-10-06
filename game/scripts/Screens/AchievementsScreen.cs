using Godot;
using EconGame.Ui;

namespace EconGame.Screens;

public partial class AchievementsScreen : Control
{
    public override void _Ready()
    {
        var main = Main.Instance!;
        var center = new CenterContainer(); center.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(center);
        var card = UI.Card(new AchievementsPanel(() => main.ShowMainMenu(), false), Pal.PanelAlt, 22); card.CustomMinimumSize = new Vector2(980, 0); center.AddChild(card);
    }
}
