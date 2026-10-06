using Sim.Core.Model;
using Sim.Core.Util;

namespace Sim.Core.Engine;

/// <summary>
/// Rule-based government used for AI countries (and optionally the player's "autopilot"). Archetype sets how aggressively
/// it targets a debt ceiling and how it trades off tax vs spending adjustment. Acts once a month on the same levers as the player.
/// </summary>
public static class PolicyAgent
{
    public static void Step(CountryState c, GlobalState g, Rng rng)
    {
        double ceiling = Math.Max(0.60, c.DebtGdp0 + 0.15);
        double debt = c.DebtToGdp, def = c.DeficitToGdp;
        double pressure = Maths.Clamp((debt - ceiling) * 1.5 + (def - 0.04) * 1.0, -0.5, 1.5);
        // no austerity in a slump: scale tightening down as the output gap goes negative, and stimulate in deep recessions
        if (pressure > 0) pressure *= Maths.Clamp(1 + c.Gap * 20, 0, 1);
        bool populist = c.Gov != "democracy" || c.Archetype == "developing";
        double step = 0.0004 * pressure; // change in rate or budget share per month

        if (pressure > 0)
        {
            // tighten: favour consumption/income tax, then trim admin and social growth
            c.TaxRate[(int)Tax.Consumption] += step * 6;
            c.TaxRate[(int)Tax.Income] += step * 2;
            c.Budget[(int)BudgetLine.Admin] = Math.Max(c.Budget0[(int)BudgetLine.Admin] * 0.5, c.Budget[(int)BudgetLine.Admin] - step * 0.5);
            if (!populist) c.Budget[(int)BudgetLine.Social] = Math.Max(c.Budget0[(int)BudgetLine.Social] * 0.85, c.Budget[(int)BudgetLine.Social] - step * 0.4);
        }
        else if (pressure < -0.2 || c.Gap < -0.03)
        {
            // loosen slowly back toward baseline settings when there is room
            for (int t = 0; t < Dim.Taxes; t++)
                c.TaxRate[t] += Maths.Clamp(c.TaxRate0[t] - c.TaxRate[t], -0.0002, 0.0002) * 0.5;
        }
        // keep spending lines from drifting below baseline in good times
        for (int l = 0; l < Dim.Lines; l++)
            if (l != (int)BudgetLine.Social) c.Budget[l] += Maths.Clamp(c.Budget0[l] - c.Budget[l], -0.0001, 0.0001) * (pressure < 0 ? 1 : 0);
    }
}
