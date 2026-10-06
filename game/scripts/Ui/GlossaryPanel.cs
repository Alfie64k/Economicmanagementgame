using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.Core.Scoring;

namespace EconGame.Ui;

/// <summary>The searchable glossary (F2): type to filter, pick a category, read how the game uses a term and jump to the page where it is changed.</summary>
public partial class GlossaryPanel : VBoxContainer
{
    readonly Action _close; readonly Action<string> _go;
    readonly LineEdit _q = new() { PlaceholderText = "Search terms, e.g. taper, NAIRU, multiplier…", ClearButtonEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    readonly VBoxContainer _list = UI.VBox(2), _detail = UI.VBox(8);
    readonly Label _count = UI.Dim("", 12);
    readonly Dictionary<string, Button> _chips = new();
    List<GlossaryTerm> _results = new();
    string _cat = ""; string _sel = "";

    public string Selected => _sel;
    public int ResultCount => _results.Count;
    public void Search(string q) { _q.Text = q; Rebuild(); }

    public GlossaryPanel(Action close, Action<string> go, string? start = null)
    {
        _close = close; _go = go;
        AddThemeConstantOverride("separation", 10);
        AddChild(UI.HBox(12, UI.H1("Glossary"), UI.Spacer(0, 0, true), UI.Dim("Esc or F2 closes", 12), UI.Btn("Close", close, false, 100)));
        _q.TextChanged += _ => Rebuild();
        _q.GuiInput += e =>
        {
            if (e is InputEventKey { Pressed: true } k && (k.Keycode == Key.Down || k.Keycode == Key.Up) && _results.Count > 0)
            {
                int i = Math.Max(0, _results.FindIndex(t => t.Term == _sel)) + (k.Keycode == Key.Down ? 1 : -1);
                Select(_results[Math.Clamp(i, 0, _results.Count - 1)].Term); _q.AcceptEvent();
            }
        };
        AddChild(_q);
        var chips = new HFlowContainer(); chips.AddThemeConstantOverride("h_separation", 6); chips.AddThemeConstantOverride("v_separation", 6);
        foreach (var c in new[] { "" }.Concat(Glossary.Categories))
        {
            string key = c; var chip = UI.Chip(c == "" ? "All" : c, c == "", () => { _cat = key; foreach (var kv in _chips) kv.Value.SetPressedNoSignal(kv.Key == key); Rebuild(); });
            _chips[c] = chip; chips.AddChild(chip);
        }
        AddChild(chips);
        var left = UI.Scroll(_list); left.CustomMinimumSize = new Vector2(280, 440);
        var right = UI.Scroll(UI.Card(_detail, Pal.Panel, 16)); right.CustomMinimumSize = new Vector2(0, 440); right.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        left.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        AddChild(UI.HBox(14, UI.VBox(4, _count, left), right));
        if (start != null && Glossary.Find(start) is { } t) _sel = t.Term;
        Rebuild();
    }

    public override void _Ready() { _q.CallDeferred(Control.MethodName.GrabFocus); }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true } k && (k.Keycode == Key.Escape || k.Keycode == Key.F2)) { GetViewport().SetInputAsHandled(); _close(); }
    }

    void Rebuild()
    {
        _results = Glossary.Search(_q.Text, _cat == "" ? null : _cat);
        foreach (var ch in _list.GetChildren().ToList()) { _list.RemoveChild(ch); ch.QueueFree(); }
        _count.Text = _results.Count == Glossary.Terms.Count ? $"{_results.Count} terms" : $"{_results.Count} of {Glossary.Terms.Count} terms";
        foreach (var t in _results)
        {
            string name = t.Term;
            var b = new Button { Text = name, ToggleMode = true, Alignment = HorizontalAlignment.Left, ThemeTypeVariation = StateStyles.ListRow, FocusMode = FocusModeEnum.All, MouseDefaultCursorShape = CursorShape.PointingHand, ClipText = true };
            b.SetPressedNoSignal(name == _sel);
            b.Pressed += () => Select(name);
            _list.AddChild(b);
        }
        if (_results.Count == 0) _list.AddChild(UI.Dim("No term matches. Try a shorter word.", 13, true));
        if (_results.Count > 0 && !_results.Any(t => t.Term == _sel)) _sel = _results[0].Term;
        else if (_results.Count == 0) _sel = "";
        ShowDetail();
    }

    void Select(string term)
    {
        _sel = term;
        for (int i = 0; i < _results.Count && i < _list.GetChildCount(); i++)
            if (_list.GetChild(i) is Button b) b.SetPressedNoSignal(_results[i].Term == term);
        ShowDetail();
    }

    void ShowDetail()
    {
        foreach (var ch in _detail.GetChildren().ToList()) { _detail.RemoveChild(ch); ch.QueueFree(); }
        var t = Glossary.Find(_sel);
        if (t == null) { _detail.AddChild(UI.Dim("Pick a term on the left.", 14, true)); return; }
        _detail.AddChild(UI.HBox(10, UI.Lbl(t.Term, 24, Pal.Text, true), Cards.Chip(t.Category, Pal.Accent)));
        if (t.Aka is { Length: > 0 }) _detail.AddChild(UI.Dim("Also called: " + string.Join(", ", t.Aka), 13, true));
        _detail.AddChild(UI.Lbl(t.Short, 16, Pal.Text, false, HorizontalAlignment.Left, true));
        _detail.AddChild(UI.Lbl("In this game", 13, Pal.Accent, true));
        _detail.AddChild(UI.Lbl(t.Game, 14, Pal.Dim, false, HorizontalAlignment.Left, true));
        if (t.See is { Length: > 0 })
        {
            _detail.AddChild(UI.Lbl("Related", 13, Pal.Accent, true));
            var row = new HFlowContainer(); row.AddThemeConstantOverride("h_separation", 6); row.AddThemeConstantOverride("v_separation", 6);
            foreach (var s in t.See) { string name = Glossary.Find(s)?.Term ?? s; var b = UI.Btn(name, () => ShowTerm(name), false, 0); row.AddChild(b); }
            _detail.AddChild(row);
        }
        if (t.Page != null) { string page = t.Page; _detail.AddChild(UI.Btn($"Go to {page}", () => { _close(); _go(page); }, true, 180)); }
    }

    /// <summary>Jump to a term even when the current filter would hide it.</summary>
    void ShowTerm(string name)
    {
        if (_results.All(t => t.Term != name)) { _q.Text = ""; _cat = ""; foreach (var kv in _chips) kv.Value.SetPressedNoSignal(kv.Key == ""); _sel = name; Rebuild(); }
        Select(name);
    }
}
