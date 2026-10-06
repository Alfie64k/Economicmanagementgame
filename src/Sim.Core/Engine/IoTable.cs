using Sim.Core.Model;

namespace Sim.Core.Engine;

/// <summary>Stylised input-output structure shared by all countries (rows = supplying sector, cols = using sector).</summary>
public static class IoTable
{
    // cols: Agri, Energy, Manuf, Services, Finance, Public
    public static readonly double[,] A =
    {
        { 0.10, 0.00, 0.12, 0.02, 0.00, 0.00 },
        { 0.05, 0.15, 0.08, 0.03, 0.01, 0.03 },
        { 0.12, 0.10, 0.28, 0.06, 0.02, 0.08 },
        { 0.08, 0.06, 0.12, 0.18, 0.15, 0.12 },
        { 0.02, 0.02, 0.03, 0.05, 0.12, 0.02 },
        { 0.00, 0.00, 0.00, 0.01, 0.01, 0.05 },
    };

    /// <summary>Value-added share of gross output per sector (1 - column sum).</summary>
    public static double[] ValueAddedRatio()
    {
        int n = Dim.Sectors; var v = new double[n];
        for (int s = 0; s < n; s++) { double cs = 0; for (int r = 0; r < n; r++) cs += A[r, s]; v[s] = 1 - cs; }
        return v;
    }

    /// <summary>(I - A)^-1 via Gauss-Jordan, flattened row-major.</summary>
    public static double[] LeontiefInverse()
    {
        int n = Dim.Sectors;
        var m = new double[n, 2 * n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++) m[i, j] = (i == j ? 1 : 0) - A[i, j];
            m[i, n + i] = 1;
        }
        for (int c = 0; c < n; c++)
        {
            int piv = c;
            for (int r = c + 1; r < n; r++) if (Math.Abs(m[r, c]) > Math.Abs(m[piv, c])) piv = r;
            if (piv != c) for (int j = 0; j < 2 * n; j++) (m[c, j], m[piv, j]) = (m[piv, j], m[c, j]);
            double d = m[c, c];
            for (int j = 0; j < 2 * n; j++) m[c, j] /= d;
            for (int r = 0; r < n; r++)
            {
                if (r == c) continue;
                double f = m[r, c];
                if (f == 0) continue;
                for (int j = 0; j < 2 * n; j++) m[r, j] -= f * m[c, j];
            }
        }
        var inv = new double[n * n];
        for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) inv[i * n + j] = m[i, n + j];
        return inv;
    }

    /// <summary>Fraction of each sector's final demand that is C, I, G, X. Rows sum to 1.</summary>
    public static readonly double[][] DemandMix =
    {
        new[] { 0.65, 0.00, 0.03, 0.32 }, // Agri
        new[] { 0.35, 0.05, 0.05, 0.55 }, // Energy
        new[] { 0.30, 0.35, 0.05, 0.30 }, // Manuf
        new[] { 0.55, 0.12, 0.23, 0.10 }, // Services
        new[] { 0.60, 0.10, 0.05, 0.25 }, // Finance
        new[] { 0.00, 0.00, 1.00, 0.00 }, // Public
    };
}
