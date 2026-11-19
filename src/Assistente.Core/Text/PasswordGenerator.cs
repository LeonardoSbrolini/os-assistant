using System.Security.Cryptography;

namespace Assistente.Core.Text;

public sealed record PasswordOptions(
    int Length = 16,
    bool Lowercase = true,
    bool Uppercase = true,
    bool Digits = true,
    bool Symbols = true,
    bool AvoidAmbiguous = false);

public static class PasswordGenerator
{
    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Digit = "0123456789";
    private const string Symbol = "!@#$%&*()-_=+[]{};:,.?/";
    private const string Ambiguous = "Il1O0o";

    /// <summary>Gera uma senha com gerador criptográfico, garantindo 1 caractere de cada grupo escolhido.</summary>
    public static string Generate(PasswordOptions options)
    {
        var groups = new List<string>();
        if (options.Lowercase) groups.Add(Lower);
        if (options.Uppercase) groups.Add(Upper);
        if (options.Digits) groups.Add(Digit);
        if (options.Symbols) groups.Add(Symbol);
        if (groups.Count == 0) throw new ArgumentException("Escolha pelo menos um tipo de caractere.");
        if (options.Length < groups.Count || options.Length > 512)
            throw new ArgumentOutOfRangeException(nameof(options), $"O tamanho deve estar entre {groups.Count} e 512.");

        if (options.AvoidAmbiguous)
            groups = groups.Select(g => new string(g.Where(c => !Ambiguous.Contains(c)).ToArray())).ToList();

        var all = string.Concat(groups);
        var chars = new char[options.Length];
        for (var i = 0; i < groups.Count; i++) chars[i] = Pick(groups[i]);
        for (var i = groups.Count; i < chars.Length; i++) chars[i] = Pick(all);

        // Embaralha (Fisher–Yates) para os caracteres obrigatórios não ficarem no início.
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars);
    }

    private static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
}
