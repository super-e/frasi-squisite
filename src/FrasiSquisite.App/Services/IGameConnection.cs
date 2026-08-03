namespace FrasiSquisite.App.Services;

/// <summary>
/// Le ViewModel dipendono da questa interfaccia e mai da HubConnection: così
/// l'intero flusso di schermate si prova a server spento (spec §5).
/// </summary>
public interface IGameConnection
{
    event Action<object>? MessageReceived;

    /// <summary>
    /// Il trasporto è stato interrotto (o si è riconnesso con una nuova
    /// connessione che non recupera l'appartenenza alla stanza SignalR): da
    /// qui in poi un bot gioca al posto del giocatore, finché non esiste un
    /// rejoin di partita (Fase 2, fuori scope). Un solo evento basta: al
    /// chiamante non serve distinguere "riconnessione in corso" da
    /// "riconnesso" da "chiuso", perché la conseguenza per il giocatore è
    /// identica in tutti e tre i casi.
    /// </summary>
    event Action? ConnectionInterrupted;

    bool IsConnected { get; }

    Task ConnectAsync(string serverUrl, CancellationToken ct = default);

    Task<string> CreateRoomAsync(Guid playerId, string nickname);

    Task JoinRoomAsync(Guid playerId, string nickname, string roomCode);

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
