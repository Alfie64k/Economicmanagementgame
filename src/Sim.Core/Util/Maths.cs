namespace Sim.Core.Util;

public static class Maths
{
    public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;
    /// <summary>Move x toward target by fraction k (0..1).</summary>
    public static double Approach(double x, double target, double k) => x + (target - x) * Clamp(k, 0, 1);
    public static double Sigmoid(double x) => 1.0 / (1.0 + Math.Exp(-x));
    public static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    /// <summary>Dead-band (soft threshold): zero while |x| is within <paramref name="band"/>, then the excess beyond it with its sign. Continuous, so nothing jumps at the edge.</summary>
    public static double Soft(double x, double band) => x > band ? x - band : x < -band ? x + band : 0.0;

    /// <summary>Standard normal CDF (complementary-error-function fit, absolute error below 1.2e-7).</summary>
    public static double NormCdf(double x)
    {
        double z = Math.Abs(x) / Math.Sqrt(2.0), t = 1.0 / (1.0 + 0.5 * z);
        double r = t * Math.Exp(-z * z - 1.26551223 + t * (1.00002368 + t * (0.37409196 + t * (0.09678418 + t * (-0.18628806 + t * (0.27886807
                   + t * (-1.13520398 + t * (1.48851587 + t * (-0.82215223 + t * 0.17087277)))))))));
        double erfcAbs = r;                       // erfc(|x|/sqrt2)
        return x >= 0 ? 1.0 - 0.5 * erfcAbs : 0.5 * erfcAbs;
    }

    /// <summary>Inverse of the standard normal CDF (Acklam's rational approximation, relative error about 1e-9) for p in (0, 1).</summary>
    public static double NormInv(double p)
    {
        if (p <= 0) return double.NegativeInfinity;
        if (p >= 1) return double.PositiveInfinity;
        double[] a = { -3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00 };
        double[] b = { -5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01 };
        double[] c = { -7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00 };
        double[] d = { 7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00 };
        const double lo = 0.02425;
        if (p < lo)
        {
            double q = Math.Sqrt(-2 * Math.Log(p));
            return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        if (p > 1 - lo)
        {
            double q = Math.Sqrt(-2 * Math.Log(1 - p));
            return -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        {
            double q = p - 0.5, r = q * q;
            return (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q / (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1);
        }
    }
}
