namespace FrasiSquisite.App.Services;

/// <summary>
/// Le ViewModel dipendono da questa interfaccia e mai da HubConnection: così
/// l'intero flusso di schermate si prova a server spento (spec §5).
/// </summary>
public interface IGameConnection
{
    event Action<object>? MessageReceived;

    /// <summary>
    /// Il trasporto sta tentando di riconnettersi, o si è chiuso del tutto:
    /// in entrambi i casi mostra il banner "connessione instabile". Non
    /// scatta più su un vero ripristino del trasporto — quello è
    /// <see cref="Reconnected"/>, separato apposta da quando esiste un
    /// tentativo di rientro (design rientro §5.2).
    /// </summary>
    event Action? ConnectionInterrupted;

    /// <summary>
    /// Il trasporto si è ripristinato (.WithAutomaticReconnect), ma con un
    /// nuovo ConnectionId che non recupera da solo l'appartenenza alla
    /// stanza SignalR: chi ascolta deve tentare un rientro esplicito
    /// (design rientro §5.2), non limitarsi a mostrare un banner.
    /// </summary>
    event Action? Reconnected;

    bool IsConnected { get; }

    Task ConnectAsync(string serverUrl, CancellationToken ct = default);

    Task<string> CreateRoomAsync(Guid playerId, string nickname);

    Task JoinRoomAsync(Guid playerId, string nickname, string roomCode);

    /// <summary>A differenza di JoinRoomAsync funziona anche a partita già iniziata (design rientro §3.3).</summary>
    Task RejoinRoomAsync(Guid playerId, string roomCode);

    Task StartGameAsync(string roomCode);

    Task SubmitSlotAsync(string roomCode, string text);

    Task AdvanceRevealAsync(string roomCode);

    /// <summary>Un voto a testa, non si cambia: il secondo tentativo torna ALREADY_VOTED.</summary>
    Task CastVoteAsync(string roomCode, int phraseIndex);

    /// <summary>Chiude il voto senza aspettare i ritardatari. Solo l'host.</summary>
    Task CloseVotingAsync(string roomCode);

    /// <summary>Solo l'host: il server rifiuta gli altri con NOT_HOST.</summary>
    Task RequestIllustrationAsync(string roomCode, int phraseIndex);

    /// <summary>Riparte subito dalla schermata finale, stessi giocatori e stesso schema (lotto-d-brief.md).</summary>
    Task NewGameAsync(string roomCode);

    /// <summary>Torna alla lobby dalla schermata finale senza avviare nulla (lotto-d-brief.md).</summary>
    Task BackToLobbyAsync(string roomCode);

    Task AddBotAsync(string roomCode);

    Task RemoveBotAsync(string roomCode, Guid botId);

    Task RenameBotAsync(string roomCode, Guid botId, string nickname);

    Task SetSchemaAsync(string roomCode, string schemaId);

    Task DisconnectAsync();
}
