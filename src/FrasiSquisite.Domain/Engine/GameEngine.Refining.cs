using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Refinement;
using FrasiSquisite.Shared.Protocol;

namespace FrasiSquisite.Domain.Engine;

/// <summary>
/// La fase fra la scrittura e il reveal, in cui si aspetta che le caselle
/// tornino rifinite. Il motore non chiama nessuno: emette un effetto e
/// aspetta l'evento di ritorno (spec §3).
/// </summary>
public sealed partial class GameEngine
{
    /// <summary>
    /// Ingresso nella fase, chiamato quando l'ultima casella e' stata scritta.
    /// </summary>
    private static EngineResult EntraInRifinitura(GameState state, List<Effect> effetti)
    {
        var rifinendo = state with { Phase = RoomPhase.Refining };

        var frasi = rifinendo.Phrases
            .Select(f => (IReadOnlyList<string>)[.. f.Slots.Select(s => s!.Text)])
            .ToList();

        effetti.Add(new BroadcastToRoom(RoomState(rifinendo)));
        effetti.Add(new RequestRefinement(frasi, rifinendo.Schema.Template));

        return new EngineResult(rifinendo, effetti);
    }

    private static EngineResult OnRefinementFinished(GameState state, RefinementFinished e)
    {
        // Se la stanza e' andata avanti - nuova partita, ritorno in lobby -
        // applicare queste caselle sovrascriverebbe la partita nuova con i
        // resti di quella vecchia. Nessun errore verso il client: non l'ha
        // chiesto nessun giocatore, e' un evento interno del server.
        if (state.Phase != RoomPhase.Refining)
        {
            return EngineResult.NoChange(state);
        }

        var applicato = e.Frasi is null ? state : ApplicaRifinitura(state, e.Frasi);

        return AvviaReveal(applicato);
    }

    /// <summary>
    /// Sostituisce il testo delle caselle lasciando intatti gli autori: la
    /// rifinitura tocca cosa c'e' scritto, non chi l'ha scritto.
    /// </summary>
    private static GameState ApplicaRifinitura(
        GameState state,
        IReadOnlyList<IReadOnlyList<string>> rifinite)
    {
        if (rifinite.Count != state.Phrases.Count)
        {
            return state;
        }

        var frasi = state.Phrases.ToArray();

        for (var i = 0; i < frasi.Length; i++)
        {
            var grezze = frasi[i].Slots.Select(s => s!.Text).ToList();
            var accettate = RefinementGuard.Applica(grezze, rifinite[i], state.Schema.Template);

            var caselle = frasi[i].Slots.ToArray();
            for (var j = 0; j < caselle.Length; j++)
            {
                caselle[j] = caselle[j]! with { Text = accettate[j] };
            }

            frasi[i] = frasi[i] with { Slots = caselle };
        }

        return state with { Phrases = frasi };
    }

    /// <summary>
    /// Porta la stanza nel reveal. Chiamato solo dalla fine della rifinitura:
    /// prima ci si arrivava direttamente dalla fine dell'ultimo round.
    ///
    /// La RevealStepMessage iniziale - nessuna casella ancora scoperta, ma
    /// già col tessuto connettivo del template (backlog #1) - e' cio' che
    /// porta tutti sulla schermata di reveal: senza, ogni client resterebbe
    /// fermo dov'era, perche' non arriva piu' nessuna SlotRequestMessage e
    /// Screen non cambia mai da solo.
    /// </summary>
    private static EngineResult AvviaReveal(GameState state)
    {
        var reveal = state with
        {
            Phase = RoomPhase.Reveal,
            RevealPhraseIndex = 0,
            RevealSlotCount = 0,
        };

        var frammenti = FrammentiReveal(reveal.Schema, reveal.Phrases[0], scoperte: 0);

        return new EngineResult(reveal, [
            new BroadcastToRoom(RoomState(reveal)),
            new BroadcastToRoom(new RevealStepMessage(0, reveal.Phrases.Count, frammenti, false)),
        ]);
    }
}
