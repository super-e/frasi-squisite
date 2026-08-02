using System.Text.Json;
using FrasiSquisite.Shared.Protocol;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Maui.ApplicationModel;

namespace FrasiSquisite.App.Services;

public sealed class SignalRGameConnection : IGameConnection, IAsyncDisposable
{
    private HubConnection? _connection;

    public event Action<object>? MessageReceived;

    public event Action? ConnectionInterrupted;

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

        // Il riconnettersi a livello di trasporto (.WithAutomaticReconnect) apre
        // una connessione nuova, con un ConnectionId diverso: non recupera da
        // solo l'appartenenza ai gruppi SignalR della stanza, che il server ha
        // già rimosso marcando il giocatore disconnesso (e facendoci giocare un
        // bot al suo posto). I tre eventi hanno quindi la stessa conseguenza
        // visibile per il giocatore: senza questo avviso il client resterebbe
        // "connesso" (IsConnected true) ma sordo a ogni messaggio successivo,
        // senza che nulla in schermata lo segnali (spec I1).
        _connection.Reconnecting += _ =>
        {
            SollevaConnectionInterrupted();
            return Task.CompletedTask;
        };
        _connection.Reconnected += _ =>
        {
            SollevaConnectionInterrupted();
            return Task.CompletedTask;
        };
        _connection.Closed += _ =>
        {
            SollevaConnectionInterrupted();
            return Task.CompletedTask;
        };

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
    private void SollevaMessageReceived(object messaggio) =>
        EseguiSulThreadUI(() => MessageReceived?.Invoke(messaggio));

    private void SollevaConnectionInterrupted() =>
        EseguiSulThreadUI(() => ConnectionInterrupted?.Invoke());

    private static void EseguiSulThreadUI(Action azione)
    {
        try
        {
            MainThread.BeginInvokeOnMainThread(azione);
        }
        catch (Exception ex)
        {
            // Nessun dispatcher disponibile (es. fuori da un contesto applicativo
            // MAUI, come in test o strumenti diagnostici): meglio eseguire
            // comunque l'azione piuttosto che far crashare il processo.
            // La traccia serve solo in debug: non deve indebolire la garanzia
            // "non crashare mai il processo", quindi resta un semplice log.
            System.Diagnostics.Debug.WriteLine(
                $"EseguiSulThreadUI: nessun dispatcher MAUI disponibile, invocazione diretta. {ex}");
            azione();
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

    public Task CastVoteAsync(string roomCode, int phraseIndex) =>
        Hub.InvokeAsync("CastVote", new CastVoteRequest(roomCode, phraseIndex));

    public Task CloseVotingAsync(string roomCode) =>
        Hub.InvokeAsync("CloseVoting", new CloseVotingRequest(roomCode));

    public Task NewGameAsync(string roomCode) =>
        Hub.InvokeAsync("NewGame", new NewGameRequest(roomCode));

    public Task BackToLobbyAsync(string roomCode) =>
        Hub.InvokeAsync("BackToLobby", new BackToLobbyRequest(roomCode));

    public Task AddBotAsync(string roomCode) =>
        Hub.InvokeAsync("AddBot", new AddBotRequest(roomCode));

    public Task RemoveBotAsync(string roomCode, Guid botId) =>
        Hub.InvokeAsync("RemoveBot", new RemoveBotRequest(roomCode, botId));

    public Task RenameBotAsync(string roomCode, Guid botId, string nickname) =>
        Hub.InvokeAsync("RenameBot", new RenameBotRequest(roomCode, botId, nickname));

    public Task SetSchemaAsync(string roomCode, string schemaId) =>
        Hub.InvokeAsync("SetSchema", new SetSchemaRequest(roomCode, schemaId));

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
        nameof(VoteRequestMessage) => payload.Deserialize<VoteRequestMessage>(ProtocolJson.Options),
        nameof(VoteProgressMessage) => payload.Deserialize<VoteProgressMessage>(ProtocolJson.Options),
        nameof(ErrorMessage) => payload.Deserialize<ErrorMessage>(ProtocolJson.Options),
        _ => null,
    };
}
