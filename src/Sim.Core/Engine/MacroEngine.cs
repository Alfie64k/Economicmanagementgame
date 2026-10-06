using Sim.Core.Model;
using Sim.Core.Util;

namespace Sim.Core.Engine;

/// <summary>Monthly macro step for one country: supply, demand, prices, rates, sectors, external sector, society, environment.</summary>
public static class MacroEngine
{
    static readonly double[] AssetRate = { 0.06, 0.04, 0.08, 0.10, 0.06, 0.04, 0.15, 0.07 }; // convergence speed of public asset indices per year

    public static void Step(CountryState c, GlobalState g, double dt, Rng rng, bool stochastic)
    {
        double shockD = stochastic ? rng.Normal() * 0.004 : 0;
        double shockS = stochastic ? rng.Normal() * 0.0015 : 0;
        RampModifiers(c);
        Demography.Step(c, dt);
        PublicAssets(c, dt);
        Supply(c, g, dt, shockS);
        Demand(c, g, dt, shockD);
        Labour(c, dt);
        Prices(c, g, dt);
        Rates(c, g, dt);
        Sectors(c, dt);
        FiscalEngine.Step(c, g, dt);
        External(c, g, dt);
        SocietyEngine.Step(c, g, dt);
        Environment(c, g, dt);
        Bound(c);
        c.Tick++;
    }

    static void RampModifiers(CountryState c)
    {
        foreach (var kv in c.ModTarget)
        {
            double cur = c.Mods.TryGetValue(kv.Key, out var v) ? v : 0;
            c.Mods[kv.Key] = cur + (kv.Value - cur) * 0.08;
        }
    }

    static void PublicAssets(CountryState c, double dt)
    {
        double corr = (1 - 0.5 * c.Corruption) / (1 - 0.5 * c.Corruption0);
        for (int a = 0; a < Dim.Assets; a++)
        {
            int l = (int)Dim.LineOf((Asset)a);
            double target = (c.Budget0[l] > 1e-6 ? c.Budget[l] / c.Budget0[l] * corr : 1.0) + c.AssetBoost[a];
            target = Maths.Clamp(target, 0.05, 4.0);
            c.AssetIdx[a] += (target - c.AssetIdx[a]) * AssetRate[a] * dt;
        }
    }

    static void Supply(CountryState c, GlobalState g, double dt, double shock)
    {
        double lnInf = Math.Log(c.AssetIdx[(int)Asset.Infrastructure]);
        double lnRnd = Math.Log(c.AssetIdx[(int)Asset.RnD]);
        double lnDig = Math.Log(c.AssetIdx[(int)Asset.Digital]);
        double hcTarget = Math.Pow(c.AssetIdx[(int)Asset.Education], 0.15) * Math.Pow(c.AssetIdx[(int)Asset.Health], 0.08);
        c.HumanCapital += (hcTarget - c.HumanCapital) * 0.04 * dt;
        double lnHc = Math.Log(c.HumanCapital);

        double gA = c.BaseTfpGrowth + 0.012 * lnRnd + 0.010 * lnInf + 0.015 * lnHc + c.Mod("tfp") + shock * 12;
        // climate damage is applied to the level of potential output below, not to growth
        for (int s = 0; s < Dim.Sectors; s++)
        {
            double extra = (s == (int)Sector.Services || s == (int)Sector.Finance) ? 0.008 * lnDig : 0.003 * lnDig;
            c.Tfp[s] *= Math.Exp((gA + extra) * dt);
        }

        double part = c.Participation * (1 + 0.04 * (c.AssetIdx[(int)Asset.Health] - 1) + c.Mod("participation"));
        double lf = c.Pop * c.Working * Maths.Clamp(part, 0.3, 0.9);
        double lstar = lf * (1 - c.NairU);
        double pot = 0;
        double dmg = 1 - c.ClimateDamage;
        for (int s = 0; s < Dim.Sectors; s++)
        {
            double l = c.LabourShare[s] * lstar * c.HumanCapital;
            double v = c.Tfp[s] * Math.Pow(Math.Max(1e-9, c.K[s]), c.Alpha[s]) * Math.Pow(Math.Max(1e-9, l), 1 - c.Alpha[s]) * dmg;
            v *= 1 + c.SectorSubsidy[s] * 0.1;
            c.SectorPot[s] = v; pot += v;
        }
        c.Potential = pot;
    }

