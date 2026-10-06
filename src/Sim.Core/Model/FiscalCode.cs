using System.Text.Json.Serialization;

namespace Sim.Core.Model;

/// <summary>The seven strands of welfare spending the Budget tab lets the player change one by one.</summary>
public enum Ben { Pension, Unemployment, Child, Disability, Housing, MeansTested, Other }

/// <summary>
/// The detailed tax-and-benefit code of the player's country: allowances, bands, contributions, corporation tax, VAT treatments and the
/// individual benefits. It does not replace the engine's five scalar effective tax rates and ten budget shares; it <i>drives</i> them.
/// Every parameter is held in multiples of mean annual earnings ("ME") as at the start of the game, so the same engine serves a rich
/// and a poor country, and the amounts shown to the player are converted to local currency.
/// At the starting values nothing here changes the simulation by a single bit.
/// </summary>
public sealed class FiscalCode
{
    public bool Init;
    public string Label = "";                 // e.g. "United Kingdom 2024/25 (approximate)"
    public string StatIndex = "";             // how the real country indexes thresholds today (shown as a note; the game starts neutral)
    public double MeanEarn;                   // local currency per year at the start: mean gross earnings of an employee
    public double Sigma = 0.7;                // dispersion of log earnings, set so net-income inequality matches the country (within realistic bounds)
    public double GiniScale = 1.0;            // widens Gini changes where the grid shows less inequality than the data does
    public double Soc0;                       // starting Social budget line (share of GDP)
    public double[] Shares = new double[7];   // share of social spending by strand at the start (sums to 1)

    /// <summary>Current and starting parameter values, by key (see <c>FiscalParams</c>).</summary>
    public Dictionary<string, double> P = new(), P0 = new();

    /// <summary>Thresholds relative to mean earnings: 1 when indexed to earnings; falls when thresholds lag (fiscal drag).</summary>
    public double Drift = 1.0, PenDrift = 1.0;
    public double LastRealWage, LastPrice;
    public double[] EarnRing = new double[12], PriceRing = new double[12];
    public int RingPos;

    /// <summary>Additive adjustments (engine-rate units) that absorb direct writes to a tax rate (the one-slider control, the autopilot); 0 = none.</summary>
    public double[] Offset = new double[5];
    /// <summary>The last effective rates this code wrote into the country state; a difference means someone else changed them.</summary>
    public double[] Written = new double[5];
    public bool Dirty = true;

    // ---- derived by TaxCodeEngine.Recalc (cached, recomputed whenever the code or the drift changes) ----
    public double[] Calib = { 1, 1, 1, 1, 1 };   // effective engine rate per unit of statutory average rate
    public double CorpInvDelta;                  // change in the cost of capital in engine-rate units (drives investment and FDI)
    public double LabourTarget = 1, LabourCur = 1;
    public double NairuTarget, NairuCur;
    public double GiniDelta, PovertyDelta, ApprovalDelta;
    public double Mix = 1.0;                     // 1 + sum over strands of share × (ratio − 1): spending on benefits relative to the start
    public double MpcTax;                        // extra household spending from who gains and loses on tax (share of GDP, annual)
    public double[] BenRatio = { 1, 1, 1, 1, 1, 1, 1 };
    public double[] BenMpc = { 0.85, 1.30, 1.10, 1.12, 1.20, 1.25, 1.00 };   // spent-per-pound relative to the average household (starting priors)

    [JsonIgnore] public object? Cache;           // last evaluation (TaxCodeEngine owns it)

    public double Get(string key, double dflt = 0) => P.TryGetValue(key, out var v) ? v : dflt;
    public double Get0(string key, double dflt = 0) => P0.TryGetValue(key, out var v) ? v : dflt;

    /// <summary>True while nothing differs from the starting code (the engine then behaves exactly as before).</summary>
    [JsonIgnore]
    public bool AtStart
    {
        get
        {
            if (Drift != 1.0 || PenDrift != 1.0) return false;
            foreach (var o in Offset) if (o != 0.0) return false;
            if (P.Count != P0.Count) return false;
            foreach (var kv in P) if (!P0.TryGetValue(kv.Key, out var v) || v != kv.Value) return false;
            return true;
        }
    }
}
