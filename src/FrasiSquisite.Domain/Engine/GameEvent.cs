using FrasiSquisite.Shared.Schemas;

namespace FrasiSquisite.Domain.Engine;

public abstract record GameEvent;

public sealed record PlayerJoined(Guid PlayerId, string Nickname) : GameEvent;

public sealed record PlayerLeft(Guid PlayerId) : GameEvent;

public sealed record GameStartRequested(Guid RequestedBy) : GameEvent;

public sealed record SlotSubmitted(Guid PlayerId, string Text) : GameEvent;

public sealed record RevealAdvanceRequested(Guid RequestedBy) : GameEvent;

/// <summary>
/// Aggiunge un bot alla lobby. Porta solo l'id (lo genera l'hub, mai il
/// motore: niente GUID nondeterministici in Domain) e il richiedente, da
/// verificare come host; il nickname lo sceglie il motore stesso, dalla
/// prima voce libera di una lista fissa (lotto-b-brief.md, punto 2).
/// </summary>
public sealed record BotAdded(Guid BotId, Guid RequestedBy) : GameEvent;

public sealed record BotRemoved(Guid BotId, Guid RequestedBy) : GameEvent;

public sealed record BotRenamed(Guid BotId, string Nickname, Guid RequestedBy) : GameEvent;

/// <summary>
/// Cambia lo schema della stanza in lobby. Porta lo <see cref="Schema"/> già
/// risolto, non un id: il motore non conosce il catalogo (niente
/// <c>ISchemaCatalog</c> qui dentro, spec del lotto), quindi è l'hub a
/// risolvere l'identificativo prima di generare questo evento — esattamente
/// come già fa generando l'id dei bot in <see cref="BotAdded"/>.
/// </summary>
public sealed record SchemaSelected(Schema Schema, Guid RequestedBy) : GameEvent;
