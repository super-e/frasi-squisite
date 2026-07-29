using System.Text.Json;
using FrasiSquisite.Shared.Protocol;
using Microsoft.AspNetCore.SignalR.Client;

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
                MessageReceived?.Invoke(messaggio);
            }
        });

        await _connection.StartAsync(ct);
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
