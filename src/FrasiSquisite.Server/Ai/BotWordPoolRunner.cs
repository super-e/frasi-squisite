using System.Text.Json;
using FrasiSquisite.Shared.Schemas;
using FrasiSquisite.Shared.Validation;

namespace FrasiSquisite.Server.Ai;

/// <summary>
/// Compone il prompt per uno schema, chiama il modello, legge la risposta
/// e valida ogni parola prima di restituirla. A differenza di
/// RefinementRunner (che lascia la fiducia a RefinementGuard, nel motore),
/// qui la validazione è definitiva: le parole dei bot finiscono nelle
/// caselle senza che il motore le rivalidi (FillDisconnected le scrive
/// direttamente), quindi ogni voce va passata per SlotTextValidator prima
/// di entrare in cache — non dopo (backlog.md §3).
/// </summary>
public sealed class BotWordPoolRunner(IAiTextProvider ai)
{
    private const string Sistema = """
        Generi parole o brevi frasi per riempire le caselle di un gioco
        surreale, quando un giocatore non è presente per scriverle da sé.

        Ricevi lo schema di un gioco: per ogni casella, il suo ruolo
        grammaticale/narrativo, un prompt che descrive cosa dovrebbe
        contenere, e un esempio già scritto per un'altra casella dello
        stesso ruolo.

        Per ciascun ruolo, genera una decina di alternative diverse fra
        loro, nello stesso stile e nello stesso registro dell'esempio dato:
        brevi, surreali, concrete, mai generiche. Non ripetere l'esempio
        stesso fra le tue proposte.

        REGOLE INDEROGABILI
        - Ogni voce sta da sola in una casella: non fare riferimento al
          resto della frase, che non conosci.
        - Massimo una manciata di parole per voce — non frasi lunghe.
        - Rispondi solo con JSON, senza commenti e senza blocchi di codice:
          {"ruoli": [{"ruolo": "...", "parole": ["...", "..."]}, ...]}
          Un elemento per ogni ruolo ricevuto, nello stesso ordine.
        """;

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>?> GeneraAsync(Schema schema, CancellationToken ct)
    {
        var utente = JsonSerializer.Serialize(new
        {
            schema = schema.Nome,
            caselle = schema.Caselle.Select(c => new { c.Ruolo, c.Prompt, c.Esempio }),
        });

        var risposta = await ai.CompletaAsync(Sistema, utente, ct, 1500);

        if (risposta is null)
        {
            return null;
        }

        var letto = Leggi(risposta);

        if (letto is null)
        {
            return null;
        }

        var validato = new Dictionary<string, IReadOnlyList<string>>();

        foreach (var (ruolo, parole) in letto)
        {
            var valide = parole
                .Select(SlotTextValidator.Validate)
                .Where(v => v.IsValid)
                .Select(v => v.Normalized)
                .ToList();

            if (valide.Count > 0)
            {
                validato[ruolo] = valide;
            }
        }

        return validato;
    }

    /// <summary>
    /// Stesso schema di ricerca del primo oggetto JSON usato in
    /// RefinementRunner.Leggi: i modelli incorniciano spesso la risposta in
    /// un blocco markdown o ci mettono una frase davanti.
    /// </summary>
    private static Dictionary<string, List<string>>? Leggi(string risposta)
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

            var ruoli = documento.RootElement.GetProperty("ruoli");
            var esito = new Dictionary<string, List<string>>();

            foreach (var voce in ruoli.EnumerateArray())
            {
                var ruolo = voce.GetProperty("ruolo").GetString();

                if (ruolo is null)
                {
                    continue;
                }

                esito[ruolo] = [.. voce.GetProperty("parole").EnumerateArray()
                    .Select(p => p.GetString())
                    .Where(p => p is not null)
                    .Select(p => p!)];
            }

            return esito;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
