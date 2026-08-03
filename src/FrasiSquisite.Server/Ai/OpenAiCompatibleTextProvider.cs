using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FrasiSquisite.Server.Ai;

/// <summary>
/// Una sola implementazione per tutti i fornitori compatibili OpenAI.
/// Non lancia mai: qualunque guasto diventa <c>null</c>, perche' il chiamante
/// ha gia' una strada per quel caso e un'eccezione lo costringerebbe a
/// duplicarla in un catch.
/// </summary>
public sealed class OpenAiCompatibleTextProvider(
    HttpClient http,
    IOptions<AiOptions> opzioni,
    ILogger<OpenAiCompatibleTextProvider> logger) : IAiTextProvider
{
    private readonly AiOptions _opzioni = opzioni.Value;

    public async Task<string?> CompletaAsync(string sistema, string utente, CancellationToken ct)
    {
        try
        {
            var richiesta = new
            {
                model = _opzioni.TextModel,
                messages = new[]
                {
                    new { role = "system", content = sistema },
                    new { role = "user", content = utente },
                },
                // GLM-5.2 e' un modello di ragionamento: senza questo spende
                // token nascosti prima di rispondere, e per una correzione di
                // bozze e' sproporzionato in tempo e in denaro (spec §4.2).
                reasoning_effort = "low",
                max_tokens = 2000,
            };

            using var risposta = await http.PostAsJsonAsync("/chat/completions", richiesta, ct);

            if (!risposta.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Il fornitore AI ha risposto {Codice}: si prosegue senza rifinitura.",
                    (int)risposta.StatusCode);
                return null;
            }

            using var documento = JsonDocument.Parse(await risposta.Content.ReadAsStringAsync(ct));

            return documento.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or IndexOutOfRangeException)
        {
            // Rete giu', timeout, o una risposta di forma inattesa: per il
            // gioco sono lo stesso caso, e nessuno di questi deve far cadere
            // una partita.
            logger.LogWarning(ex, "Chiamata al fornitore AI fallita: si prosegue senza rifinitura.");
            return null;
        }
    }
}
