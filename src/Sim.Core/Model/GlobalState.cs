namespace Sim.Core.Model;

/// <summary>World-level variables shared by all countries.</summary>
public sealed class GlobalState
{
    public double OilIdx = 1.0, FoodIdx = 1.0, CommodityIdx = 1.0;
    public double OilYoY, FoodYoY;
    public double[] OilRing = new double[12], FoodRing = new double[12];
    public double WorldDemandIdx = 1.0;      // real external demand index
    public double WorldDemandTrend = 1.0;    // baseline path of the index (exports respond to deviations from it)
    public double WorldRate = 0.05;         // USD policy rate proxy
    public double WorldInflation = 0.03;
    public double RiskAppetite = 1.0;        // 1 = normal; <1 = risk-off
    public double TempAnomaly = 1.2, TempAnomaly0 = 1.2;
    public double GlobalCarbonPrice;         // USD/t, set by player/treaty
    public double WorldGrowthTrend = 0.025;
    public double WorldRate0 = 0.05;
    public double WorldPrice = 1.0;          // world price index (USD)

    public GlobalState() { Array.Fill(OilRing, 1.0); Array.Fill(FoodRing, 1.0); }
}
