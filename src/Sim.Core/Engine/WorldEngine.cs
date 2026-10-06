using Sim.Core.Model;
using Sim.Core.Util;

namespace Sim.Core.Engine;

/// <summary>Cross-country layer: gravity trade, contagion, global anchors (USD rate, world inflation) and AI retaliation.</summary>
public static class WorldEngine
{
    const double RowShare = 0.35;

    public static string Key(string a, string b) => a + ">" + b;
    public static Relation Rel(World w, string a, string b)
    {
        var k = Key(a, b);
        if (!w.Relations.TryGetValue(k, out var r)) w.Relations[k] = r = new Relation();
        return r;
    }
    static Relation? Peek(World w, string a, string b) => w.Relations.TryGetValue(Key(a, b), out var r) ? r : null;

    public static double Haversine(CountryState a, CountryState b)
    {
        double R = 6371, p1 = a.Lat * Math.PI / 180, p2 = b.Lat * Math.PI / 180, dp = p2 - p1, dl = (b.Lon - a.Lon) * Math.PI / 180;
        double h = Math.Sin(dp / 2) * Math.Sin(dp / 2) + Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
        return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    public static void RebuildTrade(World w)
    {
        int n = w.Countries.Count;
        var t = new TradeMatrix { W = new double[n][], RowShare = new double[n], CrisisScore = new double[n] };
        for (int i = 0; i < n; i++)
        {
            t.W[i] = new double[n]; var ci = w.Countries[i];
            double sum = 0;
            for (int j = 0; j < n; j++)
            {
                if (i == j) continue;
                var cj = w.Countries[j];
                double dist = Haversine(ci, cj);
                double v = Math.Pow(Math.Max(1, cj.GdpUsdBn), 0.9) * Math.Exp(-dist / 5500.0);
                if (ci.Bloc != "none" && ci.Bloc == cj.Bloc) v *= 2.5;
                if (ci.Region == cj.Region) v *= 1.3;
                t.W[i][j] = v; sum += v;
            }
            // scale so partners in the roster take (1 - RowShare) of exports; large economies are more self-contained and trade less abroad
            double scale = sum > 0 ? (1 - RowShare) / sum : 0;
            for (int j = 0; j < n; j++) t.W[i][j] *= scale;
            t.RowShare[i] = RowShare;
        }
        w.Trade = t;
    }

    public static void Step(World w)
    {
        int n = w.Countries.Count;
        if (w.Trade.W.Length != n || w.Month % 12 == 0) RebuildTrade(w);
        var T = w.Trade;
        var cs = w.Countries;

        // ---- anchors: USD policy rate and world inflation come from the simulated economies ----
        var usa = w.Find("USA");
        if (usa != null) w.Global.WorldRate += (usa.PolicyRate - w.Global.WorldRate) * 0.3;
        double wi = 0, ww = 0;
        foreach (var c in cs) if (c.Archetype is "advanced" or "hub") { wi += Maths.Clamp(c.Inflation, -0.02, 0.10) * c.GdpUsdBn; ww += c.GdpUsdBn; }
        if (ww > 0) w.Global.WorldInflation += (wi / ww - w.Global.WorldInflation) * 0.2;

        // ---- crisis scores and contagion ----
        double stress = 0, gdpSum = 0, gapAvg = 0;
        for (int i = 0; i < n; i++)
        {
            var c = cs[i];
            double s = Maths.Clamp((c.Yield10 - 0.14) / 0.12, 0, 1) * (c.DebtToGdp > 0.5 ? 1 : 0.5) + (c.InDefault ? 1 : 0);
            T.CrisisScore[i] = Maths.Clamp(s, 0, 1);
            stress += T.CrisisScore[i] * c.GdpUsdBn; gdpSum += c.GdpUsdBn; gapAvg += c.Gap * c.GdpUsdBn;
        }
        gapAvg /= Math.Max(1, gdpSum);
        for (int j = 0; j < n; j++)
        {
            double c = 0, dev = Maths.Clamp(cs[j].GdpPerCapitaUsd / 60000, 0, 1);
            for (int i = 0; i < n; i++)
            {
                if (i == j || T.CrisisScore[i] <= 0) continue;
                double prox = T.W[j][i] + (cs[i].Region == cs[j].Region ? 0.12 : 0) + (Peek(w, cs[j].Id, cs[i].Id)?.Alliance == true ? -0.05 : 0);
                c += T.CrisisScore[i] * Math.Max(0, prox);
            }
            double target = 0.03 * c * (1.3 - dev);
            cs[j].ContagionRisk += (target - cs[j].ContagionRisk) * 0.2;
            cs[j].ContagionRisk = Maths.Clamp(cs[j].ContagionRisk, 0, 0.08);
        }
        double stressShare = stress / Math.Max(1, gdpSum);
        w.Global.RiskAppetite = Maths.Clamp(w.Global.RiskAppetite - 0.6 * stressShare * 0.1, 0.4, 1.3);

        // ---- AI retaliation: answer tariffs and mirror sanctions ----
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                if (i == j) continue;
                var r = Peek(w, cs[i].Id, cs[j].Id);
                if (r == null || r.ExtraTariff <= 0) continue;
                if (cs[j].Id == w.PlayerId) continue;
                var back = Rel(w, cs[j].Id, cs[i].Id);
                if (back.ExtraTariff < r.ExtraTariff && w.WorldRng.Chance(0.30))
                {
                    bool first = back.ExtraTariff <= 0;
                    back.ExtraTariff = Math.Min(r.ExtraTariff, back.ExtraTariff + 0.015);
                    if (first)
                        w.Log.Add(new LogEntry { Month = w.Month, Country = cs[i].Id, Kind = "news", Text = $"{cs[j].Name} retaliates against {cs[i].Name}'s tariffs." });
                }
            }

        // ---- external demand, tariff exposure ----
        double rowIdx = (w.Global.WorldDemandIdx / w.Global.WorldDemandTrend) * (1 + 0.5 * gapAvg);
        var mrel = new double[n];
        for (int j = 0; j < n; j++) mrel[j] = cs[j].Imports / Math.Max(1e-9, cs[j].M0 * cs[j].Potential / cs[j].Potential0);
        for (int i = 0; i < n; i++)
        {
            double ext = T.RowShare[i] * rowIdx, retal = 0, extraImp = 0;
            for (int j = 0; j < n; j++)
            {
                if (i == j) continue;
                double link = Link(w, cs[i].Id, cs[j].Id);
                ext += T.W[i][j] * mrel[j] * link;
                var theirs = Peek(w, cs[j].Id, cs[i].Id); // j's measures against i
                if (theirs != null) retal += T.W[i][j] * theirs.ExtraTariff;
                var mine = Peek(w, cs[i].Id, cs[j].Id);   // my measures against j
                if (mine != null) extraImp += T.W[i][j] * mine.ExtraTariff;
            }
            cs[i].ExtDemandIdx = ext;
            cs[i].TariffRetaliation = 0; // folded into Link(); kept for UI/legacy readers
            cs[i].ImportTariffExtra = extraImp;
            _ = retal;
        }

        // sanctions weigh on the target's stability
        foreach (var kv in w.Relations)
        {
            if (!kv.Value.Sanction) continue;
            var target = w.Find(kv.Key[(kv.Key.IndexOf('>') + 1)..]);
            if (target != null) target.Stability = Math.Max(0.05, target.Stability - 0.0004);
        }
    }

    /// <summary>Multiplier on i's exports to j from deals, alliances, tariffs and sanctions (with partial trade diversion).</summary>
    public static double Link(World w, string i, string j)
    {
        double f = 1.0;
        var ij = Peek(w, i, j); var ji = Peek(w, j, i);
        if (ij?.Deal == true) f *= 1.25;
        if (ij?.Alliance == true) f *= 1.05;
        double loss = 0;
        if (ji != null)
        {
            loss += Math.Min(0.9, 2.0 * ji.ExtraTariff);           // j's tariffs on my goods
            if (ji.Sanction) loss = Math.Max(loss, 0.7);           // j sanctions me
        }
        if (ij?.Sanction == true) loss = Math.Max(loss, 0.5);      // I sanction j: self-harm
        f *= 1 - 0.6 * loss;                                       // 40% of lost trade is diverted elsewhere
        return f;
    }
}
