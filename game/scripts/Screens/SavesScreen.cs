using Godot;
using EconGame.App;
using EconGame.Ui;

namespace EconGame.Screens;

/// <summary>The saves list as a full screen, for the main menu.</summary>
public partial class SavesScreen : Control
{
    public override void _Ready()
    {
        var main = Main.Instance!;
        var panel = new SavesPanel(false, () => main.ShowGame(), () => main.ShowMainMenu());
        var center = new CenterContainer(); center.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(center);
        var card = UI.Card(panel, Pal.PanelAlt, 22); card.CustomMinimumSize = new Vector2(760, 0); center.AddChild(card);
    }
}
