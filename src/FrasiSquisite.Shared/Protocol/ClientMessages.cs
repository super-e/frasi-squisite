namespace FrasiSquisite.Shared.Protocol;

public sealed record CreateRoomRequest(int ProtocolVersion, Guid PlayerId, string Nickname);

public sealed record JoinRoomRequest(int ProtocolVersion, Guid PlayerId, string Nickname, string RoomCode);

/// <summary>
/// A differenza di JoinRoomRequest non porta Nickname: il giocatore esiste
/// già nella stanza, il nickname non cambia (design rientro §4).
/// </summary>
public sealed record RejoinRoomRequest(int ProtocolVersion, Guid PlayerId, string RoomCode);

public sealed record StartGameRequest(string RoomCode);

public sealed record SubmitSlotRequest(string RoomCode, string Text);

public sealed record AddBotRequest(string RoomCode);

public sealed record RemoveBotRequest(string RoomCode, Guid BotId);

public sealed record RenameBotRequest(string RoomCode, Guid BotId, string Nickname);

public sealed record SetSchemaRequest(string RoomCode, string SchemaId);

public sealed record NewGameRequest(string RoomCode);

public sealed record BackToLobbyRequest(string RoomCode);

/// <summary>
/// <paramref name="PhraseIndex"/> è l'indice nella lista arrivata con
/// <c>VoteRequestMessage</c>, non l'indice di riga della classifica: quella
/// non esiste ancora quando si vota.
/// </summary>
public sealed record CastVoteRequest(string RoomCode, int PhraseIndex);

public sealed record CloseVotingRequest(string RoomCode);

/// <summary>
/// <paramref name="PhraseIndex"/> è l'indice della frase, lo stesso che porta
/// ogni riga di <c>PhraseResultView</c>: non l'indice di riga della classifica,
/// che dipende dall'ordinamento per voti.
/// </summary>
public sealed record RequestIllustrationRequest(string RoomCode, int PhraseIndex);
