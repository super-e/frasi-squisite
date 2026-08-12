namespace FrasiSquisite.App.ViewModels;

/// <summary>
/// Stato geometrico del pizzico/trascinamento sull'illustrazione
/// ingrandita: scala e spostamento, con l'aritmetica di accumulo e i
/// limiti elastici. Pura e senza dipendenze MAUI apposta - è la parte
/// di GamePage.xaml.cs (design pinch-to-zoom) che si può testare, dopo
/// che la revisione finale del lotto ha trovato due bug proprio in
/// questa aritmetica (PinchGestureUpdatedEventArgs.Scale è un delta
/// per-frame, non cumulativo; PanUpdatedEventArgs.TotalX/Y sono zero a
/// fine gesto). Vive in ViewModels/ non perché sia un ViewModel MVVM,
/// ma perché è l'unica cartella che FrasiSquisite.App.Tests collega
/// per intero (vedi FrasiSquisite.App.Tests.csproj).
/// </summary>
public sealed class ZoomPanState
{
    private const double ScalaMinima = 1;
    private const double ScalaMassima = 4;
    private const double TolleranzaZoomato = 1.01;

    public double Scala { get; private set; } = ScalaMinima;
    public double OffsetX { get; private set; }
    public double OffsetY { get; private set; }

    public bool EZoomato => Scala > TolleranzaZoomato;

    /// <summary>
    /// Accumula un delta di pizzico (PinchGestureUpdatedEventArgs.Scale,
    /// relativo all'ultimo evento, non cumulativo dall'inizio del
    /// gesto).
    /// </summary>
    public void ApplicaDeltaPizzico(double delta) =>
        Scala = Math.Clamp(Scala * delta, ScalaMinima, ScalaMassima);

    /// <summary>
    /// Applica i limiti elastici e li memorizza. <paramref
    /// name="latoContenuto"/> è il lato (largo o alto, il minore)
    /// dell'immagine effettivamente disegnata dentro l'Image con
    /// Aspect="AspectFit" - non le dimensioni della view, che includono
    /// il bordo vuoto quando l'immagine non riempie il suo contenitore.
    /// </summary>
    public (double X, double Y) RientraNeiLimiti(
        double xRichiesto, double yRichiesto,
        double larghezzaView, double altezzaView, double latoContenuto)
    {
        var limiteX = Math.Max(0, (latoContenuto * Scala - larghezzaView) / 2);
        var limiteY = Math.Max(0, (latoContenuto * Scala - altezzaView) / 2);

        OffsetX = Math.Clamp(xRichiesto, -limiteX, limiteX);
        OffsetY = Math.Clamp(yRichiesto, -limiteY, limiteY);

        return (OffsetX, OffsetY);
    }

    public void Azzera()
    {
        Scala = ScalaMinima;
        OffsetX = 0;
        OffsetY = 0;
    }
}
