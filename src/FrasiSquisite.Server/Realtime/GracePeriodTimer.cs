namespace FrasiSquisite.Server.Realtime;

/// <summary>
/// Astrae l'attesa del periodo di grazia (GameHost): nei test unitari si
/// sostituisce con un finto controllabile a comando, mai un vero Task.Delay
/// da attendere per davvero (design rientro §7).
/// </summary>
public interface IGracePeriodTimer
{
    Task DelayAsync(TimeSpan durata, CancellationToken ct);
}

public sealed class RealGracePeriodTimer : IGracePeriodTimer
{
    public Task DelayAsync(TimeSpan durata, CancellationToken ct) => Task.Delay(durata, ct);
}
