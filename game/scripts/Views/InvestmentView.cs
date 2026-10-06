using Godot;
using EconGame.Ui;

namespace EconGame.Views;

public partial class InvestmentView : View
{
    public override string Title => "Investment";
    public InvestmentView() { Page("Investment", "Coming in this build step."); }
    public override void Refresh() { }
}
