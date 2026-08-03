namespace FrasiSquisite.Server.Ai;

/// <summary>
/// Testo dentro, testo fuori. <c>null</c> significa "non disponibile", per
/// qualunque motivo: chiave assente, rete giu', timeout, risposta illeggibile.
/// Chi chiama non deve distinguere i casi, perche' la reazione e' la stessa.
/// </summary>
public interface IAiTextProvider
{
    Task<string?> CompletaAsync(string sistema, string utente, CancellationToken ct);
}
