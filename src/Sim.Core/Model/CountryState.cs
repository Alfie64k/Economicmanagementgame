using System.Text.Json.Serialization;

namespace Sim.Core.Model;

/// <summary>Snapshot of key aggregates for year-on-year attribution ("why did this change?").</summary>
public sealed class CompSnap
{
    public double Cons, InvPriv, GovCons, GovInv, Exports, Imports, Gdp, Potential, Revenue, Spending, Interest, GdpNom, Capital, LabourEff, Tfp, Debt, Approval, Unemp, Inflation, Gini;
}

/// <summary>A temporary policy-style modifier created by an event; removed when it expires.</summary>
public sealed class TimedMod { public string Key = ""; public double Value; public int ExpireMonth; public string Source = ""; }

/// <summary>A policy the government has enacted (possibly still waiting to take effect).</summary>
public sealed class ActivePolicy
{
    public string Id = "";
    public int EnactedMonth, ActivationMonth;
    public bool Active;
    public bool Failed;       // vote lost in the legislature
}

/// <summary>A public investment project in the build pipeline (or completed and being maintained).</summary>
public sealed class Project
{
    public string Id = "", Asset = "";
    public double Total;            // planned cost (real LCU)
    public int PlannedMonths;
    public double Overrun = 1.0;    // hidden cost multiplier
    public double Delay = 1.0;      // hidden schedule multiplier
    public double Elapsed;          // months
    public double Spent;
    public double Bonus, MaintRate;
    public bool Done;
    public int StartMonth;
    public double ActualMonths => PlannedMonths * Delay;
}


/// <summary>
/// Full dynamic state of one country. Real quantities are in base-year LCU billions (annualised flows),
/// prices are an index (1.0 at start), debt is nominal LCU billions. Public fields so the whole thing serialises as-is.
/// </summary>
public sealed class CountryState
{
    // ---- identity ----
    public string Id = "", Name = "", Currency = "", Region = "", Archetype = "", Gov = "", Bloc = "";
    public double Lat, Lon;
    public FxRegime Regime = FxRegime.Float;

    // ---- demography ----
    public double Pop;                 // millions
    public double Young, Working, Old; // shares, sum to 1
    public double Fertility, LifeExp, Participation, MigrationPer1000;

    // ---- supply side ----
    public double[] K = new double[Dim.Sectors];       // private capital by sector (real LCU bn)
    public double[] Tfp = new double[Dim.Sectors];     // total factor productivity by sector
    public double[] Alpha = new double[Dim.Sectors];   // capital share by sector
    public double[] LabourShare = new double[Dim.Sectors]; // employment share by sector
    public double[] InvestShare = new double[Dim.Sectors]; // private investment allocation
    public double[] SectorVa = new double[Dim.Sectors];    // realised value added (real)
    public double[] SectorPot = new double[Dim.Sectors];   // potential value added (real)
    public double[] Ret0 = new double[Dim.Sectors], Mpl0 = new double[Dim.Sectors], Va0 = new double[Dim.Sectors];
    public double[] Leontief = new double[Dim.Sectors * Dim.Sectors]; // (I-A)^-1, row-major
    public double[] FinalDemand0 = new double[Dim.Sectors];
    public double[] SectorSubsidy = new double[Dim.Sectors]; // industrial-policy subsidy (share of sector VA)
    public double Depreciation = 0.05;
    public double GrowthTrend;        // data: trend real GDP growth
    public double BaseTfpGrowth;      // calibrated annual baseline TFP growth
    public double HumanCapital = 1.0;
    public double Potential;          // potential real GDP
    public double Potential0;         // potential at start (for scaling)

    // ---- public asset indices and project pipeline ----
    public double[] AssetIdx = Enumerable.Repeat(1.0, Dim.Assets).ToArray();

    // ---- prices, rates ----
    public double PriceLevel = 1.0, Inflation, InflExp, InflTarget = 0.02, CbIndependence = 0.7;
    public double PolicyRate, ManualRate, Yield10, Spread, AvgDebtCost, RealLoanRate, RealLoanRate0;
    public RateMode RateMode = RateMode.Auto;
    public double NaturalRate = 0.01;
    public double Spread0, Spread0Risk, SpreadBase, SpreadStar;
    public double Cred = 0.7;
    public double DebtGdp0;
    public bool Autopilot;
    /// <summary>Latest inflation drivers (annualised pp): expectations, output gap, cost push, FX pass-through, monetisation, other. For explainability.</summary>
    public double[] InflDrivers = new double[6];

    // ---- nominal exchange rate ----
    public double Fx = 1, Fx0 = 1;     // LCU per USD
    public double Rer = 1.0;           // real exchange rate index (higher = pricier/less competitive)

