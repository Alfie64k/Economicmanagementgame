namespace Sim.Core.Model;

public enum Sector { Agriculture, Energy, Manufacturing, Services, Finance, Public }

/// <summary>Budget lines the player allocates, expressed as share of potential GDP.</summary>
public enum BudgetLine { Social, Health, Education, Defence, Infrastructure, RnD, Green, Housing, Digital, Admin }

/// <summary>Public asset stocks (index 1.0 = baseline stock-to-GDP).</summary>
public enum Asset { Infrastructure, Education, Health, RnD, Green, Housing, Digital, Defence }

public enum Tax { Income, Corporate, Consumption, Payroll, Tariff }

public enum FxRegime { Float, Managed, Peg }
public enum RateMode { Auto, Manual }

public static class Dim
{
    public static readonly int Sectors = Enum.GetValues<Sector>().Length;
    public static readonly int Lines = Enum.GetValues<BudgetLine>().Length;
    public static readonly int Assets = Enum.GetValues<Asset>().Length;
    public static readonly int Taxes = Enum.GetValues<Tax>().Length;

    /// <summary>Budget line that feeds each asset.</summary>
    public static BudgetLine LineOf(Asset a) => a switch
    {
        Asset.Infrastructure => BudgetLine.Infrastructure,
        Asset.Education => BudgetLine.Education,
        Asset.Health => BudgetLine.Health,
        Asset.RnD => BudgetLine.RnD,
        Asset.Green => BudgetLine.Green,
        Asset.Housing => BudgetLine.Housing,
        Asset.Digital => BudgetLine.Digital,
        _ => BudgetLine.Defence,
    };

    /// <summary>Share of each budget line that is capital (investment) rather than current spending.</summary>
    public static readonly double[] CapitalFraction =
    {
        0.0,  // Social (pure transfers)
        0.10, // Health
        0.10, // Education
        0.20, // Defence
        0.80, // Infrastructure
        0.30, // RnD
        0.80, // Green
        0.70, // Housing
        0.60, // Digital
        0.05, // Admin
    };
}
