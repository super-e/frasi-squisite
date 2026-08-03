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

            using var risposta = await http.PostAsJsonAsync("/v1/images/generations", richiesta, ct);

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

            using var immagine = await http.GetAsync(indirizzo, ct);

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
            // fornitore e non da noi.
            logger.LogWarning(ex, "Generazione dell'immagine fallita.");
            return null;
        }
    }
}
