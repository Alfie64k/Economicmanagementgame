using Godot;
using EconGame.Ui;

namespace EconGame.Views;

/// <summary>Base class for content pages in the game shell.</summary>
public abstract partial class View : MarginContainer
{
    public abstract string Title { get; }
    public virtual string Icon => "";
    /// <summary>Called when the page is shown and (throttled) after each simulation tick.</summary>
    public abstract void Refresh();
    /// <summary>Cheap per-month hook; override for pages that must react to every tick.</summary>
    public virtual void OnTick() { }

    protected View()
    {
        foreach (var s in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) AddThemeConstantOverride(s, 16);
        SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
    }

    protected VBoxContainer Page(string title, string? sub = null)
    {
        var box = UI.VBox(12);
        box.AddChild(UI.H1(title));
        if (sub != null) box.AddChild(UI.Dim(sub, 14, true));
        var scroll = UI.Scroll(box);
        AddChild(scroll);
        return box;
    }
}
