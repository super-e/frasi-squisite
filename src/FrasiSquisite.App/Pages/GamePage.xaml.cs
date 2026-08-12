using System.ComponentModel;
using FrasiSquisite.App.ViewModels;

namespace FrasiSquisite.App.Pages;

public partial class GamePage : ContentPage
{
    private readonly GameSessionViewModel _viewModel;

    // Stato del pizzico/trascinamento sull'illustrazione ingrandita: solo
    // di vista, mai nel ViewModel - è geometria di un gesto, non stato di
    // gioco (design pinch-to-zoom §3.1).
    private double _scaleCorrente = 1;
    private double _scalePartenza = 1;
    private double _xOffset;
    private double _yOffset;
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
    // chiude come di consueto.
    private void OnOverlayTapped(object? sender, TappedEventArgs e)
    {
        if (_scaleCorrente > 1.01)
        {
            AzzeraZoomImmagine(animato: true);
            return;
        }

        _viewModel.CollapseImageCommand.Execute(null);
    }

    // Ancoraggio al punto pizzicato (AnchorX/AnchorY) invece del calcolo
    // manuale della traslazione: più semplice, e sufficiente per un
    // overlay che deve solo zoomare in modo naturale, non restare
    // perfettamente fermo sotto le dita in ogni istante (design §3.2).
    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        switch (e.Status)
        {
            case GestureStatus.Started:
                _scalePartenza = _scaleCorrente;
                break;

            case GestureStatus.Running:
                ImmagineIngrandita.AnchorX = e.ScaleOrigin.X;
                ImmagineIngrandita.AnchorY = e.ScaleOrigin.Y;

                _scaleCorrente = Math.Clamp(_scalePartenza * e.Scale, 1, 4);
                ImmagineIngrandita.Scale = _scaleCorrente;
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                // Pizzicando fino a tornare (quasi) a 1x, lo spostamento del
                // trascinamento precedente resterebbe visibile anche a zoom
                // annullato: qui si azzera insieme allo zoom, non solo alla
                // chiusura dell'overlay (rilievo Important #1 della
                // revisione). Canceled trattato come Completed: un'
                // interruzione del gesto (es. un popup di sistema) non deve
                // lasciare lo stato fuori dai limiti previsti (rilievo
                // Important #2).
                if (_scaleCorrente <= 1.01)
                {
                    AzzeraZoomImmagine(animato: true);
                }
                break;
        }
    }

    // Trascinamento attivo solo da zoomato (design §3.3): a 1x non c'è
    // nulla da spostare. Il rientro elastico si applica solo al rilascio
    // (Completed), non a ogni frame di Running, altrimenti il
    // trascinamento risulterebbe "gommoso" invece che diretto.
    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (_scaleCorrente <= 1.01)
        {
            return;
        }

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _xOffsetPartenza = _xOffset;
                _yOffsetPartenza = _yOffset;
                break;

            case GestureStatus.Running:
                ImmagineIngrandita.TranslationX = _xOffsetPartenza + e.TotalX;
                ImmagineIngrandita.TranslationY = _yOffsetPartenza + e.TotalY;
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                var limiteX = ImmagineIngrandita.Width * (_scaleCorrente - 1) / 2;
                var limiteY = ImmagineIngrandita.Height * (_scaleCorrente - 1) / 2;

                _xOffset = Math.Clamp(_xOffsetPartenza + e.TotalX, -limiteX, limiteX);
                _yOffset = Math.Clamp(_yOffsetPartenza + e.TotalY, -limiteY, limiteY);

                _ = ImmagineIngrandita.TranslateTo(_xOffset, _yOffset, 150, Easing.CubicOut);
                break;
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
        _scaleCorrente = 1;
        _scalePartenza = 1;
        _xOffset = 0;
        _yOffset = 0;

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
