using Assistente.Core.Documents;

namespace Assistente.Core.Tests;

public class DocumentTests
{
    [Theory]
    [InlineData("529.982.247-25", true)]
    [InlineData("52998224725", true)]
    [InlineData("529.982.247-24", false)]
    [InlineData("111.111.111-11", false)]
    [InlineData("123", false)]
    [InlineData(null, false)]
    public void Cpf_IsValid(string? cpf, bool expected) => Assert.Equal(expected, Cpf.IsValid(cpf));

    [Fact]
    public void Cpf_Generate_ProducesValidNumbers()
    {
        var rng = new Random(42);
        for (var i = 0; i < 1000; i++)
        {
            var cpf = Cpf.Generate(formatted: i % 2 == 0, rng: rng);
            Assert.True(Cpf.IsValid(cpf), cpf);
        }
    }

    [Fact]
    public void Cpf_Generate_RespectsUfRegionDigit()
    {
        var cpf = Cpf.Generate(formatted: false, uf: "SP", rng: new Random(1));
        Assert.Equal('8', cpf[8]);
        Assert.True(Cpf.IsValid(cpf));
    }

    [Fact]
    public void Cpf_Format() => Assert.Equal("529.982.247-25", Cpf.Format("52998224725"));

    [Theory]
    [InlineData("11.222.333/0001-81", true)]
    [InlineData("11222333000181", true)]
    [InlineData("11.222.333/0001-80", false)]
    [InlineData("00.000.000/0000-00", false)]
    [InlineData("12.ABC.345/01DE-35", true)] // exemplo oficial de CNPJ alfanumérico
    [InlineData("12.abc.345/01de-35", true)]
    [InlineData("12.ABC.345/01DE-36", false)]
    public void Cnpj_IsValid(string cnpj, bool expected) => Assert.Equal(expected, Cnpj.IsValid(cnpj));

    [Fact]
    public void Cnpj_Generate_ProducesValidNumbers()
    {
        var rng = new Random(7);
        for (var i = 0; i < 1000; i++)
        {
            var cnpj = Cnpj.Generate(formatted: i % 2 == 0, rng: rng);
            Assert.True(Cnpj.IsValid(cnpj), cnpj);
        }
    }

    [Fact]
    public void Cnpj_Generate_UsesBranch()
    {
        var cnpj = Cnpj.Generate(formatted: false, branch: 12, rng: new Random(3));
        Assert.Equal("0012", cnpj[8..12]);
    }

    [Fact]
    public void Rg_Generate_ProducesValidNumbers()
    {
        var rng = new Random(9);
        for (var i = 0; i < 1000; i++)
        {
            var rg = Rg.Generate(formatted: i % 2 == 0, rng: rng);
            Assert.True(Rg.IsValid(rg), rg);
        }
    }

    [Theory]
    [InlineData("12.345.678-?", false)]
    [InlineData("11.111.111-1", false)]
    public void Rg_IsValid_RejectsInvalid(string rg, bool expected) => Assert.Equal(expected, Rg.IsValid(rg));
}
