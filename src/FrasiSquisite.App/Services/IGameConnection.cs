namespace FrasiSquisite.App.Services;

/// <summary>
/// Le ViewModel dipendono da questa interfaccia e mai da HubConnection: così
/// l'intero flusso di schermate si prova a server spento (spec §5).
/// </summary>
public interface IGameConnection
{
    event Action<object>? MessageReceived;

    bool IsConnected { get; }

    Task ConnectAsync(string serverUrl, CancellationToken ct = default);

    Task<string> CreateRoomAsync(Guid playerId, string nickname);

    Task JoinRoomAsync(Guid playerId, string nickname, string roomCode);

    Task StartGameAsync(string roomCode);

    Task SubmitSlotAsync(string roomCode, string text);

    Task AdvanceRevealAsync(string roomCode);

    Task DisconnectAsync();
}