    static void Demand(CountryState c, GlobalState g, double dt, double shock)
    {
        double P = c.PriceLevel;
        double incTax = FiscalEngine.TaxRevenueReal(c, Tax.Income, c.Gdp, c.Cons, c.Imports);
        double payTax = FiscalEngine.TaxRevenueReal(c, Tax.Payroll, c.Gdp, c.Cons, c.Imports);
        double interestReal = c.Debt * c.AvgDebtCost / P;
        double yd = c.HhIncomeShare * c.Gdp - incTax - payTax + FiscalEngine.SocialReal(c) + 0.7 * interestReal;

        double s = c.SavingsRate0 + 0.35 * Maths.Clamp(c.RealRate - c.RealRate0, -0.05, 0.05) + 0.20 * c.Unrest + c.Mod("savings");
        c.SavingsRate = Maths.Clamp(s, 0.0, 0.6);
        double consTarget = (1 - c.SavingsRate) * yd * (1 + shock) * (1 - 0.4 * Maths.Clamp(c.Inflation - c.InflExp, -0.05, 0.3));
        c.Cons += (consTarget - c.Cons) * 0.35;

        // private investment (+ FDI deviation)
        double corpDelta = c.TaxRate[(int)Tax.Corporate] - c.TaxRate0[(int)Tax.Corporate];
        double conf = (0.85 + 0.3 * c.Stability) / (0.85 + 0.3 * c.Stability0);
        double fdi = FdiAttractiveness(c, g) * c.Fdi0 * (c.Potential / c.Potential0);
        double i0Share = c.InvPriv0 / c.Potential0;
        double invTarget = i0Share * c.Potential
            * Math.Exp(-3.0 * Maths.Clamp(c.RealLoanRate - c.RealLoanRate0, -0.05, 0.05))
            * (1 + 0.5 * Maths.Clamp(c.Gap, -0.05, 0.05))
            * Math.Max(0.3, 1 - 1.5 * corpDelta)
            * (1 + c.Mod("invest")) * conf
            * (1 + shock)
            + (fdi - c.Fdi0 * (c.Potential / c.Potential0));
        c.InvPriv += (Math.Max(0, invTarget) - c.InvPriv) * 0.20;
        c.FdiInflow = fdi;

        // government demand
        double govCons = 0, govInv = 0;
        for (int l = 0; l < Dim.Lines; l++)
        {
            if (l == (int)BudgetLine.Social) continue;
            double amt = c.Budget[l] * c.Potential;
            govInv += Dim.CapitalFraction[l] * amt;
            govCons += (1 - Dim.CapitalFraction[l]) * amt;
        }
        govInv += c.ProjectFlow + c.MaintFlow;
        govCons += c.SubsidyCost;
        c.GovCons = govCons; c.GovInv = govInv;

        // external demand
        double wd = (c.ExtDemandIdx > 0 ? c.ExtDemandIdx : g.WorldDemandIdx / g.WorldDemandTrend) * c.WorldLink;
        double tot = c.EnergyNetExport0 * c.Potential * (g.OilIdx - 1) * 0.7;
        c.Exports = c.X0 * wd * Math.Pow(1 / c.Rer, 0.9) * Math.Pow(c.Potential / c.Potential0, 1.0)
                    * (1 - 0.8 * c.TariffRetaliation) * (1 + c.Mod("export")) + tot;
        double absorption = c.Cons + c.InvPriv + c.GovCons + c.GovInv + c.Exports;
        double tar = c.TaxRate[(int)Tax.Tariff], tar0 = c.TaxRate0[(int)Tax.Tariff];
        c.Imports = c.M0 * Math.Pow(absorption / c.AbsorptionBase, 1.0) * (1 + 1.0 * Maths.Clamp(c.Gap, -0.1, 0.1)) * Math.Pow(c.Rer, 0.8)
                    * Math.Pow((1 + tar) / (1 + tar0), -0.8) * (1 + c.Mod("import"));

        double y = c.Cons + c.InvPriv + c.GovCons + c.GovInv + c.Exports - c.Imports;
        c.Gdp = Maths.Clamp(y, 0.75 * c.Potential, 1.20 * c.Potential);
        c.Gap = c.Gdp / c.Potential - 1;
        double ago = c.GdpRing[c.Tick % 12];
        c.GdpGrowth = ago > 0 ? c.Gdp / ago - 1 : 0;
        c.GdpRing[c.Tick % 12] = c.Gdp;
    }

