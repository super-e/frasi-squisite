using FrasiSquisite.Domain.Model;
using FrasiSquisite.Shared.Protocol;
using FrasiSquisite.Shared.Validation;

namespace FrasiSquisite.Domain.Engine;

/// <summary>
/// Chi entra, chi esce, i bot. Separato dalle fasi di gioco perché questi
/// eventi arrivano in qualunque fase e non appartengono a nessuna.
/// </summary>
public sealed partial class GameEngine
{
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

    private EngineResult OnPlayerLeft(GameState state, PlayerLeft e)
    {
        var uscente = state.FindPlayer(e.PlayerId);
        if (uscente is null)
        {
            return EngineResult.NoChange(state);
        }

        // In lobby si esce davvero: N non è ancora fissato.
        if (state.Phase == RoomPhase.Lobby)
        {
            var rimasti = state.Players.Where(p => p.Id != e.PlayerId).ToList();

            // Filtrato su IsConnected come nel ramo Writing sotto: un bot non
            // è mai connesso, quindi non deve mai ereditare l'host solo
            // perché è il rimasto con JoinOrder più basso (lotto-b-brief.md:
            // "un bot non può mai diventare host").
            var host = state.HostId == e.PlayerId
                ? HostPiuAnzianoTraIConnessi(rimasti)
                : state.HostId;

            var senza = state with { Players = rimasti, HostId = host };

            return new EngineResult(senza, [new BroadcastToRoom(RoomState(senza))]);
        }

        // A partita iniziata il posto resta occupato: N è fissato (spec §2.2).
        // Il giocatore viene marcato disconnesso e da qui in poi gioca il bot.
        if (!uscente.IsConnected)
        {
            return EngineResult.NoChange(state);
        }

        var giocatori = state.Players
            .Select(p => p.Id == e.PlayerId ? p with { IsConnected = false } : p)
            .ToList();

        var nuovoHost = state.HostId == e.PlayerId
            ? giocatori.Where(p => p.IsConnected).OrderBy(p => p.JoinOrder).FirstOrDefault()?.Id ?? state.HostId
            : state.HostId;

        var aggiornato = state with { Players = giocatori, HostId = nuovoHost };

        List<Effect> effetti = [new BroadcastToRoom(RoomState(aggiornato))];

        if (aggiornato.Phase == RoomPhase.Writing)
        {
            return FillDisconnected(aggiornato, effetti);
        }

        // Chi se ne va esce dai votanti attesi: se era l'ultimo che mancava,
        // il voto va chiuso adesso. Rimandarlo al prossimo voto significa non
        // chiuderlo mai, perché non ne arriverà nessuno (spec §5).
        if (aggiornato.Phase == RoomPhase.Voting && TuttiHannoVotato(aggiornato))
        {
            return ChiudiVoto(aggiornato, effetti);
        }

        return new EngineResult(aggiornato, effetti);
    }

    private static EngineResult OnBotAdded(GameState state, BotAdded e)
    {
        if (state.Phase != RoomPhase.Lobby)
        {
            return Error(state, e.RequestedBy, "NOT_LOBBY", "I bot si aggiungono solo in lobby.");
        }

        if (state.HostId != e.RequestedBy)
        {
            return Error(state, e.RequestedBy, "NOT_HOST", "Solo chi ha creato la stanza può aggiungere un bot.");
        }

        if (state.Players.Count >= MaxPlayers)
        {
            return Error(state, e.RequestedBy, "ROOM_FULL", $"La stanza ospita al massimo {MaxPlayers} giocatori.");
        }

        var nome = NextBotName(state);
        if (nome is null)
        {
            // Non è "irraggiungibile": NextBotName confronta con i nickname di
            // TUTTI i giocatori, umani inclusi. Con meno di MaxPlayers persone
            // già in stanza (quindi ROOM_FULL non è scattato) ma otto di loro
            // con un nickname che combacia esattamente con la lista dei nomi
            // bot, la lista si esaurisce comunque. Improbabile, ma un errore
            // pulito verso il client è comunque dovuto invece di un'eccezione
            // non gestita che risalirebbe fuori da Handle.
            return Error(state, e.RequestedBy, "NO_BOT_NAMES_LEFT",
                "Nessun nome disponibile per il bot: troppi giocatori hanno già un nome della lista.");
        }

        var bot = new Player(e.BotId, nome, IsBot: true, state.NextJoinOrder, IsConnected: false);

        var nuovo = state with
        {
            Players = [.. state.Players, bot],
            NextJoinOrder = state.NextJoinOrder + 1,
        };

        return new EngineResult(nuovo, [new BroadcastToRoom(RoomState(nuovo))]);
    }

