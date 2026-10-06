using Sim.Core.Data;
using Sim.Core.Model;
using Sim.Core.Util;

namespace Sim.Core.Engine;

/// <summary>Builds a consistent starting <see cref="CountryState"/> from raw country data (steady-state calibration).</summary>
public static class Calibrator
{
    static readonly double[] CapIntensity = { 1.2, 3.0, 1.6, 0.9, 1.2, 1.4 };
    static readonly double[] Alpha = { 0.40, 0.65, 0.40, 0.33, 0.40, 0.30 };

    public static CountryState Build(CountryData d)
    {
        int n = Dim.Sectors;
        var c = new CountryState
        {
            Id = d.Id, Name = d.Name, Currency = d.Currency, Region = d.Region, Archetype = d.Archetype, Gov = d.Gov, Bloc = d.Bloc,
            Lat = d.Lat, Lon = d.Lon,
            Regime = d.Fx.ToLowerInvariant() switch { "peg" => FxRegime.Peg, "managed" => FxRegime.Managed, _ => FxRegime.Float },
        };
        double dev = Maths.Clamp(d.GdpPerCapitaUsd / 60000.0, 0, 1);
        double y0 = d.GdpLcuBn;

        // ---- demography ----
        c.Pop = c.Pop0 = d.PopM; c.Young = d.Demog.Young; c.Old = c.Old0 = d.Demog.Old; c.Working = 1 - c.Young - c.Old;
        c.Fertility = d.Demog.Fertility; c.LifeExp = d.Demog.LifeExp; c.Participation = d.Demog.Participation;
        c.MigrationPer1000 = d.Demog.Migration;

        // ---- sectors ----
        double[] shares = { d.Sectors.Agri, d.Sectors.Energy, d.Sectors.Manuf, d.Sectors.Services, d.Sectors.Finance, d.Sectors.Public };
        double ssum = shares.Sum();
        double[] v0 = shares.Select(s => s / ssum * y0).ToArray();
        double[] relProd = { 0.25 + 0.5 * dev, 3.0, 1.1, 0.9, 2.5, 0.8 };
        double[] emp = new double[n];
        for (int s = 0; s < n; s++) emp[s] = v0[s] / relProd[s];
        double esum = emp.Sum();
        for (int s = 0; s < n; s++) c.LabourShare[s] = emp[s] / esum;

        c.Unemp = c.Unemp0 = c.NairU = c.NairUBase = d.Macro.Unemployment;
        double lf = c.Pop * c.Working * c.Participation;
        double lemp = lf * (1 - c.Unemp);

        // ---- budget structure (shares of Y0) ----
        double[] b = new double[Dim.Lines];
        b[(int)BudgetLine.Health] = d.Fiscal.Health;
        b[(int)BudgetLine.Education] = d.Fiscal.Education;
        b[(int)BudgetLine.Defence] = d.Fiscal.Defence;
        b[(int)BudgetLine.Infrastructure] = d.Fiscal.Infra;
        b[(int)BudgetLine.RnD] = d.Fiscal.Rnd;
        b[(int)BudgetLine.Green] = 0.003;
        b[(int)BudgetLine.Housing] = 0.004;
        b[(int)BudgetLine.Digital] = 0.002;
        double nonSocial = b.Sum();
        b[(int)BudgetLine.Admin] = Math.Max(0.015, d.Demand.Gov - nonSocial);
        double g0Share = b.Sum();
        double govInvShare = 0;
        for (int l = 0; l < Dim.Lines; l++) govInvShare += Dim.CapitalFraction[l] * b[l];

        // ---- demand identity (anchor on data, consumption is the residual) ----
        double i0Share = d.Demand.Inv;
        double x0Share = d.Demand.Exp, m0Share = d.Demand.Imp;
        double c0Share = 1 - (i0Share - govInvShare) - g0Share - x0Share + m0Share; // data 'inv' is total GFCF, 'gov' includes public capex
        c.Gdp = c.Gdp0 = c.Potential = c.Potential0 = y0;
        c.Cons = c.Cons0 = c0Share * y0;
        c.GovCons = c.GovCons0 = (g0Share - govInvShare) * y0;
        c.GovInv = c.GovInv0 = govInvShare * y0;
        c.InvPriv = c.InvPriv0 = Math.Max(0.05, i0Share - govInvShare) * y0;
        c.X0 = c.Exports = x0Share * y0; c.M0 = c.Imports = m0Share * y0;
        c.AbsorptionBase = c.Cons + c.InvPriv + c.GovCons + c.GovInv + c.Exports;
        c.Budget0 = b.ToArray(); c.Budget = b.ToArray();

        // ---- capital & productivity ----
        double g = d.Macro.GrowthTrend;
        double ktot = Maths.Clamp(c.InvPriv / (c.Depreciation + Math.Max(0.0, g)), 1.5 * y0, 5.0 * y0);
        double kw = 0; for (int s = 0; s < n; s++) kw += CapIntensity[s] * v0[s];
        double abar = 0; for (int s = 0; s < n; s++) abar += Alpha[s] * v0[s] / y0;
        for (int s = 0; s < n; s++)
        {
            c.Alpha[s] = Alpha[s];
            c.K[s] = ktot * CapIntensity[s] * v0[s] / kw;
            double ls = c.LabourShare[s] * lemp;
            c.Tfp[s] = v0[s] / (Math.Pow(c.K[s], Alpha[s]) * Math.Pow(ls, 1 - Alpha[s]));
            c.SectorVa[s] = c.SectorPot[s] = v0[s]; c.Va0[s] = v0[s];
            c.Ret0[s] = Alpha[s] * v0[s] / c.K[s];
            c.Mpl0[s] = (1 - Alpha[s]) * v0[s] / c.LabourShare[s];
        }
        double ksum = c.K.Sum();
        for (int s = 0; s < n; s++) c.InvestShare[s] = c.K[s] / ksum;
        double gK = c.InvPriv / ksum - c.Depreciation;
        double gL = Demography.WorkingGrowth(c);
        c.BaseTfpGrowth = Maths.Clamp(g - abar * gK - (1 - abar) * gL, -0.005, 0.045);

        // ---- IO structure ----
        c.Leontief = IoTable.LeontiefInverse();
        var vr = IoTable.ValueAddedRatio();
        var x0 = new double[n];
        for (int s = 0; s < n; s++) x0[s] = v0[s] / vr[s];
        for (int s = 0; s < n; s++)
        {
            double ax = 0; for (int r = 0; r < n; r++) ax += IoTable.A[s, r] * x0[r];
            c.FinalDemand0[s] = x0[s] - ax;
        }

        // ---- prices & rates ----
        c.PriceLevel = 1;
        for (int i = 0; i < 12; i++) c.PriceRing[i] = Math.Exp(-Math.Log(1 + d.Macro.Inflation) * (11 - i) / 12.0); // price level 12 months before tick i, so yoy starts at the data value
        c.Inflation = c.InflInst = d.Macro.Inflation;
        c.InflTarget = d.Macro.InflationTarget; c.CbIndependence = d.Macro.CbIndependence; c.Cred = d.Macro.CbIndependence;
        c.InflExp = d.Macro.Inflation > 0.15 ? d.Macro.Inflation : Maths.Lerp(d.Macro.Inflation, c.InflTarget, c.CbIndependence * 0.6);
        c.PolicyRate = c.ManualRate = d.Macro.PolicyRate;
        c.NaturalRate = Maths.Clamp(d.Macro.PolicyRate - d.Macro.Inflation - MacroEngine.InflationResponse(d.Macro.Inflation - c.InflTarget), -0.30, 0.05);
        c.Yield10 = d.Macro.Yield10;
        c.AvgDebtCost = Maths.Clamp(Maths.Lerp(d.Macro.PolicyRate, d.Macro.Yield10, 0.6), 0.0, 0.10);
        c.Spread = 0; // anchored by MacroEngine.Spread0 equivalent below
        c.RealLoanRate = c.RealLoanRate0 = c.Yield10 + 0.015 - c.InflExp;

        // ---- fiscal ----
        c.Debt = d.Fiscal.Debt * y0; c.DebtGdp0 = d.Fiscal.Debt;
        c.ForeignDebtShare = d.Fiscal.ForeignDebtShare;
        double rev0 = d.Fiscal.Revenue * y0;
        c.ResourceRev0Share = d.Fiscal.ResourceRevenue * d.Fiscal.Revenue;
        double rest = rev0 * (1 - d.Fiscal.ResourceRevenue);
        double wInc = 0.10 + 0.22 * dev, wCorp = 0.12 - 0.03 * dev, wCons = 0.35 - 0.12 * dev, wPay = 0.08 + 0.17 * dev, wTar = 0.10 - 0.08 * dev, wOth = 0.10;
        double wsum = wInc + wCorp + wCons + wPay + wTar + wOth;
        c.OtherRevShare = rest * (wOth / wsum) / y0;
        c.TaxRate[(int)Tax.Income] = rest * wInc / wsum / (FiscalEngine.IncomeBaseShare * y0);
        c.TaxRate[(int)Tax.Corporate] = rest * wCorp / wsum / (FiscalEngine.CorpBaseShare * y0);
        c.TaxRate[(int)Tax.Consumption] = rest * wCons / wsum / c.Cons;
        c.TaxRate[(int)Tax.Payroll] = rest * wPay / wsum / (FiscalEngine.PayrollBaseShare * y0);
        c.TaxRate[(int)Tax.Tariff] = rest * wTar / wsum / c.Imports;
        c.TaxRate0 = c.TaxRate.ToArray();

        double interest0 = c.Debt * c.AvgDebtCost;
        double socialShare = (d.Fiscal.Revenue + d.Fiscal.Deficit) - g0Share - interest0 / y0;
        socialShare = Math.Max(0.02, socialShare);
        c.Budget[(int)BudgetLine.Social] = c.Budget0[(int)BudgetLine.Social] = socialShare;
        c.Social0Real = socialShare * y0;

        // ---- households ----
        double dev0 = dev;
        double s0 = 0.08 + 0.10 * dev0;
        double incTax = c.TaxRate[(int)Tax.Income] * FiscalEngine.IncomeBaseShare * y0;
        double payTax = c.TaxRate[(int)Tax.Payroll] * FiscalEngine.PayrollBaseShare * y0;
        double yd = c.Cons / (1 - s0);
        double hh = (yd + incTax + payTax - c.Social0Real - 0.7 * interest0) / y0;
        c.HhIncomeShare = Maths.Clamp(hh, 0.40, 0.95);
        double yd0 = c.HhIncomeShare * y0 - incTax - payTax + c.Social0Real + 0.7 * interest0;
        c.SavingsRate = c.SavingsRate0 = Maths.Clamp(1 - c.Cons / yd0, 0.0, 0.6);
        c.ConsBaseline = c.Cons;

        // ---- external ----
        c.Fx = c.Fx0 = d.UsdFx; c.Rer = 1;
        c.CaTarget = d.Macro.CurrentAccount;
        c.CaOffsetShare = d.Macro.CurrentAccount - (x0Share - m0Share);
        c.CurrentAccount = d.Macro.CurrentAccount * y0;
        c.Openness0 = (x0Share + m0Share) / 2;
        c.Fdi0 = y0 * Math.Min(0.08, 0.012 + 0.03 * c.Openness0);
        c.EnergyNetExport0 = d.Energy.NetExportGdp;
        c.Reserves = 3 + 6 * dev + (d.Macro.CurrentAccount > 0 ? 3 : 0);

        // ---- politics ----
        c.Democracy = d.Politics.Democracy; c.Corruption = d.Politics.Corruption; c.Approval = d.Politics.Approval;
        c.Stability = Maths.Clamp(0.4 + 0.3 * d.Politics.Democracy + 0.3 * (1 - d.Politics.Corruption), 0.2, 0.95);
        c.Coalition = c.Gov == "democracy" ? 0.55 : 0.8;
        c.Gini = c.Gini0 = d.Macro.Gini;
        c.Corruption0 = c.Corruption; c.Stability0 = c.Stability; c.Approval0 = c.Approval;
        c.RealRate0 = c.PolicyRate - c.InflExp;
        c.PoliticalCapital = 50;

        // ---- environment ----
        c.Renewables = d.Energy.Renewables; c.EmissionsMt = c.EmissionsMt0 = d.Energy.EmissionsMt;

        c.PotLag = y0; c.Deficit0Share = d.Fiscal.Deficit; c.LastVatRate = c.TaxRate[(int)Tax.Consumption]; c.LastTariffRate = c.TaxRate[(int)Tax.Tariff];
        c.LabourShare0 = c.LabourShare.ToArray(); c.Renewables0 = c.Renewables; c.Pop0 = c.Pop;
        c.Style = d.Archetype == "resource" ? "resource"
                : (d.Demand.Exp >= 0.30 && d.Sectors.Manuf >= 0.18 && d.Archetype != "advanced") || d.Id is "DEU" or "KOR" ? "exportled"
                : d.Macro.CbIndependence < 0.35 || (d.Gov == "hybrid" && d.Archetype != "advanced") ? "populist"
                : "technocrat";
        c.Status = "ok";
        for (int i = 0; i < 12; i++) c.GdpRing[i] = y0 * Math.Exp(-g * (11 - i) / 12.0);
        FiscalEngine.AnchorSpread(c, d.Macro.Yield10);
        return c;
    }
}