    public static double FdiAttractiveness(CountryState c, GlobalState g)
    {
        double a = (1 + c.Mod("fdi"))
                 * (1 + 0.5 * (c.Stability - c.Stability0))
                 * Math.Max(0.2, 1 - 2.0 * (c.TaxRate[(int)Tax.Corporate] - c.TaxRate0[(int)Tax.Corporate]))
                 * (1 + 0.4 * Math.Log(c.AssetIdx[(int)Asset.Infrastructure]))
                 * (1 - 0.5 * (c.Corruption - c.Corruption0))
                 * Math.Max(0.1, 1 - 4.0 * Math.Max(0, c.RiskPremium))
                 * (0.6 + 0.4 * g.RiskAppetite);
        return Maths.Clamp(a, 0.05, 3.0);
    }

    static void Labour(CountryState c, double dt)
    {
        double target = c.NairU - 0.5 * c.Gap;
        c.Unemp += (Maths.Clamp(target, 0.01, 0.45) - c.Unemp) * 0.10;
        // natural rate: policy-driven plus slow hysteresis
        c.NairUBase += (c.Unemp - c.NairU) * 0.02 * dt;
        c.NairUBase = Maths.Clamp(c.NairUBase, c.Unemp0 - 0.02, c.Unemp0 + 0.08);
        double pay = c.TaxRate[(int)Tax.Payroll] - c.TaxRate0[(int)Tax.Payroll];
        c.NairU = Maths.Clamp(c.NairUBase + 0.25 * (c.MinWageRatio - c.MinWageRatio0) + 0.15 * pay
                              - 0.03 * Math.Log(c.HumanCapital) + c.Mod("nairu"), 0.01, 0.45);

        // wage dynamics (real wage index tracks productivity and labour-market tightness)
        double prodG = c.Potential > 0 ? Math.Log(c.Potential / Math.Max(1e-9, c.PotLag)) / Math.Max(dt, 1e-9) : 0;
        c.PotLag = c.Potential;
        double realG = Maths.Clamp(prodG, -0.1, 0.1) + 0.4 * (c.NairU - c.Unemp);
        c.RealWageIdx *= Math.Exp(realG * dt);

        // sector labour reallocation toward higher marginal products (relative to start)
        double[] rel = new double[Dim.Sectors]; double avg = 0;
        for (int s = 0; s < Dim.Sectors; s++)
        {
            double l = Math.Max(1e-9, c.LabourShare[s]);
            double mpl = (1 - c.Alpha[s]) * c.SectorVa[s] / l;
            rel[s] = mpl / c.Mpl0[s];
            avg += rel[s] * c.LabourShare[s];
        }
        double sum = 0;
        for (int s = 0; s < Dim.Sectors; s++)
        {
            c.LabourShare[s] *= Math.Exp(0.3 * dt * (rel[s] / avg - 1));
            sum += c.LabourShare[s];
        }
        for (int s = 0; s < Dim.Sectors; s++) c.LabourShare[s] /= sum;
    }

