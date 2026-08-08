# Ingrandire l'illustrazione toccandola — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** toccando l'illustrazione piccola nella classifica finale, questa si apre a schermo intero; toccando di nuovo in un punto qualunque, si richiude.

**Architecture:** stato di sola UI su `GameSessionViewModel` (`ExpandedImageUrl` + due comandi), overlay `Grid` sovrapposto alla root di `GamePage.xaml`, nessun coinvolgimento del server.

**Tech Stack:** .NET MAUI, CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`), xUnit.

## Global Constraints

- Nessun cambiamento lato server, protocollo o dominio: l'indirizzo
  dell'immagine è già noto al client (spec §1, §6).
- Un solo overlay alla volta: lo stato vive sul `GameSessionViewModel` di
  pagina, non per riga di `PhraseResultRowView` (spec §3.1).
- Tocco ovunque sull'overlay lo chiude (spec §3.2, confermato in
  brainstorming).
- `PulisciStatoDiPartitaConclusa()` deve azzerare anche `ExpandedImageUrl`,
  non solo `FinalResults`/`RevealFragments`/`VoteOptions` (spec §3.3).
- Fuori scope: zoom/pan, cache/precaricamento dedicato, condivisione o
  salvataggio dell'immagine (spec §1).
- Test solo a livello di `GameSessionViewModel`: il rendering XAML
  dell'overlay non è testabile in questa codebase (spec §5).
- Lingua italiana in codice, commenti e messaggi di commit; commit firmati
  GPG (mai `--no-gpg-sign`/`--no-verify`).
- Baseline attuale (verificata con `dotnet test` prima di questo piano):
  **831 test, 0 falliti** (Shared 86, App 113, Domain 520, Server 112).

---

### Task 1: Stato e comandi dell'overlay su `GameSessionViewModel`

**Files:**
- Modify: `src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs:290` (nuova proprietà), `:558` (nuovi comandi, dopo `RequestIllustrationAsync`), `:909-918` (`PulisciStatoDiPartitaConclusa`)
- Test: `tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs`

**Interfaces:**
- Produce: `GameSessionViewModel.ExpandedImageUrl` (`string?`, osservabile), `GameSessionViewModel.ExpandImageCommand` (`IRelayCommand<string>`, generato da `[RelayCommand] private void ExpandImage(string url)`), `GameSessionViewModel.CollapseImageCommand` (`IRelayCommand`, generato da `[RelayCommand] private void CollapseImage()`). Task 2 li consuma da `GamePage.xaml`.

- [ ] **Step 1: Scrivi i due test che falliscono per i comandi**

In `tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs`, trova questo blocco (il commento e il test che lo segue):

```csharp
    // "Tornando in lobby le collezioni di partita risultano vuote" (brief del
    // lotto): senza questa pulizia la partita successiva mostrerebbe pezzi
    // di quella appena conclusa.
    [Fact]
    public async Task TornareAllaLobbySvuotaLeCollezioniDellaPartitaConclusa()
```

Inserisci questi due test **subito prima** di quel commento (stessa indentazione, dentro la classe):

```csharp
    // L'illustrazione ingrandita è uno stato di sola UI, indipendente dalle
    // righe della classifica: un solo overlay alla volta, quindi basta una
    // proprietà sul ViewModel di pagina invece che una per riga.
    [Fact]
    public void ExpandImageImpostaLUrlDellImmagineIngrandita()
    {
        var (vm, _, _) = Crea();

        vm.ExpandImageCommand.Execute("http://test/immagine.png");

        Assert.Equal("http://test/immagine.png", vm.ExpandedImageUrl);
    }

    [Fact]
    public void CollapseImageAzzeraLUrlDellImmagineIngrandita()
    {
        var (vm, _, _) = Crea();
        vm.ExpandImageCommand.Execute("http://test/immagine.png");

        vm.CollapseImageCommand.Execute(null);

        Assert.Null(vm.ExpandedImageUrl);
    }

```

- [ ] **Step 2: Esegui i due test e verifica che falliscano**

Run: `dotnet test tests/FrasiSquisite.App.Tests --filter "ExpandImageImpostaLUrlDellImmagineIngrandita|CollapseImageAzzeraLUrlDellImmagineIngrandita"`
Expected: FAIL — `GameSessionViewModel` non ha ancora `ExpandImageCommand`/`ExpandedImageUrl`/`CollapseImageCommand` (errore di compilazione).

- [ ] **Step 3: Aggiungi la proprietà osservabile**

In `src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs`, trova:

```csharp
    public ObservableCollection<PhraseResultRowView> FinalResults { get; } = [];
