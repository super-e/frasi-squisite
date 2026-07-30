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
/// <paramref name="Authors"/> resta vuoto finché <paramref name="PhraseComplete"/>
/// è false: sapere chi scrive la casella successiva ne anticiperebbe il
/// contenuto (spec §2.4).
/// </summary>
public sealed record RevealStepMessage(
    int PhraseIndex,
    int TotalPhrases,
    IReadOnlyList<string> RevealedSlots,
    bool PhraseComplete,
    IReadOnlyList<string> Authors);

public sealed record GameFinishedMessage(IReadOnlyList<string> Phrases);

public sealed record ErrorMessage(string Code, string Message);

public sealed record ProtocolRejectedMessage(int ServerVersion, string Message);
