using Sim.Core.Model;
using Sim.Core.Util;

namespace Sim.Core.Events;

/// <summary>Stochastic events, crises, decision popups and chained consequences.</summary>
public static class EventEngine
{
    public static double Metric(CountryState c, GlobalState g, string name)
    {
        double dev = Maths.Clamp(c.GdpPerCapitaUsd / 60000, 0, 1);
        return name switch
        {
            "debt" => c.DebtToGdp, "deficit" => c.DeficitToGdp, "infl" => c.Inflation, "unemp" => c.Unemp, "gap" => c.Gap,
            "approval" => c.Approval, "stability" => c.Stability, "unrest" => c.Unrest, "gini" => c.Gini, "ca" => c.CaToGdp,
            "reserves" => c.Reserves, "yield" => c.Yield10, "spread" => c.RiskPremium, "dev" => dev, "corruption" => c.Corruption,
            "democracy" => c.Democracy, "renewables" => c.Renewables, "temp" => g.TempAnomaly, "oil" => g.OilIdx,
            "growth" => c.GdpGrowth, "realloan" => c.RealLoanRate, "agri" => c.Va0[(int)Sector.Agriculture] / c.Gdp0,
            "rnd" => c.AssetIdx[(int)Asset.RnD], "peg" => c.Regime == FxRegime.Peg ? 1 : 0, "default" => c.InDefault ? 1 : 0,
            _ => throw new ArgumentException("unknown metric " + name),
        };
    }

    static bool Holds(CountryState c, GlobalState g, string cond)
    {
        int i = cond.IndexOfAny(new[] { '<', '>' });
        string m = cond[..i]; char op = cond[i];
        double v = double.Parse(cond[(i + 1)..], System.Globalization.CultureInfo.InvariantCulture);
        double x = Metric(c, g, m);
        return op == '>' ? x > v : x < v;
    }

    public static void Step(World w)
    {
        // ---- expire timed modifiers ----
        foreach (var c in w.Countries)
        {
            for (int i = c.TimedMods.Count - 1; i >= 0; i--)
            {
                var t = c.TimedMods[i];
                if (w.Month < t.ExpireMonth) continue;
                c.ModTarget[t.Key] = (c.ModTarget.TryGetValue(t.Key, out var v) ? v : 0) - t.Value;
                c.TimedMods.RemoveAt(i);
            }
            if (c.InDefault && w.Month >= c.DefaultUntil) { c.InDefault = false; w.Log.Add(new LogEntry { Month = w.Month, Country = c.Id, Kind = "crisis", Text = $"{c.Name} regains market access after its restructuring." }); }
            if (c.ImfAutopilot && w.Month >= c.ImfUntil) { c.ImfAutopilot = false; c.Autopilot = false; w.Log.Add(new LogEntry { Month = w.Month, Country = c.Id, Kind = "crisis", Text = $"{c.Name}'s IMF programme ends." }); }
        }

        // ---- scheduled follow-ups ----
        for (int i = w.Scheduled.Count - 1; i >= 0; i--)
        {
            var s = w.Scheduled[i];
            if (w.Month < s.Month) continue;
            w.Scheduled.RemoveAt(i);
            var def = EventCatalog.Find(s.EventId);
            if (def == null) continue;
            if (def.Scope == "global") TriggerGlobal(w, def);
            else { var c = w.Find(s.Country); if (c != null) Trigger(w, c, def); }
        }

        // ---- overdue decisions fall back to the default option ----
        foreach (var d in w.Decisions.Where(d => w.Month >= d.Deadline).ToList()) ResolveDecision(w, d.Id, d.DefaultChoice, auto: true);

        // ---- forced sovereign crisis ----
        var crisis = EventCatalog.Find("sovereign_debt_crisis");
        foreach (var c in w.Countries)
            if (crisis != null && c.CrisisMonths >= 6 && !c.InDefault && c.ImfUntil < w.Month && Cooled(w, c, crisis)) { Trigger(w, c, crisis); c.CrisisMonths = 0; }

        // ---- random events (one RNG draw per pair keeps streams aligned) ----
        double monthly(double annual) => 1 - Math.Pow(1 - Math.Min(0.999, annual * w.EventFrequency), 1.0 / 12);
        foreach (var def in EventCatalog.Events)
        {
            if (def.Prob <= 0) continue;
            if (def.Scope == "global")
            {
                double u = w.WorldRng.NextDouble();
                if (u < monthly(def.Prob) && GlobalCooled(w, def)) TriggerGlobal(w, def);
                continue;
            }
            for (int i = 0; i < w.Countries.Count; i++)
            {
                var c = w.Countries[i];
                double u = w.CountryRng[i].NextDouble();
                if (!Cooled(w, c, def)) continue;
                double p = def.Prob;
                foreach (var pm in def.ProbMods) p *= Maths.Clamp(1 + pm.Slope * (Metric(c, w.Global, pm.Metric) - pm.Ref), 0.2, 5);
                if (u >= monthly(p)) continue;
                if (def.Cond.Any(cd => !Holds(c, w.Global, cd))) continue;
                Trigger(w, c, def);
            }
        }
    }

