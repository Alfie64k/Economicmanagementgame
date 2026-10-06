using Godot;
using EconGame.Ui;

namespace EconGame.Views;

public partial class TradeView : View
{
    public override string Title => "Trade";
    public TradeView() { Page("Trade", "Coming in this build step."); }
    public override void Refresh() { }
}
