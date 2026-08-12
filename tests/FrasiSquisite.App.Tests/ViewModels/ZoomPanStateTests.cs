using FrasiSquisite.App.ViewModels;

namespace FrasiSquisite.App.Tests.ViewModels;

public class ZoomPanStateTests
{
    [Fact]
    public void ApplicaDeltaPizzicoAccumulaFraPiuFrame()
    {
        var stato = new ZoomPanState();

        stato.ApplicaDeltaPizzico(1.5);
        stato.ApplicaDeltaPizzico(1.5);

        Assert.Equal(2.25, stato.Scala);
    }

    [Fact]
    public void ApplicaDeltaPizzicoNonSuperaQuattro()
    {
        var stato = new ZoomPanState();

        for (var i = 0; i < 20; i++)
        {
            stato.ApplicaDeltaPizzico(1.5);
        }

        Assert.Equal(4, stato.Scala);
    }

    [Fact]
    public void ApplicaDeltaPizzicoNonScendeSottoUno()
    {
        var stato = new ZoomPanState();

        stato.ApplicaDeltaPizzico(0.1);

        Assert.Equal(1, stato.Scala);
    }

    [Fact]
    public void EZoomatoEFalsoA1x()
    {
        var stato = new ZoomPanState();

        Assert.False(stato.EZoomato);
    }

    [Fact]
    public void EZoomatoEVeroSopraLaTolleranza()
    {
        var stato = new ZoomPanState();
        stato.ApplicaDeltaPizzico(1.5);

        Assert.True(stato.EZoomato);
    }

    [Fact]
    public void UnImmagineQuadrataSuUnaViewPiuAltaCheLargaNonHaSpostamentoA1x()
    {
        var stato = new ZoomPanState();

        var (x, y) = stato.RientraNeiLimiti(500, 500, larghezzaView: 400, altezzaView: 800, latoContenuto: 400);

        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    // View 400x800, contenuto quadrato disegnato a 400x400 (AspectFit).
    // A scala 2 il contenuto scalato e' 800x800: eccede la larghezza
    // della view (400) di 400px complessivi -> limite orizzontale 200,
    // ma non eccede l'altezza della view (800) -> limite verticale 0.
    // Prova che il limite usa il lato del contenuto, non l'intera view.
    [Fact]
    public void IlLimiteUsaIlLatoDelContenutoNonLaViewIntera()
    {
        var stato = new ZoomPanState();
        stato.ApplicaDeltaPizzico(2);

        var (x, y) = stato.RientraNeiLimiti(1000, 1000, larghezzaView: 400, altezzaView: 800, latoContenuto: 400);

        Assert.Equal(200, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void RientraNeiLimitiMemorizzaLoStato()
    {
        var stato = new ZoomPanState();
        stato.ApplicaDeltaPizzico(2);

        stato.RientraNeiLimiti(1000, 1000, larghezzaView: 400, altezzaView: 800, latoContenuto: 400);

        Assert.Equal(200, stato.OffsetX);
        Assert.Equal(0, stato.OffsetY);
    }

    [Fact]
    public void AzzeraRiportaScalaEOffsetAiValoriIniziali()
    {
        var stato = new ZoomPanState();
        stato.ApplicaDeltaPizzico(2);
        stato.RientraNeiLimiti(1000, 1000, larghezzaView: 400, altezzaView: 800, latoContenuto: 400);

        stato.Azzera();

        Assert.Equal(1, stato.Scala);
        Assert.Equal(0, stato.OffsetX);
        Assert.Equal(0, stato.OffsetY);
        Assert.False(stato.EZoomato);
    }
}
