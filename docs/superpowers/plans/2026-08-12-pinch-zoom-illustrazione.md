# Pinch-to-zoom sull'illustrazione ingrandita — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** pizzicare per ingrandire/rimpicciolire l'illustrazione dentro l'overlay già esistente, e trascinare per esplorarla quando è ingrandita.

**Architecture:** stato di zoom (scala, offset) come campi privati in `GamePage.xaml.cs`, mai nel ViewModel — è geometria di un gesto, non stato di gioco. `PinchGestureRecognizer`/`PanGestureRecognizer` sull'`Image`, `TapGestureRecognizer` sul `Grid` genitore spostato da binding a comando a evento code-behind, perché la decisione "chiudi o azzera lo zoom" dipende da stato di sola vista.

**Tech Stack:** .NET MAUI (gesture recognizer standard, nessuna dipendenza nuova).

## Global Constraints

- Zoom fra 1× e 4×, nessun limite oltre (spec §1).
- Nessuna persistenza dello zoom fra un'apertura e l'altra dell'overlay:
  ogni apertura riparte da 1× (spec §1).
- Il tocco singolo chiude l'overlay **solo se non zoomato** (scala ≤
  1.01, tolleranza per gli arrotondamenti del pizzico). Da zoomato, il
  tocco riporta a 1× con una breve animazione invece di chiudere (spec
  §3.4).
- Il rientro elastico del trascinamento si applica **al rilascio**, non
  a ogni frame, con un'animazione di ~150ms (spec §3.3).
- Il tasto Indietro Android chiude l'overlay incondizionatamente,
  indipendentemente dallo zoom — comportamento già esistente
  (`OnBackButtonPressed`), da non toccare se non per assicurarsi che
  azzeri anche lo zoom.
- Stato di zoom in `GamePage.xaml.cs`, mai in `GameSessionViewModel`
  (spec §3.1).
- Nessun test automatico: gesture in code-behind XAML non testabile in
  questa codebase (spec §5). L'accettazione è build pulita + verifica
  manuale sul dispositivo.
- Lingua italiana in codice, commenti e messaggi di commit; commit
  firmati GPG.
- Baseline attuale (verificata con `dotnet test` prima di questo
  piano): **834 test, 0 falliti** (Shared 86, App 116, Domain 520,
  Server 112). Questo piano non tocca nessun progetto di test: il
  numero non cambia.

---

### Task 1: Pinch, pan e tocco adattivo sull'overlay

