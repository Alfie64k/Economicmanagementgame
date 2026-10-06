using Sim.Core.Data;
using Sim.Core.Model;
using Sim.Core.Util;

namespace Sim.Core.Engine;

/// <summary>Wage bargaining, the wage-price spiral, long-term unemployment hysteresis, strike risk and the labour share of income.</summary>
public static class LabourMarketEngine
{
    /// <summary>Sets union coverage and the labour share to their catalogue starting values and clears the dynamic state (new games and version 2 saves).</summary>
    public static void Init(CountryState c)
    {
        var p = LabourCatalog.For(c.Id, c.Archetype);
        c.UnionCoverage = c.UnionCoverage0 = p.Coverage;
        c.UnionStrength = p.Strength;
        c.LabourIncomeShare = c.LabourIncomeShare0 = c.LabourIncomeTrend = p.LabourShare;
        c.WageGap = c.WagePremium = c.WageSpiral = c.StrikeRisk = 0;
        c.LtuStock = c.NairuHyst = 0;
    }
}
