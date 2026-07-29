namespace FrasiSquisite.Domain.Engine;

/// <summary>
/// Il motore descrive cosa andrebbe fatto; è l'adapter nel server a farlo.
/// Un test può quindi asserire sui messaggi che <em>sarebbero</em> stati
/// inviati, senza mockare nulla di rete (spec §3.2).
/// </summary>
public abstract record Effect;

public sealed record SendToPlayer(Guid PlayerId, object Message) : Effect;

public sealed record BroadcastToRoom(object Message) : Effect;