    static void Prices(CountryState c, GlobalState g, double dt)
    {
        double gapEff = Maths.Clamp(c.Gap, -0.08, 0.06);
        double kappa = gapEff > 0.03 ? 0.45 : 0.30;
        double costPush = 0.07 * g.OilYoY * (1 - 0.5 * c.Renewables)
                          + 0.10 * (1 + 2 * c.Va0[(int)Sector.Agriculture] / c.Gdp0) * g.FoodYoY;
        double fxPass = (0.15 + 0.2 * (1 - Maths.Clamp(c.GdpPerCapitaUsd / 60000, 0, 1)))
                        * Math.Max(-0.2, c.FxChange - (c.InflInst - g.WorldInflation));
        double def0 = c.DeficitToGdp;
        // fiscal dominance: only when sovereign stress forces the central bank to monetise deficits
        double mon = (1 - c.CbIndependence) * Maths.Clamp((c.RiskPremium - 0.03) / 0.05, 0, 1) * 0.04;
        double target = c.InflExp + kappa * gapEff + costPush + fxPass + c.Mod("inflation") + mon
                        + 0.01 * c.CarbonPrice / 100.0;
        c.InflDrivers[0] = c.InflExp; c.InflDrivers[1] = kappa * gapEff; c.InflDrivers[2] = costPush; c.InflDrivers[3] = fxPass; c.InflDrivers[4] = mon;
        c.InflDrivers[5] = c.Mod("inflation") + 0.01 * c.CarbonPrice / 100.0;
        c.InflInst += (Maths.Clamp(target, -0.05, 3.0) - c.InflInst) * 0.20;

        double dVat = c.TaxRate[(int)Tax.Consumption] - c.LastVatRate;
        double dTar = c.TaxRate[(int)Tax.Tariff] - c.LastTariffRate;
        c.LastVatRate = c.TaxRate[(int)Tax.Consumption]; c.LastTariffRate = c.TaxRate[(int)Tax.Tariff];
        double jump = 0.7 * dVat + 0.3 * dTar * (c.Imports / Math.Max(1e-9, c.Gdp));
        double P = c.PriceLevel * Math.Pow(1 + c.InflInst, dt) * (1 + jump);
        int slot = c.Tick % 12;
        c.Inflation = P / c.PriceRing[slot] - 1;
        c.PriceRing[slot] = P;
        c.PriceLevel = P;

        // credibility is earned slowly (positive real rates, fiscal discipline) and lost quickly (monetisation, negative real rates)
        double realRate = c.PolicyRate - c.Inflation;
        bool disciplined = realRate > 0.01 && def0 - c.Deficit0Share < 0.03;
        if (disciplined) c.Cred += 0.02 * dt;
        else if (mon > 0.001 || realRate < -0.03) c.Cred -= 0.06 * dt;
        c.Cred = Maths.Clamp(c.Cred, 0.05, 0.95);
        double cred = c.Cred * Math.Exp(-2.0 * Math.Max(0, def0 - 0.06)) * Math.Clamp(1 + c.Mod("credibility"), 0.2, 1.5);
        cred = Maths.Clamp(cred, 0.05, 0.98);
        c.InflExp += (cred * (c.InflTarget - c.InflExp) + (1 - cred) * (c.Inflation - c.InflExp)) * 0.05;
        c.InflExp += (c.InflTarget - c.InflExp) * 0.004;  // slow institutional learning toward the target
        c.InflExp = Maths.Clamp(c.InflExp, -0.02, 3.0);
    }

    /// <summary>Taylor-rule inflation response: 0.5 on the first 10pp of overshoot, 0.2 on the next 40pp (orthodox hike in high-inflation regimes).</summary>
    public static double InflationResponse(double excess) =>
        0.5 * Math.Min(Math.Max(excess, -0.05), 0.10) + 0.2 * Math.Max(0, Math.Min(excess, 0.50) - 0.10);

