using Godot;
using EconGame.Ui;

namespace EconGame.Views;

public partial class SectorsView : View
{
    public override string Title => "Sectors";
    public SectorsView() { Page("Sectors", "Coming in this build step."); }
    public override void Refresh() { }
}
