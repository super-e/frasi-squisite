using FrasiSquisite.Shared.Validation;
using Xunit;

namespace FrasiSquisite.Shared.Tests.Validation;

public class NicknameValidatorTests
{
    [Theory]
    [InlineData("Anna")]
    [InlineData("Bot Ada")]
    [InlineData("a")]
    public void AccettaNomeNormale(string nome)
    {
        var esito = NicknameValidator.Validate(nome);

        Assert.True(esito.IsValid);
        Assert.Null(esito.Error);
        Assert.Equal(nome, esito.Normalized);
    }

    [Fact]
    public void RimuoveGliSpaziAiBordi()
    {
        var esito = NicknameValidator.Validate("   Anna  ");

        Assert.True(esito.IsValid);
        Assert.Equal("Anna", esito.Normalized);
    }

    [Fact]
    public void CollassaGliSpaziInterni()
    {
        var esito = NicknameValidator.Validate("Anna    Maria     Rossi");

        Assert.True(esito.IsValid);
        Assert.Equal("Anna Maria Rossi", esito.Normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void RifiutaNomeVuoto(string? nome)
    {
        var esito = NicknameValidator.Validate(nome);

        Assert.False(esito.IsValid);
        Assert.NotNull(esito.Error);
    }

    [Fact]
    public void RifiutaNomeTroppoLungo()
    {
        var nome = new string('a', NicknameValidator.MaxLength + 1);

        var esito = NicknameValidator.Validate(nome);

        Assert.False(esito.IsValid);
        Assert.Contains(NicknameValidator.MaxLength.ToString(), esito.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void AccettaNomeDiLunghezzaMassima()
    {
        var nome = new string('a', NicknameValidator.MaxLength);

        Assert.True(NicknameValidator.Validate(nome).IsValid);
    }

    [Theory]
    [InlineData("prima\nseconda")]
    [InlineData("prima\rseconda")]
    [InlineData("con\0nullo")]
    public void RifiutaCaratteriDiControllo(string nome)
    {
        var esito = NicknameValidator.Validate(nome);

        Assert.False(esito.IsValid);
        Assert.NotNull(esito.Error);
    }
}
