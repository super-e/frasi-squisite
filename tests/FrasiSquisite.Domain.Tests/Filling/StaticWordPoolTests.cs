using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Shared.Validation;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Filling;

public class StaticWordPoolTests
{
    private readonly IWordPool _pool = new StaticWordPool();

    [Theory]
    [InlineData("Soggetto")]
    [InlineData("Aggettivo")]
    [InlineData("Verbo")]
    [InlineData("Complemento")]
    public void RestituisceUnaParolaPerIRuoliNoti(string ruolo)
    {
        var parola = _pool.Take(ruolo, new SeededRandomSource(1));

        Assert.False(string.IsNullOrWhiteSpace(parola));
    }

    [Fact]
    public void PerUnRuoloSconosciutoRicadeSuUnaListaGenerica()
    {
        var parola = _pool.Take("RuoloCheNonEsiste", new SeededRandomSource(1));

        Assert.False(string.IsNullOrWhiteSpace(parola));
    }

    /// <summary>
    /// Il motore riapplica la validazione a ogni casella: se il dizionario
    /// contenesse una parola non valida, il riempimento del bot fallirebbe in
    /// partita e non qui.
    /// </summary>
    [Theory]
    [InlineData("Soggetto")]
    [InlineData("Aggettivo")]
    [InlineData("Verbo")]
    [InlineData("Complemento")]
    [InlineData("RuoloCheNonEsiste")]
    public void OgniParolaDelDizionarioSuperaLaValidazione(string ruolo)
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var parola = _pool.Take(ruolo, new SeededRandomSource(seed));

            Assert.True(SlotTextValidator.Validate(parola).IsValid, $"parola non valida: '{parola}'");
        }
    }

    [Fact]
    public void ConLoStessoSeedRestituisceLaStessaParola()
    {
        Assert.Equal(
            _pool.Take("Soggetto", new SeededRandomSource(42)),
            _pool.Take("Soggetto", new SeededRandomSource(42)));
    }
}
