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
public sealed class ImageStore(int tetto = ImageStore.TettoPredefinito)
{
    public const string Prefisso = "/illustrazioni/";

    /// <summary>
    /// Cinquanta immagini a ~1,5 MB sono circa 75 MB: il massimo che questo
    /// componente può occupare. Senza un tetto un server acceso da settimane
    /// riempirebbe la memoria del container, che ne ha poca.
    /// </summary>
    private const int TettoPredefinito = 50;

    private readonly ConcurrentDictionary<string, byte[]> _immagini = new(StringComparer.Ordinal);

    /// <summary>Ordine d'inserimento, per sapere chi esce quando si sfora.</summary>
    private readonly ConcurrentQueue<string> _ordine = new();

    public string Salva(byte[] byteImmagine)
    {
        // L'identificativo È la credenziale: chi ce l'ha vede l'immagine.
        // Quindi RandomNumberGenerator e non Random, e sedici byte, che
        // rendono inutile provare a indovinare. Con l'indice della frase
        // sarebbe bastato provare codici stanza a caso per pescare le
        // illustrazioni di partite altrui (spec §5).
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        _immagini[id] = byteImmagine;
        _ordine.Enqueue(id);

        // Più stanze salvano in contemporanea: ConcurrentDictionary e
        // ConcurrentQueue reggono da sole le singole operazioni, ma lo
        // sfratto va ragionato insieme. Il while (non un if) tollera che più
        // thread accodino prima che uno solo sfratti, e TryDequeue/TryRemove
        // non sollevano mai eccezioni se un altro thread arriva prima: nel
        // peggiore dei casi lo sfratto è di poco in ritardo o duplicato su un
        // id già rimosso (TryRemove torna semplicemente false), mai un errore
        // o un conteggio scorretto che blocchi le richieste in corso.
        while (_ordine.Count > tetto && _ordine.TryDequeue(out var vecchio))
        {
            _immagini.TryRemove(vecchio, out _);
        }

        return Prefisso + id;
    }

    public bool TryGet(string id, out byte[] byteImmagine) => _immagini.TryGetValue(id, out byteImmagine!);
}
