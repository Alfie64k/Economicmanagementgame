using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace EconGame.Ui;

public sealed class Column
{
    public string Title = "";
    public Func<object, string> Text = _ => "";
    public Func<object, IComparable> Key = _ => 0;
    public Func<object, Color?>? Tint;
    public int Width = 100;          // 0 = expand
    public HorizontalAlignment Align = HorizontalAlignment.Left;
}

/// <summary>Sortable, filterable table built from controls. Rows are arbitrary objects; columns provide text and sort keys.</summary>
public partial class DataTable : VBoxContainer
{
    public List<Column> Columns = new();
    List<object> _rows = new();
    List<object> _view = new();
    public Func<object, bool>? Filter;
    public object? Selected;
    public event Action<object>? RowSelected;
    int _sortCol = -1; bool _desc;
    VBoxContainer _body = new();
    HBoxContainer _header = new();

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 4);
        AddChild(_header);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill; scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _body.SizeFlagsHorizontal = SizeFlags.ExpandFill; _body.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(_body);
        AddChild(scroll);
        BuildHeader();
    }

    void BuildHeader()
    {
        foreach (var c in _header.GetChildren()) c.QueueFree();
        _header.AddThemeConstantOverride("separation", 4);
        for (int i = 0; i < Columns.Count; i++)
        {
            int ci = i; var col = Columns[i];
            var b = new Button { Text = col.Title + (_sortCol == i ? (_desc ? " ▼" : " ▲") : ""), Flat = true, FocusMode = FocusModeEnum.None, Alignment = col.Align == HorizontalAlignment.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left };
            b.AddThemeColorOverride("font_color", Pal.Dim); b.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(13 * Pal.TextScale));
            if (col.Width > 0) b.CustomMinimumSize = new Vector2(col.Width, 0); else b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            b.Pressed += () => { if (_sortCol == ci) _desc = !_desc; else { _sortCol = ci; _desc = false; } BuildHeader(); Refresh(); };
            _header.AddChild(b);
        }
    }

    public void SetRows(IEnumerable<object> rows) { _rows = rows.ToList(); if (IsInsideTree()) { BuildHeader(); Refresh(); } }

    public void Refresh()
    {
        _view = _rows.Where(r => Filter == null || Filter(r)).ToList();
        if (_sortCol >= 0) { var key = Columns[_sortCol].Key; _view = (_desc ? _view.OrderByDescending(key) : _view.OrderBy(key)).ToList(); }
        foreach (var c in _body.GetChildren()) c.QueueFree();
        foreach (var r in _view) _body.AddChild(MakeRow(r));
    }

    Control MakeRow(object row)
    {
        var btn = new Button { Flat = true, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 32), ToggleMode = false };
        bool sel = ReferenceEquals(row, Selected);
        btn.AddThemeStyleboxOverride("normal", AppTheme.Box(sel ? Pal.PanelHi : new Color(0, 0, 0, 0), 6, sel ? Pal.Accent : null, sel ? 1 : 0));
        btn.AddThemeStyleboxOverride("hover", AppTheme.Box(Pal.PanelAlt, 6));
        btn.AddThemeStyleboxOverride("pressed", AppTheme.Box(Pal.PanelHi, 6));
        var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 4); h.MouseFilter = MouseFilterEnum.Ignore;
        h.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var col in Columns)
        {
            var l = UI.Lbl(col.Text(row), 14, col.Tint?.Invoke(row) ?? Pal.Text, false, col.Align);
            if (col.Width > 0) l.CustomMinimumSize = new Vector2(col.Width, 0); else l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            l.ClipText = true; l.VerticalAlignment = VerticalAlignment.Center;
            h.AddChild(l);
        }
        btn.AddChild(h);
        btn.Pressed += () => { Selected = row; Refresh(); RowSelected?.Invoke(row); };
        return btn;
    }

    public void Select(object row) { Selected = row; Refresh(); RowSelected?.Invoke(row); }
    public IReadOnlyList<object> View => _view;
}
