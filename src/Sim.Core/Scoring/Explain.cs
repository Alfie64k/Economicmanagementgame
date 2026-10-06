using Sim.Core.Model;
using Sim.Core.Util;

namespace Sim.Core.Scoring;

public sealed class DriverItem
{
    public string Label = "";
    public double Value;       // in percentage points (or pp of GDP) unless Unit says otherwise
    public string Group = "";
    public override string ToString() => $"{Label}: {Value:+0.00;-0.00;0.00}";
}

public sealed class Explanation
{
    public string Metric = "", Headline = "";
    public List<DriverItem> Items = new();
    public string Summary => string.Join("; ", Items.Where(i => Math.Abs(i.Value) >= 0.05).OrderByDescending(i => Math.Abs(i.Value)).Take(4).Select(i => $"{i.Label} {i.Value:+0.0;-0.0}pp"));
}

/// <summary>"Why did this change?" attribution for headline metrics.</summary>
public static class Explain
{
    public static readonly string[] Metrics = { "inflation", "growth", "unemployment", "deficit", "debt", "approval" };

    public static Explanation Why(CountryState c, string metric)
    {
        var old = c.CompRing[c.Tick % 12] ?? MacroSnap(c);
        var e = new Explanation { Metric = metric };
        void Add(string group, string label, double v) => e.Items.Add(new DriverItem { Group = group, Label = label, Value = v * 100 });

        switch (metric)
        {
            case "inflation":
                {
                    e.Headline = $"Inflation is {Fmt.P(c.Inflation, 1)} (underlying {Fmt.P(c.InflInst, 1)}, target {Fmt.P(c.InflTarget, 1)})";
                    var d = c.InflDrivers;
                    Add("pressures", "Inflation expectations", d[0]); Add("pressures", "Output gap", d[1]); Add("pressures", "Energy & food prices", d[2]);
                    Add("pressures", "Currency pass-through", d[3]); Add("pressures", "Monetisation of deficits", d[4]); Add("pressures", "Policy & carbon price", d[5]);
                    break;
                }
            case "growth":
                {
                    e.Headline = $"Real GDP growth is {Fmt.P(c.GdpGrowth, 1)} over the past year (output gap {Fmt.P(c.Gap, 1)})";
                    double g0 = Math.Max(1e-9, old.Gdp);
                    Add("demand", "Household consumption", (c.Cons - old.Cons) / g0);
                    Add("demand", "Private investment", (c.InvPriv - old.InvPriv) / g0);
                    Add("demand", "Government spending", (c.GovCons + c.GovInv - old.GovCons - old.GovInv) / g0);
                    Add("demand", "Net exports", ((c.Exports - c.Imports) - (old.Exports - old.Imports)) / g0);
                    double abar = 0; for (int s = 0; s < Dim.Sectors; s++) abar += c.Alpha[s] * c.SectorPot[s] / Math.Max(1e-9, c.Potential);
                    double dk = Math.Log(c.K.Sum() / Math.Max(1e-9, old.Capital));
                    double dl = Math.Log(Math.Max(1e-9, c.Pop * c.Working * c.Participation * (1 - c.NairU) * c.HumanCapital) / Math.Max(1e-9, old.LabourEff));
                    double dp = Math.Log(c.Potential / Math.Max(1e-9, old.Potential));
                    Add("supply", "Capital accumulation", abar * dk); Add("supply", "Labour force & skills", (1 - abar) * dl);
                    Add("supply", "Productivity & other", dp - abar * dk - (1 - abar) * dl);
                    break;
                }
            case "unemployment":
                {
                    e.Headline = $"Unemployment is {Fmt.P(c.Unemp, 1)} against a natural rate of {Fmt.P(c.NairU, 1)}";
                    var n = c.NairuParts;
                    Add("natural", "Structural base (incl. hysteresis)", n[0]); Add("natural", "Minimum wage", n[1]); Add("natural", "Payroll tax", n[2]);
                    Add("natural", "Skills (human capital)", n[3]); Add("natural", "Labour-market policy", n[4]);
                    Add("cyclical", "Demand shortfall / surplus", c.Unemp - c.NairU);
                    break;
                }
            case "deficit":
                {
                    double now = c.DeficitToGdp, then = old.GdpNom > 0 ? (old.Spending - old.Revenue) / old.GdpNom : now;
                    e.Headline = $"The deficit is {Fmt.P(now, 1)} of GDP ({(now >= then ? "up" : "down")} {Fmt.P(Math.Abs(now - then), 1)} on a year ago)";
                    double rev = c.Revenue / Math.Max(1e-9, c.GdpNominal) - (old.GdpNom > 0 ? old.Revenue / old.GdpNom : 0);
                    double intr = c.Interest / Math.Max(1e-9, c.GdpNominal) - (old.GdpNom > 0 ? old.Interest / old.GdpNom : 0);
                    double prog = (c.Spending - c.Interest) / Math.Max(1e-9, c.GdpNominal) - (old.GdpNom > 0 ? (old.Spending - old.Interest) / old.GdpNom : 0);
                    Add("change", "Lower revenue (as % of GDP)", -rev); Add("change", "Higher debt interest", intr); Add("change", "Programme spending", prog);
                    break;
                }
            case "debt":
                {
                    double now = c.DebtToGdp, then = old.GdpNom > 0 ? old.Debt / old.GdpNom : now;
                    e.Headline = $"Debt is {Fmt.P(now, 0)} of GDP ({(now >= then ? "up" : "down")} {Fmt.P(Math.Abs(now - then), 1)} on a year ago)";
                    double gnom = old.GdpNom > 0 ? c.GdpNominal / old.GdpNom - 1 : 0;
                    double primary = -c.PrimaryBalance / Math.Max(1e-9, c.GdpNominal);
                    double interest = c.Interest / Math.Max(1e-9, c.GdpNominal);
                    double growth = -then * gnom / (1 + gnom);
                    Add("change", "Primary deficit", primary); Add("change", "Interest bill", interest); Add("change", "Growth & inflation (denominator)", growth);
                    Add("change", "Other (crises, one-offs)", (now - then) - primary - interest - growth);
                    break;
                }
            case "approval":
                {
                    e.Headline = $"Approval is {Fmt.P(c.Approval, 0)} (long-run target {Fmt.P(Maths.Clamp(c.ApprovalDrivers.Sum(), 0.03, 0.95), 0)})";
                    var a = c.ApprovalDrivers;
                    Add("target", "Baseline mood", a[0] - c.Approval0); Add("target", "Growth", a[1]); Add("target", "Unemployment", a[2]); Add("target", "Inflation", a[3]);
                    Add("target", "Inequality", a[4]); Add("target", "Tax burden", a[5]); Add("target", "Public services", a[6]); Add("target", "Corruption", a[7]); Add("target", "Policies & events", a[8]);
                    break;
                }
            default: throw new ArgumentException("metric " + metric);
        }
        return e;
    }

    static CompSnap MacroSnap(CountryState c) => Engine.MacroEngine.Snap(c);
}
