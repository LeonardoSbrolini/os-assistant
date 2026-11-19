namespace Assistente.Core.Documents;

internal static class DigitUtils
{
    public static string OnlyDigits(string? value) =>
        new((value ?? "").Where(char.IsAsciiDigit).ToArray());

    public static bool AllSame(ReadOnlySpan<int> values)
    {
        for (var i = 1; i < values.Length; i++)
            if (values[i] != values[0]) return false;
        return true;
    }

    public static bool AllSame(int[] values) => AllSame(values.AsSpan());
}
