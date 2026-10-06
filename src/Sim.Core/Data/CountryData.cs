using System.Text.Json.Serialization;

namespace Sim.Core.Data;

/// <summary>Static starting data for a country (one record in data/countries.json). Shares are fractions of GDP unless noted.</summary>
public sealed class CountryData
{
    public string Id { get; set; } = "";          // ISO3
    public string Name { get; set; } = "";
    public string Currency { get; set; } = "";
    public string Region { get; set; } = "";
    public string Archetype { get; set; } = "";   // advanced | emerging | resource | developing | hub
    public string Gov { get; set; } = "democracy"; // democracy | hybrid | autocracy
    public string Fx { get; set; } = "float";      // float | managed | peg
    public string Bloc { get; set; } = "none";
    public double Lat { get; set; }
    public double Lon { get; set; }
    public double PopM { get; set; }
    public double GdpLcuBn { get; set; }
    public double UsdFx { get; set; } = 1;        // LCU per USD

    public SectorShares Sectors { get; set; } = new();
    public DemandShares Demand { get; set; } = new();
    public FiscalData Fiscal { get; set; } = new();
    public MacroData Macro { get; set; } = new();
    public DemogData Demog { get; set; } = new();
    public EnergyData Energy { get; set; } = new();
    public PoliticsData Politics { get; set; } = new();

    public double GdpPerCapitaUsd => GdpLcuBn / UsdFx * 1000.0 / PopM;
}

public sealed class SectorShares
{
    public double Agri { get; set; }
    public double Energy { get; set; }
    public double Manuf { get; set; }
    public double Services { get; set; }
    public double Finance { get; set; }
    public double Public { get; set; }
    public double Sum => Agri + Energy + Manuf + Services + Finance + Public;
}

public sealed class DemandShares
{
    public double Cons { get; set; }
    public double Inv { get; set; }
    public double Gov { get; set; }
    public double Exp { get; set; }
    public double Imp { get; set; }
}

public sealed class FiscalData
{
    public double Revenue { get; set; }
    public double Health { get; set; }
    public double Education { get; set; }
    public double Defence { get; set; }
    public double Infra { get; set; }
    public double Rnd { get; set; }
    public double Deficit { get; set; }   // positive = deficit
    public double Debt { get; set; }      // % GDP as fraction
    public double ResourceRevenue { get; set; } // share of total revenue from resources
    public double ForeignDebtShare { get; set; } = 0.2; // share of public debt held abroad
}

public sealed class MacroData
{
    public double PolicyRate { get; set; }
    public double Yield10 { get; set; }
    public double Inflation { get; set; }
    public double Unemployment { get; set; }
    public double Gini { get; set; }
    public double GrowthTrend { get; set; }
    public double CurrentAccount { get; set; }
    public double InflationTarget { get; set; } = 0.02;
    public double CbIndependence { get; set; } = 0.7; // 0..1
}

public sealed class DemogData
{
    public double Young { get; set; }
    public double Old { get; set; }
    public double Fertility { get; set; }
    public double LifeExp { get; set; }
    public double Participation { get; set; }
    public double Migration { get; set; } // net per 1000 per year
}

public sealed class EnergyData
{
    public double Renewables { get; set; }   // share of electricity/energy 0..1
    public double EmissionsMt { get; set; }  // CO2 Mt/yr
    public double NetExportGdp { get; set; } // net energy exports as share of GDP (negative = importer)
}

public sealed class PoliticsData
{
    public double Democracy { get; set; }  // 0..1
    public double Corruption { get; set; } // 0..1 (higher = worse)
    public double Approval { get; set; } = 0.45;
}
