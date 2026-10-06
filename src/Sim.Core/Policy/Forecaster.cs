using System.Threading;
using Sim.Core.Engine;
using Sim.Core.Model;
using Sim.Core.Util;

namespace Sim.Core.Policy;

public sealed class FanSeries
{
    public string Metric = "";
    public double[] P10 = Array.Empty<double>(), P50 = Array.Empty<double>(), P90 = Array.Empty<double>();
}

public sealed class PreviewSeries
{
    public string Metric = "";
    public double[] Baseline = Array.Empty<double>(), WithPolicy = Array.Empty<double>();
    public double Final => WithPolicy.Length > 0 ? WithPolicy[^1] - Baseline[^1] : 0;
    /// <summary>Value <paramref name="month"/> months ahead (1-based) with the change / under carry-on, and the difference between them.</summary>
    public double At(int month) => WithPolicy[month - 1];
    public double BaselineAt(int month) => Baseline[month - 1];
    public double DeltaAt(int month) => WithPolicy[month - 1] - Baseline[month - 1];
}

/// <summary>Result of <see cref="Forecaster.PreviewRate"/>: expected paths with the policy rate pinned at <see cref="Target"/> versus carrying on.</summary>
public sealed class RatePreview
{
    public double Target;
    public bool Applied, Pegged, Cancelled;
    public string Note = "";
    public List<PreviewSeries> Series = new();
    public PreviewSeries S(string metric) => Series.First(x => x.Metric == metric);
    /// <summary>First month in which inflation is within 0.25pp of its target under the pinned rate (null if not within the horizon), and the same under carry-on.</summary>
    public int? MonthsToTarget, BaselineMonthsToTarget;
}

/// <summary>Monte-Carlo fan charts and deterministic what-if previews, run on throw-away clones of the world.</summary>
public static class Forecaster
{
    public static readonly string[] Metrics = { "growth", "inflation", "unemployment", "debt", "deficit", "approval", "policyRate", "yield", "gdp" };

    public static double Read(CountryState c, string metric) => metric switch
    {
        "growth" => c.GdpGrowth, "inflation" => c.Inflation, "unemployment" => c.Unemp, "debt" => c.DebtToGdp,
        "deficit" => c.DeficitToGdp, "approval" => c.Approval, "policyRate" => c.PolicyRate, "yield" => c.Yield10,
        "gdp" => c.Gdp, "gap" => c.Gap, "fx" => c.Fx, "realRate" => c.RealRate, "inflExp" => c.InflExp,
        _ => throw new ArgumentException("metric " + metric),
    };

    public static readonly int[] Horizons = { 3, 6, 12 };
    public static readonly string[] RateMetrics = { "inflation", "unemployment", "gap", "fx", "policyRate", "realRate", "inflExp" };

    /// <summary>Compact JSON copy of the world (history and logs omitted). Take it on the main thread, then hand it to a worker.</summary>
    public static string Snapshot(Simulation sim)
    {
        // the staged plan is not part of "carry on": previews pass the plan (and any draft) explicitly
        var w = sim.World; var h = w.History; var log = w.Log; var cl = w.CommandLog; var q = w.Queue;
        w.History = new(); w.Log = new(); w.CommandLog = new(); w.Queue = new();
        try { return sim.Save(); }
        finally { w.History = h; w.Log = log; w.CommandLog = cl; w.Queue = q; }
    }

    public static Simulation FromSnapshot(string json)
    {
        var c = Simulation.Load(json); c.World.RecordHistory = false; c.World.Advisors = false;
        return c;
    }

    public static Simulation Clone(Simulation sim) => FromSnapshot(Snapshot(sim));

    public static List<FanSeries> FanChart(Simulation sim, int months, int paths = 30, ulong seed = 12345, string[]? metrics = null) => FanChart(Snapshot(sim), months, paths, seed, metrics);

    public static List<FanSeries> FanChart(string snapshot, int months, int paths = 30, ulong seed = 12345, string[]? metrics = null)
    {
        metrics ??= Metrics;
        var data = metrics.ToDictionary(m => m, m => new double[months][]);
        foreach (var m in metrics) for (int t = 0; t < months; t++) data[m][t] = new double[paths];
        for (int p = 0; p < paths; p++)
        {
            var s = FromSnapshot(snapshot); var w = s.World;
            w.Stochastic = true;
            w.WorldRng = new Rng(Rng.Mix(seed + (ulong)p, "world"));
            for (int i = 0; i < w.Countries.Count; i++) w.CountryRng[i] = new Rng(Rng.Mix(seed + (ulong)p, w.Countries[i].Id));
            for (int t = 0; t < months; t++)
            {
                s.Tick();
                var pc = w.Player;
                foreach (var m in metrics) data[m][t][p] = Read(pc, m);
            }
        }
        return metrics.Select(m =>
        {
            var f = new FanSeries { Metric = m, P10 = new double[months], P50 = new double[months], P90 = new double[months] };
            for (int t = 0; t < months; t++)
            {
                var v = data[m][t].OrderBy(x => x).ToArray();
                f.P10[t] = v[(int)(0.1 * (v.Length - 1))]; f.P50[t] = v[v.Length / 2]; f.P90[t] = v[(int)Math.Ceiling(0.9 * (v.Length - 1))];
            }
            return f;
        }).ToList();
    }

