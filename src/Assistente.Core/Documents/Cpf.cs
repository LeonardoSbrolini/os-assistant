namespace Assistente.Core.Documents;

/// <summary>Geração e validação de CPF (apenas para testes de software).</summary>
public static class Cpf
{
    /// <summary>
    /// O 9º dígito do CPF indica a região fiscal de emissão.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> RegionDigitByUf = new Dictionary<string, int>
    {
        ["RS"] = 0,
        ["DF"] = 1, ["GO"] = 1, ["MS"] = 1, ["MT"] = 1, ["TO"] = 1,
        ["AC"] = 2, ["AM"] = 2, ["AP"] = 2, ["PA"] = 2, ["RO"] = 2, ["RR"] = 2,
        ["CE"] = 3, ["MA"] = 3, ["PI"] = 3,
        ["AL"] = 4, ["PB"] = 4, ["PE"] = 4, ["RN"] = 4,
        ["BA"] = 5, ["SE"] = 5,
        ["MG"] = 6,
        ["ES"] = 7, ["RJ"] = 7,
        ["SP"] = 8,
        ["PR"] = 9, ["SC"] = 9,
    };

    /// <param name="formatted">Se true, retorna no formato 000.000.000-00.</param>
    /// <param name="uf">Sigla do estado (opcional) para definir o dígito de região.</param>
    public static string Generate(bool formatted = true, string? uf = null, Random? rng = null)
    {
        rng ??= Random.Shared;
        var d = new int[11];
        do
        {
            for (var i = 0; i < 9; i++) d[i] = rng.Next(10);
            if (uf is not null && RegionDigitByUf.TryGetValue(uf.ToUpperInvariant(), out var region))
                d[8] = region;
        } while (DigitUtils.AllSame(d.AsSpan(0, 9)));

        d[9] = CheckDigit(d, 9);
        d[10] = CheckDigit(d, 10);

        var raw = string.Concat(d);
        return formatted ? Format(raw) : raw;
    }

    public static bool IsValid(string? value)
    {
        var digits = DigitUtils.OnlyDigits(value);
        if (digits.Length != 11) return false;

        var d = digits.Select(c => c - '0').ToArray();
        if (DigitUtils.AllSame(d)) return false;

        return CheckDigit(d, 9) == d[9] && CheckDigit(d, 10) == d[10];
    }

    public static string Format(string value)
    {
        var s = DigitUtils.OnlyDigits(value);
        return s.Length == 11 ? $"{s[..3]}.{s[3..6]}.{s[6..9]}-{s[9..]}" : value;
    }

    // Pesos decrescentes (10..2 para o 1º DV, 11..2 para o 2º), módulo 11.
    private static int CheckDigit(int[] d, int length)
    {
        var sum = 0;
        for (var i = 0; i < length; i++) sum += d[i] * (length + 1 - i);
        var r = sum % 11;
        return r < 2 ? 0 : 11 - r;
    }
}
