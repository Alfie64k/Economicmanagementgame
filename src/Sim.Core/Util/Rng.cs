namespace Sim.Core.Util;

/// <summary>Deterministic SplitMix64 generator; state is a single ulong so it serialises trivially.</summary>
public sealed class Rng
{
    public ulong State;
    public Rng() { }
    public Rng(ulong seed) { State = seed; }

    public static ulong Mix(ulong seed, string salt)
    {
        ulong h = seed ^ 0x9E3779B97F4A7C15UL;
        foreach (char c in salt) { h ^= c; h *= 0x100000001B3UL; }
        return h;
    }

    public ulong NextU64()
    {
        ulong z = (State += 0x9E3779B97F4A7C15UL);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform in [0,1).</summary>
    public double NextDouble() => (NextU64() >> 11) * (1.0 / (1UL << 53));

    public double Range(double lo, double hi) => lo + (hi - lo) * NextDouble();

    /// <summary>Standard normal via Box-Muller (always consumes two draws so streams stay aligned).</summary>
    public double Normal()
    {
        double u1 = Math.Max(NextDouble(), 1e-12), u2 = NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    public bool Chance(double p) => NextDouble() < p;
}
