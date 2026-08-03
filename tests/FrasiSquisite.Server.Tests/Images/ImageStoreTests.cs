using FrasiSquisite.Server.Images;
using Xunit;

namespace FrasiSquisite.Server.Tests.Images;

public class ImageStoreTests
{
    private static byte[] Immagine(byte seme) => [seme, 0, 1, 2];

    [Fact]
    public void QuelCheSiSalvaSiRilegge()
    {
        var deposito = new ImageStore();

        var percorso = deposito.Salva(Immagine(7));

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
            .Select(i => Id(deposito.Salva(Immagine((byte)i))))
            .ToList();

        Assert.Equal(identificativi.Count, identificativi.Distinct().Count());
        Assert.All(identificativi, id => Assert.True(id.Length >= 20, $"troppo corto: {id}"));
    }

    /// <summary>
    /// Senza tetto un server acceso da settimane riempie la memoria del
    /// container. La più vecchia esce: la partita a cui apparteneva è finita
    /// da un pezzo.
    /// </summary>
    [Fact]
    public void OltreIlTettoLaPiuVecchiaEsce()
    {
        var deposito = new ImageStore(tetto: 3);

        var primo = Id(deposito.Salva(Immagine(1)));
        var secondo = Id(deposito.Salva(Immagine(2)));
        deposito.Salva(Immagine(3));
        deposito.Salva(Immagine(4));

        Assert.False(deposito.TryGet(primo, out _));
        Assert.True(deposito.TryGet(secondo, out _));
    }

    [Fact]
    public void IlPercorsoEQuelloCheIlClientPuoChiamare()
    {
        Assert.StartsWith("/illustrazioni/", new ImageStore().Salva(Immagine(1)), StringComparison.Ordinal);
    }

    private static string Id(string percorso) => percorso["/illustrazioni/".Length..];
}
