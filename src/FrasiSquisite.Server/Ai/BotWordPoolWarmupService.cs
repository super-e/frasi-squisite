using FrasiSquisite.Shared.Schemas;

namespace FrasiSquisite.Server.Ai;

/// <summary>
/// Riempie CachedAiWordPool all'avvio, uno schema alla volta: una chiamata
/// per schema, sei schemi in tutto (spec AI §6). Se il modello non
/// risponde, riprova ogni 30 minuti finché la cache non è piena per tutti
/// — non è un'ottimizzazione: senza, un server acceso durante un
/// disservizio AI resterebbe sul dizionario statico per sempre senza che
/// nessuno se ne accorga.
/// </summary>
public sealed class BotWordPoolWarmupService(
    ISchemaCatalog catalogo,
    BotWordPoolRunner runner,
    CachedAiWordPool cache,
    ILogger<BotWordPoolWarmupService> logger) : BackgroundService
{
    private static readonly TimeSpan IntervalloRitentativo = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var daRiempire = new HashSet<string>(catalogo.All.Select(s => s.Id), StringComparer.Ordinal);

        while (daRiempire.Count > 0 && !stoppingToken.IsCancellationRequested)
        {
            daRiempire = await EseguiUnGiroAsync(daRiempire, stoppingToken);

            if (daRiempire.Count > 0)
            {
                logger.LogWarning(
                    "Cache bot non completa per {Schemi} schemi: nuovo tentativo tra {Minuti} minuti.",
                    daRiempire.Count, IntervalloRitentativo.TotalMinutes);

                await Task.Delay(IntervalloRitentativo, stoppingToken);
            }
        }
    }

    /// <summary>
    /// Un giro: prova a generare le parole per ogni schema ancora da
    /// riempire, popola la cache per quelli che rispondono, restituisce
    /// l'insieme di quelli rimasti. Pubblico e a parte da ExecuteAsync
    /// apposta per essere testabile senza guidare l'intero ciclo di vita
    /// di un BackgroundService.
    /// </summary>
    public async Task<HashSet<string>> EseguiUnGiroAsync(HashSet<string> daRiempire, CancellationToken ct)
    {
        var restano = new HashSet<string>(daRiempire, StringComparer.Ordinal);

        foreach (var schema in catalogo.All.Where(s => daRiempire.Contains(s.Id)))
        {
            var esito = await runner.GeneraAsync(schema, ct);

            if (esito is null)
            {
                continue;
            }

            foreach (var (ruolo, parole) in esito)
            {
                cache.Popola(ruolo, parole);
            }

            restano.Remove(schema.Id);
        }

        return restano;
    }
}
