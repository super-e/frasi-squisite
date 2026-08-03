using FrasiSquisite.Server.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FrasiSquisite.Server.Tests.Ai;

public class IllustrationRunnerTests
{
    private sealed class FintoImageProvider(byte[]? risposta) : IAiImageProvider
    {
        public string? PromptRicevuto { get; private set; }

        public int Chiamate { get; private set; }

        public Task<byte[]?> GeneraAsync(string promptInglese, CancellationToken ct)
        {
            Chiamate++;
            PromptRicevuto = promptInglese;
            return Task.FromResult(risposta);
        }
    }

    private static readonly byte[] Png = [1, 2, 3];

    private static IllustrationRunner Runner(IAiTextProvider testo, IAiImageProvider immagine) =>
        new(testo, immagine, Options.Create(new AiOptions()), NullLogger<IllustrationRunner>.Instance);

    [Fact]
    public async Task TraduceEPoiGenera()
    {
        var testo = new FakeAiTextProvider("a penguin in a pinstripe suit assembling a bookshelf");
        var immagine = new FintoImageProvider(Png);

        var esito = await Runner(testo, immagine).IllustraAsync("Un pinguino in doppiopetto…", CancellationToken.None);

        Assert.Equal(Png, esito);
        Assert.Equal("a penguin in a pinstripe suit assembling a bookshelf", immagine.PromptRicevuto);
    }

    /// <summary>
    /// Se la traduzione non arriva non si genera niente: mandare la frase
    /// italiana grezza al generatore costerebbe comunque nove centesimi per
    /// produrre un collage. Meglio fallire e lasciare che l'host riprovi.
    /// </summary>
    [Fact]
    public async Task SenzaTraduzioneNonSiGeneraEQuindiNonSiSpende()
    {
        var immagine = new FintoImageProvider(Png);

        var esito = await Runner(new FakeAiTextProvider(null), immagine).IllustraAsync("qualcosa", CancellationToken.None);

        Assert.Null(esito);
        Assert.Equal(0, immagine.Chiamate);
    }

    [Fact]
    public async Task SeLaGenerazioneFallisceLEsitoENullo()
    {
        var testo = new FakeAiTextProvider("a penguin");

        var esito = await Runner(testo, new FintoImageProvider(null)).IllustraAsync("x", CancellationToken.None);

        Assert.Null(esito);
    }

    /// <summary>
    /// Un modello che risponde con un blocco markdown o con una frase davanti
    /// è la norma, non l'eccezione: il prompt vale come preghiera, la pulizia
    /// come garanzia.
    /// </summary>
    [Theory]
    [InlineData("```\na penguin\n```", "a penguin")]
    [InlineData("  a penguin  ", "a penguin")]
    [InlineData("\"a penguin\"", "a penguin")]
    public async Task LaTraduzioneVienePulitaPrimaDiEssereUsata(string grezza, string attesa)
    {
        var immagine = new FintoImageProvider(Png);

        await Runner(new FakeAiTextProvider(grezza), immagine).IllustraAsync("x", CancellationToken.None);

        Assert.Equal(attesa, immagine.PromptRicevuto);
    }

    [Fact]
    public async Task UnaTraduzioneVuotaNonFaGenerareNiente()
    {
        var immagine = new FintoImageProvider(Png);

        var esito = await Runner(new FakeAiTextProvider("   "), immagine).IllustraAsync("x", CancellationToken.None);

        Assert.Null(esito);
        Assert.Equal(0, immagine.Chiamate);
    }
}
