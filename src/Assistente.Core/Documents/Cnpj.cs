namespace Assistente.Core.Documents;

/// <summary>
/// Geração e validação de CNPJ (apenas para testes de software).
/// A validação aceita também o CNPJ alfanumérico (IN RFB 2.229/2024), em que
/// cada caractere vale (código ASCII − 48).
/// </summary>
public static class Cnpj
{
    private static readonly int[] Weights1 = { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
    private static readonly int[] Weights2 = { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };

    /// <param name="branch">Número da filial (0001 = matriz).</param>
    public static string Generate(bool formatted = true, int branch = 1, Random? rng = null)
    {
        rng ??= Random.Shared;
        if (branch is < 1 or > 9999) throw new ArgumentOutOfRangeException(nameof(branch));

        var root = new char[8];
        do
        {
            for (var i = 0; i < 8; i++) root[i] = (char)('0' + rng.Next(10));
        } while (DigitUtils.AllSame(root.Select(c => c - '0').ToArray()));

        var baseValue = new string(root) + branch.ToString("D4");
        var raw = baseValue + ComputeCheckDigits(baseValue);
        return formatted ? Format(raw) : raw;
    }

    public static bool IsValid(string? value)
    {
        var s = Normalize(value);
        if (s.Length != 14) return false;
        if (!s[..12].All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c))) return false;
        if (!char.IsAsciiDigit(s[12]) || !char.IsAsciiDigit(s[13])) return false;
        if (s.Distinct().Count() == 1) return false;

        return ComputeCheckDigits(s[..12]) == s[12..];
    }

    public static string Format(string value)
    {
        var s = Normalize(value);
        return s.Length == 14 ? $"{s[..2]}.{s[2..5]}.{s[5..8]}/{s[8..12]}-{s[12..]}" : value;
    }

    /// <summary>Calcula os 2 dígitos verificadores para uma base de 12 caracteres.</summary>
    public static string ComputeCheckDigits(string base12)
    {
        if (base12.Length != 12) throw new ArgumentException("A base do CNPJ deve ter 12 caracteres.", nameof(base12));
        var values = base12.Select(c => c - '0').ToList();
        var d1 = Mod11(values, Weights1);
        values.Add(d1);
        var d2 = Mod11(values, Weights2);
        return $"{d1}{d2}";
    }

    private static int Mod11(IReadOnlyList<int> values, int[] weights)
    {
        var sum = 0;
        for (var i = 0; i < weights.Length; i++) sum += values[i] * weights[i];
        var r = sum % 11;
        return r < 2 ? 0 : 11 - r;
    }

    private static string Normalize(string? value) =>
        new((value ?? "").ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
}
