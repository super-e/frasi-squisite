using FrasiSquisite.Domain.Randomness;

namespace FrasiSquisite.Domain.Filling;

/// <summary>
/// Fonte di parole per riempire la casella di chi non è in grado di scriverla.
/// In fase 4 l'AI diventerà un'altra implementazione di questa idea, senza che
/// il motore cambi (spec §5, §8.2).
/// </summary>
public interface IWordPool
{
    string Take(string ruolo, IRandomSource random);
}
