namespace FrasiSquisite.Shared.Protocol;

public sealed record PlayerView(Guid Id, string Nickname, bool IsHost, bool IsConnected, bool IsBot);

public sealed record RoomStateMessage(
    string RoomCode,
    string Phase,
    IReadOnlyList<PlayerView> Players,
    string SchemaId,
    int SlotCount);

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
