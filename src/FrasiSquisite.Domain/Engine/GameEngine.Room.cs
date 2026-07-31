using FrasiSquisite.Domain.Model;
using FrasiSquisite.Shared.Protocol;

namespace FrasiSquisite.Domain.Engine;

/// <summary>Ciclo di vita della stanza: schema, nuova partita, ritorno in lobby.</summary>
public sealed partial class GameEngine
{
    /// <summary>
    /// "Nuova partita" (lotto-d-brief.md): riparte subito dalla schermata
    /// finale, senza passare per una lobby visibile. L'azzeramento è quello
    /// condiviso con <see cref="OnBackToLobbyRequested"/>; l'avvio vero e
    /// proprio riusa <see cref="StartGame"/>, la stessa strada di
    /// <see cref="OnGameStartRequested"/> - compreso il riempimento dei non
    /// connessi al round 0 che fa funzionare i bot.
    /// </summary>
    private EngineResult OnNewGameRequested(GameState state, NewGameRequested e)
    {
        if (state.Phase != RoomPhase.Finished)
        {
            return Error(state, e.RequestedBy, "NOT_FINISHED", "Si può ricominciare solo dalla schermata finale.");
        }

        if (state.HostId != e.RequestedBy)
        {
            return Error(state, e.RequestedBy, "NOT_HOST", "Solo chi ha creato la stanza può ricominciare.");
        }

        var azzerato = AzzeraPerNuovaPartita(state);

        if (azzerato.Players.Count < MinPlayers)
        {
            // Il vicolo cieco non si sposta semplicemente da Finished a un
            // altro vicolo cieco: restare in Lobby è ciò che permette
            // all'host di rimediare aggiungendo un bot (brief del lotto). Il
            // broadcast tiene comunque tutti al passo con l'espulsione dei
            // disconnessi appena avvenuta.
            return new EngineResult(azzerato, [
                new BroadcastToRoom(RoomState(azzerato)),
                new SendToPlayer(e.RequestedBy, new ErrorMessage("TOO_FEW_PLAYERS", $"Servono almeno {MinPlayers} giocatori.")),
            ]);
        }

        return StartGame(azzerato);
    }

    /// <summary>
    /// "Torna alla lobby" (lotto-d-brief.md): stesso azzeramento di
    /// <see cref="OnNewGameRequested"/>, ma qui ci si ferma - nessun avvio.
    /// </summary>
    private static EngineResult OnBackToLobbyRequested(GameState state, BackToLobbyRequested e)
    {
        if (state.Phase != RoomPhase.Finished)
        {
            return Error(state, e.RequestedBy, "NOT_FINISHED", "Si può tornare alla lobby solo dalla schermata finale.");
        }

        if (state.HostId != e.RequestedBy)
        {
            return Error(state, e.RequestedBy, "NOT_HOST", "Solo chi ha creato la stanza può tornare alla lobby.");
        }

        var azzerato = AzzeraPerNuovaPartita(state);

        return new EngineResult(azzerato, [new BroadcastToRoom(RoomState(azzerato))]);
    }

    /// <summary>
    /// Azzeramento condiviso da <see cref="OnNewGameRequested"/> e
    /// <see cref="OnBackToLobbyRequested"/> (lotto-d-brief.md): gli umani
    /// disconnessi escono dalla stanza (hanno lasciato, non devono restare a
    /// farsi giocare da un bot all'infinito) mentre i bot e gli umani
    /// connessi restano; round, frasi, invii e indici di reveal tornano allo
    /// stato iniziale; RoomCode, Schema, AvailableSchemas e NextJoinOrder non
    /// cambiano. Se l'host uscente era fra i tolti il ruolo passa al
    /// connesso presente da più tempo - la stessa regola già in
    /// <see cref="OnPlayerLeft"/>, non una terza variante.
    /// </summary>
    private static GameState AzzeraPerNuovaPartita(GameState state)
    {
        var rimasti = state.Players.Where(p => p.IsConnected || p.IsBot).ToList();

        var host = rimasti.Any(p => p.Id == state.HostId)
            ? state.HostId
            : HostPiuAnzianoTraIConnessi(rimasti);

        return state with
        {
            Phase = RoomPhase.Lobby,
            HostId = host,
            Players = rimasti,
            Round = 0,
            Phrases = [],
            SubmittedThisRound = new HashSet<Guid>(),
            RevealPhraseIndex = 0,
            RevealSlotCount = 0,
        };
    }

    /// <summary>
    /// Sostituisce <see cref="GameState.Schema"/> con quello già risolto
    /// dall'hub. Solo in lobby (nessuna partita in corso, quindi niente frasi
    /// o round da azzerare) e solo per l'host: stesse guardie di
    /// <see cref="OnBotAdded"/>.
    /// </summary>
    private static EngineResult OnSchemaSelected(GameState state, SchemaSelected e)
    {
        if (state.Phase != RoomPhase.Lobby)
        {
            return Error(state, e.RequestedBy, "NOT_LOBBY", "Lo schema si cambia solo in lobby.");
        }

        if (state.HostId != e.RequestedBy)
        {
            return Error(state, e.RequestedBy, "NOT_HOST", "Solo chi ha creato la stanza può cambiare lo schema.");
        }

        var nuovo = state with { Schema = e.Schema };

        return new EngineResult(nuovo, [new BroadcastToRoom(RoomState(nuovo))]);
    }
}
