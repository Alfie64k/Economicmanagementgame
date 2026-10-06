using Sim.Core.Engine;
using Sim.Core.Model;
using Sim.Core.Util;

namespace Sim.Core.Policy;

public sealed class CommandResult
{
    public bool Ok;
    public string Message = "";
    public double PcCost;
    public static CommandResult Fail(string m, double pc = 0) => new() { Ok = false, Message = m, PcCost = pc };
    public static CommandResult Pass(string m, double pc) => new() { Ok = true, Message = m, PcCost = pc };
}

/// <summary>Validates, prices (in political capital) and applies player commands.</summary>
public static class CommandProcessor
{
    public const int MaxConcurrentProjects = 6;

    /// <summary>Applies the command. With <paramref name="dryRun"/> only validates and prices it.</summary>
    public static CommandResult Apply(World w, Command cmd, bool dryRun = false)
    {
        var c = w.Find(cmd.Country);
        if (c == null) return CommandResult.Fail($"Unknown country {cmd.Country}");
        int idx = w.Countries.IndexOf(c);
        double pcMult = c.Gov == "autocracy" ? 0.7 : 1.0;

        CommandResult Spend(double cost, string msg, Action apply)
        {
            cost = Math.Round(cost * pcMult, 1);
            if (c.PoliticalCapital < cost) return CommandResult.Fail($"Not enough political capital ({c.PoliticalCapital:F0} of {cost:F0} needed)", cost);
            if (!dryRun) { c.PoliticalCapital -= cost; apply(); }
            return CommandResult.Pass(msg, cost);
        }

        switch (cmd.Type)
        {
            case "tax":
                {
                    if (!Enum.TryParse<Tax>(cmd.Id, out var t)) return CommandResult.Fail("Unknown tax");
                    double r0 = c.TaxRate[(int)t], r = Maths.Clamp(cmd.Value, 0, 0.9);
                    if (Math.Abs(r - r0) < 1e-9) return CommandResult.Pass("No change", 0);
                    double cost = Math.Min(40, 4 + 30 * Math.Abs(r - r0) / Math.Max(0.05, c.TaxRate0[(int)t]));
                    return Spend(cost, $"{t} tax {(r > r0 ? "raised" : "cut")} to {r:P1}", () => c.TaxRate[(int)t] = r);
                }
            case "budget":
                {
                    if (!Enum.TryParse<BudgetLine>(cmd.Id, out var l)) return CommandResult.Fail("Unknown budget line");
                    double s0 = c.Budget[(int)l], s = Maths.Clamp(cmd.Value, 0, 0.6);
                    double d = s - s0;
                    if (Math.Abs(d) < 1e-9) return CommandResult.Pass("No change", 0);
                    double cost = Math.Min(40, 2 + 150 * Math.Abs(d) * (d < 0 ? 1.5 : 1.0));
                    return Spend(cost, $"{l} budget set to {s:P2} of GDP", () => c.Budget[(int)l] = s);
                }
            case "rate":
                {
                    bool manual = cmd.Id == "Manual";
                    if (!manual) return Spend(0, "Central bank returned to its rule", () => c.RateMode = RateMode.Auto);
                    double cost = c.CbIndependence > 0.6 ? 12 : 4;
                    return Spend(cost, $"Policy rate pinned toward {cmd.Value:P2}", () =>
                    {
                        c.RateMode = RateMode.Manual; c.ManualRate = Maths.Clamp(cmd.Value, -0.01, 1.5);
                        if (c.CbIndependence > 0.6) c.Cred = Math.Max(0.05, c.Cred - 0.03);
                    });
                }
            case "minwage":
                return Spend(10, $"Minimum wage set to {cmd.Value:P0} of median", () => c.MinWageRatio = Maths.Clamp(cmd.Value, 0.2, 0.9));
            case "fxregime":
                {
                    if (!Enum.TryParse<FxRegime>(cmd.Id, out var r)) return CommandResult.Fail("Unknown regime");
                    return Spend(20, $"Exchange-rate regime: {r}", () => { c.Regime = r; if (r == FxRegime.Peg) c.Fx0 = c.Fx; });
                }
            case "autopilot":
                return CommandResult.Pass("Autopilot " + cmd.Id, 0).Also(() => { if (!dryRun) c.Autopilot = cmd.Id == "on"; });
            case "carbon":
                return Spend(10 + cmd.Value / 10, $"Carbon price {cmd.Value:F0}/t", () => c.CarbonPrice = Math.Max(0, cmd.Value));
            case "subsidy":
                {
                    if (!Enum.TryParse<Sector>(cmd.Id, out var s)) return CommandResult.Fail("Unknown sector");
                    double v = Maths.Clamp(cmd.Value, 0, 0.15);
                    return Spend(10, $"{s} subsidy {v:P1} of value added", () => c.SectorSubsidy[(int)s] = v);
                }
            case "tradedeal":
                {
                    var p = w.Find(cmd.Id);
                    if (p == null || p.Id == c.Id) return CommandResult.Fail("Unknown partner");
                    if (WorldEngine.Rel(w, c.Id, p.Id).Deal) return CommandResult.Fail("Already have a trade agreement");
                    double cost = Math.Round(15 * pcMult, 1);
                    if (c.PoliticalCapital < cost) return CommandResult.Fail($"Not enough political capital ({c.PoliticalCapital:F0} of {cost:F0} needed)", cost);
                    if (dryRun) return CommandResult.Pass($"Would cost {cost:F0} political capital", cost);
                    c.PoliticalCapital -= cost;
                    bool sameBloc = c.Bloc != "none" && c.Bloc == p.Bloc;
                    var ab = WorldEngine.Rel(w, c.Id, p.Id); var ba = WorldEngine.Rel(w, p.Id, c.Id);
                    double accept = Maths.Clamp(0.35 + (sameBloc ? 0.3 : 0) + (c.Region == p.Region ? 0.1 : 0) + (c.Democracy > 0.5 && p.Democracy > 0.5 ? 0.1 : 0)
                                                - (ab.ExtraTariff > 0 || ba.ExtraTariff > 0 ? 0.4 : 0) - (ab.Sanction || ba.Sanction ? 0.6 : 0), 0.05, 0.9);
                    if (p.Id != w.PlayerId && !w.CountryRng[w.Countries.IndexOf(p)].Chance(accept))
                    {
                        c.PoliticalCapital += cost * 0.5;
                        return CommandResult.Fail($"{p.Name} declined a trade agreement ({accept:P0} chance of acceptance)", cost * 0.5);
                    }
                    ab.Deal = ba.Deal = true; ab.DealStart = ba.DealStart = w.Month;
                    return CommandResult.Pass($"Trade agreement signed with {p.Name}", cost);
                }
            case "tariff":
                {
                    var p = w.Find(cmd.Id);
                    if (p == null || p.Id == c.Id) return CommandResult.Fail("Unknown partner");
                    double v = Maths.Clamp(cmd.Value, 0, 0.5);
                    var r = WorldEngine.Rel(w, c.Id, p.Id);
                    return Spend(10 + 100 * Math.Abs(v - r.ExtraTariff), $"Extra tariff on {p.Name}: {v:P0}", () => r.ExtraTariff = v);
                }
            case "sanction":
                {
                    var p = w.Find(cmd.Id);
                    if (p == null || p.Id == c.Id) return CommandResult.Fail("Unknown target");
                    bool on = cmd.Value > 0;
                    var r = WorldEngine.Rel(w, c.Id, p.Id);
                    return Spend(on ? 20 : 5, on ? $"Sanctions imposed on {p.Name}" : $"Sanctions on {p.Name} lifted", () => r.Sanction = on);
                }
            case "alliance":
                {
                    var p = w.Find(cmd.Id);
                    if (p == null || p.Id == c.Id) return CommandResult.Fail("Unknown partner");
                    var ab = WorldEngine.Rel(w, c.Id, p.Id); var ba = WorldEngine.Rel(w, p.Id, c.Id);
                    if (ab.Alliance) return CommandResult.Fail("Already allied");
                    double accept = Maths.Clamp(0.25 + (c.Region == p.Region ? 0.2 : 0) + (c.Gov == p.Gov ? 0.2 : -0.1) + (ab.Deal ? 0.2 : 0) - (ab.Sanction || ba.Sanction ? 0.8 : 0), 0.02, 0.9);
                    double cost = Math.Round(25 * pcMult, 1);
                    if (c.PoliticalCapital < cost) return CommandResult.Fail($"Not enough political capital ({c.PoliticalCapital:F0} of {cost:F0} needed)", cost);
                    if (dryRun) return CommandResult.Pass($"Would cost {cost:F0} political capital", cost);
                    c.PoliticalCapital -= cost;
                    if (p.Id != w.PlayerId && !w.CountryRng[w.Countries.IndexOf(p)].Chance(accept))
                    { c.PoliticalCapital += cost * 0.5; return CommandResult.Fail($"{p.Name} declined an alliance ({accept:P0})", cost * 0.5); }
                    ab.Alliance = ba.Alliance = true;
                    return CommandResult.Pass($"Alliance formed with {p.Name}", cost);
                }
            case "aid":
                {
                    var p = w.Find(cmd.Id);
                    if (p == null || p.Id == c.Id) return CommandResult.Fail("Unknown recipient");
                    double v = Maths.Clamp(cmd.Value, 0.0005, 0.02);
                    return Spend(5 + 400 * v, $"Aid of {v:P2} of GDP sent to {p.Name}", () =>
                    {
                        c.OtherRevenue -= v * c.GdpNominal * 6;
                        double usd = v * c.GdpUsdBn;
                        p.OtherRevenue += usd * p.Fx * 6;
                        p.Approval = Maths.Clamp(p.Approval + Math.Min(0.03, usd / Math.Max(1, p.GdpUsdBn) * 2), 0.02, 0.98);
                        c.Approval = Maths.Clamp(c.Approval - 5 * v, 0.02, 0.98);
                    });
                }
            case "enact": return Enact(w, c, idx, cmd.Id, dryRun, pcMult);
            case "repeal":
                {
                    var ap = c.Policies.FirstOrDefault(p => p.Id == cmd.Id);
                    var def = PolicyCatalog.Policy(cmd.Id);
                    if (ap == null || def == null) return CommandResult.Fail("Policy not enacted");
                    return Spend(def.Pc * 0.5, $"Repealed {def.Name}", () => { PolicyEngine.Deactivate(c, ap, def); c.Policies.Remove(ap); });
                }
            case "project": return StartProject(w, c, idx, cmd, dryRun, pcMult);
            case "cancelproject":
                {
                    int i = (int)cmd.Value;
                    var live = c.Projects.Where(p => !p.Done).ToList();
                    if (i < 0 || i >= live.Count) return CommandResult.Fail("No such project");
                    return Spend(10, $"Cancelled {live[i].Id} after spending {live[i].Spent:F0}", () => c.Projects.Remove(live[i]));
                }
        }
        return CommandResult.Fail("Unknown command " + cmd.Type);
    }

