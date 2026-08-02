using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Server.Rooms;
using FrasiSquisite.Shared.Protocol;
using FrasiSquisite.Shared.Schemas;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace FrasiSquisite.Server.Realtime;

public sealed class GameHub(GameHost host, IRoomRegistry rooms, ISchemaCatalog schemas, ILogger<GameHub> logger) : Hub
{
    private const string RoomKey = "room";
    private const string PlayerKey = "player";

    public async Task<string> CreateRoom(CreateRoomRequest request)
    {
        RichiediProtocolloCompatibile(request.ProtocolVersion);

        var stanza = rooms.Create();
        await EntraAsync(stanza.RoomCode, request.PlayerId);
        await host.DispatchAsync(stanza.RoomCode, new PlayerJoined(request.PlayerId, request.Nickname));

        return stanza.RoomCode;
    }

    public async Task JoinRoom(JoinRoomRequest request)
    {
        RichiediProtocolloCompatibile(request.ProtocolVersion);

        if (!rooms.TryGet(request.RoomCode, out var stanza))
        {
            throw new HubException("Stanza non trovata.");
        }

        // Il controllo va fatto PRIMA di aggiungere la connessione ai gruppi
        // SignalR (spec I2): farlo dopo iscrive comunque chi viene rifiutato,
        // che da quel momento riceve ogni RevealStepMessage e la
        // GameFinishedMessage di una partita a cui non partecipa. Una finestra
        // di corsa residua (la fase cambia proprio tra questo controllo e la
        // dispatch sotto) resta possibile - le operazioni sul registro sono
        // singolarmente thread-safe ma la sequenza no - ed è accettata: è
        // l'eccezione, non la regola come accadeva prima di questo fix.
        if (stanza.Phase != RoomPhase.Lobby)
        {
            throw new HubException("La partita è già iniziata.");
        }

        await EntraAsync(stanza.RoomCode, request.PlayerId);
        await host.DispatchAsync(stanza.RoomCode, new PlayerJoined(request.PlayerId, request.Nickname));
    }

    public Task StartGame(StartGameRequest request) =>
        host.DispatchAsync(request.RoomCode, new GameStartRequested(GiocatoreCorrente()));

    public Task SubmitSlot(SubmitSlotRequest request) =>
        host.DispatchAsync(request.RoomCode, new SlotSubmitted(GiocatoreCorrente(), request.Text));

    public Task AdvanceReveal(string roomCode) =>
        host.DispatchAsync(roomCode, new RevealAdvanceRequested(GiocatoreCorrente()));

    public Task CastVote(CastVoteRequest request) =>
        host.DispatchAsync(request.RoomCode, new VoteCast(GiocatoreCorrente(), request.PhraseIndex));

    public Task CloseVoting(CloseVotingRequest request) =>
        host.DispatchAsync(request.RoomCode, new VotingCloseRequested(GiocatoreCorrente()));

    public Task NewGame(NewGameRequest request) =>
        host.DispatchAsync(request.RoomCode, new NewGameRequested(GiocatoreCorrente()));

    public Task BackToLobby(BackToLobbyRequest request) =>
        host.DispatchAsync(request.RoomCode, new BackToLobbyRequested(GiocatoreCorrente()));

    // L'id del bot lo genera l'hub, mai il motore (Domain non produce GUID:
    // niente nondeterminismo, vedi lotto-b-brief.md punto 2). Gli altri due
    // metodi inoltrano un id già esistente, scelto da chi ha aggiunto il bot.
    public Task AddBot(AddBotRequest request) =>
        host.DispatchAsync(request.RoomCode, new BotAdded(Guid.NewGuid(), GiocatoreCorrente()));

    public Task RemoveBot(RemoveBotRequest request) =>
        host.DispatchAsync(request.RoomCode, new BotRemoved(request.BotId, GiocatoreCorrente()));

    public Task RenameBot(RenameBotRequest request) =>
        host.DispatchAsync(request.RoomCode, new BotRenamed(request.BotId, request.Nickname, GiocatoreCorrente()));

    // A differenza degli altri metodi, la risoluzione dell'id può fallire
    // PRIMA di raggiungere il motore: il motore non conosce il catalogo
    // (spec del lotto, niente ISchemaCatalog in Domain), quindi tocca
    // all'hub risolvere l'identificativo. Un id inesistente non genera
    // nessun evento - non c'è nulla da chiedere al motore di rifiutare - e
    // la risposta va solo al chiamante, mai alla stanza intera.
    public async Task SetSchema(SetSchemaRequest request)
    {
        Schema schema;
        try
        {
            schema = schemas.Get(request.SchemaId);
        }
        catch (KeyNotFoundException)
        {
            await Clients.Caller.SendAsync(
                "ReceiveMessage",
                nameof(ErrorMessage),
                new ErrorMessage("NO_SUCH_SCHEMA", $"Lo schema '{request.SchemaId}' non esiste."));
            return;
        }

        await host.DispatchAsync(request.RoomCode, new SchemaSelected(schema, GiocatoreCorrente()));
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(RoomKey, out var room) && room is string roomCode &&
            Context.Items.TryGetValue(PlayerKey, out var player) && player is Guid playerId)
        {
            try
            {
                await host.DispatchAsync(roomCode, new PlayerLeft(playerId));
            }
            catch (Exception ex)
            {
                // La stanza può essere sparita (es. riavvio del server, che
                // lancia HubException), ma su un socket già a metà morto anche
                // IOException, ObjectDisposedException o OperationCanceledException
                // sono guasti plausibili nell'invio degli effetti. In
                // disconnessione non c'è più nessun client a cui segnalarlo,
                // quindi non deve far esplodere la disconnessione - ma va comunque
                // loggato (a livello Warning, non Error: è un percorso atteso,
                // non un guasto del server) perché è l'unica traccia osservabile
                // rimasta di quel che è successo.
                logger.LogWarning(
                    ex,
                    "Disconnessione del giocatore {PlayerId} dalla stanza {RoomCode}: dispatch di PlayerLeft fallita.",
                    playerId,
                    roomCode);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task EntraAsync(string roomCode, Guid playerId)
    {
        Context.Items[RoomKey] = roomCode;
        Context.Items[PlayerKey] = playerId;

        await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);
        await Groups.AddToGroupAsync(Context.ConnectionId, GameHost.PlayerGroup(playerId));
    }

    private Guid GiocatoreCorrente() =>
        Context.Items.TryGetValue(PlayerKey, out var player) && player is Guid id
            ? id
            : throw new HubException("Non sei in una stanza.");

    private static void RichiediProtocolloCompatibile(int clientVersion)
    {
        if (!ProtocolVersion.IsCompatible(clientVersion))
        {
            throw new HubException(
                $"Versione dell'app non compatibile: il server parla la versione {ProtocolVersion.Current}. Aggiorna l'app.");
        }
    }
}
