using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace FrasiSquisite.Server.Images;

/// <summary>
/// Le immagini vivono in memoria accanto alle stanze, non su disco (spec §5).
/// Il container è deliberatamente senza stato: nessun volume da montare,
/// nessuna chiave di cifratura da custodire, nessuna pulizia da programmare.
/// Un riavvio interrompe comunque la partita in corso, quindi perdere le
/// immagini insieme a essa non toglie niente a nessuno. Si discosta dalla
/// §8.4 del design generale, che le voleva cifrate su disco, per questo.
/// </summary>
public sealed class ImageStore(long budgetByte = ImageStore.BudgetPredefinitoInByte)
{
    public const string Prefisso = "/illustrazioni/";

    /// <summary>
    /// ~75 MB: quanto questo componente può occupare in totale. Il tetto è
    /// sui byte, non sul numero di immagini, perché "numero di pezzi" non
    /// promette niente sulla memoria occupata — <c>AiOptions.ImageSize</c>
    /// è configurabile fino al 4K e i byte arrivano comunque dalla risposta
    /// di un fornitore esterno, che non è tenuto a rispettare nessuna taglia
    /// nominale. Un tetto a pezzi con immagini di dimensione libera lascia
    /// passare esattamente il guasto che dovrebbe impedire: un container con
    /// poca memoria che si riempie, visibile solo dopo settimane.
    /// </summary>
    private const long BudgetPredefinitoInByte = 75L * 1024 * 1024;

    /// <summary>
    /// Un tetto nullo o negativo non è "nessun limite": è un deposito che
    /// sfratta tutto all'istante, in silenzio, perché ogni inserimento fa
    /// scattare subito la condizione di sforamento. Meglio un errore
    /// all'avvio che una cache che sembra funzionare e non trattiene niente.
    /// </summary>
    private readonly long _budgetByte = budgetByte > 0
        ? budgetByte
        : throw new ArgumentOutOfRangeException(nameof(budgetByte), budgetByte, "Il budget in byte deve essere positivo.");

    private readonly ConcurrentDictionary<string, byte[]> _immagini = new(StringComparer.Ordinal);

    /// <summary>Ordine d'inserimento, per sapere chi esce quando si sfora.</summary>
    private readonly Queue<string> _ordine = new();

    /// <summary>Byte totali depositati, mantenuto insieme a <see cref="_ordine"/>.</summary>
    private long _totaleByte;

    /// <summary>
    /// Protegge la contabilità dei byte e lo sfratto. Il deposito è un
    /// singleton condiviso da tutte le stanze: più partite possono salvare
    /// nello stesso istante da thread diversi, e a differenza del vecchio
    /// tetto a pezzi (dove ConcurrentDictionary e ConcurrentQueue bastavano
    /// da soli, perché ogni sfratto valeva sempre "uno") qui la dimensione
    /// di ogni immagine è diversa: senza un lucchetto due thread potrebbero
    /// leggere lo stesso totale, accodare entrambi la propria dimensione e
    /// sfrattare in base a un valore già superato dall'altro, lasciando il
    /// totale a divergere dal contenuto reale — e a quel punto il tetto
    /// smette di valere senza che nessuno se ne accorga. Si salva
    /// un'immagine ogni molte decine di secondi, non mille volte al
    /// secondo: la correttezza qui vale più di qualche microsecondo di
    /// attesa. TryGet resta invece lock-free: legge soltanto, e serve le
    /// immagini a ogni richiesta HTTP.
    /// </summary>
    private readonly Lock _lucchetto = new();

    /// <summary>
    /// Torna il percorso da cui l'immagine sarà servita, oppure
    /// <see langword="null"/> se <paramref name="byteImmagine"/> da sola
    /// supera l'intero budget: accettarla vorrebbe dire sfrattare tutte le
    /// altre e restare comunque sopra il tetto da sola. Il chiamante deve
    /// trattarlo come un fallimento, non come un salvataggio riuscito.
    /// </summary>
    public string? Salva(byte[] byteImmagine)
    {
        if (byteImmagine.LongLength > _budgetByte)
        {
            return null;
        }

        // L'identificativo È la credenziale: chi ce l'ha vede l'immagine.
        // Quindi RandomNumberGenerator e non Random, e sedici byte, che
        // rendono inutile provare a indovinare. Con l'indice della frase
        // sarebbe bastato provare codici stanza a caso per pescare le
        // illustrazioni di partite altrui (spec §5).
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        lock (_lucchetto)
        {
            _immagini[id] = byteImmagine;
            _ordine.Enqueue(id);
            _totaleByte += byteImmagine.LongLength;

            // Il while (non un if) sfratta finché serve: una singola
            // immagine grande quanto tutto il budget meno un byte può da
            // sola richiedere più di uno sfratto per fare posto a sé stessa.
            while (_totaleByte > _budgetByte && _ordine.TryDequeue(out var vecchio))
            {
                if (_immagini.TryRemove(vecchio, out var rimossa))
                {
                    _totaleByte -= rimossa.LongLength;
                }
            }
        }

        return Prefisso + id;
    }

    public bool TryGet(string id, out byte[] byteImmagine) => _immagini.TryGetValue(id, out byteImmagine!);
}
