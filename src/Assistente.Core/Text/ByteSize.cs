using System.Globalization;

namespace Assistente.Core.Text;

public static class ByteSize
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string Format(double bytes, IFormatProvider? provider = null)
    {
        provider ??= CultureInfo.CurrentCulture;
        var unit = 0;
        var value = bytes;
        while (System.Math.Abs(value) >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        var format = unit == 0 ? "0" : "0.#";
        return value.ToString(format, provider) + " " + Units[unit];
    }
}
