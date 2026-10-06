using System.Collections.Generic;
using System.Globalization;
using Sim.Core.Model;

namespace EconGame.App;

/// <summary>Currency formatting. Models are in each country's own currency; neutral comparisons are shown in GBP.</summary>
public static class Money
{
    static readonly Dictionary<string, string> Symbols = new()
    {
        ["USD"] = "$", ["GBP"] = "£", ["EUR"] = "€", ["JPY"] = "¥", ["CNY"] = "CN¥", ["INR"] = "₹", ["BRL"] = "R$", ["RUB"] = "₽",
        ["SAR"] = "SAR ", ["SGD"] = "S$", ["ARS"] = "AR$", ["NGN"] = "₦", ["ZAR"] = "R", ["MXN"] = "MX$", ["IDR"] = "Rp", ["TRY"] = "₺",
        ["KRW"] = "₩", ["AUD"] = "A$", ["CAD"] = "C$", ["EGP"] = "E£", ["VND"] = "₫", ["PLN"] = "zł", ["CLP"] = "CL$", ["ETB"] = "Br",
        ["AED"] = "AED ", ["CHF"] = "CHF ", ["NOK"] = "kr ",
    };

    public static string Symbol(string cur) => Symbols.TryGetValue(cur, out var s) ? s : cur + " ";

    /// <summary>Format an amount given in billions of local currency.</summary>
    public static string Bn(string currency, double bn)
    {
        string sym = Symbol(currency); double a = System.Math.Abs(bn); string sign = bn < 0 ? "-" : "";
        string body = a >= 1e6 ? $"{a / 1e6:0.##}qd" : a >= 1000 ? $"{a / 1000:0.##}tn" : a >= 10 ? $"{a:0}bn" : a >= 1 ? $"{a:0.0}bn" : $"{a * 1000:0}m";
        return sign + sym + body;
    }

    public static string Local(CountryState c, double bn) => Bn(c.Currency, bn);

    /// <summary>GBP per USD, refreshed whenever a world is created or loaded.</summary>
    public static double GbpRate = 0.8;
    public static void Track(World w) { GbpRate = w.Find("GBR")?.Fx ?? GbpRate; }
    public static string Gbp(double usdBn) => Bn("GBP", usdBn * GbpRate);

    public static string Num(double v, int d = 1) => v.ToString("N" + d, CultureInfo.InvariantCulture);
}
