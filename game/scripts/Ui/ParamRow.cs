using System;
using System.Globalization;
using System.Linq;
using Godot;
using Sim.Core.Model;
using Sim.Core.Policy;
using Sim.Core.Util;

namespace EconGame.Ui;

/// <summary>
/// One editable tax or benefit parameter: name with an explanatory tooltip, a slider (or a choice list), an exact entry box in the country's currency,
/// the change against what is in force now, and an undo button. Values are in parameter units (multiples of mean earnings, rates as fractions, years…);
/// the row converts to and from local currency.
/// </summary>
public partial class ParamRow : VBoxContainer
{
    public readonly PDef Def;
    /// <summary>The player moved the slider, typed a value or picked an option.</summary>
    public event Action<double>? Edited;
    /// <summary>The player pressed undo: forget the draft (and anything staged) for this parameter.</summary>
    public event Action? Undone;

    readonly Label _name, _note;
    readonly AppSlider? _slider;
    readonly OptionButton? _opt;
    readonly LineEdit? _edit;
    readonly Button _undo;
    bool _sync;
    double _factor = 1, _live;
    string _currency = "";

    public ParamRow(PDef def, string? label = null)
    {
        Def = def;
        AddThemeConstantOverride("separation", 3);
        _name = UI.Lbl(label ?? def.Label, 14, Pal.Text, true);
        _name.MouseFilter = MouseFilterEnum.Stop;
        _name.TooltipText = def.Tip;
        _note = UI.Lbl("", 12, Pal.Dim, false, HorizontalAlignment.Right);
        _note.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddChild(UI.HBox(8, _name, _note));

        _undo = UI.Btn("↺", () => Undone?.Invoke(), false, 30);
        _undo.TooltipText = "Undo this change (back to what is in force now)";
        _undo.Visible = false;

        if (def.Kind == PKind.Choice)
        {
            _opt = new OptionButton { CustomMinimumSize = new Vector2(220, 0), MouseDefaultCursorShape = CursorShape.PointingHand };
            foreach (var o in def.Options) _opt.AddItem(o);
            _opt.ItemSelected += i => { if (!_sync) Edited?.Invoke(i); };
            AddChild(UI.HBox(8, _opt, UI.Spacer(0, 0, true), _undo));
        }
        else
        {
            _slider = new AppSlider { SizeFlagsHorizontal = SizeFlags.ExpandFill, LabelWidth = def.Kind == PKind.Amount ? 108 : 84 };
            _slider.Setup(def.Min, Math.Max(def.Min + def.Step, def.Max), def.Step, def.Min, FormatSlider);
            _slider.Changed += v => { if (!_sync) Edited?.Invoke(v); };
            _edit = new LineEdit { CustomMinimumSize = new Vector2(104, 0), Alignment = HorizontalAlignment.Right, SelectAllOnFocus = true };
            _edit.TextSubmitted += _ => Commit();
            _edit.FocusExited += Commit;
            AddChild(UI.HBox(8, _slider, _edit, _undo));
        }
    }

    /// <summary>Extra line shown in the name's tooltip, after the parameter's own explanation.</summary>
    public string Hint { set => _name.TooltipText = Def.Tip + (value.Length > 0 ? "\n" + value : ""); }

    string FormatSlider(double v) => Format(v);

    string Format(double v) => Def.Kind switch
    {
        PKind.Amount => v <= 0 && Def.Key is "Inc.TaperStart" or "Chi.Threshold" or "Pay.EeUpper" or "Corp.SmallLimit" ? "none" : Fmt.Amount(_currency, v * _factor),
        PKind.Rate => Fmt.P(v, 1),
        PKind.Years => $"{v:0} yrs",
        PKind.Months => $"{v:0} mo",
        PKind.Factor => $"×{v:0.00}",
        _ => v.ToString("0.##", CultureInfo.InvariantCulture),
    };

    string EditText(double v) => Def.Kind switch
    {
        PKind.Amount => Math.Round(v * _factor).ToString("N0", CultureInfo.InvariantCulture),
        PKind.Rate => (v * 100).ToString("0.0#", CultureInfo.InvariantCulture) + "%",
        PKind.Factor => v.ToString("0.00", CultureInfo.InvariantCulture),
        _ => v.ToString("0", CultureInfo.InvariantCulture),
    };

    void Commit()
    {
        if (_edit == null || _sync) return;
        string t = new string(_edit.Text.Where(ch => char.IsDigit(ch) || ch == '.' || ch == '-').ToArray());
        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) { _edit.Text = EditText(_slider!.Value); return; }
        double v = Def.Kind switch { PKind.Amount => x / Math.Max(1e-9, _factor), PKind.Rate => x / 100, _ => x };
        v = Math.Clamp(v, Def.Min, Def.Max);
        if (Math.Abs(v - _slider!.Value) > 1e-9) Edited?.Invoke(v);
        else _edit.Text = EditText(v);
    }

    /// <summary>Refresh the row. <paramref name="effective"/> is what the page would show (draft, else staged, else live); <paramref name="live"/> is in force now.</summary>
    public void Sync(CountryState c, double effective, double live, string tag = "")
    {
        _sync = true;
        _currency = c.Currency; _live = live;
        _factor = Def.Kind == PKind.Amount ? FiscalParams.NominalFactor(c, Def.Key) : 1;
        bool changed = Math.Abs(effective - live) > 1e-9;
        if (_opt != null) _opt.Selected = Math.Clamp((int)Math.Round(effective), 0, Def.Options.Length - 1);
        else
        {
            // the slider covers a useful range around the values in play rather than the whole allowed span
            double top = Math.Min(Def.Max, Math.Max(Math.Max(effective, live) * 1.6, Def.Min + Def.Step * 20));
            if (Def.Kind is PKind.Years or PKind.Months or PKind.Factor) top = Def.Max;
            else if (Def.Kind == PKind.Rate) top = Math.Min(Def.Max, Math.Max(top, 0.12));
            if (!_slider!.Dragging) _slider.Max = Math.Max(_slider.Min + Def.Step, top);   // the range must not chase the thumb while it is being dragged
            _slider.Baseline = live;
            _slider.Format = FormatSlider;
            _slider.SetExact(effective);
            if (!_edit!.HasFocus()) _edit.Text = EditText(effective);
            _slider.QueueRedraw();
        }
        _undo.Visible = changed || tag != "";
        string chg = changed ? $"now {Format(live)} → {Format(effective)}" : "";
        _note.Text = string.Join("  ·  ", new[] { chg, tag }.Where(x => x != ""));
        _note.AddThemeColorOverride("font_color", changed ? Pal.Warn : Pal.Dim);
        _sync = false;
    }
}
