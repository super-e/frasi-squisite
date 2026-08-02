using FrasiSquisite.Domain.Voting;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Voting;

public class VoteTallyTests
{
    private static Guid Giocatore(int i) => Guid.Parse($"00000000-0000-0000-0000-{i:D12}");

    [Fact]
    public void ContaIVotiPerFrase()
    {
        var voti = new Dictionary<Guid, int>
        {
            [Giocatore(0)] = 1,
            [Giocatore(1)] = 1,
            [Giocatore(2)] = 2,
        };

        var conteggio = VoteTally.From(voti, phraseCount: 3);

        Assert.Equal(0, conteggio.Ranking.Single(r => r.PhraseIndex == 0).Votes);
        Assert.Equal(2, conteggio.Ranking.Single(r => r.PhraseIndex == 1).Votes);
        Assert.Equal(1, conteggio.Ranking.Single(r => r.PhraseIndex == 2).Votes);
    }

    [Fact]
    public void LaClassificaEOrdinataPerPunteggioDecrescente()
    {
        var voti = new Dictionary<Guid, int>
        {
            [Giocatore(0)] = 2,
            [Giocatore(1)] = 2,
            [Giocatore(2)] = 0,
        };

        var conteggio = VoteTally.From(voti, phraseCount: 3);

        Assert.Equal([2, 0, 1], conteggio.Ranking.Select(r => r.PhraseIndex));
    }

    /// <summary>
    /// Senza il secondo criterio l'ordine fra frasi a pari punteggio
    /// dipenderebbe dall'implementazione dell'ordinamento e potrebbe cambiare
    /// fra due build — lo stesso difetto già corretto sul catalogo degli schemi.
    /// </summary>
    [Fact]
    public void APariPunteggioOrdinaPerIndiceCrescente()
    {
        var voti = new Dictionary<Guid, int>
        {
            [Giocatore(0)] = 2,
            [Giocatore(1)] = 0,
        };

        var conteggio = VoteTally.From(voti, phraseCount: 4);

        Assert.Equal([0, 2, 1, 3], conteggio.Ranking.Select(r => r.PhraseIndex));
    }

    [Fact]
    public void LOrdineRestaLoStessoInConteggiRipetuti()
    {
        var voti = new Dictionary<Guid, int> { [Giocatore(0)] = 1, [Giocatore(1)] = 2 };

        var ordini = Enumerable.Range(0, 20)
            .Select(_ => VoteTally.From(voti, 4).Ranking.Select(r => r.PhraseIndex).ToList())
            .ToList();

        Assert.All(ordini, ordine => Assert.Equal(ordini[0], ordine));
    }

    [Fact]
    public void LaPiuVotataEVincitrice()
    {
        var voti = new Dictionary<Guid, int> { [Giocatore(0)] = 1, [Giocatore(1)] = 1, [Giocatore(2)] = 0 };

        var conteggio = VoteTally.From(voti, phraseCount: 2);

        Assert.Equal([1], conteggio.WinnerIndexes);
    }

    [Fact]
    public void TutteQuelleAPunteggioMassimoSonoVincitriciExAequo()
    {
        var voti = new Dictionary<Guid, int> { [Giocatore(0)] = 0, [Giocatore(1)] = 1, [Giocatore(2)] = 2 };

        var conteggio = VoteTally.From(voti, phraseCount: 3);

        Assert.Equal([0, 1, 2], conteggio.WinnerIndexes);
    }

    /// <summary>
    /// Il caso che tiene in piedi il lotto AI: senza questa regola "nessuno ha
    /// votato" si presenterebbe come "hanno vinto tutte a pari merito", e
    /// l'illustrazione non saprebbe che non c'è niente da illustrare.
    /// </summary>
    [Fact]
    public void SenzaVotiNessunaFraseEVincitrice()
    {
        var conteggio = VoteTally.From(new Dictionary<Guid, int>(), phraseCount: 3);

        Assert.Empty(conteggio.WinnerIndexes);
        Assert.All(conteggio.Ranking, r => Assert.False(r.IsWinner));
        Assert.All(conteggio.Ranking, r => Assert.Equal(0, r.Votes));
    }

    [Fact]
    public void LaClassificaHaUnaRigaPerFraseAncheSenzaVoti()
    {
        var conteggio = VoteTally.From(new Dictionary<Guid, int>(), phraseCount: 5);

        Assert.Equal(5, conteggio.Ranking.Count);
        Assert.Equal([0, 1, 2, 3, 4], conteggio.Ranking.Select(r => r.PhraseIndex).Order());
    }

    /// <summary>
    /// Il motore valida l'indice prima di scriverlo nello stato, quindi un
    /// indice fuori intervallo qui dentro è un difetto del chiamante:
    /// contarlo in silenzio produrrebbe una classifica sbagliata senza che
    /// nulla lo segnali.
    /// </summary>
    [Fact]
    public void UnIndiceFuoriIntervalloEUnErrore()
    {
        var voti = new Dictionary<Guid, int> { [Giocatore(0)] = 7 };

        Assert.Throws<ArgumentOutOfRangeException>(() => VoteTally.From(voti, phraseCount: 3));
    }
}
