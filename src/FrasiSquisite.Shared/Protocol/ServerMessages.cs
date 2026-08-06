namespace FrasiSquisite.Shared.Protocol;

public sealed record PlayerView(Guid Id, string Nickname, bool IsHost, bool IsConnected, bool IsBot);

/// <summary>
/// Voce del catalogo degli schemi disponibili, proiettata da
/// <c>FrasiSquisite.Shared.Schemas.Schema</c>: qui non serve la lista delle
/// caselle né il template, solo ciò che il selettore in lobby deve mostrare.
/// </summary>
public sealed record SchemaView(string Id, string Nome, int SlotCount);

public sealed record RoomStateMessage(
    string RoomCode,
    string Phase,
    IReadOnlyList<PlayerView> Players,
    string SchemaId,
    int SlotCount,
    IReadOnlyList<SchemaView>? AvailableSchemas = null)
{
    /// <summary>
    /// Elenco costante per server, ritrasmesso qui a ogni RoomStateMessage
    /// invece che in un messaggio a sé (lotto-c-brief.md, §Protocollo): a
    /// cinque schemi e nove giocatori il costo è irrilevante e il client ha
    /// sempre quel che gli serve senza una seconda chiamata da sincronizzare.
    /// Se un giorno gli schemi diventassero decine, questa scelta va rivista.
    /// Il parametro del costruttore resta annullabile solo per non dover
    /// toccare le fixture di test esistenti che non lo passano (stesso
    /// principio del default di GameState.NewRoom); la proprietà pubblica
    /// resta sempre non nulla.
    /// </summary>
    public IReadOnlyList<SchemaView> AvailableSchemas { get; init; } = AvailableSchemas ?? [];
}

/// <summary>
/// Contiene esclusivamente il ruolo da riempire. Nessun campo trasporta testo
/// già scritto, e questa assenza è il modo in cui la segretezza del gioco è
/// garantita dal tipo e non dalla disciplina di chi scrive il codice
/// (spec §2.3, §4.2).
/// </summary>
public sealed record SlotRequestMessage(
    int Round,
    int TotalRounds,
    string Ruolo,
    string Prompt,
    string Esempio);

public sealed record RoundProgressMessage(int Round, int Submitted, int Total);

/// <summary>
/// Un pezzo della frase mostrata nel reveal, nell'ordine di lettura: testo
/// fisso del template (sempre presente, non rivela nulla su nessuno) o una
/// casella. Le caselle non ancora scoperte arrivano comunque, con
/// <see cref="IsRevealed"/> false e <see cref="Text"/> vuoto — così il
/// client conosce da subito la lunghezza e la punteggiatura della frase
/// intera, come già succede nella pagina del voto (backlog #1), senza che
/// il server mandi mai il contenuto di una casella coperta.
/// </summary>
public sealed record RevealFragment(bool IsSlot, string Text, bool IsRevealed);

/// <summary>
/// Il passo di scoprimento non porta gli autori: il voto che segue è cieco
/// (spec §3). Il campo non è vuoto — non esiste, così come
/// <see cref="SlotRequestMessage"/> non ha un campo per il testo già scritto.
/// Una fuga durante il reveal non è una regressione possibile.
/// </summary>
public sealed record RevealStepMessage(
    int PhraseIndex,
    int TotalPhrases,
    IReadOnlyList<RevealFragment> Fragments,
    bool PhraseComplete);

/// <summary>
/// Le frasi su cui votare, nell'ordine in cui sono state costruite. Nessun
/// autore: il voto è cieco (spec §3). L'indice nella lista è l'indice da
/// rimandare indietro con <c>CastVoteRequest</c>.
/// </summary>
public sealed record VoteRequestMessage(IReadOnlyList<string> Phrases);

/// <summary>
/// Gemello di <see cref="RoundProgressMessage"/>. <paramref name="Total"/> è
/// il numero di umani connessi, non di giocatori: bot e disconnessi non votano
/// (spec §2), e contarli lascerebbe l'attesa ferma a un numero irraggiungibile.
/// </summary>
public sealed record VoteProgressMessage(int Voted, int Total);

/// <summary>
/// Una riga della classifica finale, già al posto giusto: il client disegna e
/// non calcola, come per <see cref="SchemaView"/> e <see cref="PlayerView"/>.
/// <paramref name="IsWinner"/> è falso su tutte le righe quando nessuno ha
/// votato — "nessuna vincitrice" e "tutte a pari merito" sono cose diverse.
/// <paramref name="Authors"/> è l'insieme delle persone che hanno contribuito
/// alla frase, ciascuna una volta sola e nell'ordine in cui hanno scritto la
/// prima casella: non un autore per casella, che con più caselle che giocatori
/// ripeterebbe gli stessi nomi e direbbe quale casella è di chi.
/// </summary>
public sealed record PhraseResultView(
    int PhraseIndex,
    string Text,
    IReadOnlyList<string> Authors,
    int Votes,
    bool IsWinner);

/// <summary>Ordinate: punteggio decrescente, a parità indice crescente.</summary>
public sealed record GameFinishedMessage(IReadOnlyList<PhraseResultView> Results);

public sealed record ErrorMessage(string Code, string Message);

public sealed record ProtocolRejectedMessage(int ServerVersion, string Message);

/// <summary>
/// <paramref name="Path"/> è relativo (es. <c>/illustrazioni/xY3…</c>) e non
/// assoluto: il client conosce già l'indirizzo del server, e un indirizzo
/// assoluto costruito lato server sbaglierebbe ogni volta che c'è un reverse
/// proxy davanti — che è esattamente la configurazione in cui gira (Caddy).
///
/// L'identificativo dentro il percorso è casuale e non l'indice della frase:
/// il codice stanza è corto e indovinabile, e con l'indice chiunque potrebbe
/// pescare le illustrazioni di partite altrui provando codici a caso (spec §5).
/// </summary>
public sealed record IllustrationReadyMessage(int PhraseIndex, string Path);

/// <summary>
/// Il pulsante deve tornare disponibile: senza questo messaggio resterebbe in
/// attesa per sempre, e l'host non saprebbe se riprovare.
/// </summary>
public sealed record IllustrationFailedMessage(int PhraseIndex, string Message);
