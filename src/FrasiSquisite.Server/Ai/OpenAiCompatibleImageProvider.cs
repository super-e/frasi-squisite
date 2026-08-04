using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FrasiSquisite.Server.Ai;

/// <summary>
/// Due passi in uno: genera, poi scarica. Il fornitore risponde con un URL
/// firmato che scade, quindi i byte vanno presi subito (spec §5).
/// </summary>
public sealed class OpenAiCompatibleImageProvider(
    HttpClient http,
    IHttpClientFactory httpClientFactory,
    IOptions<AiOptions> opzioni,
    ILogger<OpenAiCompatibleImageProvider> logger) : IAiImageProvider
{
    private readonly AiOptions _opzioni = opzioni.Value;

    public async Task<byte[]?> GeneraAsync(string promptInglese, CancellationToken ct)
    {
        try
        {
            var richiesta = new
            {
                model = _opzioni.ImageModel,
                prompt = promptInglese,
                n = 1,
                size = _opzioni.ImageSize,
            };

            // La chiave sta qui, sulla singola richiesta, e non fra i
            // DefaultRequestHeaders del client (vedi Program.cs): quelli si
            // unirebbero anche alla richiesta di download più sotto, che va
            // verso un indirizzo scelto dal fornitore e non da noi.
            using var richiestaGenerazione = new HttpRequestMessage(HttpMethod.Post, "/v1/images/generations")
            {
                Content = JsonContent.Create(richiesta),
            };
            richiestaGenerazione.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opzioni.ApiKey);

            using var risposta = await http.SendAsync(richiestaGenerazione, ct);

            if (!risposta.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Il fornitore ha risposto {Codice} alla generazione dell'immagine.",
                    (int)risposta.StatusCode);
                return null;
            }

            using var documento = JsonDocument.Parse(await risposta.Content.ReadAsStringAsync(ct));

            var indirizzo = documento.RootElement
                .GetProperty("data")[0]
                .GetProperty("url")
                .GetString();

            if (string.IsNullOrWhiteSpace(indirizzo))
            {
                logger.LogWarning("Generazione senza indirizzo nella risposta.");
                return null;
            }

            // L'indirizzo arriva dal fornitore, non da noi: deve essere un URI
            // assoluto http/https prima di passare a HttpClient. Senza questo
            // controllo uno schema come "file:" o "ftp:" farebbe lanciare a
            // HttpClient una NotSupportedException che il catch qui sotto non
            // elenca — e i provider non lanciano mai. La stessa validazione
            // rende anche possibile il confronto sull'host qui sotto.
            if (!Uri.TryCreate(indirizzo, UriKind.Absolute, out var indirizzoUri) ||
                (indirizzoUri.Scheme != Uri.UriSchemeHttp && indirizzoUri.Scheme != Uri.UriSchemeHttps))
            {
                logger.LogWarning("Generazione con indirizzo immagine non valido.");
                return null;
            }

            // Il download NON usa "http": quel client è quello della
            // generazione, e un domani chiunque gli riattaccasse un
            // DefaultRequestHeaders.Authorization (per "semplificare")
            // farebbe ripartire la fuga, perché DefaultRequestHeaders si
            // unisce a ogni richiesta di quel client a prescindere dall'host
            // — anche impostando esplicitamente Headers.Authorization = null
            // sulla richiesta, come si è verificato. Un client separato,
            // creato senza configurazione, non ha questo problema: la chiave
            // ci finisce sopra solo se la mettiamo noi, qui sotto.
            using var downloadHttp = httpClientFactory.CreateClient();
            using var richiestaDownload = new HttpRequestMessage(HttpMethod.Get, indirizzoUri);

            // La chiave viaggia verso l'host configurato (alcuni fornitori la
            // richiedono anche sui propri media) e verso nessun altro:
            // l'indirizzo è firmato, quindi la firma stessa è già la
            // credenziale del download. Mandare la chiave a un host diverso
            // da quello scelto in AiOptions.BaseUrl la spedirebbe a chiunque
            // il fornitore decida di usare per servire l'immagine — un
            // fornitore diverso da ppq.ai potrebbe benissimo firmare gli URL
            // su un bucket o una CDN separati.
            var hostConfigurato = new Uri(_opzioni.BaseUrl).Host;
            if (string.Equals(indirizzoUri.Host, hostConfigurato, StringComparison.OrdinalIgnoreCase))
            {
                richiestaDownload.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opzioni.ApiKey);
            }

            using var immagine = await downloadHttp.SendAsync(richiestaDownload, ct);

            if (!immagine.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Download dell'immagine fallito con {Codice}: l'indirizzo firmato può essere già scaduto.",
                    (int)immagine.StatusCode);
                return null;
            }

            return await immagine.Content.ReadAsByteArrayAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
            or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException or UriFormatException)
        {
            // Stessa lista del provider di testo, e per la stessa ragione: su
            // un documento sintatticamente valido JsonElement lancia
            // KeyNotFoundException (proprietà assente), IndexOutOfRangeException
            // (indice fuori range) o InvalidOperationException (tipo sbagliato,
            // es. "data": null oppure "url": 42). InvalidOperationException in
            // particolare era il difetto Critico trovato nel provider di testo.
            // UriFormatException si aggiunge qui perché l'indirizzo arriva dal
            // fornitore e non da noi: quanto a schemi non http/https, non
            // servono nella lista perché non arrivano mai a HttpClient (vedi
            // il controllo sopra).
            logger.LogWarning(ex, "Generazione dell'immagine fallita.");
            return null;
        }
    }
}
