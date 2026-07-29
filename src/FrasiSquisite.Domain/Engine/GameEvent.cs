namespace FrasiSquisite.Domain.Engine;

public abstract record GameEvent;

public sealed record PlayerJoined(Guid PlayerId, string Nickname) : GameEvent;

public sealed record PlayerLeft(Guid PlayerId) : GameEvent;

public sealed record GameStartRequested(Guid RequestedBy) : GameEvent;

public sealed record SlotSubmitted(Guid PlayerId, string Text) : GameEvent;

public sealed record RevealAdvanceRequested(Guid RequestedBy) : GameEvent;
