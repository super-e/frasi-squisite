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
        result.Effects.Select(e => e switch
        {
            SendToPlayer s => s.Message,
            BroadcastToRoom b => b.Message,
            _ => throw new InvalidOperationException($"Effetto non gestito: {e.GetType().Name}"),
        });
}
