using System;
using System.Linq;
using Godot;
using EconGame.App;

namespace EconGame.Ui;

/// <summary>Every save on disk with its country, date and grade: load, overwrite, delete, or save the running game under a new name. Used from the main menu and from inside a game.</summary>
public partial class SavesPanel : VBoxContainer
{
    readonly bool _inGame; readonly Action _loaded, _close;
    readonly VBoxContainer _rows = UI.VBox(6);
    readonly LineEdit _name = new() { PlaceholderText = "Name for a new save", SizeFlagsHorizontal = SizeFlags.ExpandFill, MaxLength = 32 };
    readonly Label _msg = UI.Dim("", 13, true);
    string _confirmDelete = "";

    public SavesPanel(bool inGame, Action loaded, Action close)
    {
        _inGame = inGame; _loaded = loaded; _close = close;
        AddThemeConstantOverride("separation", 10);
        AddChild(UI.HBox(12, UI.H1("Saves"), UI.Spacer(0, 0, true), UI.Btn(inGame ? "Close" : "← Back", close, false, 100)));
        AddChild(UI.Dim("Three autosaves roll over each January (latest, previous, oldest). F5 quick-saves and F9 quick-loads during a game.", 13, true));
        if (inGame)
        {
            var save = UI.Btn("Save", SaveNew, true, 110);
            _name.TextSubmitted += _ => SaveNew();
            AddChild(UI.HBox(8, _name, save));
        }
        AddChild(_msg);
        var sc = UI.Scroll(_rows); sc.CustomMinimumSize = new Vector2(0, 380); AddChild(sc);
        Rebuild();
    }

    public override void _Input(InputEvent e)
    {
        if (_inGame && e is InputEventKey { Pressed: true, Keycode: Key.Escape }) { GetViewport().SetInputAsHandled(); _close(); }
    }

    void SaveNew()
    {
        string slot = SaveStore.Sanitise(_name.Text);
        if (slot.Length == 0) { _msg.Text = "Type a name first (letters, digits, spaces, hyphens)."; return; }
        if (slot is "auto" or "auto1" or "auto2" or "quick") { _msg.Text = "That name is reserved."; return; }
        _msg.Text = Game.Save(slot) ? $"Saved as “{slot.Replace('_', ' ')}”." : "The save failed. Check the disk is writable.";
        _name.Text = ""; Rebuild();
    }

    void Rebuild()
    {
        foreach (var ch in _rows.GetChildren().ToList()) { _rows.RemoveChild(ch); ch.QueueFree(); }
        var saves = SaveStore.List();
        if (saves.Count == 0) _rows.AddChild(UI.Dim("No saves yet. Press F5 in a game, or use Save above.", 14, true));
        foreach (var s in saves)
        {
            var info = s; string slot = s.Slot;
            string detail = s.HasDetails ? $"{s.Country} · {s.Date}" + (s.Grade != "" ? $" · grade {s.Grade}" : "") + (s.Scenario != "" ? $" · {s.Scenario}" : "") : "no details (older save)";
            var left = UI.VBox(2, UI.Lbl(s.Label, 15, Pal.Text, true), UI.Dim($"{detail}   ·   saved {SaveStore.When(s.SavedAt)}", 12, true));
            left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var row = UI.HBox(8, left);
            if (_confirmDelete == slot)
            {
                row.AddChild(UI.Lbl("Delete this save?", 13, Pal.Bad));
                row.AddChild(UI.Btn("Yes, delete", () => { SaveStore.Delete(slot); _confirmDelete = ""; _msg.Text = "Deleted."; Rebuild(); }, false, 110));
                row.AddChild(UI.Btn("Keep", () => { _confirmDelete = ""; Rebuild(); }, false, 80));
            }
            else
            {
                row.AddChild(UI.Btn("Load", () => { if (Game.Load(slot)) _loaded(); else _msg.Text = "That save could not be read."; }, true, 90));
                if (_inGame && slot is not ("auto" or "auto1" or "auto2")) row.AddChild(UI.Btn("Overwrite", () => { _msg.Text = Game.Save(slot) ? "Saved." : "The save failed."; Rebuild(); }, false, 110));
                row.AddChild(UI.Btn("Delete", () => { _confirmDelete = slot; Rebuild(); }, false, 90));
            }
            _rows.AddChild(UI.Card(row, Pal.PanelAlt, 10));
        }
    }
}
