using FrasiSquisite.Domain.Engine;

namespace FrasiSquisite.Domain.Tests;

public static class EngineTestExtensions
{
    public static IEnumerable<T> Broadcasts<T>(this EngineResult result) =>
        result.Effects.OfType<BroadcastToRoom>().Select(e => e.Message).OfType<T>();

    public static IEnumerable<T> MessagesTo<T>(this EngineResult result, Guid playerId) =>
        result.Effects.OfType<SendToPlayer>()
            .Where(e => e.PlayerId == playerId)
            .Select(e => e.Message)
            .OfType<T>();

    public static IEnumerable<object> AllMessages(this EngineResult result) =>
        result.Effects.SelectMany(e => e switch
        {
            SendToPlayer s => (IEnumerable<object>)[s.Message],
            BroadcastToRoom b => [b.Message],
            // RequestRefinement non ha un messaggio per un client: è un
            // effetto interno fra motore e server, non passa mai per la
            // connessione (spec §3). Niente da aggiungere qui, ma resta
            // esplicito - il case di default sotto continua a bloccare gli
            // effetti davvero non previsti.
            RequestRefinement => [],
            _ => throw new InvalidOperationException($"Effetto non gestito: {e.GetType().Name}"),
        });
}
