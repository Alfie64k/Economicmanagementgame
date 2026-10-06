namespace Sim.Core.Util;

public static class Maths
{
    public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;
    /// <summary>Move x toward target by fraction k (0..1).</summary>
    public static double Approach(double x, double target, double k) => x + (target - x) * Clamp(k, 0, 1);
    public static double Sigmoid(double x) => 1.0 / (1.0 + Math.Exp(-x));
    public static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
}
