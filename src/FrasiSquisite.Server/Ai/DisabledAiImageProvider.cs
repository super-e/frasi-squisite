namespace FrasiSquisite.Server.Ai;

/// <summary>
/// Il degrado come implementazione e non come <c>if</c>: senza chiave si
/// registra questo, e nessun altro file sa che l'AI è spenta.
/// </summary>
public sealed class DisabledAiImageProvider : IAiImageProvider
{
    public Task<byte[]?> GeneraAsync(string promptInglese, CancellationToken ct) =>
        Task.FromResult<byte[]?>(null);
}
