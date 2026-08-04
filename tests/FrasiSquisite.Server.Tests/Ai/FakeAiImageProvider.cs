using FrasiSquisite.Server.Ai;

namespace FrasiSquisite.Server.Tests.Ai;

/// <summary>
/// Provider in memoria: nessuna rete nei test. Stesso ruolo di
/// <see cref="FakeAiTextProvider"/> ma per il passo di generazione
/// dell'immagine: permette di simulare la risposta, il fallimento e la
/// lentezza, condiviso fra i test di <c>GameHost</c> e quelli d'integrazione
/// dell'hub.
/// </summary>
public sealed class FakeAiImageProvider : IAiImageProvider
{
    public byte[]? Risposta { get; set; }

    /// <summary>Se impostato, la chiamata attende questo prima di rispondere.</summary>
    public TimeSpan Ritardo { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Se impostata, la prossima chiamata la lancia invece di rispondere (e si
    /// azzera da sola): simula un guasto del modello, senza un vero client
    /// HTTP (stesso schema di <see cref="FakeAiTextProvider.ProssimoErrore"/>).
    /// </summary>
    public Exception? ProssimoErrore { get; set; }

    public int Chiamate { get; private set; }

    public string? UltimoPrompt { get; private set; }

    public async Task<byte[]?> GeneraAsync(string promptInglese, CancellationToken ct)
    {
        Chiamate++;
        UltimoPrompt = promptInglese;

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
