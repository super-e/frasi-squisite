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

        /// <summary>Se impostato, la chiamata attende questo prima di rispondere (vedi FakeAiTextProvider.Ritardo).</summary>
        public TimeSpan Ritardo { get; set; } = TimeSpan.Zero;

        public async Task<byte[]?> GeneraAsync(string promptInglese, CancellationToken ct)
        {
            Chiamate++;
            PromptRicevuto = promptInglese;

            if (Ritardo > TimeSpan.Zero)
            {
                await Task.Delay(Ritardo, ct);
            }

            return risposta;
        }
    }

    private static readonly byte[] Png = [1, 2, 3];

    private static IllustrationRunner Runner(IAiTextProvider testo, IAiImageProvider immagine, int imageTimeoutSecondi = 90) =>
        new(testo, immagine, Options.Create(new AiOptions { ImageTimeoutSeconds = imageTimeoutSecondi }), NullLogger<IllustrationRunner>.Instance);

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
    // Troppo debole (rilievo): un blocco recintato con la parola del
    // linguaggio sulla prima riga, come lo scrivono davvero i modelli, deve
    // sparire per intero - tag compreso - non solo il recinto.
    [InlineData("```text\na penguin\n```", "a penguin")]
    // Troppo aggressiva (rilievo): una virgoletta isolata, senza la sua
    // compagna di apertura, non e' una coppia e va lasciata dov'e'. Mutilarla
    // vorrebbe dire alterare la descrizione invece di solo ripulirla.
    [InlineData("a penguin wearing a hat\"", "a penguin wearing a hat\"")]
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

    /// <summary>
    /// Il budget di ImageTimeoutSeconds copre il PRIMO passo (la traduzione):
    /// se il modello di testo ci mette troppo, l'illustrazione fallisce senza
    /// eccezioni invece di restare appesa.
    /// </summary>
    [Fact]
    public async Task OltreIlTimeoutSulPassoDiTraduzioneSiRestituisceNull()
    {
        var testo = new FakeAiTextProvider
        {
            Risposta = "a penguin",
            Ritardo = TimeSpan.FromSeconds(5),
        };
        var immagine = new FintoImageProvider(Png);

        var esito = await Runner(testo, immagine, imageTimeoutSecondi: 1)
            .IllustraAsync("x", CancellationToken.None);

        Assert.Null(esito);
    }

    /// <summary>
    /// Il budget di ImageTimeoutSeconds copre anche il SECONDO passo (la
    /// generazione dell'immagine), non solo il primo: qui la traduzione e'
    /// veloce ma la generazione e' lenta, e il risultato deve comunque essere
    /// null. E' questo il test che dimostra che il limite governa l'intera
    /// operazione a due passi - se qualcuno usasse per errore TimeoutSeconds
    /// (15s, la base della rifinitura, pensata per un'altra operazione) al
    /// posto di ImageTimeoutSeconds (90s), l'operazione fallirebbe comunque
    /// ma per la ragione sbagliata, e senza un test come questo nessuno se
    /// ne accorgerebbe.
    /// </summary>
    [Fact]
    public async Task OltreIlTimeoutSulPassoDiGenerazioneSiRestituisceNull()
    {
        var testo = new FakeAiTextProvider("a penguin");
        var immagine = new FintoImageProvider(Png) { Ritardo = TimeSpan.FromSeconds(5) };

        var esito = await Runner(testo, immagine, imageTimeoutSecondi: 1)
            .IllustraAsync("x", CancellationToken.None);

        Assert.Null(esito);
    }
}