    private static EngineResult OnBotRemoved(GameState state, BotRemoved e)
    {
        if (state.Phase != RoomPhase.Lobby)
        {
            return Error(state, e.RequestedBy, "NOT_LOBBY", "I bot si rimuovono solo in lobby.");
        }

        if (state.HostId != e.RequestedBy)
        {
            return Error(state, e.RequestedBy, "NOT_HOST", "Solo chi ha creato la stanza può rimuovere un bot.");
        }

        var bersaglio = state.FindPlayer(e.BotId);
        if (bersaglio is null)
        {
            return Error(state, e.RequestedBy, "NO_SUCH_PLAYER", "Questo giocatore non esiste più.");
        }

        if (!bersaglio.IsBot)
        {
            return Error(state, e.RequestedBy, "NOT_A_BOT", "Solo i bot si possono rimuovere così.");
        }

        var nuovo = state with { Players = [.. state.Players.Where(p => p.Id != e.BotId)] };

        return new EngineResult(nuovo, [new BroadcastToRoom(RoomState(nuovo))]);
    }

    private static EngineResult OnBotRenamed(GameState state, BotRenamed e)
    {
        if (state.Phase != RoomPhase.Lobby)
        {
            return Error(state, e.RequestedBy, "NOT_LOBBY", "I bot si rinominano solo in lobby.");
        }

        if (state.HostId != e.RequestedBy)
        {
            return Error(state, e.RequestedBy, "NOT_HOST", "Solo chi ha creato la stanza può rinominare un bot.");
        }

        var bersaglio = state.FindPlayer(e.BotId);
        if (bersaglio is null)
        {
            return Error(state, e.RequestedBy, "NO_SUCH_PLAYER", "Questo giocatore non esiste più.");
        }

        if (!bersaglio.IsBot)
        {
            return Error(state, e.RequestedBy, "NOT_A_BOT", "Solo i bot si possono rinominare così.");
        }

        // Stesso validatore del client: server e client non possono divergere.
        var esito = NicknameValidator.Validate(e.Nickname);
        if (!esito.IsValid)
        {
            return Error(state, e.RequestedBy, "INVALID_NICKNAME", esito.Error!);
        }

        // Innocuo per il motore (gli id restano la chiave), ma vanificherebbe
        // la lista di nomi senza collisioni e confonderebbe la lista in
        // lobby: due righe con lo stesso nome sembrerebbero un bug.
        if (state.Players.Any(p => p.Id != e.BotId && p.Nickname == esito.Normalized))
        {
            return Error(state, e.RequestedBy, "INVALID_NICKNAME", "Questo nome è già usato da un altro giocatore.");
        }

        var giocatori = state.Players
            .Select(p => p.Id == e.BotId ? p with { Nickname = esito.Normalized } : p)
            .ToList();

        var nuovo = state with { Players = giocatori };

        return new EngineResult(nuovo, [new BroadcastToRoom(RoomState(nuovo))]);
    }

    /// <summary>
    /// Primo nome libero della lista fissa: nessuna casualità, nessuna
    /// collisione. Null se sono tutti già in uso (vedi il chiamante).
    /// </summary>
    private static string? NextBotName(GameState state) =>
        BotNames.FirstOrDefault(nome => state.Players.All(p => p.Nickname != nome));
}
