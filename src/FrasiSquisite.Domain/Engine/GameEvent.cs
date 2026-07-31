using FrasiSquisite.Shared.Schemas;

namespace FrasiSquisite.Domain.Engine;

public abstract record GameEvent;

public sealed record PlayerJoined(Guid PlayerId, string Nickname) : GameEvent;

public sealed record PlayerLeft(Guid PlayerId) : GameEvent;

public sealed record GameStartRequested(Guid RequestedBy) : GameEvent;

public sealed record SlotSubmitted(Guid PlayerId, string Text) : GameEvent;

public sealed record RevealAdvanceRequested(Guid RequestedBy) : GameEvent;

/// <summary>
/// Riparte subito dalla schermata finale, stessi giocatori e stesso schema,
/// dritti al primo round (lotto-d-brief.md). Valido solo in
/// <see cref="FrasiSquisite.Domain.Model.RoomPhase.Finished"/> e solo per
/// l'host.
/// </summary>
public sealed record NewGameRequested(Guid RequestedBy) : GameEvent;

/// <summary>
/// Torna alla lobby dalla schermata finale senza avviare nulla (lotto-d-brief.md).
/// Valido solo in <see cref="FrasiSquisite.Domain.Model.RoomPhase.Finished"/> e
/// solo per l'host.
/// </summary>
public sealed record BackToLobbyRequested(Guid RequestedBy) : GameEvent;

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

/// <summary>
/// Un voto per la frase all'indice indicato. Un voto a testa, non si cambia
/// (spec §2): il secondo tentativo dello stesso giocatore è un errore.
/// </summary>
public sealed record VoteCast(Guid PlayerId, int PhraseIndex) : GameEvent;

/// <summary>
/// L'host chiude il voto senza aspettare i ritardatari. Esiste perché il
/// timer di fase è fase 2: senza né timer né questo, un giocatore che posa il
/// telefono bloccherebbe la partita a tempo indefinito (spec §1).
/// </summary>
public sealed record VotingCloseRequested(Guid RequestedBy) : GameEvent;
