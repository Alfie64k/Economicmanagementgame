using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core.Model;
using Sim.Core.Policy;

namespace EconGame.App;

/// <summary>Uncommitted budget, tax and benefit changes the player is experimenting with. Shared by the Budget and Forecast pages.</summary>
public static class Draft
{
    public static readonly Dictionary<Tax, double> Taxes = new();
    public static readonly Dictionary<BudgetLine, double> Lines = new();
    /// <summary>Draft values of tax-code and benefit parameters, by <see cref="FiscalParams"/> key.</summary>
    public static readonly Dictionary<string, double> Fiscal = new();
    /// <summary>A draft income-tax band table (replaces the whole table when staged); null when the bands are untouched.</summary>
    public static List<(double From, double Rate)>? Bands;
    public static event Action? Changed;

    public static void Clear() { Taxes.Clear(); Lines.Clear(); Fiscal.Clear(); Bands = null; Changed?.Invoke(); }
    public static void Set(Tax t, double v, double current) { if (Math.Abs(v - current) < 1e-9) Taxes.Remove(t); else Taxes[t] = v; Changed?.Invoke(); }
    public static void Set(BudgetLine l, double v, double current) { if (Math.Abs(v - current) < 1e-9) Lines.Remove(l); else Lines[l] = v; Changed?.Invoke(); }
    /// <summary>Draft a tax-code parameter; <paramref name="current"/> is what is in force or already staged (setting it back removes the draft).</summary>
    public static void SetFiscal(string key, double v, double current) { if (Math.Abs(v - current) < 1e-9) Fiscal.Remove(key); else Fiscal[key] = v; Changed?.Invoke(); }
    public static void SetBands(List<(double From, double Rate)>? bands) { Bands = bands; Changed?.Invoke(); }
    public static void ClearFiscal(string key) { if (Fiscal.Remove(key)) Changed?.Invoke(); }
    public static bool Any => Taxes.Count + Lines.Count + Fiscal.Count > 0 || Bands != null;

    public static List<Command> ToCommands(CountryState c)
    {
        var cmds = new List<Command>();
        foreach (var kv in Taxes) cmds.Add(Command.SetTax(c.Id, kv.Key, kv.Value));
        foreach (var kv in Lines) cmds.Add(Command.SetBudget(c.Id, kv.Key, kv.Value));
        foreach (var kv in Fiscal.OrderBy(k => k.Key, StringComparer.Ordinal)) cmds.Add(Command.SetFiscal(c.Id, kv.Key, kv.Value));
        if (Bands != null) cmds.Add(Command.SetBands(c.Id, Bands));
        return cmds;
    }

    /// <summary>The tax code as it would stand after the staged plan and then the unsent draft, against the code in force.</summary>
    public static Dictionary<string, double> EffectiveCode(CountryState c, IEnumerable<Command> staged)
    {
        var cmds = staged.Where(q => q.Type == "fiscal").Concat(ToCommands(c).Where(q => q.Type == "fiscal"));
        return FiscalDraft.Apply(c, cmds);
    }

    public static double PoliticalCost(World w, IEnumerable<Command> cmds) =>
        cmds.Sum(cmd => CommandProcessor.Apply(w, cmd, dryRun: true).PcCost);
}
