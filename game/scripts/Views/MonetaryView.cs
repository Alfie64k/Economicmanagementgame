using Godot;
using EconGame.Ui;

namespace EconGame.Views;

public partial class MonetaryView : View
{
    public override string Title => "Monetary";
    public MonetaryView() { Page("Monetary", "Coming in this build step."); }
    public override void Refresh() { }
}
