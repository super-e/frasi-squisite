using System.Collections.Concurrent;
using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Randomness;

namespace FrasiSquisite.Server.Ai;

/// <summary>
/// Seconda implementazione di IWordPool (spec AI §6): una cache popolata in
/// sottofondo da BotWordPoolWarmupService, con fallback su StaticWordPool
/// quando un ruolo non è ancora (o non è mai stato) messo in cache. Take
/// resta sincrono: il motore lo chiama così, e non sa nulla dell'AI dietro.
/// </summary>
public sealed class CachedAiWordPool(StaticWordPool fallback) : IWordPool
{
    private readonly ConcurrentDictionary<string, string[]> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Chiamato solo da BotWordPoolWarmupService. Ignora liste vuote: un
    /// ruolo senza parole valide resta al dizionario statico, non a una
    /// voce di cache vuota che farebbe esplodere Take.
    /// </summary>
    public void Popola(string ruolo, IReadOnlyList<string> parole)
    {
        if (parole.Count > 0)
        {
            _cache[ruolo] = [.. parole];
        }
    }

    public string Take(string ruolo, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);

        return _cache.TryGetValue(ruolo, out var parole)
            ? parole[random.Next(parole.Length)]
            : fallback.Take(ruolo, random);
    }
}