```

Sostituiscila con:

```csharp
    public ObservableCollection<PhraseResultRowView> FinalResults { get; } = [];

    /// <summary>
    /// L'illustrazione ingrandita a schermo intero, o null se nessuna è
    /// aperta. Un solo overlay alla volta: non serve stato per riga.
    /// </summary>
    [ObservableProperty]
    private string? _expandedImageUrl;
```

- [ ] **Step 4: Aggiungi i due comandi**

Nello stesso file, trova il comando `RequestIllustrationAsync` (subito dopo `CloseVotingAsync`):

```csharp
    [RelayCommand]
    private Task RequestIllustrationAsync(PhraseResultRowView riga) => EseguiComandoAsync(async () =>
    {
        // L'attesa si accende PRIMA dell'await: è ciò che impedisce il secondo
        // tocco mentre il primo è in volo. Il motore rifiuterebbe comunque il
        // doppione, ma con un errore a schermo invece che con niente.
        riga.IsWaiting = true;

        try
        {
            await _connection.RequestIllustrationAsync(RoomCode, riga.PhraseIndex);
        }
        catch
        {
            // Se non è nemmeno partita, l'attesa non deve restare accesa: non
            // arriverà nessun messaggio a spegnerla. Il guasto lo racconta
            // EseguiComandoAsync, che rilancia.
            riga.IsWaiting = false;
            throw;
        }
    });
```

Aggiungi questi due comandi **subito dopo** la chiusura di `RequestIllustrationAsync` (dopo il `});` finale, prima del prossimo membro):

```csharp

    /// <summary>
    /// Apre l'illustrazione a schermo intero. Nessuna chiamata al server:
    /// l'indirizzo è già noto al client (spec 2026-08-08).
    /// </summary>
    [RelayCommand]
    private void ExpandImage(string url) => ExpandedImageUrl = url;

    /// <summary>
    /// Chiude l'illustrazione ingrandita. Tocco ovunque sull'overlay, non
    /// solo su un pulsante di chiusura dedicato.
    /// </summary>
    [RelayCommand]
    private void CollapseImage() => ExpandedImageUrl = null;
```

- [ ] **Step 5: Esegui i due test e verifica che passino**

Run: `dotnet test tests/FrasiSquisite.App.Tests --filter "ExpandImageImpostaLUrlDellImmagineIngrandita|CollapseImageAzzeraLUrlDellImmagineIngrandita"`
Expected: PASS — 2/2

- [ ] **Step 6: Estendi i due test di pulizia per includere `ExpandedImageUrl`**

Nello stesso file di test, trova (dentro `TornareAllaLobbySvuotaLeCollezioniDellaPartitaConclusa`):

```csharp
        conn.Emit(new GameFinishedMessage([new PhraseResultView(0, "Il cadavere squisito", [], 0, false)]));

        Assert.NotEmpty(vm.FinalResults);
        Assert.NotEmpty(vm.RevealFragments);
        Assert.NotEmpty(vm.VoteOptions);
        Assert.True(vm.HasVoted);
        Assert.Equal(1, vm.VotedCount);
        Assert.Equal(3, vm.VotersExpected);

        await vm.BackToLobbyCommand.ExecuteAsync(null);

        Assert.Empty(vm.FinalResults);
        Assert.Empty(vm.RevealFragments);
        Assert.Empty(vm.VoteOptions);
        Assert.False(vm.HasVoted);
        Assert.Equal(0, vm.VotedCount);
        Assert.Equal(0, vm.VotersExpected);
    }