    /// <summary>Result of <see cref="PreviewPlan"/>: the series plus a note for every command that could not be applied.</summary>
    public sealed class PlanPreview
    {
        public List<PreviewSeries> Series = new();
        public List<string> Skipped = new();
        public int Applied;
    }

    /// <summary>Like <see cref="Preview(string, IEnumerable{Command}, int, string[]?)"/>, but reports commands that fail (for instance for lack of political capital) instead of dropping them silently.</summary>
    public static PlanPreview PreviewPlan(string snapshot, IEnumerable<Command> commands, int months, string[]? metrics = null)
    {
        metrics ??= Metrics;
        var a = FromSnapshot(snapshot); var b = FromSnapshot(snapshot);
        a.World.Stochastic = false; b.World.Stochastic = false;
        var res = new PlanPreview();
        foreach (var cmd in commands)
        {
            var r = b.Execute(cmd);
            if (r.Ok) res.Applied++; else res.Skipped.Add(PlanText.Describe(b.World, cmd) + " — " + r.Message);
        }
        res.Series = metrics.Select(m => new PreviewSeries { Metric = m, Baseline = new double[months], WithPolicy = new double[months] }).ToList();
        for (int t = 0; t < months; t++)
        {
            a.Tick(); b.Tick();
            for (int k = 0; k < metrics.Length; k++)
            {
                res.Series[k].Baseline[t] = Read(a.World.Player, metrics[k]);
                res.Series[k].WithPolicy[t] = Read(b.World.Player, metrics[k]);
            }
        }
        return res;
    }

    /// <summary>
    /// Expected path of the player's economy over the next <paramref name="months"/> if the central bank pins its rate at <paramref name="target"/>,
    /// against carrying on (the rule when it is in charge, the current pin otherwise). Deterministic: no noise and no random events, so it shows the
    /// policy's own effect and is not a forecast of what will actually happen. Any staged plan is left out of both paths.
    /// </summary>
    public static RatePreview PreviewRate(string snapshot, double target, int months = 12, string[]? metrics = null, CancellationToken ct = default)
    {
        metrics ??= RateMetrics;
        var a = FromSnapshot(snapshot); var b = FromSnapshot(snapshot);
        foreach (var s in new[] { a, b }) { s.World.Stochastic = false; s.World.Events = false; }
        var res = new RatePreview { Target = target, Pegged = b.World.Player.Regime == FxRegime.Peg };
        b.World.Player.PoliticalCapital = 100;                                    // price is shown separately; here only the effect matters
        res.Applied = b.Execute(Command.SetRate(b.World.PlayerId, true, target)).Ok;
        res.Note = res.Pegged ? "Under a peg the policy rate follows the anchor currency, so pinning it has little effect." : "";
        res.Series = metrics.Select(m => new PreviewSeries { Metric = m, Baseline = new double[months], WithPolicy = new double[months] }).ToList();
        double tgt = a.World.Player.InflTarget;
        for (int t = 0; t < months; t++)
        {
            if (ct.IsCancellationRequested) { res.Cancelled = true; return res; }
            a.Tick(); b.Tick();
            for (int k = 0; k < metrics.Length; k++)
            {
                res.Series[k].Baseline[t] = Read(a.World.Player, metrics[k]);
                res.Series[k].WithPolicy[t] = Read(b.World.Player, metrics[k]);
            }
            if (res.MonthsToTarget == null && Math.Abs(b.World.Player.Inflation - tgt) < 0.0025) res.MonthsToTarget = t + 1;
            if (res.BaselineMonthsToTarget == null && Math.Abs(a.World.Player.Inflation - tgt) < 0.0025) res.BaselineMonthsToTarget = t + 1;
        }
        return res;
    }

    /// <summary>Deterministic comparison of "carry on" vs "apply these commands now" for the player's country.</summary>
    public static List<PreviewSeries> Preview(Simulation sim, IEnumerable<Command> commands, int months, string[]? metrics = null) => Preview(Snapshot(sim), commands, months, metrics);

    public static List<PreviewSeries> Preview(string snapshot, IEnumerable<Command> commands, int months, string[]? metrics = null)
    {
        metrics ??= Metrics;
        var a = FromSnapshot(snapshot); var b = FromSnapshot(snapshot);
        a.World.Stochastic = false; b.World.Stochastic = false;
        foreach (var cmd in commands) b.Execute(cmd);
        var res = metrics.Select(m => new PreviewSeries { Metric = m, Baseline = new double[months], WithPolicy = new double[months] }).ToList();
        for (int t = 0; t < months; t++)
        {
            a.Tick(); b.Tick();
            for (int k = 0; k < metrics.Length; k++)
            {
                res[k].Baseline[t] = Read(a.World.Player, metrics[k]);
                res[k].WithPolicy[t] = Read(b.World.Player, metrics[k]);
            }
        }
        return res;
    }
}
