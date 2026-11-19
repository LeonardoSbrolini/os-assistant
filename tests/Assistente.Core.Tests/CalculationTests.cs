using Assistente.Core.Calculations;

namespace Assistente.Core.Tests;

public class CalculationTests
{
    [Fact]
    public void CompoundInterest_MatchesClosedForm()
    {
        double p = 1000, pmt = 200, rate = 1; // 1% a.m.
        var n = 24;
        var r = Finance.CompoundInterest(p, pmt, rate, RatePeriod.Monthly, n);

        var i = 0.01;
        var expected = p * Math.Pow(1 + i, n) + pmt * (Math.Pow(1 + i, n) - 1) / i;
        Assert.Equal(expected, r.FinalBalance, 6);
        Assert.Equal(p + pmt * n, r.TotalInvested, 6);
        Assert.Equal(n, r.Rows.Count);
    }

    [Fact]
    public void YearlyRate_IsConvertedToEquivalentMonthly()
    {
        var monthly = Finance.ToMonthlyRate(12, RatePeriod.Yearly);
        Assert.Equal(1.12, Math.Pow(1 + monthly, 12), 10);
    }

    [Theory]
    [InlineData(2, 10, 6, false, 30)]  // 2 → 10, 6 → 30
    [InlineData(4, 6, 8, true, 3)]     // 4 pedreiros em 6 dias → 8 pedreiros em 3 dias
    public void RuleOfThree(double a, double b, double c, bool inverse, double expected) =>
        Assert.Equal(expected, Finance.RuleOfThree(a, b, c, inverse), 10);

    [Fact]
    public void Percentages()
    {
        Assert.Equal(25, Finance.PercentOf(25, 100), 10);
        Assert.Equal(20, Finance.WhatPercent(50, 250), 10);
        Assert.Equal(50, Finance.PercentChange(100, 150), 10);
        Assert.Equal(-50, Finance.PercentChange(100, 50), 10);
        Assert.Equal(110, Finance.ApplyPercent(100, 10), 10);
    }

    [Theory]
    [InlineData("1+2*3", 7)]
    [InlineData("(1+2)*3", 9)]
    [InlineData("2^3^2", 512)]
    [InlineData("-2^2", -4)]
    [InlineData("10 ÷ 4", 2.5)]
    [InlineData("1,5 × 2", 3)]
    [InlineData("200 * 10%", 20)]
    [InlineData("5 − 8", -3)]
    public void ExpressionEvaluator_Evaluates(string expr, double expected) =>
        Assert.Equal(expected, ExpressionEvaluator.Evaluate(expr), 10);

    [Theory]
    [InlineData("1/0")]
    [InlineData("(1+2")]
    [InlineData("2*")]
    [InlineData("abc")]
    public void ExpressionEvaluator_RejectsInvalid(string expr) =>
        Assert.False(ExpressionEvaluator.TryEvaluate(expr, out _));
}
