using FrasiSquisite.Domain.Model;
using FrasiSquisite.Shared.Protocol;

namespace FrasiSquisite.Domain.Engine;

/// <summary>
/// Fase di voto: un voto a testa, cieco, chiuso da chi vota o dall'host.
/// </summary>
public sealed partial class GameEngine
{
    private static EngineResult OnVoteCast(GameState state, VoteCast e)
    {
        if (state.Phase != RoomPhase.Voting)
        {
            return Error(state, e.PlayerId, "NOT_VOTING", "Non è il momento di votare.");
        }

        var votante = state.FindPlayer(e.PlayerId);
        if (votante is null)
        {
            return Error(state, e.PlayerId, "NOT_IN_ROOM", "Non sei in questa stanza.");
        }

        // Irraggiungibile dall'hub — un bot non ha connessione e un
        // disconnesso nemmeno — ma è l'invariante su cui poggia il conteggio:
        // se un giorno un chiamante diverso generasse questo evento, il
        // motore non deve accettarlo in silenzio.
        if (votante.IsBot || !votante.IsConnected)
        {
            return Error(state, e.PlayerId, "CANNOT_VOTE", "Solo chi sta giocando può votare.");
        }

        if (state.Votes.ContainsKey(e.PlayerId))
        {
            return Error(state, e.PlayerId, "ALREADY_VOTED", "Hai già votato.");
        }

        if (e.PhraseIndex < 0 || e.PhraseIndex >= state.Phrases.Count)
        {
            return Error(state, e.PlayerId, "NO_SUCH_PHRASE", "Questa frase non esiste.");
        }

        var nuovo = state with
        {
            Votes = new Dictionary<Guid, int>(state.Votes) { [e.PlayerId] = e.PhraseIndex },
        };

        if (TuttiHannoVotato(nuovo))
        {
            return ChiudiVoto(nuovo, []);
        }

        var (votanti, attesi) = Avanzamento(nuovo);

        return new EngineResult(nuovo, [new BroadcastToRoom(new VoteProgressMessage(votanti, attesi))]);
    }

    private static EngineResult OnVotingCloseRequested(GameState state, VotingCloseRequested e)
    {
        if (state.Phase != RoomPhase.Voting)
        {
            return Error(state, e.RequestedBy, "NOT_VOTING", "Non è il momento di votare.");
        }

        if (state.HostId != e.RequestedBy)
        {
            return Error(state, e.RequestedBy, "NOT_HOST", "Solo chi ha creato la stanza può chiudere il voto.");
        }

        return ChiudiVoto(state, []);
    }

    /// <summary>
    /// Chi ci si aspetta che voti: umani connessi. I bot non votano per
    /// scelta (design generale §8.3, "il vincitore lo decidono le persone"),
    /// i disconnessi perché non c'è nessuno dietro lo schermo.
    /// </summary>
    private static IReadOnlyList<Player> VotantiAttesi(GameState state) =>
        [.. state.Players.Where(p => !p.IsBot && p.IsConnected)];

    /// <summary>
    /// Vero anche con l'insieme vuoto, per vacuità — ed è la risposta giusta:
    /// se non resta nessuno da aspettare, aspettare all'infinito sarebbe il
    /// difetto (spec §4).
    /// </summary>
    private static bool TuttiHannoVotato(GameState state) =>
        VotantiAttesi(state).All(p => state.Votes.ContainsKey(p.Id));

    /// <summary>
    /// Votanti e attesi. Non si usa <c>Votes.Count</c> come numeratore: chi
    /// vota e poi si disconnette resta nella mappa ma esce dagli attesi, e
    /// il conteggio direbbe "1 di 0".
    /// </summary>
    private static (int Votanti, int Attesi) Avanzamento(GameState state)
    {
        var attesi = VotantiAttesi(state);

        return (attesi.Count(p => state.Votes.ContainsKey(p.Id)), attesi.Count);
    }

    /// <summary>
    /// Ingresso nella fase, chiamato dall'ultimo passo di reveal. La chiusura
    /// va valutata <em>subito</em>: se non c'è nessun votante atteso, nessun
    /// evento successivo arriverebbe mai a rivalutarla e la stanza resterebbe
    /// appesa (spec §4).
    /// </summary>
    private static EngineResult EntraInVoto(GameState state, List<Effect> effetti)
    {
        var voto = state with
        {
            Phase = RoomPhase.Voting,
            Votes = new Dictionary<Guid, int>(),
        };

        effetti.Add(new BroadcastToRoom(RoomState(voto)));
        effetti.Add(new BroadcastToRoom(new VoteRequestMessage(FrasiComposte(voto))));

        if (TuttiHannoVotato(voto))
        {
            return ChiudiVoto(voto, effetti);
        }

        return new EngineResult(voto, effetti);
    }

    /// <summary>
    /// Chiude e pubblica la classifica. Unico punto che porta a
    /// <see cref="RoomPhase.Finished"/>: le strade che ci arrivano
    /// (ultimo voto, host che forza la chiusura, disconnessione dell'ultimo
    /// votante atteso in <c>OnPlayerLeft</c>) devono produrre esattamente gli
    /// stessi messaggi.
    /// </summary>
    private static EngineResult ChiudiVoto(GameState state, List<Effect> effetti)
    {
        var finito = state with { Phase = RoomPhase.Finished };

        return new EngineResult(finito, [
            .. effetti,
            new BroadcastToRoom(RoomState(finito)),
            new BroadcastToRoom(new GameFinishedMessage(Classifica(finito, finito.Votes))),
        ]);
    }
}
