using FrasiSquisite.App.Services;

namespace FrasiSquisite.App.Tests;

/// <summary>
/// Implementazione in memoria di <see cref="IGameConnection"/>: registra le
/// chiamate e permette di simulare i messaggi in arrivo dal server.
/// </summary>
public sealed class FakeGameConnection : IGameConnection
{
    private readonly List<string> _calls = [];

    public event Action<object>? MessageReceived;

    public bool IsConnected { get; private set; }

    public IReadOnlyList<string> Calls => _calls;

    public string NextRoomCode { get; set; } = "ABCD";

    public void Emit(object message) => MessageReceived?.Invoke(message);

    public Task ConnectAsync(string serverUrl, CancellationToken ct = default)
    {
        _calls.Add($"Connect({serverUrl})");
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task<string> CreateRoomAsync(Guid playerId, string nickname)
    {
        _calls.Add($"CreateRoom({nickname})");
        return Task.FromResult(NextRoomCode);
    }

    public Task JoinRoomAsync(Guid playerId, string nickname, string roomCode)
    {
        _calls.Add($"JoinRoom({nickname},{roomCode})");
        return Task.CompletedTask;
    }

    public Task StartGameAsync(string roomCode)
    {
        _calls.Add($"StartGame({roomCode})");
        return Task.CompletedTask;
    }

    public Task SubmitSlotAsync(string roomCode, string text)
    {
        _calls.Add($"SubmitSlot({roomCode},{text})");
        return Task.CompletedTask;
    }

    public Task AdvanceRevealAsync(string roomCode)
    {
        _calls.Add($"AdvanceReveal({roomCode})");
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        _calls.Add("Disconnect()");
        IsConnected = false;
        return Task.CompletedTask;
    }
}
