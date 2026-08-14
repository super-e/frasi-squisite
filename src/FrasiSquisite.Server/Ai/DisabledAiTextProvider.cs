namespace FrasiSquisite.Server.Ai;

/// <summary>
/// Il degrado come implementazione e non come <c>if</c> (spec §5 del design
/// generale). Quando la chiave non c'e', il container risolve questo: il
/// resto del codice non sa nemmeno che l'AI e' spenta.
/// </summary>
public sealed class DisabledAiTextProvider : IAiTextProvider
{
    public Task<string?> CompletaAsync(string sistema, string utente, CancellationToken ct, int maxTokens) =>
        Task.FromResult<string?>(null);
}