**Files:**
- Modify: `src/FrasiSquisite.App/Pages/GamePage.xaml:496-506` (overlay: nome sull'`Image`, gesture recognizer, tocco spostato a evento)
- Modify: `src/FrasiSquisite.App/Pages/GamePage.xaml.cs` (intero file: nuovi campi, nuovi handler, sottoscrizione a `PropertyChanged`)

**Interfaces:**
- Consuma: `GameSessionViewModel.ExpandedImageUrl` (`string?`, osservabile), `GameSessionViewModel.CollapseImageCommand` (`IRelayCommand`) — già esistenti dal lotto precedente, non modificati.
- Non produce nulla di nuovo per altri task: è l'unico task del piano.

- [ ] **Step 1: Nomina l'`Image` e aggiungi i gesture recognizer in XAML**

In `src/FrasiSquisite.App/Pages/GamePage.xaml`, trova:

```xml
        <!-- L'illustrazione ingrandita: tocco ovunque per chiudere. Qui il
             BindingContext è già GameSessionViewModel (nessun template di
             CollectionView in mezzo), quindi i binding sono diretti. -->
        <Grid BackgroundColor="#CC000000"
              IsVisible="{Binding ExpandedImageUrl, Converter={StaticResource NotEmpty}}">
            <Grid.GestureRecognizers>
                <TapGestureRecognizer Command="{Binding CollapseImageCommand}" />
            </Grid.GestureRecognizers>
            <Image Source="{Binding ExpandedImageUrl}" Aspect="AspectFit" Margin="24" />
        </Grid>
```

Sostituiscilo con:

```xml
        <!-- L'illustrazione ingrandita: pizzico per zoomare, trascina per
             esplorare quando ingrandita. Il tocco per chiudere è passato
             da Command a un evento code-behind (OnOverlayTapped) perché la
             decisione "chiudi o azzera lo zoom" dipende da stato di sola
             vista (la scala corrente), che il ViewModel non conosce e non
             deve conoscere - non è stato di gioco. -->
        <Grid BackgroundColor="#CC000000"
              IsVisible="{Binding ExpandedImageUrl, Converter={StaticResource NotEmpty}}">
            <Grid.GestureRecognizers>
                <TapGestureRecognizer Tapped="OnOverlayTapped" />
            </Grid.GestureRecognizers>
            <Image x:Name="ImmagineIngrandita"
                   Source="{Binding ExpandedImageUrl}" Aspect="AspectFit" Margin="24">
                <Image.GestureRecognizers>
                    <PinchGestureRecognizer PinchUpdated="OnPinchUpdated" />
                    <PanGestureRecognizer PanUpdated="OnPanUpdated" />
                </Image.GestureRecognizers>
            </Image>
        </Grid>
```

- [ ] **Step 2: Riscrivi `GamePage.xaml.cs` con lo stato e gli handler di zoom**

Sostituisci l'intero contenuto di `src/FrasiSquisite.App/Pages/GamePage.xaml.cs` con:

```csharp
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
        if (e.Status == GestureStatus.Started)
        {
            _scalePartenza = _scaleCorrente;
            return;
        }

        if (e.Status != GestureStatus.Running)
        {
            return;
        }

        ImmagineIngrandita.AnchorX = e.ScaleOrigin.X;
        ImmagineIngrandita.AnchorY = e.ScaleOrigin.Y;

        _scaleCorrente = Math.Clamp(_scalePartenza * e.Scale, 1, 4);
        ImmagineIngrandita.Scale = _scaleCorrente;
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
```

- [ ] **Step 3: Verifica che il progetto App compili**

Run: `dotnet build src/FrasiSquisite.App/FrasiSquisite.App.csproj -f net10.0-android`
Expected: Build succeeded, 0 errori. (Come per il lotto precedente, un
nome sbagliato in un evento XAML — `OnOverlayTapped`, `OnPinchUpdated`,
`OnPanUpdated` — o un tipo di firma non corrispondente fallisce qui,
grazie a `MauiXamlInflator=SourceGen`.)

- [ ] **Step 4: Esegui l'intera suite per verificare che nulla si sia rotto**

Run: `dotnet test`
Expected: PASS — 834/834 (Shared 86, App 116, Domain 520, Server 112,
invariati: questo task non tocca nessun progetto di test).

- [ ] **Step 5: Commit**

```bash
git add src/FrasiSquisite.App/Pages/GamePage.xaml src/FrasiSquisite.App/Pages/GamePage.xaml.cs
git commit -m "feat(illustrazione): pizzico per zoomare e trascinare l'immagine ingrandita"
```

---

## Verifica manuale (fuori dal piano, per chi gioca dopo)

Non automatizzabile da qui: dopo l'implementazione, sul dispositivo —

1. Apri un'illustrazione, pizzica per ingrandire: verifica che zoomi in
   modo fluido, fino a un massimo (circa 4×) oltre cui non zooma più.
2. Da zoomato, trascina fino al bordo dell'immagine: verifica il
   rientro elastico animato invece di un blocco secco o un'immagine
   che scompare fuori schermo.
3. Da zoomato, tocca una volta: verifica che **non** chiuda l'overlay,
   ma torni a 1× con un'animazione breve.
4. Tocca una seconda volta (ormai a 1×): verifica che chiuda l'overlay
   come prima di questo lotto.
5. Riapri la stessa illustrazione: verifica che riparta da 1×, non
   dall'ultimo zoom.
6. Zooma, poi premi il tasto Indietro: verifica che chiuda comunque
   l'overlay, e che una riapertura successiva riparta da 1×.
