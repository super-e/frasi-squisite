using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Shared.Protocol;

namespace FrasiSquisite.Domain.Engine;

public sealed class GameEngine(IGameMode mode) : IGameEngine
{
    public const int MinPlayers = 2;

    private readonly IGameMode _mode = mode;

    public EngineResult Handle(GameState state, GameEvent evt) => evt switch
    {
        PlayerJoined e => OnPlayerJoined(state, e),
        PlayerLeft e => OnPlayerLeft(state, e),
        GameStartRequested e => OnGameStartRequested(state, e),
        _ => EngineResult.NoChange(state),
    };

    private static EngineResult OnPlayerJoined(GameState state, PlayerJoined e)
    {
        if (state.Phase != RoomPhase.Lobby)
        {
            return Error(state, e.PlayerId, "GAME_IN_PROGRESS", "La partita è già iniziata.");
        }

        if (state.FindPlayer(e.PlayerId) is not null)
        {
            return new EngineResult(state, [new BroadcastToRoom(RoomState(state))]);
        }

        var giocatore = new Player(e.PlayerId, e.Nickname, IsBot: false, state.NextJoinOrder, IsConnected: true);

        var nuovo = state with
        {
            Players = [.. state.Players, giocatore],
            NextJoinOrder = state.NextJoinOrder + 1,
            HostId = state.Players.Count == 0 ? e.PlayerId : state.HostId,
        };

        return new EngineResult(nuovo, [new BroadcastToRoom(RoomState(nuovo))]);
    }

    private static EngineResult OnPlayerLeft(GameState state, PlayerLeft e)
    {
        if (state.FindPlayer(e.PlayerId) is null)
        {
            return EngineResult.NoChange(state);
        }

        var rimasti = state.Players.Where(p => p.Id != e.PlayerId).ToList();

        // Successione dell'host: il presente da più tempo. La partita non muore
        // con chi l'ha creata (spec §9).
        var nuovoHost = state.HostId == e.PlayerId
            ? rimasti.OrderBy(p => p.JoinOrder).FirstOrDefault()?.Id ?? Guid.Empty
            : state.HostId;

        var nuovo = state with { Players = rimasti, HostId = nuovoHost };

        return new EngineResult(nuovo, [new BroadcastToRoom(RoomState(nuovo))]);
    }

    private EngineResult OnGameStartRequested(GameState state, GameStartRequested e)
    {
        if (state.Phase != RoomPhase.Lobby)
        {
            return Error(state, e.RequestedBy, "GAME_IN_PROGRESS", "La partita è già iniziata.");
        }

        if (state.HostId != e.RequestedBy)
        {
            return Error(state, e.RequestedBy, "NOT_HOST", "Solo chi ha creato la stanza può avviare.");
        }

        if (state.Players.Count < MinPlayers)
        {
            return Error(state, e.RequestedBy, "TOO_FEW_PLAYERS", $"Servono almeno {MinPlayers} giocatori.");
        }

        return StartGame(state);
    }

    // Implementato nel Task 6.
    private EngineResult StartGame(GameState state) => EngineResult.NoChange(state);

    private static EngineResult Error(GameState state, Guid playerId, string code, string message) =>
        new(state, [new SendToPlayer(playerId, new ErrorMessage(code, message))]);

    private static RoomStateMessage RoomState(GameState state) =>
        new(
            state.RoomCode,
            state.Phase.ToString(),
            [.. state.Players.Select(p => new PlayerView(p.Id, p.Nickname, p.Id == state.HostId, p.IsConnected))],
            state.Schema.Id,
            state.Schema.SlotCount);
}
