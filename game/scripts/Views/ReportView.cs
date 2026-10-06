using Godot;
using EconGame.Ui;

namespace EconGame.Views;

public partial class ReportView : View
{
    public override string Title => "Report";
    public ReportView() { Page("Report", "Coming in this build step."); }
    public override void Refresh() { }
}
