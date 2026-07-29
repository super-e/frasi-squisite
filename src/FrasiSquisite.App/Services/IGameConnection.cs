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

    Task DisconnectAsync();
}
