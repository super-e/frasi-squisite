using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Server.Rooms;
using FrasiSquisite.Shared.Protocol;
using Microsoft.AspNetCore.SignalR;

namespace FrasiSquisite.Server.Realtime;

public sealed class GameHub(GameHost host, IRoomRegistry rooms) : Hub
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

        await EntraAsync(stanza.RoomCode, request.PlayerId);
        await host.DispatchAsync(stanza.RoomCode, new PlayerJoined(request.PlayerId, request.Nickname));
    }

    public Task StartGame(StartGameRequest request) =>
        host.DispatchAsync(request.RoomCode, new GameStartRequested(GiocatoreCorrente()));

    public Task SubmitSlot(SubmitSlotRequest request) =>
        host.DispatchAsync(request.RoomCode, new SlotSubmitted(GiocatoreCorrente(), request.Text));

    public Task AdvanceReveal(string roomCode) =>
        host.DispatchAsync(roomCode, new RevealAdvanceRequested(GiocatoreCorrente()));

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(RoomKey, out var room) && room is string roomCode &&
            Context.Items.TryGetValue(PlayerKey, out var player) && player is Guid playerId)
        {
            await host.DispatchAsync(roomCode, new PlayerLeft(playerId));
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
