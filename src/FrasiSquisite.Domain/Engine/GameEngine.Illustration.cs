using FrasiSquisite.Domain.Model;
using FrasiSquisite.Shared.Protocol;

namespace FrasiSquisite.Domain.Engine;

/// <summary>
/// L'illustrazione su richiesta dell'host (spec §5). Come per la rifinitura il
/// motore non chiama nessuno: emette un effetto e aspetta l'evento di ritorno.
/// </summary>
public sealed partial class GameEngine
{
    private static EngineResult OnIllustrationRequested(GameState state, IllustrationRequested e)
    {
        if (state.Phase != RoomPhase.Finished)
        {
            return Error(state, e.RequestedBy, "NOT_FINISHED", "La partita non è ancora finita.");
        }

        if (state.HostId != e.RequestedBy)
        {
            return Error(state, e.RequestedBy, "NOT_HOST", "Solo chi ospita può chiedere l'illustrazione.");
        }

        if (e.PhraseIndex < 0 || e.PhraseIndex >= state.Phrases.Count)
        {
            return Error(state, e.RequestedBy, "NO_SUCH_PHRASE", "Quella frase non esiste.");
        }

        if (state.IllustrationsRequested.Contains(e.PhraseIndex))
        {
            return Error(state, e.RequestedBy, "ILLUSTRATION_ALREADY_REQUESTED", "Quella frase ce l'ha già.");
        }

        var chieste = new HashSet<int>(state.IllustrationsRequested) { e.PhraseIndex };
        var chiesto = state with { IllustrationsRequested = chieste };

        var frase = state.Schema.Compose([.. state.Phrases[e.PhraseIndex].Slots.Select(s => s!.Text)]);

        return new EngineResult(chiesto, [new RequestIllustration(e.PhraseIndex, frase)]);
    }

    private static EngineResult OnIllustrationFinished(GameState state, IllustrationFinished e)
    {
        // Stessa guardia della rifinitura: se la stanza e' ripartita, questo
        // esito appartiene a una partita che non c'e' piu'. Nessun errore verso
        // il client: non l'ha chiesto nessun giocatore, e' un evento interno.
        if (state.Phase != RoomPhase.Finished)
        {
            return EngineResult.NoChange(state);
        }

        // Per la rifinitura la fase basta da sola: si esce da Refining al primo
        // evento processato, quindi un secondo esito e' automaticamente fuori
        // fase. Qui no: si resta in Finished per sempre e le richieste sono
        // piu' d'una, concorrenti su indici diversi. Un esito duplicato o
        // tardivo per un indice che non e' (piu') in attesa va ignorato, non
        // ribroadcast: altrimenti un doppio evento per la stessa frase
        // manderebbe a tutta la stanza un pronta/fallita che non riguarda piu'
        // niente.
        if (!state.IllustrationsRequested.Contains(e.PhraseIndex))
        {
            return EngineResult.NoChange(state);
        }

        if (e.Path is not null)
        {
            return new EngineResult(state, [
                new BroadcastToRoom(new IllustrationReadyMessage(e.PhraseIndex, e.Path)),
            ]);
        }

        // Togliere l'indice e' cio' che riaccende il pulsante: senza, l'host
        // resterebbe con un'attesa che non finisce e nessun modo di riprovare.
        var chieste = new HashSet<int>(state.IllustrationsRequested);
        chieste.Remove(e.PhraseIndex);

        return new EngineResult(state with { IllustrationsRequested = chieste }, [
            new BroadcastToRoom(new IllustrationFailedMessage(
                e.PhraseIndex,
                "L'illustrazione non è arrivata. Si può riprovare.")),
        ]);
    }
}
