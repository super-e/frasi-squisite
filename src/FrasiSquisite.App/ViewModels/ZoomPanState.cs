namespace FrasiSquisite.App.ViewModels;

/// <summary>
/// Stato geometrico del pizzico sull'illustrazione ingrandita: solo la
/// scala accumulata. Il calcolo dei limiti elastici del trascinamento è
/// puro (nessuno stato memorizzato) perché la posizione vera vive
/// sempre nella Image stessa (TranslationX/Y) - due fonti di verità per
/// la stessa cosa avevano già causato un rilievo della revisione
/// (l'annullamento di un'animazione in corso disallineava lo stato
/// memorizzato dalla posizione visiva reale). Pura e senza dipendenze
/// MAUI apposta - vive in ViewModels/ non perché sia un ViewModel MVVM,
/// ma perché è l'unica cartella che FrasiSquisite.App.Tests collega
/// per intero (vedi FrasiSquisite.App.Tests.csproj).
/// </summary>
public sealed class ZoomPanState
{
    private const double ScalaMinima = 1;
    private const double ScalaMassima = 4;
    private const double TolleranzaZoomato = 1.01;

    public double Scala { get; private set; } = ScalaMinima;

    public bool EZoomato => Scala > TolleranzaZoomato;

    /// <summary>
    /// Accumula un delta di pizzico (PinchGestureUpdatedEventArgs.Scale,
    /// relativo all'ultimo evento, non cumulativo dall'inizio del
    /// gesto).
    /// </summary>
    public void ApplicaDeltaPizzico(double delta) =>
        Scala = Math.Clamp(Scala * delta, ScalaMinima, ScalaMassima);

    /// <summary>
    /// Riallinea la scala alla posizione visiva reale, quando un gesto
    /// nuovo interrompe un'animazione in corso (CancelAnimations lascia
    /// la Image al valore raggiunto in quel momento, non a quello di
    /// partenza né di arrivo).
    /// </summary>
    public void ImpostaScala(double valore) =>
        Scala = Math.Clamp(valore, ScalaMinima, ScalaMassima);

    public void Azzera() => Scala = ScalaMinima;

    /// <summary>
    /// Limiti elastici del trascinamento, assumendo un pivot di scala
    /// centrato (AnchorX/AnchorY = 0.5, mai spostato). <paramref
    /// name="latoContenuto"/> è il lato (largo o alto, il minore)
    /// dell'immagine effettivamente disegnata dentro l'Image con
    /// Aspect="AspectFit" - non le dimensioni della view, che includono
    /// il bordo vuoto quando l'immagine non riempie il suo contenitore.
    /// Funzione pura: non legge né scrive stato di questa classe.
    /// </summary>
    public static (double X, double Y) RientraNeiLimiti(
        double xRichiesto, double yRichiesto,
        double larghezzaView, double altezzaView, double latoContenuto, double scala)
    {
        var limiteX = Math.Max(0, (latoContenuto * scala - larghezzaView) / 2);
        var limiteY = Math.Max(0, (latoContenuto * scala - altezzaView) / 2);

        return (Math.Clamp(xRichiesto, -limiteX, limiteX), Math.Clamp(yRichiesto, -limiteY, limiteY));
    }
}
