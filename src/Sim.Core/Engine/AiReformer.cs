using Sim.Core.Model;
using Sim.Core.Policy;

namespace Sim.Core.Engine;

/// <summary>AI governments occasionally enact catalogue policies that fit their style (no political-capital cost; delays still apply).</summary>
public static class AiReformer
{
    static readonly Dictionary<string, string[]> Agenda = new()
    {
        ["technocrat"] = new[] { "fiscal_rule", "competition_policy", "rnd_tax_credits", "apprenticeships", "housing_reform", "tax_compliance", "cb_independence", "carbon_tax_50", "renewables_subsidy" },
        ["populist"] = new[] { "ubi", "fossil_subsidies", "import_substitution", "nationalise_strategic", "agri_support", "universal_healthcare", "restrict_immigration" },
        ["exportled"] = new[] { "fta_network", "fdi_incentives", "sez", "industrial_manufacturing", "export_credit", "rnd_tax_credits", "deregulation", "apprenticeships" },
        ["resource"] = new[] { "sovereign_fund", "industrial_services", "fdi_incentives", "anti_corruption", "judicial_reform", "renewables_subsidy", "tax_compliance" },
    };

    public static void Step(World w, CountryState c)
    {
        if (w.Month == 0 || (w.Month + c.Id[0] + c.Id[1]) % 12 != 0) return;
        if (!Agenda.TryGetValue(c.Style, out var list)) return;
        int idx = w.Countries.IndexOf(c);
        var rng = w.CountryRng[idx];
        if (!rng.Chance(0.30)) return;
        var id = list[(int)(rng.NextDouble() * list.Length) % list.Length];
        var def = PolicyCatalog.Policy(id);
        if (def == null || c.Policies.Any(p => p.Id == id && !p.Failed)) return;
        if (c.Democracy < def.MinDemocracy || c.Democracy > def.MaxDemocracy) return;
        if (def.Group != "" && c.Policies.Any(p => !p.Failed && PolicyCatalog.Policy(p.Id)?.Group == def.Group)) return;
        c.Policies.Add(new ActivePolicy { Id = id, EnactedMonth = w.Month, ActivationMonth = w.Month + def.Delay });
        w.Log.Add(new LogEntry { Month = w.Month, Country = c.Id, Kind = "news", Text = $"{c.Name} announces: {def.Name}." });
    }
}
