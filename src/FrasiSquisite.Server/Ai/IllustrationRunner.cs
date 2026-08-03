using Microsoft.Extensions.Options;

namespace FrasiSquisite.Server.Ai;

/// <summary>
/// Due chiamate, non una (spec §5): prima si traduce la frase italiana in una
/// descrizione visiva inglese tenendo solo ciò che si può disegnare, poi si
/// genera. La frase intera data al generatore produrrebbe un collage.
/// </summary>
public sealed class IllustrationRunner(
    IAiTextProvider testo,
    IAiImageProvider immagini,
    IOptions<AiOptions> opzioni,
    ILogger<IllustrationRunner> logger)
{
    private readonly AiOptions _opzioni = opzioni.Value;

    private const string Sistema = """
        Ricevi una frase surreale in italiano, scritta a più mani in un gioco.
        Trasformala in una descrizione visiva IN INGLESE per un generatore di
        immagini.

        REGOLE
        - Tieni solo ciò che si può disegnare: soggetti, luoghi, azioni,
          oggetti. Scarta ciò che non ha forma — commenti della gente,
          motivazioni, ciò che qualcuno dice, come è andata a finire.
        - Non rendere la scena sensata: l'assurdo è il punto. Se un pinguino
          indossa un doppiopetto, disegnalo col doppiopetto.
        - Niente testo, niente scritte, niente fumetti dentro l'immagine.
        - Una sola scena, non un collage.
        - Massimo quaranta parole.

        Rispondi con la sola descrizione, senza virgolette, senza blocchi di
        codice, senza spiegazioni.
        """;

    public async Task<byte[]?> IllustraAsync(string fraseItaliana, CancellationToken ct)
    {
        // Come in RefinementRunner: questo e' il limite a livello di
        // contratto sull'intera operazione a due passi (traduzione piu'
        // generazione), non un doppione del timeout dell'HttpClient di una
        // specifica implementazione - e' cio' che rende il timeout provabile
        // con doppi finti, senza rete. Qui si usa ImageTimeoutSeconds (90) e
        // non TimeoutSeconds (10): una generazione d'immagine richiede molto
        // piu' tempo di una rifinitura testuale.
        using var scadenza = CancellationTokenSource.CreateLinkedTokenSource(ct);
        scadenza.CancelAfter(TimeSpan.FromSeconds(_opzioni.ImageTimeoutSeconds));

        try
        {
            var grezza = await testo.CompletaAsync(Sistema, fraseItaliana, scadenza.Token);
            var prompt = Pulisci(grezza);

            if (string.IsNullOrWhiteSpace(prompt))
            {
                // Senza traduzione non si genera: mandare l'italiano grezzo
                // costerebbe comunque nove centesimi per un risultato che non
                // somiglierebbe alla frase.
                logger.LogWarning("Traduzione per l'illustrazione non arrivata: niente da generare.");
                return null;
            }

            return await immagini.GeneraAsync(prompt, scadenza.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Illustrazione scaduta dopo {Secondi}s.", _opzioni.ImageTimeoutSeconds);
            return null;
        }
    }

    /// <summary>
    /// I modelli incorniciano volentieri la risposta in un blocco markdown o
    /// fra virgolette. Scartarla per questo sarebbe uno spreco.
    /// </summary>
    private static string? Pulisci(string? risposta)
    {
        if (risposta is null)
        {
            return null;
        }

        var pulita = risposta.Trim().Trim('`').Trim().Trim('"').Trim();

        return pulita.Length == 0 ? null : pulita;
    }
}
