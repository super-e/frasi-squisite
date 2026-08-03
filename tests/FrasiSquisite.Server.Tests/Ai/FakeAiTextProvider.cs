using FrasiSquisite.Server.Ai;

namespace FrasiSquisite.Server.Tests.Ai;

/// <summary>
/// Provider in memoria: nessuna rete nei test. Permette di simulare la
/// risposta, il fallimento e la lentezza, che sono i tre casi che il gioco
/// deve saper reggere.
/// </summary>
public sealed class FakeAiTextProvider : IAiTextProvider
{
    public string? Risposta { get; set; }

    /// <summary>Se impostato, la chiamata attende questo prima di rispondere.</summary>
    public TimeSpan Ritardo { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Se impostata, la prossima chiamata la lancia invece di rispondere (e si
    /// azzera da sola): simula un guasto del modello - rete giù, errore HTTP,
    /// risposta malformata a livello di trasporto - senza dover implementare un
    /// vero client HTTP solo per provare che chi chiama regga il fallimento
    /// (stesso schema di <c>FakeGameConnection.NextFailure</c> nei test
    /// dell'app).
    /// </summary>
    public Exception? ProssimoErrore { get; set; }

    public int Chiamate { get; private set; }

    public string? UltimoSistema { get; private set; }

    public string? UltimoUtente { get; private set; }

    public async Task<string?> CompletaAsync(string sistema, string utente, CancellationToken ct)
    {
        Chiamate++;
        UltimoSistema = sistema;
        UltimoUtente = utente;

        if (ProssimoErrore is { } errore)
        {
            ProssimoErrore = null;
            throw errore;
        }

        if (Ritardo > TimeSpan.Zero)
        {
            await Task.Delay(Ritardo, ct);
        }

        return Risposta;
    }
}