    static void Rates(CountryState c, GlobalState g, double dt)
    {
        // the natural real rate normalises toward its long-run level over ~3 years
        c.NaturalRate += (0.015 - c.NaturalRate) * dt / 3.0;
        if (c.Regime == FxRegime.Peg)
        {
            double anchor = g.WorldRate + 0.003 + Math.Max(0, c.RiskPremium);
            double dom = c.NaturalRate + c.Inflation + InflationResponse(c.Inflation - c.InflTarget) + 1.0 * Maths.Clamp(c.Gap, -0.15, 0.10);
            c.PolicyRate += (0.5 * anchor + 0.5 * dom - c.PolicyRate) * 0.2;
        }
        else if (c.RateMode == RateMode.Manual)
        {
            double step = Maths.Clamp(c.ManualRate - c.PolicyRate, -0.005, 0.005);
            c.PolicyRate += step;
        }
        else
        {
            double taylor = c.NaturalRate + c.Inflation + InflationResponse(c.Inflation - c.InflTarget) + 1.0 * Maths.Clamp(c.Gap, -0.15, 0.10);
            if (c.Regime == FxRegime.Managed) taylor += 0.3 * Math.Max(0, c.FxChange - 0.05);
            c.PolicyRate += (taylor - c.PolicyRate) * 0.12;
        }
        c.PolicyRate = Maths.Clamp(c.PolicyRate, -0.01, 1.5);
    }

    static void Sectors(CountryState c, double dt)
    {
        int n = Dim.Sectors;
        double sC = c.Cons / c.Cons0;
        double sI = (c.InvPriv + c.GovInv) / (c.InvPriv0 + c.GovInv0);
        double sG = (c.GovCons + c.GovInv) / (c.GovCons0 + c.GovInv0);
        double sX = c.Exports / c.X0;
        var f = new double[n];
        for (int s = 0; s < n; s++)
        {
            var m = IoTable.DemandMix[s];
            f[s] = c.FinalDemand0[s] * (m[0] * sC + m[1] * sI + m[2] * sG + m[3] * sX);
        }
        var vr = IoTable.ValueAddedRatio();
        double tot = 0;
        for (int s = 0; s < n; s++)
        {
            double x = 0; for (int r = 0; r < n; r++) x += c.Leontief[s * n + r] * f[r];
            c.SectorVa[s] = Math.Max(0, x) * vr[s]; tot += c.SectorVa[s];
        }
        double norm = tot > 0 ? c.Gdp / tot : 1;
        for (int s = 0; s < n; s++) c.SectorVa[s] *= norm;

        // private capital allocation follows relative returns (vs. start) and industrial subsidies
        double[] w = new double[n]; double ws = 0, ksum = c.K.Sum();
        for (int s = 0; s < n; s++)
        {
            double ret = c.Alpha[s] * c.SectorVa[s] / Math.Max(1e-9, c.K[s]) / c.Ret0[s];
            w[s] = (c.K[s] / ksum) * Math.Exp(1.5 * (ret - 1) + 8.0 * c.SectorSubsidy[s]);
            ws += w[s];
        }
        double ss = 0;
        for (int s = 0; s < n; s++) { c.InvestShare[s] += (w[s] / ws - c.InvestShare[s]) * 0.05; ss += c.InvestShare[s]; }
        for (int s = 0; s < n; s++)
        {
            c.InvestShare[s] /= ss;
            c.K[s] = Math.Max(1e-6, c.K[s] + (c.InvestShare[s] * c.InvPriv - c.Depreciation * c.K[s]) * dt);
        }
    }