```

Sostituiscilo con:

```csharp
        conn.Emit(new GameFinishedMessage([new PhraseResultView(0, "Il cadavere squisito", [], 0, false)]));
        vm.ExpandImageCommand.Execute("http://test/immagine.png");

        Assert.NotEmpty(vm.FinalResults);
        Assert.NotEmpty(vm.RevealFragments);
        Assert.NotEmpty(vm.VoteOptions);
        Assert.True(vm.HasVoted);
        Assert.Equal(1, vm.VotedCount);
        Assert.Equal(3, vm.VotersExpected);
        Assert.NotNull(vm.ExpandedImageUrl);

        await vm.BackToLobbyCommand.ExecuteAsync(null);

        Assert.Empty(vm.FinalResults);
        Assert.Empty(vm.RevealFragments);
        Assert.Empty(vm.VoteOptions);
        Assert.False(vm.HasVoted);
        Assert.Equal(0, vm.VotedCount);
        Assert.Equal(0, vm.VotersExpected);
        Assert.Null(vm.ExpandedImageUrl);
    }
```

Poi trova (dentro `NuovaPartitaSvuotaLeCollezioniDellaPartitaConclusa`):

```csharp
        conn.Emit(new GameFinishedMessage([new PhraseResultView(0, "Il cadavere squisito", [], 0, false)]));

        Assert.NotEmpty(vm.FinalResults);
        Assert.NotEmpty(vm.VoteOptions);
        Assert.True(vm.HasVoted);
        Assert.Equal(1, vm.VotedCount);
        Assert.Equal(3, vm.VotersExpected);

        await vm.NewGameCommand.ExecuteAsync(null);

        Assert.Empty(vm.FinalResults);
        Assert.Empty(vm.VoteOptions);
        Assert.False(vm.HasVoted);
        Assert.Equal(0, vm.VotedCount);
        Assert.Equal(0, vm.VotersExpected);
    }
```

Sostituiscilo con:

```csharp
        conn.Emit(new GameFinishedMessage([new PhraseResultView(0, "Il cadavere squisito", [], 0, false)]));
        vm.ExpandImageCommand.Execute("http://test/immagine.png");

        Assert.NotEmpty(vm.FinalResults);
        Assert.NotEmpty(vm.VoteOptions);
        Assert.True(vm.HasVoted);
        Assert.Equal(1, vm.VotedCount);
        Assert.Equal(3, vm.VotersExpected);
        Assert.NotNull(vm.ExpandedImageUrl);

        await vm.NewGameCommand.ExecuteAsync(null);

        Assert.Empty(vm.FinalResults);
        Assert.Empty(vm.VoteOptions);
        Assert.False(vm.HasVoted);
        Assert.Equal(0, vm.VotedCount);
        Assert.Equal(0, vm.VotersExpected);
        Assert.Null(vm.ExpandedImageUrl);
    }
```

- [ ] **Step 7: Esegui i due test estesi e verifica che falliscano**

Run: `dotnet test tests/FrasiSquisite.App.Tests --filter "TornareAllaLobbySvuotaLeCollezioniDellaPartitaConclusa|NuovaPartitaSvuotaLeCollezioniDellaPartitaConclusa"`
Expected: FAIL — `Assert.NotNull(vm.ExpandedImageUrl)` (o `Assert.Null` dopo la pulizia) non è ancora vero, perché `PulisciStatoDiPartitaConclusa()` non tocca ancora `ExpandedImageUrl`.

- [ ] **Step 8: Azzera `ExpandedImageUrl` nella pulizia**

In `src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs`, trova:

```csharp
    private void PulisciStatoDiPartitaConclusa()
    {
        FinalResults.Clear();
        RevealFragments.Clear();
        _fraseCompleta = false;
        VoteOptions.Clear();
        HasVoted = false;
        VotedCount = 0;
        VotersExpected = 0;
    }
```

Sostituiscila con:

```csharp
    private void PulisciStatoDiPartitaConclusa()
    {
        FinalResults.Clear();
        RevealFragments.Clear();
        _fraseCompleta = false;
        VoteOptions.Clear();
        HasVoted = false;
        VotedCount = 0;
        VotersExpected = 0;
        ExpandedImageUrl = null;
    }
