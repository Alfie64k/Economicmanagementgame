using Sim.Core.Model;
using Sim.Core.Util;

namespace Sim.Core.Engine;

/// <summary>Three-cohort demographic model (0-14, 15-64, 65+) with a simple demographic transition.</summary>
public static class Demography
{
    const double YoungToWork = 1.0 / 15.0, WorkToOld = 1.0 / 50.0;

    static (double dYoung, double dWork, double dOld) Flows(CountryState c)
    {
        double births = 0.01 * c.Fertility * c.Working;                 // per year, as share of population
        double oldDeath = 1.0 / Math.Max(8.0, c.LifeExp - 65 + 4);
        double dY = births - c.Young * YoungToWork - c.Young * 0.0006;
        double mig = c.MigrationPer1000 / 1000.0 * (1 + c.Mod("migration"));
        double dW = c.Young * YoungToWork - c.Working * WorkToOld - c.Working * 0.0025 + mig;
        double dO = c.Working * WorkToOld - c.Old * oldDeath;
        return (dY, dW, dO);
    }

    /// <summary>Annualised growth rate of the working-age population.</summary>
    public static double WorkingGrowth(CountryState c)
    {
        var (dY, dW, dO) = Flows(c);
        double popG = dY + dW + dO;
        return (dW - c.Working * popG) / c.Working + popG;
    }

    public static void Step(CountryState c, double dt)
    {
        var (dY, dW, dO) = Flows(c);
        double popG = dY + dW + dO;
        double y = c.Young + dY * dt - c.Young * popG * dt;
        double w = c.Working + dW * dt - c.Working * popG * dt;
        double o = c.Old + dO * dt - c.Old * popG * dt;
        double sum = y + w + o;
        c.Young = y / sum; c.Working = w / sum; c.Old = o / sum;
        c.Pop *= 1 + popG * dt;

        // demographic transition: fertility drifts toward replacement-minus with income, shifted by policy
        double target = 1.5 + 0.0 + c.Mod("fertility");
        double rate = 0.004 * (c.Fertility > target ? 1 : 0.3);
        c.Fertility = Maths.Clamp(c.Fertility + (target - c.Fertility) * rate * dt * 12, 0.9, 7.5);
        // life expectancy: converges to a ceiling that rises with health capital and income
        double ceiling = 78 + 10 * Maths.Clamp(Math.Log(c.AssetIdx[(int)Asset.Health]) * 2 + 0.3, 0, 1) + c.Mod("lifeexp");
        c.LifeExp += (ceiling - c.LifeExp) * 0.012 * dt;
        c.LifeExp = Maths.Clamp(c.LifeExp, 40, 95);
    }
}
