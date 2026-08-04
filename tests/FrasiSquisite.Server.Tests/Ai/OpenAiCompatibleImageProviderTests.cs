using System.Net;
using System.Text;
using FrasiSquisite.Server.Ai;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FrasiSquisite.Server.Tests.Ai;

public class OpenAiCompatibleImageProviderTests
{
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3];

    /// <summary>
    /// Risponde in base al percorso: la generazione torna il JSON passato, il
    /// resto torna i byte. Un handler che rispondesse sempre uguale non
    /// distinguerebbe i due passi, che è proprio ciò che va provato.
    /// </summary>
    private sealed class FintoHandler(string jsonGenerazione, HttpStatusCode codiceGenerazione = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public List<string> PercorsiChiamati { get; } = [];

        /// <summary>
        /// Una voce per ogni richiesta arrivata all'handler: l'host di
        /// destinazione e se portava un header Authorization. È ciò che
        /// permette di verificare che la chiave non segua il download verso
        /// un host diverso da quello configurato (il difetto di questo file).
        /// </summary>
        public List<(string Host, bool ConChiave)> RichiesteRegistrate { get; } = [];

        public HttpStatusCode CodiceDownload { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            PercorsiChiamati.Add(request.RequestUri!.AbsolutePath);
            RichiesteRegistrate.Add((request.RequestUri!.Host, request.Headers.Authorization is not null));

            if (request.RequestUri!.AbsolutePath.Contains("images/generations", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(codiceGenerazione)
                {
                    Content = new StringContent(jsonGenerazione, Encoding.UTF8, "application/json"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(CodiceDownload)
            {
                Content = new ByteArrayContent(Png),
            });
        }
    }

    /// <summary>
    /// In produzione IHttpClientFactory.CreateClient() torna un client "vuoto"
    /// (nessun BaseAddress, nessun header di default): è il punto centrale
    /// della correzione, perché il download lo usa apposta per non ereditare
    /// nulla dal client della generazione. Qui torna un client sullo stesso
    /// FintoHandler, così un solo elenco di richieste vede entrambi i passi;
    /// disposeHandler: false perché, come in produzione, il factory non deve
    /// perdere l'handler quando il singolo HttpClient restituito viene
    /// dismesso a fine chiamata.
    /// </summary>
    private sealed class FintaHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static OpenAiCompatibleImageProvider Provider(FintoHandler handler) =>
        new(
            new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.esempio/") },
            new FintaHttpClientFactory(handler),
            Options.Create(new AiOptions
            {
                BaseUrl = "https://api.esempio/",
                ApiKey = "chiave-segreta",
                ImageModel = "nano-banana-2",
                ImageSize = "1K",
                TimeoutSeconds = 10,
            }),
            NullLogger<OpenAiCompatibleImageProvider>.Instance);

    private const string RispostaBuona =
        """{"data":[{"url":"https://api.esempio/v1/media/xyz?exp=1","content_type":"image/png"}],"cost":0.092}""";

    [Fact]
    public async Task ScaricaIByteDellImmagineGenerata()
    {
        var handler = new FintoHandler(RispostaBuona);

        var byteOttenuti = await Provider(handler).GeneraAsync("a penguin in a suit", CancellationToken.None);

        Assert.Equal(Png, byteOttenuti);
        Assert.Equal(2, handler.PercorsiChiamati.Count);
    }

    /// <summary>
    /// È la richiesta che deve funzionare: senza la chiave verso l'host
    /// configurato il fornitore risponderebbe 401.
    /// </summary>
    [Fact]
    public async Task LaRichiestaDiGenerazionePortaLaChiaveVersoLHostConfigurato()
    {
        var handler = new FintoHandler(RispostaBuona);

        await Provider(handler).GeneraAsync("a penguin in a suit", CancellationToken.None);

        var generazione = handler.RichiesteRegistrate[0];
        Assert.Equal("api.esempio", generazione.Host);
        Assert.True(generazione.ConChiave);
    }

    /// <summary>
    /// Questo è il test che conta (rilievo 1): l'indirizzo firmato torna da
    /// un host diverso da BaseUrl, come farebbe un fornitore che serve le
    /// immagini da un bucket o una CDN separati. Nel codice prima della
    /// correzione, dove generazione e download condividevano lo stesso
    /// HttpClient (con la chiave nei DefaultRequestHeaders), questo test
    /// fallisce perché la chiave parte comunque — verificato costruendo a
    /// mano quel client vulnerabile e osservando l'asserzione fallire prima
    /// di scrivere la correzione.
    /// </summary>
    [Fact]
    public async Task IlDownloadVersoUnHostDiversoNonPortaLaChiave()
    {
        const string rispostaConHostEsterno =
            """{"data":[{"url":"https://cdn.altro-fornitore.example/img.png"}]}""";
        var handler = new FintoHandler(rispostaConHostEsterno);

        var byteOttenuti = await Provider(handler).GeneraAsync("x", CancellationToken.None);

        Assert.Equal(Png, byteOttenuti);
        var download = handler.RichiesteRegistrate[1];
        Assert.Equal("cdn.altro-fornitore.example", download.Host);
        Assert.False(download.ConChiave);
    }

    /// <summary>
    /// Rilievo 3: l'indirizzo arriva dal fornitore, non da noi. Uno schema
    /// diverso da http/https (qui ftp) deve essere scartato prima di arrivare
    /// a HttpClient — che con l'handler vero lancerebbe NotSupportedException,
    /// non elencata nel catch. La verifica che l'handler finto non venga mai
    /// invocato per il download conferma che lo scarto avviene prima, non che
    /// l'eccezione viene solo catturata dopo.
    /// </summary>
    [Fact]
    public async Task UnIndirizzoConSchemaNonHttpTornaNullSenzaScaricare()
    {
        const string rispostaConSchemaNonSupportato =
            """{"data":[{"url":"ftp://esempio.invalid/img.png"}]}""";
        var handler = new FintoHandler(rispostaConSchemaNonSupportato);

        var risultato = await Provider(handler).GeneraAsync("x", CancellationToken.None);

        Assert.Null(risultato);
        Assert.Single(handler.RichiesteRegistrate);
    }

    [Fact]
    public async Task UnaRispostaDiErroreDelFornitoreTornaNull()
    {
        var handler = new FintoHandler("""{"error":"no credit"}""", HttpStatusCode.PaymentRequired);

        Assert.Null(await Provider(handler).GeneraAsync("x", CancellationToken.None));
    }

    /// <summary>
    /// Le forme che JsonElement può far esplodere con un fornitore terzo:
    /// "data" assente, "data" non array, "url" di tipo sbagliato. Sono le
    /// stesse che avevano fatto passare un difetto Critico nel provider di
    /// testo, dove il catch non prendeva InvalidOperationException.
    /// </summary>
    [Theory]
    [InlineData("""{"cost":0.09}""")]
    [InlineData("""{"data":null}""")]
    [InlineData("""{"data":[]}""")]
    [InlineData("""{"data":[{"url":42}]}""")]
    [InlineData("non è json")]
    public async Task UnaRispostaDiFormaInattesaTornaNull(string corpo)
    {
        Assert.Null(await Provider(new FintoHandler(corpo)).GeneraAsync("x", CancellationToken.None));
    }

    [Fact]
    public async Task SeIlDownloadFallisceTornaNull()
    {
        var handler = new FintoHandler(RispostaBuona) { CodiceDownload = HttpStatusCode.Forbidden };

        Assert.Null(await Provider(handler).GeneraAsync("x", CancellationToken.None));
    }

    [Fact]
    public async Task SenzaChiaveIlProviderSpentoTornaNullSenzaChiamareNessuno()
    {
        Assert.Null(await new DisabledAiImageProvider().GeneraAsync("x", CancellationToken.None));
    }
}
