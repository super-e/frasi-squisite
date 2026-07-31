using FrasiSquisite.Domain.Model;
using FrasiSquisite.Shared.Protocol;

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

        var testi = frase.Slots.Take(scoperte).Select(s => s!.Text).ToList();

        // Gli autori compaiono solo a frase completa (spec §2.4).
        var autori = completa
            ? frase.Slots.Select(s => state.FindPlayer(s!.AuthorId)?.Nickname ?? "?").ToList()
            : [];

        var passo = new RevealStepMessage(
            state.RevealPhraseIndex,
            state.Phrases.Count,
            testi,
            completa,
            autori);

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

        var finito = state with { Phase = RoomPhase.Finished };

        var frasiComposte = finito.Phrases
            .Select(f => finito.Schema.Compose([.. f.Slots.Select(s => s!.Text)]))
            .ToList();

        return new EngineResult(finito, [
            new BroadcastToRoom(passo),
            new BroadcastToRoom(new GameFinishedMessage(frasiComposte)),
        ]);
    }
}
