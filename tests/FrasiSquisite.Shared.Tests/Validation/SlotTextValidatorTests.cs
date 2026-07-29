using FrasiSquisite.Shared.Validation;
using Xunit;

namespace FrasiSquisite.Shared.Tests.Validation;

public class SlotTextValidatorTests
{
    [Theory]
    [InlineData("Il cadavere")]
    [InlineData("squisito")]
    [InlineData("berrà l'acqua")]
    [InlineData("a")]
    public void AccettaTestoNormale(string testo)
    {
        var esito = SlotTextValidator.Validate(testo);

        Assert.True(esito.IsValid);
        Assert.Null(esito.Error);
        Assert.Equal(testo, esito.Normalized);
    }

    [Fact]
    public void RimuoveGliSpaziAiBordi()
    {
        var esito = SlotTextValidator.Validate("   squisito  ");

        Assert.True(esito.IsValid);
        Assert.Equal("squisito", esito.Normalized);
    }

    [Fact]
    public void CollassaGliSpaziInterni()
    {
        var esito = SlotTextValidator.Validate("il    vino     nuovo");

        Assert.True(esito.IsValid);
        Assert.Equal("il vino nuovo", esito.Normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void RifiutaTestoVuoto(string? testo)
    {
        var esito = SlotTextValidator.Validate(testo);

        Assert.False(esito.IsValid);
        Assert.NotNull(esito.Error);
    }

    [Fact]
    public void RifiutaTestoTroppoLungo()
    {
        var testo = new string('a', SlotTextValidator.MaxLength + 1);

        var esito = SlotTextValidator.Validate(testo);

        Assert.False(esito.IsValid);
        Assert.Contains(SlotTextValidator.MaxLength.ToString(), esito.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void AccettaTestoDiLunghezzaMassima()
    {
        var testo = new string('a', SlotTextValidator.MaxLength);

        Assert.True(SlotTextValidator.Validate(testo).IsValid);
    }

    [Theory]
    [InlineData("prima\nseconda")]
    [InlineData("prima\rseconda")]
    [InlineData("con\0nullo")]
    public void RifiutaCaratteriDiControllo(string testo)
    {
        var esito = SlotTextValidator.Validate(testo);

        Assert.False(esito.IsValid);
        Assert.NotNull(esito.Error);
    }
}
