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
    /// <summary>Identity used to keep the selection when rows are recreated (default: the object itself).</summary>
    public Func<object, object>? RowKey;
    public event Action<object>? RowSelected;
    readonly ButtonGroup _group = new();
    ScrollContainer _scroll = new();
    object KeyOf(object o) => RowKey?.Invoke(o) ?? o;
    bool IsSelected(object row) => Selected != null && Equals(KeyOf(row), KeyOf(Selected));
    int _sortCol = -1; bool _desc;
    VBoxContainer _body = new();
    HBoxContainer _header = new();

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 4);
        AddChild(_header);
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        _scroll.SizeFlagsVertical = SizeFlags.ExpandFill; _scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _body.SizeFlagsHorizontal = SizeFlags.ExpandFill; _body.AddThemeConstantOverride("separation", 2);
        _scroll.AddChild(_body);
        AddChild(_scroll);
        BuildHeader();
    }

    void BuildHeader()
    {
        foreach (var c in _header.GetChildren()) c.QueueFree();
        _header.AddThemeConstantOverride("separation", 4);
        for (int i = 0; i < Columns.Count; i++)
        {
            int ci = i; var col = Columns[i];
            var b = new Button { Text = col.Title + (_sortCol == i ? (_desc ? " ▼" : " ▲") : ""), ThemeTypeVariation = StateStyles.ColumnHeader, FocusMode = FocusModeEnum.All, MouseDefaultCursorShape = CursorShape.PointingHand, Alignment = col.Align == HorizontalAlignment.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left };
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
        int scroll = _scroll.ScrollVertical;
        foreach (var c in _body.GetChildren()) { _body.RemoveChild(c); c.QueueFree(); }
        foreach (var r in _view) _body.AddChild(MakeRow(r));
        Callable.From(() => { if (IsInstanceValid(_scroll)) _scroll.ScrollVertical = scroll; }).CallDeferred();
    }

    Control MakeRow(object row)
    {
        var btn = new Button
        {
            FocusMode = FocusModeEnum.All, CustomMinimumSize = new Vector2(0, 32), ToggleMode = true, ButtonGroup = _group,
            ThemeTypeVariation = StateStyles.ListRow, MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        btn.SetPressedNoSignal(IsSelected(row));
        var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 4); h.MouseFilter = MouseFilterEnum.Ignore;
        h.SetAnchorsPreset(LayoutPreset.FullRect);
        h.OffsetLeft = 8; h.OffsetRight = -8;
        foreach (var col in Columns)
        {
            var l = UI.Lbl(col.Text(row), 14, col.Tint?.Invoke(row) ?? Pal.Text, false, col.Align);
            if (col.Width > 0) l.CustomMinimumSize = new Vector2(col.Width, 0); else l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            l.ClipText = true; l.VerticalAlignment = VerticalAlignment.Center;
            h.AddChild(l);
        }
        btn.AddChild(h);
        btn.Pressed += () => { Selected = row; RowSelected?.Invoke(row); };   // restyle in place: rows are not rebuilt, so hover and focus survive
        return btn;
    }

    /// <summary>Sets the selection and restyles the rows without raising RowSelected.</summary>
    public void SelectQuiet(object row)
    {
        Selected = row;
        int i = _view.FindIndex(r => IsSelected(r));
        if (i >= 0 && i < _body.GetChildCount() && _body.GetChild(i) is Button b) b.SetPressedNoSignal(true);
    }

    public void Select(object row)
    {
        Selected = row;
        int i = _view.FindIndex(r => IsSelected(r));
        if (i >= 0 && i < _body.GetChildCount() && _body.GetChild(i) is Button b)
        {
            b.SetPressedNoSignal(true);
            Callable.From(() => { if (IsInstanceValid(_scroll) && IsInstanceValid(b)) _scroll.EnsureControlVisible(b); }).CallDeferred();
        }
        RowSelected?.Invoke(row);
    }

    /// <summary>Row currently shown at the given offset from the selection (used for Up/Down keyboard stepping).</summary>
    public object? Step(int delta)
    {
        if (_view.Count == 0) return null;
        int i = Selected == null ? -1 : _view.FindIndex(r => IsSelected(r));
        return _view[Math.Clamp(i + delta, 0, _view.Count - 1)];
    }

    public IReadOnlyList<object> View => _view;
}
