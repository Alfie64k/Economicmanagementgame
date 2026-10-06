using Sim.Core.Model;
using Sim.Core.Util;

namespace Sim.Core.Engine;

/// <summary>Approval, stability, unrest, inequality, corruption and political capital.</summary>
public static class SocietyEngine
{
    public static double TaxBurden(CountryState c)
    {
        double r = 0;
        for (int t = 0; t < Dim.Taxes; t++) r += FiscalEngine.TaxRevenueReal(c, (Tax)t, c.Gdp, c.Cons, c.Imports);
        return r / Math.Max(1e-9, c.Gdp);
    }

    public static void Step(CountryState c, GlobalState g, double dt)
    {
        double burden0 = c.Burden0 > 0 ? c.Burden0 : (c.Burden0 = TaxBurden(c));
        double burden = TaxBurden(c);
        double idx(Asset a) => Math.Log(c.AssetIdx[(int)a]);

        // inequality: unemployment, redistribution, education and housing
        double socialShare = c.Budget[(int)BudgetLine.Social];
        double giniTarget = c.Gini0
            + 0.5 * (c.Unemp - c.Unemp0)
            - 0.8 * (socialShare - c.Budget0[(int)BudgetLine.Social])
            - 0.3 * (c.TaxRate[(int)Tax.Income] - c.TaxRate0[(int)Tax.Income])
            - 0.04 * idx(Asset.Education) - 0.02 * idx(Asset.Housing)
            + c.Mod("gini");
        c.Gini += (Maths.Clamp(giniTarget, 0.2, 0.65) - c.Gini) * 0.02 * dt * 12;

        // approval
        double popG = 0.0;
        double growth = c.GdpGrowth - popG;
        double target = c.Approval0
            + 2.0 * (growth - 0.02)
            - 1.5 * (c.Unemp - c.Unemp0)
            - 1.2 * Math.Max(0, c.Inflation - 0.04)
            - 0.8 * (c.Gini - c.Gini0)
            - 1.5 * (burden - c.Burden0)
            + 0.15 * idx(Asset.Health) + 0.10 * idx(Asset.Education) + 0.10 * idx(Asset.Housing) + 0.05 * idx(Asset.Infrastructure)
            - 0.3 * (c.Corruption - c.Corruption0)
            - 0.5 * Math.Max(0, c.DebtToGdp - 1.2) * 0.1
            + c.Mod("approval");
        c.Approval += (Maths.Clamp(target, 0.03, 0.95) - c.Approval) * 0.04;

        // unrest and stability
        double unrestT = 0.5 * Math.Max(0, c.Gini - 0.35) + 1.2 * Math.Max(0, c.Unemp - 0.08)
                         + 0.6 * Math.Max(0, c.Inflation - 0.10) + 0.6 * Math.Max(0, 0.4 - c.Approval) + c.Mod("unrest");
        c.Unrest += (Maths.Clamp(unrestT, 0, 1) - c.Unrest) * 0.08;
        double stabT = 0.45 + 0.35 * c.Approval + 0.15 * (1 - c.Corruption) + 0.1 * c.Democracy - 0.5 * c.Unrest
                       + 0.05 * idx(Asset.Defence);
        double stabBase = c.Stability0;
        c.Stability += (Maths.Clamp(stabT, 0.05, 0.98) * 0.5 + stabBase * 0.5 - c.Stability) * 0.03;

        // corruption drifts with policy and democracy (very slowly)
        c.Corruption = Maths.Clamp(c.Corruption + dt * (c.Mod("corruption") - 0.002 * (c.Democracy - 0.5)), 0, 1);

        // political capital: regenerates with approval, capped
        double regen = (1.2 + 2.2 * c.Approval + c.Mod("polcap")) * (c.Gov == "autocracy" ? 1.2 : 1.0);
        c.PoliticalCapital = Maths.Clamp(c.PoliticalCapital + regen, 0, 100) ;
        // note: regen is per month; spending happens when policies are enacted
    }
}
