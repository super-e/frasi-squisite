namespace FrasiSquisite.Domain.Voting;

/// <summary>Il punteggio di una frase nella classifica finale.</summary>
public sealed record PhraseScore(int PhraseIndex, int Votes, bool IsWinner);

/// <summary>
/// Conteggio dei voti: funzione pura di una riga di stato. Vive fuori dal
/// motore perché è dove stanno le sottigliezze (pareggi, zero voti, ordine
/// deterministico) e provarle non deve richiedere di montare una partita.
/// </summary>
public sealed record VoteTally(IReadOnlyList<PhraseScore> Ranking)
{
    /// <param name="votes">Chi ha votato cosa. Gli indici devono essere validi.</param>
    /// <param name="phraseCount">Quante frasi ha la partita.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Se un voto punta a una frase inesistente. Il motore valida prima di
    /// scrivere nello stato: qui sarebbe un difetto del chiamante, e contarlo
    /// in silenzio darebbe una classifica sbagliata senza segnalazione.
    /// </exception>
    public static VoteTally From(IReadOnlyDictionary<Guid, int> votes, int phraseCount)
    {
        ArgumentNullException.ThrowIfNull(votes);
        ArgumentOutOfRangeException.ThrowIfNegative(phraseCount);

        var conteggi = new int[phraseCount];

        foreach (var indice in votes.Values)
        {
            if (indice < 0 || indice >= phraseCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(votes),
                    indice,
                    $"Voto per la frase {indice}, ma le frasi sono {phraseCount}.");
            }

            conteggi[indice]++;
        }

        // Zero voti significa nessuna vincitrice, non "tutte a pari merito":
        // il massimo è 0 per tutte, e senza questa soglia sarebbero tutte
        // IsWinner. Vedi spec §6.3.
        var massimo = conteggi.Length == 0 ? 0 : conteggi.Max();

        var righe = Enumerable.Range(0, phraseCount)
            .Select(i => new PhraseScore(i, conteggi[i], massimo > 0 && conteggi[i] == massimo))
            .OrderByDescending(r => r.Votes)
            .ThenBy(r => r.PhraseIndex)
            .ToList();

        return new VoteTally(righe);
    }

    public IReadOnlyList<int> WinnerIndexes =>
        [.. Ranking.Where(r => r.IsWinner).Select(r => r.PhraseIndex)];
}
