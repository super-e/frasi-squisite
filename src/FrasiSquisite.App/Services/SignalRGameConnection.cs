using System.Text.Json;
using FrasiSquisite.Shared.Protocol;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Maui.ApplicationModel;

namespace FrasiSquisite.App.Services;

public sealed class SignalRGameConnection : IGameConnection, IAsyncDisposable
{
    private HubConnection? _connection;

    public event Action<object>? MessageReceived;

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public async Task ConnectAsync(string serverUrl, CancellationToken ct = default)
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _connection = new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri(serverUrl), "hubs/game"))
            .WithAutomaticReconnect()
            .Build();

        _connection.On<string, JsonElement>("ReceiveMessage", (type, payload) =>
        {
            if (Deserializza(type, payload) is { } messaggio)
            {
                SollevaMessageReceived(messaggio);
            }
        });

        await _connection.StartAsync(ct);
    }

    // Il callback di SignalR arriva su un thread di background del client hub,
    // ma GameSessionViewModel aggiorna proprietà bindate e ObservableCollection
    // lette da CollectionView/Label in GamePage.xaml: mutarle fuori dal thread
    // UI rischia su Android un crash da accesso cross-thread alla view, ad ogni
    // singolo messaggio push del server. Il marshalling va fatto qui - la
    // ViewModel non può conoscere MAUI, quindi è la connessione (che sa di
    // girare su una piattaforma con un thread UI) a doversene occupare per
    // tutti i suoi consumatori. Non "semplificare" rimuovendo questo dispatch.
    private void SollevaMessageReceived(object messaggio)
    {
        try
        {
            MainThread.BeginInvokeOnMainThread(() => MessageReceived?.Invoke(messaggio));
        }
        catch (Exception)
        {
            // Nessun dispatcher disponibile (es. fuori da un contesto applicativo
            // MAUI, come in test o strumenti diagnostici): meglio consegnare
            // comunque il messaggio piuttosto che far crashare il processo.
            MessageReceived?.Invoke(messaggio);
        }
    }

    public Task<string> CreateRoomAsync(Guid playerId, string nickname) =>
        Hub.InvokeAsync<string>("CreateRoom", new CreateRoomRequest(ProtocolVersion.Current, playerId, nickname));

    public Task JoinRoomAsync(Guid playerId, string nickname, string roomCode) =>
        Hub.InvokeAsync("JoinRoom", new JoinRoomRequest(ProtocolVersion.Current, playerId, nickname, roomCode));

    public Task StartGameAsync(string roomCode) =>
        Hub.InvokeAsync("StartGame", new StartGameRequest(roomCode));

    public Task SubmitSlotAsync(string roomCode, string text) =>
        Hub.InvokeAsync("SubmitSlot", new SubmitSlotRequest(roomCode, text));

    public Task AdvanceRevealAsync(string roomCode) =>
        Hub.InvokeAsync("AdvanceReveal", roomCode);

    public async Task DisconnectAsync()
    {
        if (_connection is not null)
        {
            await _connection.StopAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    private HubConnection Hub =>
        _connection ?? throw new InvalidOperationException("Connessione non stabilita.");

    private static object? Deserializza(string type, JsonElement payload) => type switch
    {
        nameof(RoomStateMessage) => payload.Deserialize<RoomStateMessage>(ProtocolJson.Options),
        nameof(SlotRequestMessage) => payload.Deserialize<SlotRequestMessage>(ProtocolJson.Options),
        nameof(RoundProgressMessage) => payload.Deserialize<RoundProgressMessage>(ProtocolJson.Options),
        nameof(RevealStepMessage) => payload.Deserialize<RevealStepMessage>(ProtocolJson.Options),
        nameof(GameFinishedMessage) => payload.Deserialize<GameFinishedMessage>(ProtocolJson.Options),
        nameof(ErrorMessage) => payload.Deserialize<ErrorMessage>(ProtocolJson.Options),
        _ => null,
    };
}
