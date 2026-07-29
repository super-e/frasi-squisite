namespace FrasiSquisite.Domain.Randomness;

/// <summary>
/// La casualità è una dipendenza per poter riprodurre una partita da seed
/// (spec §3.3).
/// </summary>
public interface IRandomSource
{
    int Next(int maxExclusive);
}
