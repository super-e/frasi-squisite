namespace FrasiSquisite.Domain.Engine;

/// <summary>
/// Il motore descrive cosa andrebbe fatto; è l'adapter nel server a farlo.
/// Un test può quindi asserire sui messaggi che <em>sarebbero</em> stati
/// inviati, senza mockare nulla di rete (spec §3.2).
/// </summary>
public abstract record Effect;

public sealed record SendToPlayer(Guid PlayerId, object Message) : Effect;

public sealed record BroadcastToRoom(object Message) : Effect;

/// <summary>
/// Chiede che le caselle vengano rifinite. Porta i dati e nient'altro: il
/// motore non sa se dietro ci sia un modello, un dizionario o niente
/// (spec §3).
/// </summary>
public sealed record RequestRefinement(
    IReadOnlyList<IReadOnlyList<string>> Frasi,
    string Template,
    IReadOnlyList<string> Ruoli) : Effect;

/// <summary>
/// Chiede l'illustrazione di una frase. Porta la frase composta e nient'altro:
/// il motore non sa se dietro ci sia un modello, una cache o niente.
/// </summary>
public sealed record RequestIllustration(int PhraseIndex, string Frase) : Effect;
