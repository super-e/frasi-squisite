using System.Net;
using System.Text;
using System.Text.Json;
using FrasiSquisite.Server.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FrasiSquisite.Server.Tests.Ai;

/// <summary>
/// L'unica implementazione con logica vera del contratto <see cref="IAiTextProvider"/>
/// non era esercitata da nessun test: e' il motivo per cui il difetto sul
/// <c>catch</c> (manca <see cref="InvalidOperationException"/>, che e' quella
/// che <see cref="System.Text.Json.JsonElement"/> lancia per le forme di
/// risposta plausibili con un fornitore terzo) e' passato inosservato.
/// Questi test usano un <see cref="HttpMessageHandler"/> fittizio: nessuna
/// chiamata di rete vera, nessuna chiave reale.
/// </summary>
public class OpenAiCompatibleTextProviderTests
{
    private const string ChiaveDiProva = "chiave-di-prova-mai-reale";

    private static OpenAiCompatibleTextProvider CreaProvider(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://fornitore-fittizio.test/") };
        var opzioni = Options.Create(new AiOptions
        {
            BaseUrl = "http://fornitore-fittizio.test/",
            ApiKey = ChiaveDiProva,
            TextModel = "modello-di-prova",
            TimeoutSeconds = 5,
        });

        // NullLogger: non ci interessa cosa viene loggato in questi test (lo
        // verifichiamo leggendo il codice, non con un logger fittizio), ma
        // dobbiamo comunque passare un ILogger valido al costruttore.
        return new OpenAiCompatibleTextProvider(http, opzioni, NullLogger<OpenAiCompatibleTextProvider>.Instance);
    }

    [Fact]
    public async Task CasoFelice_RispostaBenFormata_RestituisceIlContenuto()
    {
        var handler = HandlerFittizio.ConRisposta(HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"Frase rifinita con garbo."}}]}""");
        var provider = CreaProvider(handler);

        var risultato = await provider.CompletaAsync("sistema", "utente", CancellationToken.None, maxTokens: 2000);

        Assert.Equal("Frase rifinita con garbo.", risultato);
    }

    [Fact]
    public async Task IlMaxTokensPassatoDalChiamanteFiniceNellaRichiesta()
    {
        var handler = HandlerFittizio.ConRisposta(HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"ok"}}]}""");
        var provider = CreaProvider(handler);

        await provider.CompletaAsync("sistema", "utente", CancellationToken.None, maxTokens: 777);

        Assert.NotNull(handler.UltimaRichiestaGrezza);
        using var documento = JsonDocument.Parse(handler.UltimaRichiestaGrezza!);
        Assert.Equal(777, documento.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task CodiceDiStatoErrore_RestituisceNullSenzaLanciare()
    {
        var handler = HandlerFittizio.ConRisposta(HttpStatusCode.InternalServerError,
            """{"error":"guasto interno del fornitore"}""");
        var provider = CreaProvider(handler);

        var risultato = await provider.CompletaAsync("sistema", "utente", CancellationToken.None, maxTokens: 2000);

        Assert.Null(risultato);
    }

    [Fact]
    public async Task CorpoNonJson_RestituisceNullSenzaLanciare()
    {
        var handler = HandlerFittizio.ConRisposta(HttpStatusCode.OK, "questo non e' JSON, e' testo qualunque");
        var provider = CreaProvider(handler);

        var risultato = await provider.CompletaAsync("sistema", "utente", CancellationToken.None, maxTokens: 2000);

        Assert.Null(risultato);
    }

    /// <summary>
    /// "choices": null e' una forma plausibile di risposta d'errore di un
    /// fornitore compatibile OpenAI. Indicizzare [0] su un JsonElement che
    /// non e' un array lancia <see cref="InvalidOperationException"/>, non
    /// <see cref="IndexOutOfRangeException"/> come si potrebbe pensare
    /// (verificato a runtime: vedi il report del task). E' esattamente il
    /// caso che il difetto Critical lasciava scoperto: prima della
    /// correzione questo test avrebbe visto l'eccezione risalire fuori da
    /// <c>CompletaAsync</c> invece di un <c>null</c>.
    /// </summary>
    [Fact]
    public async Task ChoicesNull_FormaInattesaDiRisposta_RestituisceNullSenzaLanciare()
    {
        var handler = HandlerFittizio.ConRisposta(HttpStatusCode.OK, """{"choices": null}""");
        var provider = CreaProvider(handler);

        var risultato = await provider.CompletaAsync("sistema", "utente", CancellationToken.None, maxTokens: 2000);

        Assert.Null(risultato);
    }

    /// <summary>
    /// "content" numerico invece che stringa: <c>GetString()</c> su un
    /// JsonElement che non e' una stringa lancia
    /// <see cref="InvalidOperationException"/>. E' il secondo caso che il
    /// difetto Critical lasciava scoperto.
    /// </summary>
    [Fact]
    public async Task ContentNumerico_FormaInattesaDiRisposta_RestituisceNullSenzaLanciare()
    {
        var handler = HandlerFittizio.ConRisposta(HttpStatusCode.OK,
            """{"choices":[{"message":{"content": 42}}]}""");
        var provider = CreaProvider(handler);

        var risultato = await provider.CompletaAsync("sistema", "utente", CancellationToken.None, maxTokens: 2000);

        Assert.Null(risultato);
    }

    [Fact]
    public async Task EccezioneDiTrasporto_RestituisceNullSenzaLanciare()
    {
        var handler = HandlerFittizio.ConEccezione(new HttpRequestException("rete giu'"));
        var provider = CreaProvider(handler);

        var risultato = await provider.CompletaAsync("sistema", "utente", CancellationToken.None, maxTokens: 2000);

        Assert.Null(risultato);
    }

    /// <summary>
    /// Handler HTTP fittizio: intercetta la richiesta prima che tocchi la
    /// rete. O restituisce una risposta preconfezionata, o lancia
    /// un'eccezione di trasporto, cosi' come farebbe <see cref="HttpClient"/>
    /// con la rete giu'.
    /// </summary>
    private sealed class HandlerFittizio : HttpMessageHandler
    {
        private readonly HttpStatusCode? _codice;
        private readonly string? _corpo;
        private readonly Exception? _daLanciare;

        public string? UltimaRichiestaGrezza { get; private set; }

        private HandlerFittizio(HttpStatusCode? codice, string? corpo, Exception? daLanciare)
        {
            _codice = codice;
            _corpo = corpo;
            _daLanciare = daLanciare;
        }

        public static HandlerFittizio ConRisposta(HttpStatusCode codice, string corpo) =>
            new(codice, corpo, null);

        public static HandlerFittizio ConEccezione(Exception eccezione) =>
            new(null, null, eccezione);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                UltimaRichiestaGrezza = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            if (_daLanciare is not null)
            {
                return await Task.FromException<HttpResponseMessage>(_daLanciare);
            }

            var risposta = new HttpResponseMessage(_codice!.Value)
            {
                Content = new StringContent(_corpo!, Encoding.UTF8, "application/json"),
            };
            return risposta;
        }
    }
}