    static CommandResult Also(this CommandResult r, Action a) { a(); return r; }

    static CommandResult Enact(World w, CountryState c, int idx, string id, bool dry, double pcMult)
    {
        var def = PolicyCatalog.Policy(id);
        if (def == null) return CommandResult.Fail("Unknown policy");
        if (c.Policies.Any(p => p.Id == id && !p.Failed)) return CommandResult.Fail("Already enacted");
        if (c.Democracy < def.MinDemocracy || c.Democracy > def.MaxDemocracy) return CommandResult.Fail("Not available under this political system");
        if (def.Group != "" && c.Policies.Any(p => !p.Failed && PolicyCatalog.Policy(p.Id)?.Group == def.Group))
            return CommandResult.Fail($"Conflicts with an enacted '{def.Group}' policy; repeal it first");
        double cost = Math.Round(def.Pc * pcMult, 1);
        if (c.PoliticalCapital < cost) return CommandResult.Fail($"Not enough political capital ({c.PoliticalCapital:F0} of {cost:F0} needed)", cost);
        if (dry) return CommandResult.Pass($"Would cost {cost:F0} political capital", cost);

        c.PoliticalCapital -= cost;
        double pass = c.Gov == "autocracy" ? 0.97 : Maths.Clamp(0.55 + 0.6 * (c.Coalition - 0.5) + 0.3 * (c.Approval - 0.4) - 0.003 * def.Pc, 0.2, 0.97);
        var ap = new ActivePolicy { Id = id, EnactedMonth = w.Month, ActivationMonth = w.Month + def.Delay };
        if (!w.CountryRng[idx].Chance(pass))
        {
            ap.Failed = true; c.Policies.Add(ap);
            c.PoliticalCapital += cost * 0.5; // half the capital is recovered after a lost vote
            return CommandResult.Fail($"The legislature rejected {def.Name} ({pass:P0} chance of passing)", cost * 0.5);
        }
        c.Policies.Add(ap);
        return CommandResult.Pass($"{def.Name} passed; takes effect in {def.Delay} months", cost);
    }