    static bool Cooled(World w, CountryState c, EventDef def) => !c.EventLast.TryGetValue(def.Id, out var last) || w.Month - last >= def.Cooldown;
    static bool GlobalCooled(World w, EventDef def) => !w.EventHistory.Any(e => e.EventId == def.Id && e.Country == "WORLD" && w.Month - e.Month < def.Cooldown);

    public static void TriggerGlobal(World w, EventDef def)
    {
        w.EventHistory.Add(new EventRecord { Month = w.Month, Country = "WORLD", EventId = def.Id, Name = def.Name });
        w.Log.Add(new LogEntry { Month = w.Month, Country = "WORLD", Kind = "event", Text = $"{def.Name}: {def.Text}" });
        foreach (var e in def.Effects.Where(e => e.Kind == "global")) ApplyGlobal(w.Global, e);
        if (def.AllCountries)
            foreach (var c in w.Countries) ApplyEffects(w, c, def.Effects.Where(e => e.Kind != "global"), def.Name);
        foreach (var n in def.Next)
            if (w.WorldRng.Chance(n.Prob)) w.Scheduled.Add(new ScheduledEvent { Month = w.Month + n.Delay, EventId = n.Id });
    }

    public static void Trigger(World w, CountryState c, EventDef def)
    {
        c.EventLast[def.Id] = w.Month;
        w.EventHistory.Add(new EventRecord { Month = w.Month, Country = c.Id, EventId = def.Id, Name = def.Name });
        bool player = c.Id == w.PlayerId;
        w.Log.Add(new LogEntry { Month = w.Month, Country = c.Id, Kind = def.Category == "crisis" ? "crisis" : "event", Text = $"{def.Name}: {def.Text}" });
        ApplyEffects(w, c, def.Effects, def.Name);

        if (def.Choices.Count > 0)
        {
            if (player)
            {
                w.Decisions.Add(new PendingDecision
                {
                    Id = w.NextDecisionId++, Month = w.Month, Deadline = w.Month + def.Deadline, Country = c.Id, EventId = def.Id, Title = def.Name, Text = def.Text,
                    Labels = def.Choices.Select(x => x.Label).ToList(), Costs = def.Choices.Select(x => x.Cost).ToList(), DefaultChoice = def.DefaultChoice,
                });
            }
            else ApplyChoice(w, c, def, def.DefaultChoice);
        }
        foreach (var n in def.Next)
            if (w.CountryRng[w.Countries.IndexOf(c)].Chance(n.Prob)) w.Scheduled.Add(new ScheduledEvent { Month = w.Month + n.Delay, EventId = n.Id, Country = c.Id });
    }

    public static bool ResolveDecision(World w, int id, int choice, bool auto = false)
    {
        var d = w.Decisions.FirstOrDefault(x => x.Id == id);
        if (d == null) return false;
        var def = EventCatalog.Find(d.EventId); var c = w.Find(d.Country);
        w.Decisions.Remove(d);
        if (def == null || c == null) return false;
        choice = (int)Maths.Clamp(choice, 0, def.Choices.Count - 1);
        ApplyChoice(w, c, def, choice);
        w.Log.Add(new LogEntry { Month = w.Month, Country = c.Id, Kind = "event", Text = $"{(auto ? "(default) " : "")}{def.Name} — {def.Choices[choice].Label}" });
        return true;
    }

    static void ApplyChoice(World w, CountryState c, EventDef def, int idx)
    {
        var ch = def.Choices[idx];
        if (ch.Cost > 0) c.OtherRevenue -= ch.Cost * c.GdpNominal * 6;
        ApplyEffects(w, c, ch.Effects, def.Name);
        var rec = w.EventHistory.LastOrDefault(e => e.Country == c.Id && e.EventId == def.Id);
        if (rec != null) rec.Choice = ch.Label;
    }

