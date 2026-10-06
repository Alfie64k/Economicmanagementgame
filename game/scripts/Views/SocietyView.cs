using Godot;
using EconGame.Ui;

namespace EconGame.Views;

public partial class SocietyView : View
{
    public override string Title => "Society";
    public SocietyView() { Page("Society", "Coming in this build step."); }
    public override void Refresh() { }
}
