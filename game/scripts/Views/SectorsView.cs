using System;
using System.Linq;
using Godot;
using Sim.Core.Engine;
using Sim.Core.Model;
using EconGame.App;
using EconGame.Ui;

namespace EconGame.Views;

public partial class SectorsView : View
{
    public override string Title => "Sectors";
    readonly StackedChart _stack = new() { CustomMinimumSize = new Vector2(300, 240) };
    readonly StackedChart _share = new() { CustomMinimumSize = new Vector2(300, 240), Percent = true };
    readonly VBoxContainer _table = new();
    readonly Heatmap _io = new() { CustomMinimumSize = new Vector2(420, 260) };

    public SectorsView()
    {
        var page = Page("Sectors", "A six-sector input-output economy. Demand shifts, subsidies and capital flows change the mix over time.");
        var charts = UI.HBox(12);
        charts.AddChild(UI.Fill(Cards.Section("Value added by sector (real)", _stack), true, false));
        charts.AddChild(UI.Fill(Cards.Section("Share of GDP", _share), true, false));
        page.AddChild(charts);
        page.AddChild(Cards.Section("Sector detail", _table));
        page.AddChild(Cards.Section("Input-output linkages", _io, "Share of each using sector's gross output bought from each supplying sector. Darker = lighter dependence."));
    }

    public override void Refresh()
    {
        if (!Game.Running) return;
        var c = Game.Player; var h = Game.History(c.Id).Where(p => p.SectorVa.Length == Dim.Sectors).ToList();
        string[] names = Enum.GetNames<Sector>();
        var x = h.Select(p => (double)p.Month).ToArray();
        var layers = Enumerable.Range(0, Dim.Sectors).Select(s => new Series { Name = names[s], X = x, Y = h.Select(p => p.SectorVa[s]).ToArray(), Color = Pal.Series[s % 8] }).ToList();
        _stack.StartYear = _share.StartYear = Game.World.StartYear;
        _stack.YFormat = v => Money.Local(c, v); _stack.Set(layers); _share.Set(layers);

        foreach (var ch in _table.GetChildren().ToList()) ch.QueueFree();
        var head = UI.HBox(8, Hd("Sector", 150), Hd("Output", 120), Hd("Share", 70), Hd("Growth since start", 150), Hd("Workforce", 90), Hd("Capital", 120), Hd("Productivity", 110), Hd("Subsidy", 80));
        _table.AddChild(head);
        double tot = c.SectorVa.Sum();
        for (int s = 0; s < Dim.Sectors; s++)
        {
            double g = c.SectorVa[s] / Math.Max(1e-9, c.Va0[s]) - 1, gdpG = c.Gdp / c.Gdp0 - 1;
            _table.AddChild(UI.HBox(8, Cell(names[s], 150, true), Cell(Money.Local(c, c.SectorVa[s] * c.PriceLevel), 120), Cell(UI.Pct(c.SectorVa[s] / tot, 1), 70),
                Cell(UI.Sign(g * 100, "0.0") + "%", 150, false, g >= gdpG ? Pal.Good : Pal.Bad), Cell(UI.Pct(c.LabourShare[s], 1), 90), Cell(Money.Local(c, c.K[s]), 120),
                Cell((c.Tfp[s] / Math.Max(1e-9, c.Tfp.Average()) * 100).ToString("0") + " idx", 110), Cell(UI.Pct(c.SectorSubsidy[s], 1), 80)));
        }
        var a = new double[Dim.Sectors, Dim.Sectors];
        for (int i = 0; i < Dim.Sectors; i++) for (int j = 0; j < Dim.Sectors; j++) a[i, j] = IoTable.A[i, j];
        _io.Set(names, names, a);
    }

    static Label Hd(string t, int w) { var l = UI.Dim(t, 12); l.CustomMinimumSize = new Vector2(w, 0); return l; }
    static Label Cell(string t, int w, bool bold = false, Color? col = null) { var l = UI.Lbl(t, 14, col ?? Pal.Text, bold); l.CustomMinimumSize = new Vector2(w, 0); return l; }
}
