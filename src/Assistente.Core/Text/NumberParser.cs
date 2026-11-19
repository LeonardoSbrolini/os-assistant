using System.Globalization;

namespace Assistente.Core.Text;

/// <summary>Interpreta números digitados no padrão brasileiro ou internacional.</summary>
public static class NumberParser
{
    /// <summary>
    /// "1.234,56" → 1234.56 ; "1234,56" → 1234.56 ; "1234.56" → 1234.56 ; "R$ 10" → 10 ; "5%" → 5
    /// </summary>
    public static bool TryParse(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var s = text.Trim().Replace("R$", "", StringComparison.OrdinalIgnoreCase).Replace("%", "").Replace(" ", "").Replace(" ", "");

        if (s.Contains(','))
        {
            // Padrão brasileiro: ponto é milhar, vírgula é decimal.
            s = s.Replace(".", "").Replace(',', '.');
        }
        else if (s.Count(c => c == '.') > 1)
        {
            // "1.234.567" → milhar.
            s = s.Replace(".", "");
        }

        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
