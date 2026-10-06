using Godot;
using EconGame.App;
using EconGame.Ui;

namespace EconGame.Screens;

public partial class MainMenuScreen : Control
{
    public override void _Ready()
    {
        var center = new CenterContainer(); center.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(center);
        var box = UI.VBox(14);
        box.CustomMinimumSize = new Vector2(420, 0);
        box.AddChild(UI.Lbl("ECONOMIC MANAGEMENT GAME", 38, Pal.Text, true, HorizontalAlignment.Center));
        box.AddChild(UI.Lbl("Run a nation's economy. Balance budgets, policy, investment, trade and politics.", 16, Pal.Dim, false, HorizontalAlignment.Center, true));
        box.AddChild(UI.Spacer(0, 18));
        var main = Main.Instance!;
        box.AddChild(UI.Btn("New game (sandbox)", () => main.ShowCountrySelect(), true, 420));
        box.AddChild(UI.Btn("Scenarios & campaign", () => main.ShowScenarios(), false, 420));
        var cont = UI.Btn("Continue autosave", () => { if (Game.Load("auto")) main.ShowGame(); }, false, 420);
        cont.Disabled = !Game.HasSave("auto"); box.AddChild(cont);
        var load = UI.Btn("Load slot 1", () => { if (Game.Load("slot1")) main.ShowGame(); }, false, 420);
        load.Disabled = !Game.HasSave("slot1"); box.AddChild(load);
        box.AddChild(UI.Btn("Settings", () => main.ShowSettings(() => main.ShowMainMenu()), false, 420));
        box.AddChild(UI.Btn("Quit", () => GetTree().Quit(), false, 420));
        box.AddChild(UI.Spacer(0, 10));
        box.AddChild(UI.Lbl("Data are 2023/24 approximations compiled from public sources for gameplay, not forecasts.", 12, Pal.Faint, false, HorizontalAlignment.Center, true));
        center.AddChild(box);
    }
}
