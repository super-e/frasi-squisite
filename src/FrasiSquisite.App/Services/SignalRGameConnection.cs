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

    public event Action? Reconnected;

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public async Task ConnectAsync(string serverUrl, CancellationToken ct = default)
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        // Stesso problema e stessa correzione delle URL di illustrazione in
        // GameSessionViewModel (vedi baseConSlash lì): se ServerUrl porta un
        // path-prefix di un reverse proxy (path-based, non a sottodominio),
        // combinarlo con Uri senza uno '/' finale lo scarterebbe in
        // silenzio. "hubs/game" (relativo, senza '/' iniziale) non ha
        // bisogno di TrimStart qui: è già nella forma giusta perché Uri lo
        // aggiunga invece di sostituirlo.
        var baseConSlash = serverUrl.EndsWith('/') ? serverUrl : serverUrl + "/";

        _connection = new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri(baseConSlash), "hubs/game"))
            .WithAutomaticReconnect()
            .Build();

        _connection.On<string, JsonElement>("ReceiveMessage", (type, payload) =>
        {
            if (Deserializza(type, payload) is { } messaggio)
            {
                SollevaMessageReceived(messaggio);
            }
        });

        // Reconnecting/Closed: il trasporto è giù o ci sta provando, stesso
        // banner di prima (spec I1). Reconnected è diverso da quando esiste
        // il rientro (design rientro §5.2): il trasporto è tornato, ma con
        // un ConnectionId nuovo che non recupera da solo l'appartenenza al
        // gruppo SignalR della stanza — chi ascolta deve tentare un rientro
        // esplicito, non limitarsi a mostrare un banner.
        _connection.Reconnecting += _ =>
        {
            SollevaConnectionInterrupted();
            return Task.CompletedTask;
        };
        _connection.Reconnected += _ =>
        {
            SollevaReconnected();
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

    private void SollevaReconnected() =>
        EseguiSulThreadUI(() => Reconnected?.Invoke());

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

    public Task RejoinRoomAsync(Guid playerId, string roomCode) =>
        Hub.InvokeAsync("RejoinRoom", new RejoinRoomRequest(ProtocolVersion.Current, playerId, roomCode));

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

    public Task RequestIllustrationAsync(string roomCode, int phraseIndex) =>
        Hub.InvokeAsync("RequestIllustration", new RequestIllustrationRequest(roomCode, phraseIndex));

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
        nameof(IllustrationReadyMessage) => payload.Deserialize<IllustrationReadyMessage>(ProtocolJson.Options),
        nameof(IllustrationFailedMessage) => payload.Deserialize<IllustrationFailedMessage>(ProtocolJson.Options),
        nameof(ErrorMessage) => payload.Deserialize<ErrorMessage>(ProtocolJson.Options),
        nameof(RejoinRejectedMessage) => payload.Deserialize<RejoinRejectedMessage>(ProtocolJson.Options),
        _ => null,
    };
}
