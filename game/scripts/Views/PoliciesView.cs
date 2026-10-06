using Godot;
using EconGame.Ui;

namespace EconGame.Views;

public partial class PoliciesView : View
{
    public override string Title => "Policies";
    public PoliciesView() { Page("Policies", "Coming in this build step."); }
    public override void Refresh() { }
}
