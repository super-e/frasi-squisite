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

    public event Action? ConnectionInterrupted;

    public bool IsConnected { get; private set; }

    public IReadOnlyList<string> Calls => _calls;

    public string NextRoomCode { get; set; } = "ABCD";

    /// <summary>
    /// Se impostata, il prossimo metodo invocato la lancia invece di
    /// completare con successo (e si azzera da sola): simula un guasto di
    /// rete o di hub (es. HubException, HttpRequestException) senza dover
    /// implementare un vero trasporto solo per provare che la ViewModel
    /// gestisca il fallimento invece di propagarlo (spec C2).
    /// </summary>
    public Exception? NextFailure { get; set; }

    /// <summary>
    /// Se impostato, viene emesso <em>durante</em> la prossima
    /// <see cref="SubmitSlotAsync"/>, prima che il Task ritorni (e si azzera
    /// da solo).
    ///
    /// Non è uno scenario di fantasia: <c>GameHost.DispatchAsync</c> esegue
    /// il motore e attende l'invio di tutti gli effetti prima che il metodo
    /// hub ritorni, quindi con un bot in stanza — che rende l'invio dell'unico
    /// umano capace di chiudere il round — la richiesta di casella del round
    /// successivo arriva davvero prima che l'<c>await</c> del client si
    /// sblocchi. Senza questo aggancio la ViewModel non poteva essere provata
    /// contro quell'ordine, ed è esattamente il buco da cui è passato il bug
    /// del blocco su "in attesa".
    /// </summary>
    public object? MessaggioDuranteInvio { get; set; }

    public void Emit(object message) => MessageReceived?.Invoke(message);

    public void EmitConnectionInterrupted() => ConnectionInterrupted?.Invoke();

    public Task ConnectAsync(string serverUrl, CancellationToken ct = default)
    {
        LanciaSeImpostato();
        _calls.Add($"Connect({serverUrl})");
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task<string> CreateRoomAsync(Guid playerId, string nickname)
    {
        LanciaSeImpostato();
        _calls.Add($"CreateRoom({nickname})");
        return Task.FromResult(NextRoomCode);
    }

    public Task JoinRoomAsync(Guid playerId, string nickname, string roomCode)
    {
        LanciaSeImpostato();
        _calls.Add($"JoinRoom({nickname},{roomCode})");
        return Task.CompletedTask;
    }

    public Task StartGameAsync(string roomCode)
    {
        LanciaSeImpostato();
        _calls.Add($"StartGame({roomCode})");
        return Task.CompletedTask;
    }

    public Task SubmitSlotAsync(string roomCode, string text)
    {
        LanciaSeImpostato();
        _calls.Add($"SubmitSlot({roomCode},{text})");

        if (MessaggioDuranteInvio is { } messaggio)
        {
            MessaggioDuranteInvio = null;
            MessageReceived?.Invoke(messaggio);
        }

        return Task.CompletedTask;
    }

    public Task AdvanceRevealAsync(string roomCode)
    {
        LanciaSeImpostato();
        _calls.Add($"AdvanceReveal({roomCode})");
        return Task.CompletedTask;
    }

    public Task NewGameAsync(string roomCode)
    {
        LanciaSeImpostato();
        _calls.Add($"NewGame({roomCode})");
        return Task.CompletedTask;
    }

    public Task BackToLobbyAsync(string roomCode)
    {
        LanciaSeImpostato();
        _calls.Add($"BackToLobby({roomCode})");
        return Task.CompletedTask;
    }

    public Task AddBotAsync(string roomCode)
    {
        LanciaSeImpostato();
        _calls.Add($"AddBot({roomCode})");
        return Task.CompletedTask;
    }

    public Task RemoveBotAsync(string roomCode, Guid botId)
    {
        LanciaSeImpostato();
        _calls.Add($"RemoveBot({roomCode},{botId})");
        return Task.CompletedTask;
    }

    public Task RenameBotAsync(string roomCode, Guid botId, string nickname)
    {
        LanciaSeImpostato();
        _calls.Add($"RenameBot({roomCode},{botId},{nickname})");
        return Task.CompletedTask;
    }

    public Task SetSchemaAsync(string roomCode, string schemaId)
    {
        LanciaSeImpostato();
        _calls.Add($"SetSchema({roomCode},{schemaId})");
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        _calls.Add("Disconnect()");
        IsConnected = false;
        return Task.CompletedTask;
    }

    private void LanciaSeImpostato()
    {
        if (NextFailure is { } eccezione)
        {
            NextFailure = null;
            throw eccezione;
        }
    }
}
