using System.Globalization;

namespace Sim.Core.Util;

/// <summary>Culture-stable text formatting (British conventions, no space before %).</summary>
public static class Fmt
{
    public static string P(double v, int digits = 0) => (v * 100).ToString("F" + digits, CultureInfo.InvariantCulture) + "%";

    static readonly Dictionary<string, string> Symbols = new()
    {
        ["USD"] = "$", ["GBP"] = "£", ["EUR"] = "€", ["JPY"] = "¥", ["CNY"] = "CN¥", ["INR"] = "₹", ["BRL"] = "R$", ["RUB"] = "₽",
        ["SAR"] = "SAR ", ["SGD"] = "S$", ["ARS"] = "AR$", ["NGN"] = "₦", ["ZAR"] = "R", ["MXN"] = "MX$", ["IDR"] = "Rp", ["TRY"] = "₺",
        ["KRW"] = "₩", ["AUD"] = "A$", ["CAD"] = "C$", ["EGP"] = "E£", ["VND"] = "₫", ["PLN"] = "zł", ["CLP"] = "CL$", ["ETB"] = "Br",
        ["AED"] = "AED ", ["CHF"] = "CHF ", ["NOK"] = "kr ",
    };

    public static string Symbol(string currency) => Symbols.TryGetValue(currency, out var s) ? s : currency + " ";

    /// <summary>A personal amount (an allowance, a benefit) in local currency: "£12,570", "¥4.8m", "₫310m".</summary>
    public static string Amount(string currency, double v)
    {
        string sign = v < 0 ? "-" : ""; double a = Math.Abs(v);
        string body = a >= 1e9 ? (a / 1e9).ToString("0.##", CultureInfo.InvariantCulture) + "bn"
                    : a >= 1e6 ? (a / 1e6).ToString("0.##", CultureInfo.InvariantCulture) + "m"
                    : a >= 100 ? Math.Round(a).ToString("N0", CultureInfo.InvariantCulture)
                    : a.ToString("0.##", CultureInfo.InvariantCulture);
        return sign + Symbol(currency) + body;
    }
}
