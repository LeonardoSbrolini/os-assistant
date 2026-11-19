namespace Assistente.Core.Calculations;

public enum RatePeriod { Monthly, Yearly }

public sealed record CompoundRow(int Month, double Invested, double Interest, double Balance);

public sealed record CompoundResult(
    double FinalBalance,
    double TotalInvested,
    double TotalInterest,
    double MonthlyRate,
    IReadOnlyList<CompoundRow> Rows);

public static class Finance
{
    public const int MaxMonths = 1200;

    /// <summary>Converte uma taxa (em %) para taxa mensal equivalente (fração).</summary>
    public static double ToMonthlyRate(double ratePercent, RatePeriod period)
    {
        var r = ratePercent / 100.0;
        return period == RatePeriod.Monthly ? r : System.Math.Pow(1 + r, 1.0 / 12) - 1;
    }

    /// <summary>
    /// Juros compostos com aporte mensal no fim de cada mês.
    /// </summary>
    public static CompoundResult CompoundInterest(
        double initial, double monthlyContribution, double ratePercent, RatePeriod period, int months)
    {
        if (months is < 0 or > MaxMonths)
            throw new ArgumentOutOfRangeException(nameof(months), $"O prazo deve estar entre 0 e {MaxMonths} meses.");
        if (ratePercent <= -100)
            throw new ArgumentOutOfRangeException(nameof(ratePercent), "A taxa deve ser maior que -100%.");

        var i = ToMonthlyRate(ratePercent, period);
        double balance = initial, invested = initial;
        var rows = new List<CompoundRow>(months);

        for (var m = 1; m <= months; m++)
        {
            balance = balance * (1 + i) + monthlyContribution;
            invested += monthlyContribution;
            rows.Add(new CompoundRow(m, invested, balance - invested, balance));
        }

        return new CompoundResult(balance, invested, balance - invested, i, rows);
    }

    /// <summary>Regra de três: A está para B assim como C está para X.</summary>
    public static double RuleOfThree(double a, double b, double c, bool inverse = false)
    {
        if (inverse)
        {
            if (c == 0) throw new DivideByZeroException("C não pode ser zero na regra de três inversa.");
            return a * b / c;
        }

        if (a == 0) throw new DivideByZeroException("A não pode ser zero na regra de três direta.");
        return b * c / a;
    }

    public static double PercentOf(double percent, double value) => value * percent / 100.0;

    public static double WhatPercent(double part, double total) =>
        total == 0 ? throw new DivideByZeroException("O total não pode ser zero.") : part / total * 100.0;

    public static double PercentChange(double from, double to) =>
        from == 0 ? throw new DivideByZeroException("O valor inicial não pode ser zero.") : (to - from) / System.Math.Abs(from) * 100.0;

    public static double ApplyPercent(double value, double percent) => value * (1 + percent / 100.0);
}
