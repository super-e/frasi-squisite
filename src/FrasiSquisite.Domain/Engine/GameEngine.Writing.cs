using FrasiSquisite.Domain.Model;
using FrasiSquisite.Shared.Protocol;
using FrasiSquisite.Shared.Validation;

namespace FrasiSquisite.Domain.Engine;

/// <summary>Dalla partenza alla fine dell'ultimo round.</summary>
public sealed partial class GameEngine
{
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

        // Punto 1 del brief: un bot presente al kick-off non è mai connesso,
        // e senza questo richiamo nessuno riempirebbe la sua casella del
        // round 0 - il round non si completerebbe mai e la partita
        // resterebbe bloccata al primo turno. AdvanceRound fa già lo stesso
        // controllo per i round successivi; StartGame deve farlo per il
        // round 0 esattamente allo stesso modo.
        if (nuovo.Players.Any(p => !p.IsConnected))
        {
            return FillDisconnected(nuovo, effetti);
        }

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

        var nuovo = ApplySlot(state, e.PlayerId, esito.Normalized);

        if (nuovo.SubmittedThisRound.Count < nuovo.Players.Count)
        {
            return new EngineResult(nuovo, [
                new BroadcastToRoom(new RoundProgressMessage(
                    nuovo.Round, nuovo.SubmittedThisRound.Count, nuovo.Players.Count)),
            ]);
        }

        return AdvanceRound(nuovo);
    }

    private GameState ApplySlot(GameState state, Guid playerId, string testoNormalizzato)
    {
        var indice = state.IndexOfPlayer(playerId);
        var assegnazione = _mode.AssignSlot(state.Round, indice, state.Players.Count, state.Schema);

        var frasi = state.Phrases.ToArray();
        frasi[assegnazione.PhraseIndex] = frasi[assegnazione.PhraseIndex]
            .With(assegnazione.SlotIndex, new Slot(playerId, testoNormalizzato));

        return state with
        {
            Phrases = frasi,
            SubmittedThisRound = new HashSet<Guid>(state.SubmittedThisRound) { playerId },
        };
    }

    /// <summary>
    /// Riempie con il bot la casella di ogni giocatore disconnesso che non ha
    /// ancora inviato in questo round, e fa avanzare il round se con questo
    /// tutti hanno una casella. Nessuno resta mai in attesa di chi non c'è.
    /// </summary>
    private EngineResult FillDisconnected(GameState state, List<Effect> effetti)
    {
        var corrente = state;

        foreach (var giocatore in state.Players.Where(p => !p.IsConnected))
        {
            if (corrente.SubmittedThisRound.Contains(giocatore.Id))
            {
                continue;
            }

            corrente = ApplySlot(corrente, giocatore.Id, BotWord(corrente, giocatore.Id));
        }

        if (corrente.SubmittedThisRound.Count < corrente.Players.Count)
        {
            effetti.Add(new BroadcastToRoom(new RoundProgressMessage(
                corrente.Round, corrente.SubmittedThisRound.Count, corrente.Players.Count)));

            return new EngineResult(corrente, effetti);
        }

        var avanzato = AdvanceRound(corrente);

        return new EngineResult(avanzato.State, [.. effetti, .. avanzato.Effects]);
    }

    private string BotWord(GameState state, Guid playerId)
    {
        var indice = state.IndexOfPlayer(playerId);
        var assegnazione = _mode.AssignSlot(state.Round, indice, state.Players.Count, state.Schema);

        return _pool.Take(state.Schema.Caselle[assegnazione.SlotIndex].Ruolo, _random);
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

            // Nessuna SlotRequestMessage arriva più: senza altri messaggi ogni
            // client resterebbe fermo sulla schermata di attesa (Screen non
            // cambia mai da solo). Il RoundProgressMessage saturo evita che
            // l'attesa resti bloccata a N-1/N, e la RevealStepMessage iniziale
            // - vuota, nessuna casella ancora scoperta - è ciò che porta tutti
            // sulla schermata di reveal: solo l'host può poi far avanzare lo
            // scoprimento vero e proprio (RevealAdvanceRequested).
            return new EngineResult(reveal, [
                new BroadcastToRoom(RoomState(reveal)),
                new BroadcastToRoom(new RoundProgressMessage(state.Round, state.Players.Count, state.Players.Count)),
                new BroadcastToRoom(new RevealStepMessage(0, reveal.Phrases.Count, [], false, [])),
            ]);
        }

        List<Effect> effetti = [
            new BroadcastToRoom(new RoundProgressMessage(state.Round, state.Players.Count, state.Players.Count)),
        ];
        effetti.AddRange(SlotRequests(prossimo));

        if (prossimo.Players.Any(p => !p.IsConnected))
        {
            return FillDisconnected(prossimo, effetti);
        }

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
}
