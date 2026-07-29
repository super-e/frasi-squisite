using System.Collections.Concurrent;
using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Server.Rooms;
using Microsoft.AspNetCore.SignalR;

namespace FrasiSquisite.Server.Realtime;

/// <summary>
/// Esegue gli effetti prodotti dal motore. È l'unico punto del server che
/// conosce sia il dominio sia SignalR: il motore resta ignaro della rete
/// (spec §3.2).
/// </summary>
public sealed class GameHost(
    IGameEngine engine,
    IRoomRegistry rooms,
    IHubContext<GameHub> hub)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Serializza gli eventi per stanza: due invii simultanei leggerebbero lo
    /// stesso stato e si sovrascriverebbero a vicenda.
    /// </summary>
    public async Task DispatchAsync(string roomCode, GameEvent evt, CancellationToken ct = default)
    {
        var gate = Locks.GetOrAdd(roomCode, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(ct);
        try
        {
            if (!rooms.TryGet(roomCode, out var stato))
            {
                return;
            }

            var risultato = engine.Handle(stato, evt);
            rooms.Set(roomCode, risultato.State);

            foreach (var effetto in risultato.Effects)
            {
                await EseguiAsync(roomCode, effetto, ct);
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