    static CommandResult StartProject(World w, CountryState c, int idx, Command cmd, bool dry, double pcMult)
    {
        var def = PolicyCatalog.Project(cmd.Id);
        if (def == null) return CommandResult.Fail("Unknown project");
        if (c.Projects.Count(p => !p.Done) >= MaxConcurrentProjects) return CommandResult.Fail("Delivery capacity reached: finish or cancel a project first");
        double scale = Maths.Clamp(cmd.Value <= 0 ? 1 : cmd.Value, 0.25, 3.0);
        double cost = Math.Round(def.Pc * (0.6 + 0.4 * scale) * pcMult, 1);
        if (c.PoliticalCapital < cost) return CommandResult.Fail($"Not enough political capital ({c.PoliticalCapital:F0} of {cost:F0} needed)", cost);
        if (dry) return CommandResult.Pass($"Would cost {cost:F0} political capital", cost);
        c.PoliticalCapital -= cost;
        var rng = w.CountryRng[idx];
        Enum.TryParse<Asset>(def.Asset, out var asset);
        c.Projects.Add(new Project
        {
            Id = def.Id, Asset = def.Asset, Total = def.Cost * c.Potential * scale, PlannedMonths = def.Months,
            Overrun = Maths.Clamp(1 + Math.Max(0, rng.Normal() * 0.2 + 0.15) + 1.0 * c.Corruption * rng.NextDouble(), 0.9, 2.5),
            Delay = Maths.Clamp(1 + Math.Max(0, rng.Normal() * 0.2 + 0.10) + 0.6 * c.Corruption * rng.NextDouble(), 0.9, 2.0),
            Bonus = def.Bonus * scale, MaintRate = def.Maint, StartMonth = w.Month,
        });
        return CommandResult.Pass($"{def.Name} started (budget {def.Cost * scale:P1} of GDP over {def.Months} months)", cost);
    }
}
