using System.Globalization;
using Assistente.Core.Text;

namespace Assistente.Core.Tests;

public class TextTests
{
    [Theory]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1234,56", 1234.56)]
    [InlineData("1234.56", 1234.56)]
    [InlineData("1.234.567", 1234567)]
    [InlineData("R$ 10", 10)]
    [InlineData("5%", 5)]
    [InlineData("-3,5", -3.5)]
    public void NumberParser_Parses(string text, double expected)
    {
        Assert.True(NumberParser.TryParse(text, out var v));
        Assert.Equal(expected, v, 10);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public void NumberParser_Rejects(string text) => Assert.False(NumberParser.TryParse(text, out _));

    [Fact]
    public void ByteSize_Formats() =>
        Assert.Equal("1.5 GB", ByteSize.Format(1.5 * 1024 * 1024 * 1024, CultureInfo.InvariantCulture));

    [Fact]
    public void Password_HasRequestedLengthAndGroups()
    {
        for (var i = 0; i < 200; i++)
        {
            var p = PasswordGenerator.Generate(new PasswordOptions(Length: 12));
            Assert.Equal(12, p.Length);
            Assert.Contains(p, char.IsLower);
            Assert.Contains(p, char.IsUpper);
            Assert.Contains(p, char.IsDigit);
            Assert.Contains(p, c => !char.IsLetterOrDigit(c));
        }
    }

    [Fact]
    public void Password_RejectsNoGroups() =>
        Assert.Throws<ArgumentException>(() => PasswordGenerator.Generate(new PasswordOptions(8, false, false, false, false)));
}
