using Sim.Core.Data;
using Sim.Core.Engine;
using Sim.Core.Model;

namespace Sim.Core.Policy;

/// <summary>Brings saved games from earlier versions up to the current model without changing the economy they hold.</summary>
public static class Migrations
{
    public static void Upgrade(World w)
    {
        if (w.Version < 2)
        {
            // version 2 added the detailed tax-and-benefit code for the player's country: it starts from the country default and takes
            // whatever tax rates the saved game had as an offset, so nothing jumps on loading
            var p = w.Find(w.PlayerId);
            if (p != null && p.Fiscal == null)
            {
                var saved = p.TaxRate.ToArray();
                var f = TaxCodeCatalog.Build(p);
                if (f != null)
                {
                    p.Fiscal = f;
                    p.TaxRate = saved;                       // the first step reads the difference from the code's own rates as an offset
                    for (int i = 0; i < Dim.Taxes; i++) f.Written[i] = p.TaxRate0[i];
                }
            }
            w.Version = 2;
        }
    }
}