    static double Resilience(CountryState c, string scale)
    {
        double dev = Maths.Clamp(c.GdpPerCapitaUsd / 60000, 0, 1);
        return scale switch
        {
            "vuln" => Maths.Clamp(1.4 - dev, 0.6, 1.5),
            "health" => Maths.Clamp(2 - c.AssetIdx[(int)Asset.Health], 0.5, 1.5),
            "infra" => Maths.Clamp(2 - c.AssetIdx[(int)Asset.Infrastructure], 0.5, 1.5),
            "digital" => Maths.Clamp(2 - c.AssetIdx[(int)Asset.Digital], 0.5, 1.5),
            "defence" => Maths.Clamp(2 - c.AssetIdx[(int)Asset.Defence], 0.5, 1.5),
            _ => 1.0,
        };
    }

    static void ApplyGlobal(GlobalState g, EffectDef e)
    {
        switch (e.Key)
        {
            case "oil": g.OilIdx = Maths.Clamp(g.OilIdx * e.Value, 0.3, 4.0); break;
            case "food": g.FoodIdx = Maths.Clamp(g.FoodIdx * e.Value, 0.5, 2.5); break;
            case "risk": g.RiskAppetite = Maths.Clamp(g.RiskAppetite + e.Value, 0.4, 1.3); break;
            case "demand": g.WorldDemandIdx *= 1 + e.Value; break;
        }
    }

    public static void ApplyEffects(World w, CountryState c, IEnumerable<EffectDef> effects, string source)
    {
        foreach (var e in effects)
        {
            double v = e.Value * Resilience(c, e.Scale);
            if (c.Id == w.PlayerId && e.Kind is "level" or "state" && e.Value < 0) v *= w.EventSeverity;
            switch (e.Kind)
            {
                case "level":
                    switch (e.Key)
                    {
                        case "cons": c.Cons *= 1 + v; break;
                        case "inv": c.InvPriv *= 1 + v; break;
                        case "exports": c.Exports *= 1 + v; break;
                        case "tfp": for (int s = 0; s < Dim.Sectors; s++) c.Tfp[s] *= 1 + v; break;
                        case "capital": for (int s = 0; s < Dim.Sectors; s++) c.K[s] *= 1 + v; break;
                        case "pop": c.Pop *= 1 + v; break;
                        case "debt": c.Debt = Math.Max(0, c.Debt + v * c.GdpNominal); break;
                        case "fx": c.Fx *= 1 + v; break;
                        case "reserves": c.Reserves = Math.Max(0, c.Reserves + v); break;
                    }
                    break;
                case "state":
                    switch (e.Key)
                    {
                        case "approval": c.Approval = Maths.Clamp(c.Approval + v, 0.02, 0.98); break;
                        case "stability": c.Stability = Maths.Clamp(c.Stability + v, 0.02, 0.98); break;
                        case "unrest": c.Unrest = Maths.Clamp(c.Unrest + v, 0, 1); break;
                        case "corruption": c.Corruption = Maths.Clamp(c.Corruption + v, 0, 1); break;
                        case "polcap": c.PoliticalCapital = Maths.Clamp(c.PoliticalCapital + v, 0, 100); break;
                        case "gini": c.Gini = Maths.Clamp(c.Gini + v, 0.15, 0.7); break;
                    }
                    break;
                case "mod":
                    {
                        int months = Math.Max(1, e.Months);
                        c.ModTarget[e.Key] = (c.ModTarget.TryGetValue(e.Key, out var cur) ? cur : 0) + v;
                        c.TimedMods.Add(new TimedMod { Key = e.Key, Value = v, ExpireMonth = w.Month + months, Source = source });
                        break;
                    }
                case "imf":
                    c.OtherRevenue += e.Value * c.GdpNominal * 6;
                    c.ImfUntil = w.Month + e.Months; c.ImfAutopilot = true; c.Autopilot = true; c.CrisisMonths = 0;
                    break;
                case "default":
                    c.Debt *= 1 - e.Value; c.InDefault = true; c.DefaultUntil = w.Month + 24; c.CrisisMonths = 0;
                    c.Approval = Maths.Clamp(c.Approval - 0.10, 0.02, 0.98);
                    break;
                case "election":
                    if (c.Democracy > 0.4) c.NextElectionMonth = w.Month + 3;
                    break;
                case "gameover":
                    if (c.Id == w.PlayerId && w.CountryRng[w.Countries.IndexOf(c)].Chance(e.Value))
                    { w.GameOver = true; w.GameOverReason = $"Government overthrown ({source})."; }
                    else if (c.Id != w.PlayerId) Politics.RegimeChange(w, c, source);
                    break;
            }
        }
    }
}