```

- [ ] **Step 9: Esegui tutta la suite App.Tests e verifica che passi**

Run: `dotnet test tests/FrasiSquisite.App.Tests`
Expected: PASS — 115/115 (113 preesistenti + 2 nuovi: `ExpandImageImpostaLUrlDellImmagineIngrandita`, `CollapseImageAzzeraLUrlDellImmagineIngrandita`; i due test estesi al Passo 6 non aggiungono al conteggio, restano solo più severi)

- [ ] **Step 10: Commit**

```bash
git add src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs
git commit -m "feat(illustrazione): stato e comandi per l'overlay a schermo intero"
```

---

### Task 2: Overlay in `GamePage.xaml`

**Files:**
- Modify: `src/FrasiSquisite.App/Pages/GamePage.xaml:9-12` (wrapping in `Grid`), `:462-465` (gesto sull'immagine piccola), `:487-489` (chiusura `Grid` + overlay)

**Interfaces:**
- Consuma: `GameSessionViewModel.ExpandedImageUrl`, `.ExpandImageCommand` (accetta `string`), `.CollapseImageCommand` — prodotti dal Task 1.

Nessun test automatico in questo task: il rendering XAML dell'overlay non è
testabile in questa codebase (Global Constraints). La verifica è la build e,
quando possibile, un controllo manuale sul dispositivo.

- [ ] **Step 1: Avvolgi la root della pagina in un `Grid`**

In `src/FrasiSquisite.App/Pages/GamePage.xaml`, trova:

```xml
             Shell.NavBarIsVisible="False"
             Title="Frasi Squisite">

    <ScrollView>
```

Sostituiscilo con:

```xml
             Shell.NavBarIsVisible="False"
             Title="Frasi Squisite">

    <Grid>
    <ScrollView>
```

(L'indentazione di `<ScrollView>` e di tutto il suo contenuto resta
invariata di proposito: reindentare centinaia di righe immutate
gonfierebbe il diff senza alcun beneficio funzionale in XAML, dove
l'indentazione non ha significato semantico.)

- [ ] **Step 2: Aggiungi il gesto di tocco sull'immagine piccola**

Nello stesso file, trova:

```xml
                                    <Image Source="{Binding ImageUrl}"
                                           Aspect="AspectFit"
                                           HeightRequest="240"
                                           IsVisible="{Binding ImageUrl, Converter={StaticResource NotEmpty}}" />
```

Sostituiscilo con:

```xml
                                    <Image Source="{Binding ImageUrl}"
                                           Aspect="AspectFit"
                                           HeightRequest="240"
                                           IsVisible="{Binding ImageUrl, Converter={StaticResource NotEmpty}}">
                                        <Image.GestureRecognizers>
                                            <TapGestureRecognizer Command="{Binding Source={x:Reference RootPage}, Path=BindingContext.ExpandImageCommand}"
                                                                   CommandParameter="{Binding ImageUrl}" />
                                        </Image.GestureRecognizers>
                                    </Image>
```

(Stesso pattern già usato dal pulsante "Illustra" subito sopra:
`Source={x:Reference RootPage}` perché il `BindingContext` qui dentro è la
riga `PhraseResultRowView`, non la pagina.)

- [ ] **Step 3: Aggiungi l'overlay e chiudi il `Grid`**

Nello stesso file, trova:

```xml
        </VerticalStackLayout>
    </ScrollView>
</ContentPage>
```

Sostituiscilo con:

```xml
        </VerticalStackLayout>
    </ScrollView>

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
    </Grid>
</ContentPage>
```

- [ ] **Step 4: Verifica che il progetto App compili**

Run: `dotnet build src/FrasiSquisite.App/FrasiSquisite.App.csproj -f net10.0-android`
Expected: Build succeeded, 0 errori. (La generazione XAML a compile-time del
progetto — `MauiXamlInflator=SourceGen`, già vista nell'output di `dotnet
test` — valida i binding e i nomi dei comandi a questo punto: un typo in
`ExpandImageCommand` o `ExpandedImageUrl` fallisce qui, non solo a runtime.)

- [ ] **Step 5: Esegui l'intera suite per verificare che nulla si sia rotto**

Run: `dotnet test`
Expected: PASS — 833/833 (Shared 86, App 115, Domain 520, Server 112)

- [ ] **Step 6: Commit**

```bash
git add src/FrasiSquisite.App/Pages/GamePage.xaml
git commit -m "feat(illustrazione): overlay a schermo intero, tocco per aprire e chiudere"
```

---

## Verifica manuale (fuori dal piano, per chi gioca dopo)

Non automatizzabile da qui: dopo l'implementazione, toccare un'illustrazione
già generata nella classifica finale e verificare che si apra a schermo
intero, e che il tocco in un punto qualunque (incluso sopra l'immagine
stessa, dato che l'`Image` non ha un proprio `TapGestureRecognizer` che
intercetti l'evento) la richiuda.