    // ---- real activity (annualised, real) ----
    public double Gdp, Cons, InvPriv, GovCons, GovInv, Exports, Imports, FdiInflow;
    public double Gap, Unemp, NairU, RealWageIdx = 1.0, WageIdx = 1.0, Gini, Gini0;
    public double GdpGrowth;       // yoy, smoothed
    public double GdpLag12;        // GDP a year ago (rolling via ring)
    public double[] GdpRing = new double[12];
    public double SavingsRate, SavingsRate0, ConsBaseline;
    public double HhIncomeShare = 0.65;
    public double Wealth = 0;
    public double Cons0, InvPriv0, GovCons0, GovInv0, Gdp0, Old0, Unemp0, NairUBase, AbsorptionBase, Pop0;
    public double[] PriceRing = new double[12];
    public double InflInst, FxChange;
    public int Tick;
    public double Burden0;
    public double PotLag, Deficit0Share, LastVatRate, LastTariffRate, ExtDemandIdx, Renewables0;
    public double[] LabourShare0 = new double[Dim.Sectors];
    public double Corruption0, RealRate0, CaOffsetShare, Stability0, Approval0;

    // ---- government ----
    public double[] TaxRate = new double[Dim.Taxes];   // effective rates on their bases
    public double[] TaxRate0 = new double[Dim.Taxes];
    public double[] Budget = new double[Dim.Lines];    // share of potential GDP
    public double[] Budget0 = new double[Dim.Lines];
    public double ResourceRev0Share;                    // baseline resource revenue (share of GDP)
    public double OtherRevShare;                        // non-tax revenue as share of GDP
    public double Social0Real;                          // baseline social transfers (real)
    public double Debt;               // nominal LCU bn
    public double Revenue, Spending, Interest, PrimaryBalance, Deficit; // nominal flows, annualised
    public double ForeignDebtShare = 0.2;
    public double ImportTariffExtra, ContagionRisk, TradeRevenueExtra;
    public string Style = "technocrat";
    public double OtherRevenue;       // one-offs (privatisation etc.), nominal, annualised
    public double MinWageRatio = 0.5, MinWageRatio0 = 0.5;

    // ---- external ----
    public double CurrentAccount;     // real, annualised
    public double CaTarget;           // sustainable CA/GDP
    public double Reserves = 6;       // months of imports
    public double WorldLink = 1.0;    // multiplier on external demand (sanctions, deals)
    public double TariffRetaliation;  // foreign tariffs on our exports
    public double Openness0;
    public double X0, M0, Fdi0, EnergyNetExport0;

    // ---- society & politics ----
    public double Democracy, Corruption, Approval, Stability = 0.7, Unrest, PoliticalCapital = 50;
    public int NextElectionMonth = -1;
    public double Coalition = 0.5;    // legislative friction proxy (1 = strong majority)

    // ---- environment ----
    public double Renewables, EmissionsMt, EmissionsMt0, CarbonPrice, ClimateDamage;
    public double EnergyNet0;

    // ---- crises / status ----
    public CompSnap[] CompRing = new CompSnap[12];
    public double[] ApprovalDrivers = new double[9];   // base, growth, unemployment, inflation, inequality, taxes, services, corruption, other
    public double[] NairuParts = new double[5];        // base, minimum wage, payroll tax, human capital, policy
    public bool InDefault;
    public int DefaultUntil, ImfUntil, ElectionTerm = 48;
    public bool ImfAutopilot;
    public List<TimedMod> TimedMods = new();
    public Dictionary<string, int> EventLast = new();
    public int CrisisMonths;
    public string Status = "";

    // ---- policies & projects ----
    public List<ActivePolicy> Policies = new();
    public List<Project> Projects = new();
    public double[] AssetBoost = new double[Dim.Assets];   // permanent boost from completed projects
    public double ProjectFlow, MaintFlow, SubsidyCost;     // annualised real flows

    // ---- policy modifiers (ramped), see PolicyCatalog ----
    public Dictionary<string, double> Mods = new();
    public Dictionary<string, double> ModTarget = new();

    /// <summary>Rise in sovereign risk premium above its (slowly normalising) baseline.</summary>
    [JsonIgnore] public double RiskPremium => Spread - SpreadBase;
    [JsonIgnore] public double EffTariff => TaxRate[(int)Tax.Tariff] + ImportTariffExtra;
    [JsonIgnore] public double GdpNominal => Gdp * PriceLevel;
    [JsonIgnore] public double DebtToGdp => Debt / Math.Max(1e-9, GdpNominal);
    [JsonIgnore] public double GdpUsdBn => GdpNominal / Fx;
    [JsonIgnore] public double GdpPerCapitaUsd => GdpUsdBn * 1000.0 / Pop;
    [JsonIgnore] public double DeficitToGdp => Deficit / Math.Max(1e-9, GdpNominal);
    [JsonIgnore] public double CaToGdp => CurrentAccount / Math.Max(1e-9, Gdp);
    [JsonIgnore] public double RealRate => PolicyRate - InflExp;

    public double Mod(string key) => Mods.TryGetValue(key, out var v) ? v : 0.0;
}
