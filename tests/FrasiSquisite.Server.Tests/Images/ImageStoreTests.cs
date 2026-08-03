using System.Collections.Concurrent;
using FrasiSquisite.Server.Images;
using Xunit;

namespace FrasiSquisite.Server.Tests.Images;

public class ImageStoreTests
{
    private static byte[] Immagine(byte seme) => [seme, 0, 1, 2];

    private static byte[] ImmagineDiByte(int numeroByte)
    {
        var byteImmagine = new byte[numeroByte];
        Array.Fill(byteImmagine, (byte)1);
        return byteImmagine;
    }

    [Fact]
    public void QuelCheSiSalvaSiRilegge()
    {
        var deposito = new ImageStore();

        var percorso = deposito.Salva(Immagine(7));

        Assert.NotNull(percorso);
        Assert.True(deposito.TryGet(Id(percorso), out var letti));
        Assert.Equal(Immagine(7), letti);
    }

    [Fact]
    public void UnIdentificativoInventatoNonTrovaNiente()
    {
        Assert.False(new ImageStore().TryGet("non-esiste", out _));
    }

    /// <summary>
    /// L'identificativo È la credenziale: chi ce l'ha vede l'immagine. Due
    /// salvataggi non devono mai produrre lo stesso, e la lunghezza deve
    /// rendere inutile provare a indovinare.
    /// </summary>
    [Fact]
    public void GliIdentificativiSonoTuttiDiversiEAbbastanzaLunghi()
    {
        var deposito = new ImageStore();

        var identificativi = Enumerable.Range(0, 200)
            .Select(i => Id(deposito.Salva(Immagine((byte)i))!))
            .ToList();

        Assert.Equal(identificativi.Count, identificativi.Distinct().Count());
        Assert.All(identificativi, id => Assert.True(id.Length >= 20, $"troppo corto: {id}"));
    }

    /// <summary>
    /// Il tetto è sui byte totali depositati, non sul numero di pezzi: due
    /// immagini grosse pesano quanto cinquanta piccole. Qui il budget è tre
    /// unità e ogni immagine ne pesa una, quindi la quarta fa uscire la
    /// prima — stesso comportamento di prima, ma la garanzia ora vale anche
    /// quando le immagini non pesano tutte uguale (vedi i test sotto).
    /// </summary>
    [Fact]
    public void OltreIlBudgetInByteLaPiuVecchiaEsce()
    {
        var deposito = new ImageStore(budgetByte: 3);

        var primo = Id(deposito.Salva(ImmagineDiByte(1))!);
        var secondo = Id(deposito.Salva(ImmagineDiByte(1))!);
        deposito.Salva(ImmagineDiByte(1));
        deposito.Salva(ImmagineDiByte(1));

        Assert.False(deposito.TryGet(primo, out _));
        Assert.True(deposito.TryGet(secondo, out _));
    }

    /// <summary>
    /// Questo è il caso che il conteggio a pezzi non poteva vedere: poche
    /// immagini grandi riempiono il budget tanto quanto tante piccole.
    /// Un'unica immagine da 5 byte con budget 10 sfratta la prima appena il
    /// totale supera il tetto, anche se le "immagini" sono solo due.
    /// </summary>
    [Fact]
    public void PocheImmaginiGrandiSfrattanoAncheSeSonoPoche()
    {
        var deposito = new ImageStore(budgetByte: 10);

        var primo = Id(deposito.Salva(ImmagineDiByte(6))!);
        var secondo = Id(deposito.Salva(ImmagineDiByte(6))!);

        Assert.False(deposito.TryGet(primo, out _));
        Assert.True(deposito.TryGet(secondo, out _));
    }

    /// <summary>
    /// Un'immagine più grande dell'intero budget non può essere accettata:
    /// se lo fosse, sfratterebbe tutte le altre e resterebbe comunque dentro
    /// da sola, superando il tetto in modo permanente. Salva deve dirlo al
    /// chiamante invece di fingere che sia andata bene.
    /// </summary>
    [Fact]
    public void UnImmagineOltreIlBudgetNonVieneAccettata()
    {
        var deposito = new ImageStore(budgetByte: 10);

        var percorso = deposito.Salva(ImmagineDiByte(11));

        Assert.Null(percorso);
    }

    /// <summary>
    /// Rifiutare l'immagine troppo grande non deve intaccare quelle già
    /// presenti: il tentativo fallito non tocca lo stato del deposito.
    /// </summary>
    [Fact]
    public void UnRifiutoNonSfrattaNiente()
    {
        var deposito = new ImageStore(budgetByte: 10);
        var primo = Id(deposito.Salva(ImmagineDiByte(5))!);

        deposito.Salva(ImmagineDiByte(11));

        Assert.True(deposito.TryGet(primo, out _));
    }

    /// <summary>
    /// Il deposito è un singleton condiviso da tutte le stanze: più partite
    /// salvano nello stesso istante da thread diversi. Sotto carico
    /// concorrente, i byte delle immagini ancora recuperabili non devono mai
    /// superare il budget — se la contabilità potesse divergere dal
    /// contenuto reale, il tetto smetterebbe di valere silenziosamente.
    /// Non c'è modo di ispezionare il totale interno dall'esterno (ed è
    /// giusto così: non è uno stato che serve a nessun altro), quindi la
    /// prova passa dal contenuto osservabile via TryGet.
    /// </summary>
    [Fact]
    public void SottoCaricoConcorrenteIByteRecuperabiliRestanoEntroIlBudget()
    {
        const int budget = 1000;
        var deposito = new ImageStore(budgetByte: budget);

        var percorsi = new ConcurrentBag<(string Id, int Dimensione)>();
        Parallel.For(0, 500, i =>
        {
            var dimensione = 1 + i % 7;
            var percorso = deposito.Salva(ImmagineDiByte(dimensione));
            if (percorso is not null)
            {
                percorsi.Add((Id(percorso), dimensione));
            }
        });

        var byteRecuperabili = percorsi
            .Where(p => deposito.TryGet(p.Id, out _))
            .Sum(p => p.Dimensione);

        Assert.True(byteRecuperabili <= budget,
            $"i byte recuperabili ({byteRecuperabili}) superano il budget ({budget})");
    }

    [Fact]
    public void IlPercorsoEQuelloCheIlClientPuoChiamare()
    {
        Assert.StartsWith("/illustrazioni/", new ImageStore().Salva(Immagine(1)), StringComparison.Ordinal);
    }

    /// <summary>
    /// Un tetto nullo o negativo renderebbe ogni immagine irrecuperabile
    /// all'istante, in silenzio: meglio un errore rumoroso all'avvio.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UnBudgetNonPositivoVieneRifiutatoAllaCostruzione(long budgetByte)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageStore(budgetByte));
    }

    private static string Id(string percorso) => percorso["/illustrazioni/".Length..];
}
