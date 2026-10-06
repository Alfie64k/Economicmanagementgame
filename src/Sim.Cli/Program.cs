using Sim.Core.Data;
using Sim.Core.Engine;
using Sim.Core.Model;
using Sim.Core.Policy;

static class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0) { Console.WriteLine("usage: Sim.Cli <smoke|run|validate> [--years N] [--country ID] [--seed N] [--det]"); return 1; }
        string cmd = args[0];
        int years = Arg(args, "--years", 30);
        ulong seed = (ulong)Arg(args, "--seed", 1);
        bool det = args.Contains("--det");
        string country = ArgS(args, "--country", "GBR");

        switch (cmd)
        {
            case "validate":
                {
                    int bad = 0;
                    foreach (var d in CountryLoader.LoadEmbedded()) foreach (var p in CountryLoader.Validate(d)) { Console.WriteLine(p); bad++; }
                    Console.WriteLine(bad == 0 ? "roster OK" : $"{bad} problems");
                    return bad == 0 ? 0 : 2;
                }
            case "run":
                {
                    var sim = Simulation.New(country, seed, !det);
                    var c = sim.World.Player;
                    Console.WriteLine("year   gdp$bn  gr%  infl%  u%   rate%  y10%  debt%  def%  fx      appr  gini  ca%  gap%  nairu");
                    for (int y = 0; y <= years; y++)
                    {
                        Console.WriteLine(Row(sim.World.Year, c));
                        if (y < years) sim.Run(12);
                    }
                    return 0;
                }
            case "events":
                {
                    var sim = Simulation.New(country, seed, true);
                    sim.World.RecordHistory = false;
                    while (sim.World.Month < years * 12 && !sim.World.GameOver)
                    {
                        sim.Tick();
                        foreach (var d in sim.World.Decisions.ToList()) sim.Resolve(d.Id, d.DefaultChoice);
                    }
                    foreach (var g in sim.World.EventHistory.GroupBy(e => e.EventId).OrderByDescending(g => g.Count()))
                        Console.WriteLine($"{g.Key,-28} {g.Count(),4}  ({g.Count() / (double)years:F2}/yr)");
                    Console.WriteLine(sim.World.GameOver ? "GAME OVER: " + sim.World.GameOverReason : "alive");
                    foreach (var l in sim.World.Log.Where(l => l.Kind is "news" or "crisis").Take(25)) Console.WriteLine($"{l.Month / 12 + 2024}.{l.Month % 12 + 1:D2} [{l.Country}] {l.Text}");
                    return 0;
                }
            case "reckless":
                {
                    var sim = Simulation.New(country, seed, !det);
                    sim.World.RecordHistory = false; sim.World.Advisors = false;
                    var c = sim.World.Player;
                    Console.WriteLine("year   gdp$bn  gr%  infl%  u%   rate%  y10%  debt%  def%  fx      appr  gini  ca%  gap%  nairu");
                    for (int y = 0; y <= years; y++)
                    {
                        Console.WriteLine(Row(sim.World.Year, c));
                        if (y == years) break;
                        c.PoliticalCapital = 100;
                        sim.Execute(Command.SetTax(c.Id, Tax.Consumption, c.TaxRate[(int)Tax.Consumption] * 0.8));
                        sim.Execute(Command.SetTax(c.Id, Tax.Income, c.TaxRate[(int)Tax.Income] * 0.85));
                        sim.Execute(Command.SetBudget(c.Id, BudgetLine.Social, c.Budget[(int)BudgetLine.Social] + 0.01));
                        sim.Run(12);
                        foreach (var d in sim.World.Decisions.ToList()) sim.Resolve(d.Id, d.DefaultChoice);
                    }
                    return 0;
                }
            case "balance":
                {
                    int seeds = Arg(args, "--seeds", 3);
                    var countries = (ArgS(args, "--countries", "USA,GBR,DEU,JPN,CHN,IND,BRA,NGA,SAU,SGP,TUR,ETH,KOR,MEX,POL,RUS")).Split(',');
                    var res = Sim.Core.Scoring.Balance.Matrix(countries, seeds, years);
                    var rep = Sim.Core.Scoring.Balance.Analyse(res);
                    var strategies = Sim.Core.Scoring.Balance.Strategies.Select(x => x.Name).ToList();
                    Console.WriteLine($"{"archetype",-12}" + string.Join("", strategies.Select(x => $"{x,14}")) + "   best");
                    foreach (var kv in rep.MeanByArchetype)
                        Console.WriteLine($"{kv.Key,-12}" + string.Join("", strategies.Select(x => $"{kv.Value[x],14:F1}")) + $"   {rep.BestByArchetype[kv.Key]}");
                    Console.WriteLine($"{"overall",-12}" + string.Join("", strategies.Select(x => $"{rep.Overall[x],14:F1}")));
                    Console.WriteLine($"runaway rate {rep.RunawayRate:P1}");
                    foreach (var f in rep.Findings) Console.WriteLine("FINDING: " + f);
                    return rep.Findings.Count == 0 ? 0 : 4;
                }
            case "trace":
                {
                    var sim = Simulation.New(country, seed, false);
                    var c = sim.World.Player;
                    Console.WriteLine("m    gdp     pot    gap%   C       I       Gc      Gi      X       M     infl%  u%   rate  rer   fx");
                    for (int m = 0; m <= years * 12; m++)
                    {
                        if (m < 14 || m % 12 == 0) Console.WriteLine($"{m,3} {c.Gdp,7:F0} {c.Potential,7:F0} {c.Gap * 100,5:F1} {c.Cons,7:F0} {c.InvPriv,7:F0} {c.GovCons,7:F0} {c.GovInv,6:F0} {c.Exports,7:F0} {c.Imports,7:F0} {c.Inflation * 100,5:F1} {c.Unemp * 100,4:F1} {c.PolicyRate * 100,5:F2} {c.Rer,5:F3} {c.Fx,5:F3} | d: {string.Join(" ", c.InflDrivers.Select(x => (x * 100).ToString("F1")))}");
                        sim.Tick();
                    }
                    return 0;
                }
            case "smoke":
                {
                    var sim = Simulation.New("GBR", seed, !det);
                    sim.World.RecordHistory = false;
                    var st = sim.World.Countries.ToDictionary(c => c.Id, c => new double[] { 0, 0, 9 }); // maxInfl, maxDebt, minGrowth
                    for (int y = 0; y < years; y++)
                    {
                        sim.Run(12);
                        foreach (var c in sim.World.Countries)
                        {
                            var a = st[c.Id];
                            a[0] = Math.Max(a[0], c.Inflation); a[1] = Math.Max(a[1], c.DebtToGdp); a[2] = Math.Min(a[2], c.GdpGrowth);
                        }
                    }
                    int bad = 0;
                    Console.WriteLine("id  maxInfl% maxDebt% minGr% | end: " + "year   gdp$bn  gr%  infl%  u%   rate%  y10%  debt%  def%  fx      appr  gini  ca%  gap%  nairu");
                    foreach (var c in sim.World.Countries)
                    {
                        string? why = Check(c);
                        var a = st[c.Id];
                        bool unstable = why != null || a[0] > 1.2 || a[1] > 4.0 || a[2] < -0.20;
                        if (unstable) { bad++; }
                        Console.WriteLine($"{(unstable ? "!!" : "  ")}{c.Id} {a[0] * 100,7:F0} {a[1] * 100,7:F0} {a[2] * 100,6:F1} | {Row(sim.World.Year, c)} {why}");
                    }
                    Console.WriteLine(bad == 0 ? "smoke OK" : $"{bad} unstable");
                    return bad == 0 ? 0 : 3;
                }
        }
        return 1;
    }

    public static string? Check(CountryState c)
    {
        double[] v = { c.Gdp, c.Debt, c.PriceLevel, c.PolicyRate, c.Fx, c.Unemp, c.Approval, c.Pop, c.Inflation, c.Yield10, c.Potential };
        if (v.Any(x => double.IsNaN(x) || double.IsInfinity(x))) return "non-finite state";
        if (c.K.Any(k => k <= 0)) return "non-positive capital";
        if (c.Gdp <= 0) return "non-positive GDP";
        return null;
    }

    static string Row(int year, CountryState c) =>
        $"{year} {c.GdpUsdBn,9:F0} {c.GdpGrowth * 100,5:F1} {c.Inflation * 100,6:F1} {c.Unemp * 100,5:F1} {c.PolicyRate * 100,5:F1} {c.Yield10 * 100,5:F1} {c.DebtToGdp * 100,6:F0} {c.DeficitToGdp * 100,5:F1} {c.Fx,8:F2} {c.Approval,5:F2} {c.Gini,5:F2} {c.CaToGdp * 100,5:F1} {c.Gap * 100,5:F1} {c.NairU * 100,5:F1}";

    static int Arg(string[] a, string k, int d) { int i = Array.IndexOf(a, k); return i >= 0 && i + 1 < a.Length && int.TryParse(a[i + 1], out var v) ? v : d; }
    static string ArgS(string[] a, string k, string d) { int i = Array.IndexOf(a, k); return i >= 0 && i + 1 < a.Length ? a[i + 1] : d; }
}
