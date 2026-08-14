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

    public async Task<string?> CompletaAsync(string sistema, string utente, CancellationToken ct, int maxTokens)
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
                max_tokens = maxTokens,
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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException)
        {
            // Rete giu', timeout, o una risposta di forma inattesa: per il
            // gioco sono lo stesso caso, e nessuno di questi deve far cadere
            // una partita.
            //
            // InvalidOperationException e' qui perche' JsonElement la lancia
            // nei casi realistici con un fornitore terzo: indicizzare [0] su
            // "choices" quando non e' un array (es. "choices": null oppure
            // "choices": {}, forme plausibili in una risposta d'errore), o
            // GetString() su un "content" che non e' una stringa (es. un
            // numero). Verificato a runtime per ogni passo di navigazione del
            // JSON usato qui sotto: le uniche eccezioni che JsonElement puo'
            // lanciare su un documento sintatticamente valido sono
            // KeyNotFoundException (proprieta' assente), IndexOutOfRangeException
            // (indice fuori range su un array vero) e InvalidOperationException
            // (tipo sbagliato) — tutte e tre gia' catturate qui.
            logger.LogWarning(ex, "Chiamata al fornitore AI fallita: si prosegue senza rifinitura.");
            return null;
        }
    }
}