    static void External(CountryState c, GlobalState g, double dt)
    {
        double ca = (c.Exports - c.Imports) + c.CaOffsetShare * c.Gdp;
        c.CurrentAccount = ca;
        double gapCa = ca / Math.Max(1e-9, c.Gdp) - c.CaTarget;
        double wr = g.WorldRate - g.WorldInflation, wr0 = g.WorldRate0 - 0.025;
        double dev = Maths.Clamp(c.GdpPerCapitaUsd / 60000, 0, 1);
        double logTarget = gapCa / 0.45
                           + 2.0 * Maths.Clamp((c.RealRate - wr) - (c.RealRate0 - wr0), -0.08, 0.08)
                           - 1.5 * c.RiskPremium
                           - 0.4 * (1 - g.RiskAppetite) * (1 - dev)
                           + c.Mod("fx");
        logTarget = Maths.Clamp(logTarget, -0.6, 0.6);
        double pwr = c.PriceLevel / g.WorldPrice;
        double lnRer = Math.Log(pwr * c.Fx0 / c.Fx);

        double speed = c.Regime == FxRegime.Float ? 0.4 : c.Regime == FxRegime.Managed ? 0.2 : 0.06;
        double dlnFx = (Math.Log(1 + c.InflInst) - Math.Log(1 + g.WorldInflation)) + speed * (lnRer - logTarget);
        if (c.Regime == FxRegime.Peg) dlnFx = Maths.Clamp(speed * (lnRer - logTarget), -0.03, 0.03); // crawling peg: only corrects large misalignments
        dlnFx = Maths.Clamp(dlnFx, -1.5, 3.0);
        c.Fx *= Math.Exp(dlnFx * dt);
        c.FxChange += (dlnFx - c.FxChange) * 0.3;
        c.Rer = Math.Exp(Math.Log(pwr * c.Fx0 / c.Fx));

        double impMonth = Math.Max(1e-9, c.Imports) / 12;
        c.Reserves = Maths.Clamp(c.Reserves + dt * (ca - c.CaTarget * c.Gdp + (c.FdiInflow - c.Fdi0)) / impMonth * 0.5, 0, 30);
        if (c.Regime == FxRegime.Peg && c.Reserves < 0.8 && c.Rer > 1.25)
        {
            c.Regime = FxRegime.Float; c.Fx *= 1.25; c.Status = "peg-broken";
            c.Stability = Math.Max(0.1, c.Stability - 0.1);
        }
    }

    static void Environment(CountryState c, GlobalState g, double dt)
    {
        double green = Math.Log(c.AssetIdx[(int)Asset.Green]);
        double carbon = Math.Max(c.CarbonPrice, g.GlobalCarbonPrice);
        c.Renewables = Maths.Clamp(c.Renewables + dt * ((0.004 + 0.015 * green + 0.00012 * carbon + c.Mod("renewables")) * (1 - c.Renewables)), 0, 0.97);
        double years = c.Tick / 12.0;
        double intensity = (1 - 0.8 * (c.Renewables - c.Renewables0)) * Math.Exp(-0.012 * years) * (1 - Math.Min(0.4, 0.0015 * carbon)) * (1 + c.Mod("emissions"));
        c.EmissionsMt = c.EmissionsMt0 * (c.Gdp / c.Gdp0) * Math.Max(0.05, intensity);
        double vuln = Maths.Clamp(1.4 - Maths.Clamp(c.GdpPerCapitaUsd / 60000, 0, 1), 0.6, 1.5);
        double dT = Math.Max(0, g.TempAnomaly * g.TempAnomaly - g.TempAnomaly0 * g.TempAnomaly0);
        c.ClimateDamage = Maths.Clamp(0.0035 * dT * vuln, 0, 0.5);
    }

    static void Bound(CountryState c)
    {
        c.Inflation = Maths.Clamp(c.Inflation, -0.2, 5.0);
        c.Approval = Maths.Clamp(c.Approval, 0.02, 0.98);
        c.Stability = Maths.Clamp(c.Stability, 0.02, 0.98);
        c.Unrest = Maths.Clamp(c.Unrest, 0, 1);
        c.Gini = Maths.Clamp(c.Gini, 0.15, 0.7);
        c.PoliticalCapital = Maths.Clamp(c.PoliticalCapital, 0, 100);
        c.Corruption = Maths.Clamp(c.Corruption, 0, 1);
        for (int i = 0; i < Dim.Taxes; i++) c.TaxRate[i] = Maths.Clamp(c.TaxRate[i], 0, 0.9);
        for (int i = 0; i < Dim.Lines; i++) c.Budget[i] = Maths.Clamp(c.Budget[i], 0, 0.6);
    }
}
