using Sim.Core.Model;
using Sim.Core.Util;

namespace Sim.Core.Policy;

/// <summary>One-line, player-facing descriptions of commands for the turn plan (before the command has run).</summary>
public static class PlanText
{
    public static readonly Dictionary<Tax, string> TaxNames = new()
    {
        [Tax.Income] = "Income tax", [Tax.Corporate] = "Corporation tax", [Tax.Consumption] = "VAT / consumption tax",
        [Tax.Payroll] = "Payroll & social contributions", [Tax.Tariff] = "Import tariffs",
    };

    public static readonly Dictionary<BudgetLine, string> LineNames = new()
    {
        [BudgetLine.Social] = "Pensions & welfare", [BudgetLine.Health] = "Health services", [BudgetLine.Education] = "Education", [BudgetLine.Defence] = "Defence",
        [BudgetLine.Infrastructure] = "Infrastructure", [BudgetLine.RnD] = "Research & development", [BudgetLine.Green] = "Energy transition", [BudgetLine.Housing] = "Housing",
        [BudgetLine.Digital] = "Digital & broadband", [BudgetLine.Admin] = "Administration & justice",
    };

    public static string Describe(World w, Command cmd)
    {
        var c = w.Find(cmd.Country); if (c == null) return cmd.Type;
        string Who(string id) => w.Find(id)?.Name ?? id;
        switch (cmd.Type)
        {
            case "tax" when Enum.TryParse<Tax>(cmd.Id, out var t):
                return $"{TaxNames[t]}: {Fmt.P(c.TaxRate[(int)t], 1)} → {Fmt.P(Maths.Clamp(cmd.Value, 0, 0.9), 1)}";
            case "budget" when Enum.TryParse<BudgetLine>(cmd.Id, out var l):
                return $"{LineNames[l]}: {Fmt.P(c.Budget[(int)l], 2)} → {Fmt.P(Maths.Clamp(cmd.Value, 0, 0.6), 2)} of GDP";
            case "fiscal":
                {
                    if (c.Fiscal is not { Init: true } f) return "Change the tax code";
                    if (cmd.Id == FiscalParams.BandsKey)
                        return FiscalParams.DecodeBands(cmd.Data) is { } nb ? "Income-tax bands: " + FiscalParams.ShowBands(c, nb) : "Income-tax bands";
                    var def = FiscalParams.Def(cmd.Id);
                    if (def == null) return "Change the tax code";
                    double nv = Maths.Clamp(cmd.Value, def.Min, def.Max);
                    return $"{def.Group}: {def.Label} {FiscalParams.Show(c, cmd.Id, f.Get(cmd.Id))} → {FiscalParams.Show(c, cmd.Id, nv)}";
                }
            case "rate":
                return cmd.Id == "Manual" ? $"Pin the policy rate at {Fmt.P(cmd.Value, 2)}" : "Return the central bank to its rule";
            case "minwage": return $"Minimum wage: {Fmt.P(c.MinWageRatio, 0)} → {Fmt.P(cmd.Value, 0)} of the median";
            case "fxregime": return $"Exchange-rate regime: {c.Regime} → {cmd.Id}";
            case "autopilot": return "Cabinet autopilot " + cmd.Id;
            case "carbon": return $"Carbon price: {c.CarbonPrice:0} → {cmd.Value:0} per tonne";
            case "subsidy" when Enum.TryParse<Sector>(cmd.Id, out var s):
                return $"{s} subsidy: {Fmt.P(c.SectorSubsidy[(int)s], 1)} → {Fmt.P(cmd.Value, 1)} of value added";
            case "tradedeal": return $"Propose a trade agreement to {Who(cmd.Id)}";
            case "alliance": return $"Propose an alliance to {Who(cmd.Id)}";
            case "tariff": return $"Extra tariff on {Who(cmd.Id)}: {Fmt.P(cmd.Value, 0)}";
            case "sanction": return cmd.Value > 0 ? $"Impose sanctions on {Who(cmd.Id)}" : $"Lift sanctions on {Who(cmd.Id)}";
            case "aid": return $"Send {Fmt.P(cmd.Value, 2)} of GDP in aid to {Who(cmd.Id)}";
            case "enact": return $"Put {PolicyCatalog.Policy(cmd.Id)?.Name ?? cmd.Id} to the legislature";
            case "repeal": return $"Repeal {PolicyCatalog.Policy(cmd.Id)?.Name ?? cmd.Id}";
            case "project": return $"Start {PolicyCatalog.Project(cmd.Id)?.Name ?? cmd.Id}" + (Math.Abs(cmd.Value - 1) > 1e-9 && cmd.Value > 0 ? $" at {cmd.Value:0.##}× scale" : "");
            case "cancelproject": return $"Cancel {(cmd.Id != "" ? PolicyCatalog.Project(cmd.Id)?.Name ?? cmd.Id : "project #" + ((int)cmd.Value + 1))}";
        }
        return cmd.Type;
    }
}
