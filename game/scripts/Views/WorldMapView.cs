using Godot;
using EconGame.Ui;

namespace EconGame.Views;

public partial class WorldMapView : View
{
    public override string Title => "WorldMap";
    public WorldMapView() { Page("WorldMap", "Coming in this build step."); }
    public override void Refresh() { }
}
