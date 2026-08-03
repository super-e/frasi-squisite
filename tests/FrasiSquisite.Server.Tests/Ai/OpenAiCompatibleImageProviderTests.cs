using System.Net;
using System.Text;
using FrasiSquisite.Server.Ai;
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

        public HttpStatusCode CodiceDownload { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            PercorsiChiamati.Add(request.RequestUri!.AbsolutePath);

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

    private static OpenAiCompatibleImageProvider Provider(FintoHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.esempio/") },
            Options.Create(new AiOptions { ImageModel = "nano-banana-2", ImageSize = "1K", TimeoutSeconds = 10 }),
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
