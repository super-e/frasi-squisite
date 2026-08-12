using System.ComponentModel;
using FrasiSquisite.App.ViewModels;

namespace FrasiSquisite.App.Pages;

public partial class GamePage : ContentPage
{
    private readonly GameSessionViewModel _viewModel;

    // Aritmetica di scala/spostamento estratta in una classe pura e
    // testabile (design pinch-to-zoom, rilievo Critical della revisione
    // finale): qui restano solo la lettura dei gesti MAUI e la scrittura
    // sulla Image, mai calcoli che potrebbero sbagliare senza un test a
    // scoprirlo.
    private readonly ZoomPanState _zoom = new();
    private double _xOffsetPartenza;
    private double _yOffsetPartenza;

    public GamePage(GameSessionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    // Copre l'avvio a freddo (design rientro §5.2): GamePage è l'unica
    // ShellContent dell'app (AppShell.xaml), quindi OnAppearing scatta una
    // volta all'avvio. TryRejoinAsync è già no-op silenzioso se non c'è
    // nulla da rientrare, quindi non serve guardia in più qui.
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.TryRejoinAsync();
    }

    // Il tasto Indietro di Android è la convenzione più forte per chiudere
    // un overlay a schermo intero: senza questo, chiude l'app invece,
    // perché GamePage è l'unica ShellContent (vedi commento sopra).
    protected override bool OnBackButtonPressed()
    {
        if (_viewModel.ExpandedImageUrl is not null)
        {
            _viewModel.CollapseImageCommand.Execute(null);
            return true;
        }

        return base.OnBackButtonPressed();
    }

    // Tocco sull'overlay (design pinch-to-zoom §3.4): chiude solo a 1x. Da
    // zoomato, un tocco per errore mentre si esplora l'immagine non deve
    // buttare fuori dall'overlay - riporta invece a 1x, un secondo tocco
    // chiude come di consueto. Presente sia sul Grid genitore sia
    // sull'Image stessa (rilievo della revisione: un tocco sull'immagine,
    // che ora porta anche PinchGestureRecognizer e PanGestureRecognizer,
    // potrebbe non risalire al Grid).
    private void OnOverlayTapped(object? sender, TappedEventArgs e)
    {
        if (_zoom.EZoomato)
        {
            AzzeraZoomImmagine(animato: true);
            return;
        }

        _viewModel.CollapseImageCommand.Execute(null);
    }

    // PinchGestureUpdatedEventArgs.Scale è relativo all'ultimo evento
    // ricevuto, non cumulativo dall'inizio del gesto (rilievo Critical
    // della revisione: il codice precedente lo trattava come cumulativo,
    // perdendo così l'accumulo di ogni frame intermedio).
    // ZoomPanState.ApplicaDeltaPizzico applica il delta correttamente,
    // frame per frame - non serve più uno stato "di inizio gesto" per la
    // scala.
    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (e.Status == GestureStatus.Running)
        {
            ImmagineIngrandita.CancelAnimations();
            ImmagineIngrandita.AnchorX = e.ScaleOrigin.X;
            ImmagineIngrandita.AnchorY = e.ScaleOrigin.Y;

            _zoom.ApplicaDeltaPizzico(e.Scale);
            ImmagineIngrandita.Scale = _zoom.Scala;
            return;
        }

        if (e.Status is GestureStatus.Completed or GestureStatus.Canceled)
        {
            if (_zoom.EZoomato)
            {
                // Pizzicando in giù senza tornare fino a 1x, i limiti del
                // trascinamento si restringono con la nuova scala: senza
                // questo, uno spostamento valido alla scala precedente
                // potrebbe restare fuori dai nuovi limiti (rilievo della
                // revisione).
                RientraNeiLimitiElastici(animato: true);
            }
            else
            {
                AzzeraZoomImmagine(animato: true);
            }
        }
    }

    // Trascinamento attivo solo da zoomato (design §3.3): a 1x non c'è
    // nulla da spostare.
    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (!_zoom.EZoomato)
        {
            return;
        }

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                ImmagineIngrandita.CancelAnimations();
                _xOffsetPartenza = _zoom.OffsetX;
                _yOffsetPartenza = _zoom.OffsetY;
                break;

            case GestureStatus.Running:
                // TotalX/TotalY QUI sono cumulativi dall'inizio del gesto
                // (a differenza di Completed/Canceled, dove sono zero -
                // rilievo Critical della revisione).
                ImmagineIngrandita.TranslationX = _xOffsetPartenza + e.TotalX;
                ImmagineIngrandita.TranslationY = _yOffsetPartenza + e.TotalY;
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                RientraNeiLimitiElastici(animato: true);
                break;
        }
    }

    // Rientro elastico: legge la posizione già applicata alla Image
    // (ImmagineIngrandita.TranslationX/Y), non gli argomenti del gesto -
    // a fine gesto PanUpdatedEventArgs.TotalX/Y sono zero, non il totale
    // spostato (rilievo Critical della revisione). Il lato del contenuto
    // è il minore fra larghezza e altezza della Image: con
    // Aspect="AspectFit" e un'illustrazione quadrata, è quello
    // effettivamente disegnato, non l'intera area della view che include
    // il bordo vuoto (rilievo della revisione).
    private void RientraNeiLimitiElastici(bool animato)
    {
        var latoContenuto = Math.Min(ImmagineIngrandita.Width, ImmagineIngrandita.Height);
        var (x, y) = _zoom.RientraNeiLimiti(
            ImmagineIngrandita.TranslationX,
            ImmagineIngrandita.TranslationY,
            ImmagineIngrandita.Width,
            ImmagineIngrandita.Height,
            latoContenuto);

        if (animato)
        {
            _ = ImmagineIngrandita.TranslateTo(x, y, 150, Easing.CubicOut);
        }
        else
        {
            ImmagineIngrandita.TranslationX = x;
            ImmagineIngrandita.TranslationY = y;
        }
    }

    // Azzera zoom e posizione: alla riapertura (fuori scope la
    // persistenza dello zoom, design §1) e ogni volta che l'overlay si
    // chiude da qualunque via - tocco a 1x, tasto Indietro, o il
    // ViewModel che lo chiude da solo (es. cambio schermata di un
    // non-host, vedi il rilievo Important #1 della revisione finale del
    // lotto precedente). Senza questo, la prossima apertura ripartirebbe
    // zoomata.
    private void AzzeraZoomImmagine(bool animato)
    {
        _zoom.Azzera();
        ImmagineIngrandita.AnchorX = 0.5;
        ImmagineIngrandita.AnchorY = 0.5;

        if (animato)
        {
            _ = ImmagineIngrandita.ScaleTo(1, 150, Easing.CubicOut);
            _ = ImmagineIngrandita.TranslateTo(0, 0, 150, Easing.CubicOut);
        }
        else
        {
            ImmagineIngrandita.Scale = 1;
            ImmagineIngrandita.TranslationX = 0;
            ImmagineIngrandita.TranslationY = 0;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameSessionViewModel.ExpandedImageUrl)
            && _viewModel.ExpandedImageUrl is null)
        {
            AzzeraZoomImmagine(animato: false);
        }
    }
}
