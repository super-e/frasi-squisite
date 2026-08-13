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
        - Le parole scelte dai giocatori restano le stesse: puoi aggiustarne
          delicatamente la forma — plurale, genere, coniugazione — per farle
          concordare con il resto della frase. Resta aderente alla parola
          data: cambia solo la desinenza o piccole variazioni morfologiche,
          mai la radice. Non sostituirla con una parola diversa, non
          cambiarne il significato.
        - Non aggiungere idee, aggettivi o dettagli tuoi.
        - Non riordinare le caselle e non spostarne il contenuto.
        - Se una casella si legge già bene, restituiscila identica.
        - Il template della frase contiene già del testo fisso: non ripeterlo,
          e non ripeterne nemmeno il senso.
        - Il campo "ruoli" dice la funzione grammaticale di ogni casella nella
          frase (es. "Con chi?", "Dove?"), nello stesso ordine delle caselle:
          usalo per scegliere la preposizione o l'accordo giusto, non per
          cambiare cosa la casella dice.

        L'assurdo è voluto. Non renderlo sensato: rendilo leggibile.

        Rispondi solo con JSON, senza commenti e senza blocchi di codice:
        {"frasi": [{"caselle": ["...", "..."]}, ...]}
        Tante frasi quante ne ricevi, tante caselle quante ne ha ciascuna,
        nello stesso ordine.
        """;

    public async Task<IReadOnlyList<IReadOnlyList<string>>?> RifinisciAsync(
        IReadOnlyList<IReadOnlyList<string>> frasi,
        string template,
        IReadOnlyList<string> ruoli,
        CancellationToken ct)
    {
        // Non e' un doppione del c.Timeout impostato in Program.cs sull'
        // HttpClient di OpenAiCompatibleTextProvider: quello limita solo la
        // richiesta HTTP di QUELLA implementazione. Questo qui e' il limite a
        // livello di contratto sull'intera operazione "IAiTextProvider.
        // CompletaAsync", valido per qualunque implementazione dietro
        // l'interfaccia, presente o futura - ed e' cio' che permette di
        // provare il timeout (vedi OltreIlTimeoutSiRestituisceNull) con un
        // doppio finto, senza rete. Il valore qui cresce con il numero di
        // frasi (design 2026-08-12 "migliora la rifinitura", §3.1): una
        // rifinitura batch per tutta la partita costa di piu' con piu'
        // giocatori, e un tetto fisso a 10s scadeva sistematicamente anche
        // con poche frasi. TimeoutMassimoSecondi resta cio' che impedisce a
        // una partita numerosa di aspettare troppo.
        var secondi = Math.Min(
            _opzioni.TimeoutMassimoSecondi,
            _opzioni.TimeoutSeconds + _opzioni.TimeoutSecondiPerFraseAggiuntiva * Math.Max(0, frasi.Count - 1));

        using var scadenza = CancellationTokenSource.CreateLinkedTokenSource(ct);
        scadenza.CancelAfter(TimeSpan.FromSeconds(secondi));

        try
        {
            var utente = JsonSerializer.Serialize(new
            {
                template,
                ruoli,
                frasi = frasi.Select(f => new { caselle = f }),
            });

            var risposta = await ai.CompletaAsync(Sistema, utente, scadenza.Token);

            return risposta is null ? null : Leggi(risposta, frasi.Count);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Rifinitura scaduta dopo {Secondi}s: si prosegue con le caselle grezze.", secondi);
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
