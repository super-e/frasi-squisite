using FrasiSquisite.Domain.Modes;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Modes;

public class RoleSchemaModeTests
{
    private readonly IGameMode _modalita = new RoleSchemaMode();

    public static TheoryData<int, int> GiocatoriECaselle()
    {
        var dati = new TheoryData<int, int>();
        for (var n = 2; n <= 12; n++)
        {
            for (var k = 3; k <= 8; k++)
            {
                dati.Add(n, k);
            }
        }

        return dati;
    }

    /// <summary>
    /// La proprietà su cui poggia l'intero gioco (spec §2.2). Se questo test
    /// fallisce, esistono frasi con caselle doppie o mancanti.
    /// </summary>
    [Theory]
    [MemberData(nameof(GiocatoriECaselle))]
    public void OgniFraseRiceveOgniCasellaEsattamenteUnaVolta(int n, int k)
    {
        var schema = TestSchemas.WithSlots(k);
        var conteggi = new int[n, k];

        for (var round = 0; round < k; round++)
        {
            for (var giocatore = 0; giocatore < n; giocatore++)
            {
                var assegnazione = _modalita.AssignSlot(round, giocatore, n, schema);
                conteggi[assegnazione.PhraseIndex, assegnazione.SlotIndex]++;
            }
        }

        for (var frase = 0; frase < n; frase++)
        {
            for (var casella = 0; casella < k; casella++)
            {
                Assert.Equal(1, conteggi[frase, casella]);
            }
        }
    }

    /// <summary>
    /// Ogni giocatore copre tutte le K caselle dello schema, una per round.
    /// Asserisce sulle assegnazioni restituite, non sul numero di iterazioni
    /// del ciclo — altrimenti il test passerebbe qualunque cosa restituisca
    /// AssignSlot.
    /// </summary>
    [Theory]
    [MemberData(nameof(GiocatoriECaselle))]
    public void OgniGiocatoreCopreTutteLeCaselleUnaVoltaCiascuna(int n, int k)
    {
        var schema = TestSchemas.WithSlots(k);

        for (var giocatore = 0; giocatore < n; giocatore++)
        {
            var caselleScritte = Enumerable.Range(0, k)
                .Select(round => _modalita.AssignSlot(round, giocatore, n, schema).SlotIndex)
                .OrderBy(i => i)
                .ToList();

            Assert.Equal(Enumerable.Range(0, k), caselleScritte);
        }
    }

    /// <summary>
    /// Finché le caselle non superano i giocatori, nessuno scrive due volte
    /// sulla stessa frase: è ciò che rende varie le frasi risultanti.
    /// </summary>
    [Theory]
    [MemberData(nameof(GiocatoriECaselle))]
    public void UnGiocatoreNonTornaSullaStessaFraseFinchePuo(int n, int k)
    {
        var schema = TestSchemas.WithSlots(k);
        var frasiDistinteAttese = Math.Min(n, k);

        for (var giocatore = 0; giocatore < n; giocatore++)
        {
            var frasi = Enumerable.Range(0, k)
                .Select(round => _modalita.AssignSlot(round, giocatore, n, schema).PhraseIndex)
                .Distinct()
                .Count();

            Assert.Equal(frasiDistinteAttese, frasi);
        }
    }

    [Theory]
    [MemberData(nameof(GiocatoriECaselle))]
    public void InUnDatoRoundDueGiocatoriNonScrivonoMaiSullaStessaFrase(int n, int k)
    {
        var schema = TestSchemas.WithSlots(k);

        for (var round = 0; round < k; round++)
        {
            var frasi = Enumerable.Range(0, n)
                .Select(p => _modalita.AssignSlot(round, p, n, schema).PhraseIndex)
                .ToList();

            Assert.Equal(n, frasi.Distinct().Count());
        }
    }

    [Fact]
    public void IlNumeroDiFrasiEQuelloDeiGiocatori()
    {
        var schema = TestSchemas.WithSlots(5);

        Assert.Equal(4, _modalita.PhraseCount(4, schema));
    }
}
