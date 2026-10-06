using Sim.Core.Model;
using Sim.Core.Util;

namespace Sim.Core.Policy;

/// <summary>Monthly processing of enacted policies and the project pipeline.</summary>
public static class PolicyEngine
{
    public static void Step(World w, CountryState c)
    {
        foreach (var ap in c.Policies)
        {
            if (ap.Active || ap.Failed || w.Month < ap.ActivationMonth) continue;
            var def = PolicyCatalog.Policy(ap.Id);
            if (def == null) continue;
            Activate(c, ap, def);
            w.Log.Add(new LogEntry { Month = w.Month, Country = c.Id, Kind = "policy", Text = $"{def.Name} is now in force." });
        }

        double flow = 0, maint = 0;
        const double dt = 1.0 / 12;
        for (int i = 0; i < c.Projects.Count; i++)
        {
            var p = c.Projects[i];
            if (p.Done) { maint += p.Total * p.MaintRate; continue; }
            double monthly = p.Total * p.Overrun / (p.PlannedMonths * p.Delay);
            p.Spent += monthly; p.Elapsed += 1;
            flow += monthly * 12;
            if (p.Elapsed >= p.ActualMonths)
            {
                p.Done = true;
                if (Enum.TryParse<Asset>(p.Asset, out var a)) c.AssetBoost[(int)a] += p.Bonus * (1 - 0.4 * c.Corruption);
                string note = p.Overrun > 1.25 ? $" (cost overrun {Fmt.P(p.Overrun - 1, 0)}" + (p.Delay > 1.15 ? $", {Fmt.P(p.Delay - 1, 0)} late)" : ")") : p.Delay > 1.15 ? $" ({Fmt.P(p.Delay - 1, 0)} late)" : "";
                w.Log.Add(new LogEntry { Month = w.Month, Country = c.Id, Kind = "policy", Text = $"Project completed: {PolicyCatalog.Project(p.Id)?.Name ?? p.Id}{note}." });
            }
        }
        c.ProjectFlow = flow; c.MaintFlow = maint;

        double sub = 0;
        for (int s = 0; s < Dim.Sectors; s++) sub += c.SectorSubsidy[s] * c.SectorVa[s];
        c.SubsidyCost = sub;
        _ = dt;
    }

    public static void Activate(CountryState c, ActivePolicy ap, PolicyDef def)
    {
        ap.Active = true;
        foreach (var kv in def.Mods) c.ModTarget[kv.Key] = (c.ModTarget.TryGetValue(kv.Key, out var v) ? v : 0) + kv.Value;
        foreach (var kv in def.Budget) if (Enum.TryParse<BudgetLine>(kv.Key, out var l)) c.Budget[(int)l] = Math.Max(0, c.Budget[(int)l] + kv.Value);
        foreach (var kv in def.Subsidy) if (Enum.TryParse<Sector>(kv.Key, out var s)) c.SectorSubsidy[(int)s] += kv.Value;
        if (def.Carbon is double cp) c.CarbonPrice = cp;
        if (def.OneOffRevenue != 0) c.OtherRevenue += def.OneOffRevenue * c.GdpNominal * 6; // booked over ~2 months (annualised flow)
        c.Approval = Maths.Clamp(c.Approval + def.ApprovalShock, 0.02, 0.98);
    }

    public static void Deactivate(CountryState c, ActivePolicy ap, PolicyDef def)
    {
        if (!ap.Active) return;
        foreach (var kv in def.Mods) c.ModTarget[kv.Key] = (c.ModTarget.TryGetValue(kv.Key, out var v) ? v : 0) - kv.Value;
        foreach (var kv in def.Budget) if (Enum.TryParse<BudgetLine>(kv.Key, out var l)) c.Budget[(int)l] = Math.Max(0, c.Budget[(int)l] - kv.Value);
        foreach (var kv in def.Subsidy) if (Enum.TryParse<Sector>(kv.Key, out var s)) c.SectorSubsidy[(int)s] = Math.Max(0, c.SectorSubsidy[(int)s] - kv.Value);
        if (def.Carbon != null) c.CarbonPrice = 0;
    }
}
