namespace Assistente.Core.Documents;

/// <summary>
/// Geração e validação de RG no modelo de São Paulo (8 dígitos + DV, que pode ser "X").
/// Cada estado tem sua própria regra; este é o modelo mais usado em massas de teste.
/// </summary>
public static class Rg
{
    public static string Generate(bool formatted = true, Random? rng = null)
    {
        rng ??= Random.Shared;
        var d = new int[8];
        do
        {
            for (var i = 0; i < 8; i++) d[i] = rng.Next(10);
        } while (DigitUtils.AllSame(d));

        var raw = string.Concat(d) + CheckDigit(d);
        return formatted ? Format(raw) : raw;
    }

    public static bool IsValid(string? value)
    {
        var s = new string((value ?? "").ToUpperInvariant().Where(c => char.IsAsciiDigit(c) || c == 'X').ToArray());
        if (s.Length != 9 || !s[..8].All(char.IsAsciiDigit)) return false;

        var d = s[..8].Select(c => c - '0').ToArray();
        if (DigitUtils.AllSame(d)) return false;
        return CheckDigit(d) == s[8];
    }

    public static string Format(string value)
    {
        var s = new string((value ?? "").ToUpperInvariant().Where(c => char.IsAsciiDigit(c) || c == 'X').ToArray());
        return s.Length == 9 ? $"{s[..2]}.{s[2..5]}.{s[5..8]}-{s[8]}" : value ?? "";
    }

    // Pesos 2..9, DV = 11 - (soma % 11); 10 => X, 11 => 0.
    private static char CheckDigit(int[] d)
    {
        var sum = 0;
        for (var i = 0; i < 8; i++) sum += d[i] * (i + 2);
        var dv = 11 - sum % 11;
        return dv switch
        {
            10 => 'X',
            11 => '0',
            _ => (char)('0' + dv),
        };
    }
}
