using System.Globalization;

namespace Sim.Core.Util;

/// <summary>Culture-stable text formatting (British conventions, no space before %).</summary>
public static class Fmt
{
    public static string P(double v, int digits = 0) => (v * 100).ToString("F" + digits, CultureInfo.InvariantCulture) + "%";
}
