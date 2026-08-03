using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FrasiSquisite.Server.Ai;

/// <summary>
/// Compone il prompt, chiama il modello, legge la risposta. Non decide se
/// fidarsi di quello che torna: quello e' compito di RefinementGuard, dentro
/// il motore, dove e' provabile senza rete.
/// </summary>
public sealed class RefinementRunner(
    IAiTextProvider ai,
    IOptions<AiOptions> opzioni,
    ILogger<RefinementRunner> logger)
{
    private readonly AiOptions _opzioni = opzioni.Value;

    private const string Sistema = """
        Sei un correttore di bozze per un gioco di frasi surreali.
        Ricevi le caselle di una frase, scritte da giocatori diversi che non
        si sono visti fra loro. Il tuo compito è UNICO: aggiungere il minimo
        tessuto connettivo perché la frase si legga — preposizioni, articoli,
        congiunzioni, accordi.

        REGOLE INDEROGABILI
        - Non sostituire le parole scelte dai giocatori. Devono comparire
          tutte, invariate, dentro la casella corrispondente.
        - Non riordinare le caselle e non spostarne il contenuto.
        - Non aggiungere idee, aggettivi o dettagli tuoi.
        - Se una casella si legge già bene, restituiscila identica.
        - Il template della frase contiene già del testo fisso: non ripeterlo,
          e non ripeterne nemmeno il senso.

        L'assurdo è voluto. Non renderlo sensato: rendilo leggibile.

        Rispondi solo con JSON, senza commenti e senza blocchi di codice:
        {"frasi": [{"caselle": ["...", "..."]}, ...]}
        Tante frasi quante ne ricevi, tante caselle quante ne ha ciascuna,
        nello stesso ordine.
        """;

    public async Task<IReadOnlyList<IReadOnlyList<string>>?> RifinisciAsync(
        IReadOnlyList<IReadOnlyList<string>> frasi,
        string template,
        CancellationToken ct)
    {
        using var scadenza = CancellationTokenSource.CreateLinkedTokenSource(ct);
        scadenza.CancelAfter(TimeSpan.FromSeconds(_opzioni.TimeoutSeconds));

        try
        {
            var utente = JsonSerializer.Serialize(new
            {
                template,
                frasi = frasi.Select(f => new { caselle = f }),
            });

            var risposta = await ai.CompletaAsync(Sistema, utente, scadenza.Token);

            return risposta is null ? null : Leggi(risposta, frasi.Count);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Rifinitura scaduta dopo {Secondi}s: si prosegue con le caselle grezze.", _opzioni.TimeoutSeconds);
            return null;
        }
    }

    /// <summary>
    /// I modelli incorniciano spesso il JSON in un blocco markdown, o ci
    /// mettono una frase davanti. Scartare una risposta buona per questo
    /// sarebbe uno spreco, quindi si cerca il primo oggetto JSON.
    /// </summary>
    private IReadOnlyList<IReadOnlyList<string>>? Leggi(string risposta, int atteso)
    {
        try
        {
            var inizio = risposta.IndexOf('{');
            var fine = risposta.LastIndexOf('}');

            if (inizio < 0 || fine <= inizio)
            {
                return null;
            }

            using var documento = JsonDocument.Parse(risposta[inizio..(fine + 1)]);

            var frasi = documento.RootElement.GetProperty("frasi");
            var esito = new List<IReadOnlyList<string>>();

            foreach (var frase in frasi.EnumerateArray())
            {
                esito.Add([.. frase.GetProperty("caselle").EnumerateArray().Select(c => c.GetString() ?? string.Empty)]);
            }

            // Il conteggio sbagliato lo gestisce comunque RefinementGuard, ma
            // fermarsi qui evita di portarsi dietro una risposta inutile.
            return esito.Count == atteso ? esito : null;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Risposta del modello illeggibile: si prosegue con le caselle grezze.");
            return null;
        }
    }
}
