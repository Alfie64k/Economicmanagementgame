using Sim.Core.Model;
using Sim.Core.Util;

namespace Sim.Core.Policy;

public enum Severity { Info, Warning, Alert }

public sealed class AdvisorNote
{
    public string Advisor = "";
    public Severity Severity;
    public string Key = "";
    public string Text = "";
    public Command? Suggestion;
}

/// <summary>Rule-based cabinet. Advisors deliberately pull in different directions (fiscal hawk vs social minister).</summary>
public static class Advisors
{
    public static List<AdvisorNote> Generate(World w, CountryState c)
    {
        var n = new List<AdvisorNote>();
        void Add(string who, Severity sev, string key, string text, Command? sug = null) =>
            n.Add(new AdvisorNote { Advisor = who, Severity = sev, Key = key, Text = text, Suggestion = sug });

        double ceiling = Math.Max(0.60, c.DebtGdp0 + 0.15);
        double debt = c.DebtToGdp, def = c.DeficitToGdp;
        double revShare = c.Revenue > 0 ? c.Interest / c.Revenue : 0;

        // ---- Chancellor / finance minister ----
        if (debt > ceiling && def > 0.03)
            Add("Finance Minister", debt > ceiling + 0.3 ? Severity.Alert : Severity.Warning, "debt",
                $"Debt is {Fmt.P(debt, 0)} of GDP with a {Fmt.P(def, 1)} deficit. A consolidation of about {Fmt.P(Math.Max(0.01, def - 0.03), 1)} of GDP is needed to stabilise it; I recommend raising consumption tax first.",
                Command.SetTax(c.Id, Tax.Consumption, c.TaxRate[(int)Tax.Consumption] * 1.08));
        else if (def > 0.06)
            Add("Finance Minister", Severity.Warning, "deficit", $"The deficit has reached {Fmt.P(def, 1)} of GDP. Investors will start to price this in.");
        if (c.RiskPremium > 0.02)
            Add("Finance Minister", c.RiskPremium > 0.05 ? Severity.Alert : Severity.Warning, "spread",
                $"Sovereign spreads are {c.RiskPremium * 10000:F0}bp above their baseline and 10-year yields are {Fmt.P(c.Yield10, 1)}.");
        if (revShare > 0.15)
            Add("Finance Minister", Severity.Warning, "interest", $"Debt interest now absorbs {Fmt.P(revShare, 0)} of revenue, crowding out services.");
        if (debt < ceiling - 0.2 && def < 0.02 && c.Gap < 0.01)
            Add("Finance Minister", Severity.Info, "space", $"We have fiscal space (debt {Fmt.P(debt, 0)}, deficit {Fmt.P(def, 1)}). A productive investment programme would be affordable.");

        // ---- Governor of the central bank ----
        double rule = c.NaturalRate + c.Inflation + Engine.MacroEngine.InflationResponse(c.Inflation - c.InflTarget) + Maths.Clamp(c.Gap, -0.15, 0.10);
        if (c.Inflation > c.InflTarget + 0.02)
            Add("Central Bank Governor", c.Inflation > c.InflTarget + 0.06 ? Severity.Alert : Severity.Warning, "inflation",
                $"Inflation is {Fmt.P(c.Inflation, 1)} against a {Fmt.P(c.InflTarget, 0)} target. The rule points to a policy rate near {Fmt.P(rule, 1)}; we are at {Fmt.P(c.PolicyRate, 1)}.");
        else if (c.Inflation < c.InflTarget - 0.015 && c.Gap < -0.01)
            Add("Central Bank Governor", Severity.Warning, "deflation", $"Inflation of {Fmt.P(c.Inflation, 1)} and a negative output gap raise deflation risk. Rates are {Fmt.P(c.PolicyRate, 1)}.");
        if (c.RateMode == RateMode.Manual && Math.Abs(c.PolicyRate - rule) > 0.02)
            Add("Central Bank Governor", Severity.Warning, "manual", $"The policy rate is being held {Math.Abs(c.PolicyRate - rule) * 10000:F0}bp {(c.PolicyRate > rule ? "above" : "below")} what the rule suggests; expectations may de-anchor.");

        // ---- Trade & industry ----
        if (c.CaToGdp < -0.05)
            Add("Trade Secretary", Severity.Warning, "ca", $"The current account deficit is {Fmt.P(-c.CaToGdp, 1)} of GDP and reserves cover {c.Reserves:F1} months of imports.");
        if (c.Reserves < 3 && c.Regime != FxRegime.Float)
            Add("Trade Secretary", Severity.Alert, "reserves", $"Reserves are down to {c.Reserves:F1} months of imports; the {c.Regime.ToString().ToLower()} regime is vulnerable.");
        if (c.FxChange > 0.12)
            Add("Trade Secretary", Severity.Warning, "fx", $"The currency is depreciating at {Fmt.P(c.FxChange, 0)} a year, pushing up import prices.");
        if (c.AssetIdx[(int)Asset.Infrastructure] < 0.9)
            Add("Trade Secretary", Severity.Info, "infra", $"Infrastructure quality is {Fmt.P(c.AssetIdx[(int)Asset.Infrastructure], 0)} of baseline; firms report it as a constraint on investment.");

        // ---- Social / work & pensions ----
        if (c.Unemp > c.NairU + 0.02)
            Add("Social Policy Minister", Severity.Warning, "unemp", $"Unemployment is {Fmt.P(c.Unemp, 1)}, {(c.Unemp - c.NairU) * 100:F1}pp above the natural rate. Demand support or active labour programmes would help.");
        if (c.Gini > c.Gini0 + 0.02)
            Add("Social Policy Minister", Severity.Warning, "gini", $"Inequality has risen (Gini {c.Gini:F2} vs {c.Gini0:F2}).");
        if (c.Approval < 0.30)
            Add("Chief Whip", c.Approval < 0.2 ? Severity.Alert : Severity.Warning, "approval", $"Approval has fallen to {Fmt.P(c.Approval, 0)}. Backbenchers are restless.");
        if (c.Unrest > 0.4)
            Add("Home Secretary", Severity.Alert, "unrest", $"Civil unrest is at {Fmt.P(c.Unrest, 0)}; consider relief measures before it hardens.");
        if (c.Old > c.Old0 + 0.02)
            Add("Social Policy Minister", Severity.Info, "ageing", $"The over-65s are now {Fmt.P(c.Old, 0)} of the population ({Fmt.P(c.Old0, 0)} at the start). Pension costs will keep rising.");

        // ---- Environment & energy ----
        if (c.ClimateDamage > 0.01)
            Add("Energy & Climate Secretary", Severity.Warning, "climate", $"Climate damage is costing about {Fmt.P(c.ClimateDamage, 1)} of potential output.");
        else if (c.Renewables < 0.3 && c.CarbonPrice <= 0)
            Add("Energy & Climate Secretary", Severity.Info, "carbon", "A carbon price or renewables support would accelerate decarbonisation.");

        // ---- Chief of staff ----
        if (c.PoliticalCapital > 85)
            Add("Chief of Staff", Severity.Info, "pc", "Political capital is close to its ceiling; unused capital is wasted. Now is the time for reform.");
        else if (c.PoliticalCapital < 10)
            Add("Chief of Staff", Severity.Warning, "pclow", "Political capital is nearly exhausted; further reforms will have to wait.");
        if (c.NextElectionMonth > 0 && c.NextElectionMonth - w.Month is > 0 and <= 12 && c.Approval < 0.42)
            Add("Chief of Staff", Severity.Alert, "election", $"An election is {c.NextElectionMonth - w.Month} months away and approval is {Fmt.P(c.Approval, 0)}.");

        return n;
    }

    /// <summary>Posts new advisor notes to the log, de-duplicated per key within a cooldown window.</summary>
    public static int Post(World w, CountryState c, int cooldownMonths = 9)
    {
        int posted = 0;
        foreach (var note in Generate(w, c))
        {
            string k = c.Id + ":" + note.Key;
            if (w.AdvisorLast.TryGetValue(k, out var last) && w.Month - last < cooldownMonths && note.Severity != Severity.Alert) continue;
            if (w.AdvisorLast.TryGetValue(k, out last) && w.Month - last < 3) continue;
            w.AdvisorLast[k] = w.Month;
            w.Log.Add(new LogEntry { Month = w.Month, Country = c.Id, Kind = "advisor", Text = $"{note.Advisor}: {note.Text}" });
            posted++;
        }
        return posted;
    }
}
