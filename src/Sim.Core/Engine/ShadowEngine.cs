using Sim.Core.Data;
using Sim.Core.Model;

namespace Sim.Core.Engine;

/// <summary>The informal ("shadow") economy: its starting size per country and, below, how it responds to taxes, corruption and enforcement.</summary>
public static class ShadowEngine
{
    /// <summary>Sets the shadow share to its catalogue starting value (new games and version 2 saves).</summary>
    public static void Init(CountryState c)
    {
        double s = ShadowCatalog.Share(c.Id, c.Archetype);
        c.Shadow = c.Shadow0 = s;
        Array.Clear(c.ShadowDrivers);
        c.ShadowDrivers[0] = s;
    }
}
