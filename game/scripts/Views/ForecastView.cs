using Godot;
using EconGame.Ui;

namespace EconGame.Views;

public partial class ForecastView : View
{
    public override string Title => "Forecast";
    public ForecastView() { Page("Forecast", "Coming in this build step."); }
    public override void Refresh() { }
}
