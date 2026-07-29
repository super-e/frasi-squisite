using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Shared.Protocol;
using FrasiSquisite.Shared.Validation;

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
        SlotSubmitted e => OnSlotSubmitted(state, e),
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

    private EngineResult StartGame(GameState state)
    {
        var frasi = Enumerable
            .Range(0, _mode.PhraseCount(state.Players.Count, state.Schema))
            .Select(i => Phrase.Empty(i, state.Schema.SlotCount))
            .ToList();

        var nuovo = state with
        {
            Phase = RoomPhase.Writing,
            Round = 0,
            Phrases = frasi,
            SubmittedThisRound = new HashSet<Guid>(),
        };

        List<Effect> effetti = [new BroadcastToRoom(RoomState(nuovo))];
        effetti.AddRange(SlotRequests(nuovo));

        return new EngineResult(nuovo, effetti);
    }

    private EngineResult OnSlotSubmitted(GameState state, SlotSubmitted e)
    {
        if (state.Phase != RoomPhase.Writing)
        {
            return Error(state, e.PlayerId, "NOT_WRITING", "Non è il momento di scrivere.");
        }

        var indice = state.IndexOfPlayer(e.PlayerId);
        if (indice < 0)
        {
            return Error(state, e.PlayerId, "NOT_IN_ROOM", "Non sei in questa stanza.");
        }

        if (state.SubmittedThisRound.Contains(e.PlayerId))
        {
            return Error(state, e.PlayerId, "ALREADY_SUBMITTED", "Hai già inviato per questo round.");
        }

        // Il server riapplica la validazione: non può fidarsi del client.
        var esito = SlotTextValidator.Validate(e.Text);
        if (!esito.IsValid)
        {
            return Error(state, e.PlayerId, "INVALID_TEXT", esito.Error!);
        }

        var assegnazione = _mode.AssignSlot(state.Round, indice, state.Players.Count, state.Schema);

        var frasi = state.Phrases.ToArray();
        frasi[assegnazione.PhraseIndex] = frasi[assegnazione.PhraseIndex]
            .With(assegnazione.SlotIndex, new Slot(e.PlayerId, esito.Normalized));

        var inviati = new HashSet<Guid>(state.SubmittedThisRound) { e.PlayerId };

        var nuovo = state with { Phrases = frasi, SubmittedThisRound = inviati };

        if (inviati.Count < nuovo.Players.Count)
        {
            return new EngineResult(nuovo, [
                new BroadcastToRoom(new RoundProgressMessage(nuovo.Round, inviati.Count, nuovo.Players.Count)),
            ]);
        }

        return AdvanceRound(nuovo);
    }

    private EngineResult AdvanceRound(GameState state)
    {
        var prossimo = state with
        {
            Round = state.Round + 1,
            SubmittedThisRound = new HashSet<Guid>(),
        };

        if (_mode.IsComplete(prossimo))
        {
            var reveal = prossimo with
            {
                Phase = RoomPhase.Reveal,
                RevealPhraseIndex = 0,
                RevealSlotCount = 0,
            };

            return new EngineResult(reveal, [new BroadcastToRoom(RoomState(reveal))]);
        }

        List<Effect> effetti = [
            new BroadcastToRoom(new RoundProgressMessage(state.Round, state.Players.Count, state.Players.Count)),
        ];
        effetti.AddRange(SlotRequests(prossimo));

        return new EngineResult(prossimo, effetti);
    }

    private IEnumerable<Effect> SlotRequests(GameState state)
    {
        for (var i = 0; i < state.Players.Count; i++)
        {
            var assegnazione = _mode.AssignSlot(state.Round, i, state.Players.Count, state.Schema);
            var casella = state.Schema.Caselle[assegnazione.SlotIndex];

            // Nota: PhraseIndex resta deliberatamente fuori dal messaggio.
            yield return new SendToPlayer(
                state.Players[i].Id,
                new SlotRequestMessage(
                    state.Round,
                    state.Schema.SlotCount,
                    casella.Ruolo,
                    casella.Prompt,
                    casella.Esempio));
        }
    }

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
