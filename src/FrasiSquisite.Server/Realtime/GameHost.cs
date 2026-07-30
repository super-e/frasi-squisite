using System.Collections.Concurrent;
using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Server.Rooms;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace FrasiSquisite.Server.Realtime;

/// <summary>
/// Esegue gli effetti prodotti dal motore. È l'unico punto del server che
/// conosce sia il dominio sia SignalR: il motore resta ignaro della rete
/// (spec §3.2).
/// </summary>
public sealed class GameHost(
    IGameEngine engine,
    IRoomRegistry rooms,
    IHubContext<GameHub> hub,
    ILogger<GameHost> logger)
{
    /// <summary>
    /// Un lucchetto per codice stanza. <b>Deve restare un campo d'istanza, non
    /// statico:</b> protegge lo stato tenuto da <see cref="IRoomRegistry"/>,
    /// che vive nel container di dipendenze, quindi deve avere lo stesso
    /// ambito. Da <c>static</c> la tabella sopravviveva al container e veniva
    /// condivisa fra host diversi nello stesso processo — in produzione senza
    /// conseguenze, perché <c>GameHost</c> è registrato come singleton e di
    /// host ce n'è uno solo, ma nei test due host indipendenti che pescano lo
    /// stesso codice stanza finivano per serializzarsi a vicenda pur non
    /// avendo nulla in comune.
    /// </summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Serializza gli eventi per stanza: due invii simultanei leggerebbero lo
    /// stesso stato e si sovrascriverebbero a vicenda.
    /// </summary>
    /// <exception cref="HubException">
    /// La stanza non esiste (es. persa per un riavvio del server): senza
    /// questo segnale il chiamante crederebbe che il comando sia andato a
    /// buon fine e la partita si bloccherebbe in silenzio.
    /// </exception>
    public async Task DispatchAsync(string roomCode, GameEvent evt, CancellationToken ct = default)
    {
        var gate = _locks.GetOrAdd(roomCode, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(ct);
        try
        {
            if (!rooms.TryGet(roomCode, out var stato))
            {
                throw new HubException("Stanza non trovata.");
            }

            var risultato = engine.Handle(stato, evt);
            rooms.Set(roomCode, risultato.State);

            foreach (var effetto in risultato.Effects)
            {
                try
                {
                    await EseguiAsync(roomCode, effetto, ct);
                }
                catch (Exception ex)
                {
                    // Non viene inghiottita: il chiamante deve comunque vedere il
                    // fallimento, ma senza questo log resterebbe invisibile sul
                    // percorso di disconnessione, dove non c'è alcun client a cui
                    // segnalarlo (spec §3.2).
                    logger.LogError(
                        ex,
                        "Invio effetto fallito per la stanza {RoomCode}, tipo effetto {EffectType}.",
                        roomCode,
                        effetto.GetType().Name);
                    throw;
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private Task EseguiAsync(string roomCode, Effect effetto, CancellationToken ct) => effetto switch
    {
        BroadcastToRoom b => hub.Clients.Group(roomCode)
            .SendAsync("ReceiveMessage", b.Message.GetType().Name, b.Message, ct),

        SendToPlayer s => hub.Clients.Group(PlayerGroup(s.PlayerId))
            .SendAsync("ReceiveMessage", s.Message.GetType().Name, s.Message, ct),

        _ => throw new InvalidOperationException($"Effetto non gestito: {effetto.GetType().Name}"),
    };

    public static string PlayerGroup(Guid playerId) => $"player:{playerId}";
}
