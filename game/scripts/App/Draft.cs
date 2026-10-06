using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core.Model;
using Sim.Core.Policy;

namespace EconGame.App;

/// <summary>Uncommitted budget/tax changes the player is experimenting with. Shared by the Budget and Forecast pages.</summary>
public static class Draft
{
    public static readonly Dictionary<Tax, double> Taxes = new();
    public static readonly Dictionary<BudgetLine, double> Lines = new();
    public static event Action? Changed;

    public static void Clear() { Taxes.Clear(); Lines.Clear(); Changed?.Invoke(); }
    public static void Set(Tax t, double v, double current) { if (Math.Abs(v - current) < 1e-9) Taxes.Remove(t); else Taxes[t] = v; Changed?.Invoke(); }
    public static void Set(BudgetLine l, double v, double current) { if (Math.Abs(v - current) < 1e-9) Lines.Remove(l); else Lines[l] = v; Changed?.Invoke(); }
    public static bool Any => Taxes.Count + Lines.Count > 0;

    public static List<Command> ToCommands(CountryState c)
    {
        var cmds = new List<Command>();
        foreach (var kv in Taxes) cmds.Add(Command.SetTax(c.Id, kv.Key, kv.Value));
        foreach (var kv in Lines) cmds.Add(Command.SetBudget(c.Id, kv.Key, kv.Value));
        return cmds;
    }

    public static double PoliticalCost(World w, IEnumerable<Command> cmds) =>
        cmds.Sum(cmd => CommandProcessor.Apply(w, cmd, dryRun: true).PcCost);
}
