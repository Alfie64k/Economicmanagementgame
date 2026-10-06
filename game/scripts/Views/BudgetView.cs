using Godot;
using EconGame.Ui;

namespace EconGame.Views;

public partial class BudgetView : View
{
    public override string Title => "Budget";
    public BudgetView() { Page("Budget", "Coming in this build step."); }
    public override void Refresh() { }
}
