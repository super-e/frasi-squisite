using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Voting;
using FrasiSquisite.Shared.Protocol;
using FrasiSquisite.Shared.Schemas;

namespace FrasiSquisite.Domain.Engine;

/// <summary>Scoprimento casella per casella, guidato dall'host.</summary>
public sealed partial class GameEngine
{
    private static EngineResult OnRevealAdvance(GameState state, RevealAdvanceRequested e)
    {
        if (state.Phase != RoomPhase.Reveal)
        {
            return Error(state, e.RequestedBy, "NOT_REVEALING", "Non è il momento del reveal.");
        }

        if (state.HostId != e.RequestedBy)
        {
            return Error(state, e.RequestedBy, "NOT_HOST", "Solo chi ha creato la stanza fa avanzare il reveal.");
        }

        var scoperte = state.RevealSlotCount + 1;
        var frase = state.Phrases[state.RevealPhraseIndex];
        var completa = scoperte >= frase.Slots.Count;

        var passo = new RevealStepMessage(
            state.RevealPhraseIndex,
            state.Phrases.Count,
            FrammentiReveal(state.Schema, frase, scoperte),
            completa);

        if (!completa)
        {
            return new EngineResult(
                state with { RevealSlotCount = scoperte },
                [new BroadcastToRoom(passo)]);
        }

        var prossimaFrase = state.RevealPhraseIndex + 1;

        if (prossimaFrase < state.Phrases.Count)
        {
            return new EngineResult(
                state with { RevealPhraseIndex = prossimaFrase, RevealSlotCount = 0 },
                [new BroadcastToRoom(passo)]);
        }

        return EntraInVoto(state, [new BroadcastToRoom(passo)]);
    }

    /// <summary>
    /// Il tessuto connettivo del template intercalato alle caselle scoperte
    /// (backlog #1): è come si legge la pagina del voto, dove
    /// <see cref="Schema.Compose"/> fa lo stesso lavoro in un colpo solo.
    /// Le caselle non ancora scoperte arrivano comunque (per punteggiatura e
    /// posizione corrette lato client) ma senza testo — il voto che segue è
    /// cieco, e questo è il punto che non deve mai regredire.
    /// </summary>
    private static IReadOnlyList<RevealFragment> FrammentiReveal(Schema schema, Phrase frase, int scoperte) =>
        [.. schema.Segments.Select(s => s.IsSlot
            ? new RevealFragment(true, s.SlotIndex < scoperte ? frase.Slots[s.SlotIndex]!.Text : string.Empty, s.SlotIndex < scoperte)
            : new RevealFragment(false, s.Literal, true))];

    /// <summary>Le frasi composte secondo il template dello schema.</summary>
    private static IReadOnlyList<string> FrasiComposte(GameState state) =>
        [.. state.Phrases.Select(f => state.Schema.Compose([.. f.Slots.Select(s => s!.Text)]))];

    /// <summary>
    /// La classifica pronta da mandare. Con nessun voto — il voto chiuso
    /// prima che qualcuno esprimesse una preferenza — produce tutte le frasi
    /// a zero e nessuna vincitrice, che è esattamente il significato giusto.
    /// </summary>
    private static IReadOnlyList<PhraseResultView> Classifica(
        GameState state,
        IReadOnlyDictionary<Guid, int> voti)
    {
        var frasi = FrasiComposte(state);

        return [.. VoteTally.From(voti, state.Phrases.Count).Ranking
            .Select(r => new PhraseResultView(
                r.PhraseIndex,
                frasi[r.PhraseIndex],
                // Chi ha scritto la frase, non chi ha riempito ogni casella:
                // con otto caselle e due giocatori l'elenco per casella
                // ripeteva ogni nome quattro volte, e l'ordine posizionale
                // diceva pure quale casella fosse di chi — che il voto cieco
                // non prevede di rivelare. La deduplica e' sull'identita' e
                // non sul nome: due omonimi restano due persone.
                [.. state.Phrases[r.PhraseIndex].Slots
                    .Select(s => s!.AuthorId)
                    .Distinct()
                    .Select(id => state.FindPlayer(id)?.Nickname ?? "?")],
                r.Votes,
                r.IsWinner))];
    }
}
