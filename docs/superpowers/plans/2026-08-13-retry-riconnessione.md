# Retry di riconnessione sulle azioni + bottone manuale — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ogni comando verso il server ritenta una riconnessione + rientro in stanza prima di mostrare l'errore, e un bottone "Riconnetti" nel banner permette un tentativo esplicito immediato.

**Architecture:** tutto il lavoro è nel client (`GameSessionViewModel`, `GamePage.xaml`): un nuovo helper `ReconnectTransportAndRoomAsync` riusato sia dal retry automatico dentro `EseguiComandoAsync` sia da un nuovo `ReconnectCommand`; nessun cambiamento a `IGameConnection`, `SignalRGameConnection` o al server.

**Tech Stack:** .NET 10 MAUI, CommunityToolkit.Mvvm (`[RelayCommand]`), xUnit.

## Global Constraints

- Un solo tentativo di riconnessione per comando (o per pressione del
  bottone): nessun retry-del-retry, nessun backoff (spec §1, "Fuori
  scope").
- Nessuna deduplica esplicita delle azioni non idempotenti: le guardie
  server-side esistenti (`ALREADY_SUBMITTED`, `ALREADY_VOTED`, guardie di
  fase) bastano; l'unica eccezione nota (`AddBot`, nessuna guardia) resta
  senza mitigazione, rimediabile con `RemoveBotAsync` già esistente (spec
  §1).
- Nessun cambiamento al protocollo, a `IGameConnection`, a
  `SignalRGameConnection` o al server: tutto il lavoro è nel client (spec
  §1, §3).
- Non toccare la retry policy del trasporto SignalR
  (`WithAutomaticReconnect`): fuori scope (spec §1).
- Lingua italiana in codice, commenti e messaggi di commit; commit firmati
  GPG.
- Baseline attuale (verificata con `dotnet test` prima di questo piano):
  848 test, 0 falliti (Shared 86, App 126, Domain 520, Server 116 + il
  flake noto e documentato di `GameHubTests`, backlog #2, che compare solo
  su run dell'intera suite — verificare sempre in isolamento prima di
  considerarlo una regressione).

---

### Task 1: Retry di riconnessione centralizzato in `EseguiComandoAsync`

**Files:**
- Modify: `src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs`
- Modify: `tests/FrasiSquisite.App.Tests/FakeGameConnection.cs` (nuova proprietà di test)
- Test: `tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs`

**Interfaces:**
- Produce: `GameSessionViewModel.ReconnectTransportAndRoomAsync()` (privato, `Task`) — usato anche dal Task 3.
- Consuma: `EnsureConnectedAsync()` (privato, già esistente), `_connection.RejoinRoomAsync(Guid, string)` (già esistente su `IGameConnection`), `FakeGameConnection.AlwaysFailWith` (nuovo, introdotto in questo task).

- [ ] **Step 1: Estendi `FakeGameConnection` con un guasto permanente**

In `tests/FrasiSquisite.App.Tests/FakeGameConnection.cs`, trova:

```csharp
    /// <summary>
    /// Se impostata, il prossimo metodo invocato la lancia invece di
    /// completare con successo (e si azzera da sola): simula un guasto di
    /// rete o di hub (es. HubException, HttpRequestException) senza dover
    /// implementare un vero trasporto solo per provare che la ViewModel
    /// gestisca il fallimento invece di propagarlo (spec C2).
    /// </summary>
    public Exception? NextFailure { get; set; }
```

Sostituiscilo con:

```csharp
    /// <summary>
    /// Se impostata, il prossimo metodo invocato la lancia invece di
    /// completare con successo (e si azzera da sola): simula un guasto di
    /// rete o di hub (es. HubException, HttpRequestException) senza dover
    /// implementare un vero trasporto solo per provare che la ViewModel
    /// gestisca il fallimento invece di propagarlo (spec C2).
    /// </summary>
    public Exception? NextFailure { get; set; }

    /// <summary>
    /// Se impostata, OGNI metodo invocato la lancia, senza azzerarsi:
    /// simula una rete giù per davvero (non un singolo blip), dove anche il
    /// tentativo di riconnessione fallisce. A differenza di
    /// <see cref="NextFailure"/> serve per provare il retry-di-riconnessione
    /// (design 2026-08-13 "retry di riconnessione") quando SIA il comando
    /// originale SIA il tentativo di riconnessione devono fallire.
    /// </summary>
    public Exception? AlwaysFailWith { get; set; }
```

Poi, nello stesso file, trova:

```csharp
    private void LanciaSeImpostato()
    {
        if (NextFailure is { } eccezione)
        {
            NextFailure = null;
            throw eccezione;
        }
    }
```

Sostituiscilo con:

```csharp
    private void LanciaSeImpostato()
    {
        if (AlwaysFailWith is { } permanente)
        {
            throw permanente;
        }

        if (NextFailure is { } eccezione)
        {
            NextFailure = null;
            throw eccezione;
        }
    }
```

- [ ] **Step 2: Scrivi i test che falliscono**

In `tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs`, subito dopo il test `UnGuastoNellInvioDellaCasellaVieneMostratoENonPortaInAttesa` (quello che imposta `conn.NextFailure = new HubException(...)`), aggiungi:

```csharp
    [Fact]
    public async Task UnGuastoDiTrasportoNellInvioVieneRitentatoConSuccesso()
    {
        var (vm, conn, _) = Crea();
        vm.RoomCode = "ABCD";
        conn.Emit(new SlotRequestMessage(0, 5, "Soggetto", "prompt", "esempio", GiaInviato: false));
        vm.SlotText = "Il cadavere";
        conn.NextFailure = new InvalidOperationException("guasto di trasporto simulato");

        await vm.SubmitSlotCommand.ExecuteAsync(null);

        Assert.Contains($"RejoinRoom({Anna},ABCD)", conn.Calls);
        Assert.Contains("SubmitSlot(ABCD,Il cadavere)", conn.Calls);
        Assert.Equal(string.Empty, vm.ErrorText);
        Assert.Equal(ScreenState.Waiting, vm.Screen);
    }

    [Fact]
    public async Task UnGuastoDiTrasportoCheNonSiRiconnetteMostraLErroreDiSempre()
    {
        var (vm, conn, _) = Crea();
        vm.RoomCode = "ABCD";
        conn.Emit(new SlotRequestMessage(0, 5, "Soggetto", "prompt", "esempio", GiaInviato: false));
        vm.SlotText = "Il cadavere";
        conn.AlwaysFailWith = new InvalidOperationException("rete giù per davvero");

        await vm.SubmitSlotCommand.ExecuteAsync(null);

        Assert.Equal("Non riesco a raggiungere il server.", vm.ErrorText);
        Assert.Equal(ScreenState.Writing, vm.Screen);
    }
```

- [ ] **Step 3: Esegui i due test e verifica che falliscano**

Run: `dotnet test tests/FrasiSquisite.App.Tests --filter "FullyQualifiedName~UnGuastoDiTrasportoNellInvioVieneRitentatoConSuccesso|FullyQualifiedName~UnGuastoDiTrasportoCheNonSiRiconnetteMostraLErroreDiSempre"`
Expected:
- `UnGuastoDiTrasportoNellInvioVieneRitentatoConSuccesso` FAIL — oggi `EseguiComandoAsync` non ritenta nulla: `ErrorText` diventa "Non riesco a raggiungere il server." invece di restare vuoto, e `Screen` resta `Writing`.
- `UnGuastoDiTrasportoCheNonSiRiconnetteMostraLErroreDiSempre` PASS già oggi (il comportamento attuale coincide con quello atteso) — è un test di non-regressione per il passo successivo, non un test TDD in senso stretto.

- [ ] **Step 4: Aggiungi `ReconnectTransportAndRoomAsync` ed estendi `EseguiComandoAsync`**

In `src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs`, trova:

```csharp
    private async Task EnsureConnectedAsync()
    {
        if (!_connection.IsConnected)
        {
            await _connection.ConnectAsync(ServerUrl);
        }
    }
```

Sostituiscilo con:

```csharp
    private async Task EnsureConnectedAsync()
    {
        if (!_connection.IsConnected)
        {
            await _connection.ConnectAsync(ServerUrl);
        }
    }

    /// <summary>
    /// Riconnette il trasporto e, se si crede già in una stanza, rientra
    /// anche lì (design 2026-08-13 "retry di riconnessione", §3.1). A
    /// differenza di <see cref="TryRejoinAsync"/> non inghiotte le
    /// eccezioni: chi lo chiama (il retry in <see cref="EseguiComandoAsync"/>
    /// o <see cref="ReconnectAsync"/>) decide come mostrarle.
    /// </summary>
    private async Task ReconnectTransportAndRoomAsync()
    {
        await EnsureConnectedAsync();

        if (RoomCode.Length > 0)
        {
            await _connection.RejoinRoomAsync(_playerId, RoomCode);
        }
    }
```

Poi, nello stesso file, trova:

```csharp
    private async Task EseguiComandoAsync(Func<Task> azione)
    {
        try
        {
            await azione();
        }
        catch (HubException ex)
        {
            // Il server risponde già con un messaggio pensato per l'utente,
            // in italiano (es. "Stanza non trovata.", "...Aggiorna l'app."):
            // lo si mostra così com'è, senza riformularlo.
            ErrorText = ex.Message;
        }
        catch (Exception)
        {
            // Guasto di trasporto (URL irraggiungibile, connessione caduta
            // prima ancora di parlare con l'hub, ecc.): non c'è un messaggio
            // del server da mostrare, quindi uno generico ma comunque visibile
            // - non deve mai sparire nel nulla.
            ErrorText = "Non riesco a raggiungere il server.";
        }
    }
```

Sostituiscilo con:

```csharp
    private async Task EseguiComandoAsync(Func<Task> azione)
    {
        try
        {
            await azione();
        }
        catch (HubException ex)
        {
            // Il server risponde già con un messaggio pensato per l'utente,
            // in italiano (es. "Stanza non trovata.", "...Aggiorna l'app."):
            // lo si mostra così com'è, senza riformularlo. Un rifiuto del
            // server non è un guasto di trasporto: ritentarlo otterrebbe
            // solo lo stesso rifiuto, quindi non passa dal retry sotto.
            ErrorText = ex.Message;
        }
        catch (Exception)
        {
            // Guasto di trasporto (URL irraggiungibile, connessione caduta
            // prima ancora di parlare con l'hub, ecc.): un solo tentativo di
            // riconnessione + rientro, poi si ripete l'azione una volta sola
            // (design 2026-08-13 "retry di riconnessione", §3.2). Nessun
            // backoff, nessun retry-del-retry: se fallisce ancora, stesso
            // messaggio generico di prima - l'utente può riprovare lui
            // stesso (azione o bottone "Riconnetti").
            try
            {
                await ReconnectTransportAndRoomAsync();
                await azione();
            }
            catch (HubException ex)
            {
                ErrorText = ex.Message;
            }
            catch (Exception)
            {
                ErrorText = "Non riesco a raggiungere il server.";
            }
        }
    }
```

- [ ] **Step 5: Rimuovi le chiamate a `EnsureConnectedAsync` ormai ridondanti**

In `src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs`, trova (dentro `CreateRoomAsync`):

```csharp
        _playerProfile.SaveNickname(Nickname);

        await EnsureConnectedAsync();
        RoomCode = await _connection.CreateRoomAsync(_playerId, Nickname);
    });
```

Sostituiscilo con:

```csharp
        _playerProfile.SaveNickname(Nickname);

        RoomCode = await _connection.CreateRoomAsync(_playerId, Nickname);
    });
```

Poi, nello stesso file, trova (dentro `JoinRoomAsync`):

```csharp
        _playerProfile.SaveNickname(Nickname);

        await EnsureConnectedAsync();
        RoomCode = JoinCode.Trim().ToUpperInvariant();
        await _connection.JoinRoomAsync(_playerId, Nickname, RoomCode);
    });
```

Sostituiscilo con:

```csharp
        _playerProfile.SaveNickname(Nickname);

        RoomCode = JoinCode.Trim().ToUpperInvariant();
        await _connection.JoinRoomAsync(_playerId, Nickname, RoomCode);
    });
```

Un fallimento di trasporto in queste due chiamate è ora coperto dal retry
generale in `EseguiComandoAsync` (Step 4): il primo tentativo fallisce con
`_connection` ancora `null` (`Hub` lancia `InvalidOperationException`), il
retry chiama `ReconnectTransportAndRoomAsync` che crea la connessione (e non
tenta un rientro, perché `RoomCode` è ancora vuoto in questo punto), poi
ripete `CreateRoomAsync`/`JoinRoomAsync` con la connessione ora pronta.

- [ ] **Step 6: Esegui tutta la suite di `App.Tests` e verifica che passi**

Run: `dotnet test tests/FrasiSquisite.App.Tests`
Expected: PASS — 128/128 (126 preesistenti, invariati nel comportamento,
+ i 2 nuovi test dello Step 2; nessuna asserzione esistente dipende
dall'ordine "Connect prima di CreateRoom/JoinRoom", verificato a mano
prima di questo piano).

- [ ] **Step 7: Commit**

```bash
git add src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs tests/FrasiSquisite.App.Tests/FakeGameConnection.cs tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs
git commit -m "feat(connessione): un guasto di trasporto ritenta riconnessione + rientro prima di fallire"
```

---

### Task 2: Il banner di connessione si svuota quando la connessione torna

**Files:**
- Modify: `src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs`
- Test: `tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs`

**Interfaces:**
- Consuma: nessuna nuova interfaccia — usa `ConnectionBanner` e `OnMessage` già esistenti.

- [ ] **Step 1: Scrivi il test che fallisce**

In `tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs`, subito dopo il test `UnInterruzioneDiConnessioneMostraUnAvviso`, aggiungi:

```csharp
    [Fact]
    public void UnMessaggioDalServerSvuotaIlBannerDiConnessione()
    {
        var (vm, conn, _) = Crea();

        conn.EmitConnectionInterrupted();
        Assert.False(string.IsNullOrEmpty(vm.ConnectionBanner));

        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [new PlayerView(Anna, "Anna", true, true, false)],
            "surrealista-classico", 5));

        Assert.Equal(string.Empty, vm.ConnectionBanner);
    }
```

- [ ] **Step 2: Esegui il test e verifica che fallisca**

Run: `dotnet test tests/FrasiSquisite.App.Tests --filter "FullyQualifiedName~UnMessaggioDalServerSvuotaIlBannerDiConnessione"`
Expected: FAIL — `ConnectionBanner` resta con il testo dell'avviso, non
viene mai svuotato da nessun messaggio in arrivo oggi.

- [ ] **Step 3: Svuota `ConnectionBanner` insieme a `ErrorText` in `OnMessage`**

In `src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs`, trova:

```csharp
        if (message is not ErrorMessage)
        {
            ErrorText = string.Empty;
        }
```

Sostituiscilo con:

```csharp
        // Qualunque messaggio dal server (incluso un RoomStateMessage dopo
        // un rientro riuscito) è prova che il giro di andata e ritorno
        // funziona di nuovo: sia l'errore sia l'avviso di connessione
        // instabile sono ormai stantii (design 2026-08-13 "retry di
        // riconnessione", §3.4).
        if (message is not ErrorMessage)
        {
            ErrorText = string.Empty;
            ConnectionBanner = string.Empty;
        }
```

- [ ] **Step 4: Esegui tutta la suite di `App.Tests` e verifica che passi**

Run: `dotnet test tests/FrasiSquisite.App.Tests`
Expected: PASS — 129/129 (128 dal Task 1 + questo nuovo test).

- [ ] **Step 5: Commit**

```bash
git add src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs
git commit -m "fix(connessione): il banner \"un bot gioca al tuo posto\" si svuota quando la connessione torna"
```

---

### Task 3: Bottone "Riconnetti" nel banner

**Files:**
- Modify: `src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs`
- Modify: `src/FrasiSquisite.App/Pages/GamePage.xaml`
- Test: `tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs`

**Interfaces:**
- Produce: `GameSessionViewModel.ReconnectCommand` (generato da `[RelayCommand]` su `ReconnectAsync`, `IAsyncRelayCommand`).
- Consuma: `ReconnectTransportAndRoomAsync()` (Task 1), `EseguiComandoAsync(Func<Task>)` (già esistente).

- [ ] **Step 1: Scrivi il test che fallisce**

In `tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs`, subito dopo il test `UnMessaggioDalServerSvuotaIlBannerDiConnessione` (Task 2), aggiungi:

```csharp
    [Fact]
    public async Task IlBottoneRiconnettiRipristinaIlTrasportoERientraInStanza()
    {
        var (vm, conn, _) = Crea();
        vm.RoomCode = "ABCD";

        await vm.ReconnectCommand.ExecuteAsync(null);

        Assert.Contains(conn.Calls, c => c.StartsWith("Connect(", StringComparison.Ordinal));
        Assert.Contains($"RejoinRoom({Anna},ABCD)", conn.Calls);
    }

    [Fact]
    public async Task IlBottoneRiconnettiSenzaStanzaNonTentaUnRientro()
    {
        var (vm, conn, _) = Crea();

        await vm.ReconnectCommand.ExecuteAsync(null);

        Assert.Contains(conn.Calls, c => c.StartsWith("Connect(", StringComparison.Ordinal));
        Assert.DoesNotContain(conn.Calls, c => c.StartsWith("RejoinRoom", StringComparison.Ordinal));
    }
```

- [ ] **Step 2: Esegui i due test e verifica che falliscano a compilare**

Run: `dotnet test tests/FrasiSquisite.App.Tests --filter "FullyQualifiedName~IlBottoneRiconnetti"`
Expected: FAIL a compilare — `GameSessionViewModel` non contiene ancora
`ReconnectCommand` (il generatore di `[RelayCommand]` non l'ha ancora
creato).

- [ ] **Step 3: Aggiungi `ReconnectCommand`**

In `src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs`, trova:

```csharp
    private void OnConnectionInterrupted()
    {
        // Il trasporto è giù o ci sta provando (Reconnecting/Closed): il
        // giocatore potrebbe restare temporaneamente sostituito da un bot
        // finché non rientra per davvero. Reconnected (sopra, OnReconnected)
        // è separato apposta: solo lì ha senso tentare un rientro, non qui.
        ConnectionBanner = "Connessione persa: un bot sta giocando al tuo posto.";
    }
```

Sostituiscilo con:

Una bozza precedente faceva passare `ReconnectAsync` da
`EseguiComandoAsync(ReconnectTransportAndRoomAsync)`, ma quel wrapper
ritenta l'azione al suo interno: essendo qui l'azione e il meccanismo di
recupero la stessa cosa, il risultato era un secondo `RejoinRoomAsync`
duplicato verso il server a una singola pressione. Il try/catch standalone
sotto evita il problema non passando da `EseguiComandoAsync`:

```csharp
    /// <summary>
    /// Un solo tentativo per pressione, senza passare da EseguiComandoAsync:
    /// quel wrapper ritenterebbe chiamando ReconnectTransportAndRoomAsync una
    /// seconda volta al suo interno - dato che qui è sia l'azione sia il
    /// meccanismo di recupero, il risultato sarebbe un secondo RejoinRoomAsync
    /// duplicato verso il server a una singola pressione (design 2026-08-13
    /// "retry di riconnessione", §3.3: "nessun retry-del-retry interno").
    /// </summary>
    [RelayCommand]
    private async Task ReconnectAsync()
    {
        try
        {
            await ReconnectTransportAndRoomAsync();
        }
        catch (HubException ex)
        {
            ErrorText = ex.Message;
        }
        catch (Exception)
        {
            ErrorText = "Non riesco a raggiungere il server.";
        }
    }

    private void OnConnectionInterrupted()
    {
        // Il trasporto è giù o ci sta provando (Reconnecting/Closed): il
        // giocatore potrebbe restare temporaneamente sostituito da un bot
        // finché non rientra per davvero. Reconnected (sopra, OnReconnected)
        // è separato apposta: solo lì ha senso tentare un rientro, non qui.
        ConnectionBanner = "Connessione persa: un bot sta giocando al tuo posto.";
    }
```

- [ ] **Step 4: Esegui i due test e verifica che passino**

Run: `dotnet test tests/FrasiSquisite.App.Tests --filter "FullyQualifiedName~IlBottoneRiconnetti"`
Expected: PASS — 2/2.

- [ ] **Step 5: Aggiungi il bottone "Riconnetti" in `GamePage.xaml`**

In `src/FrasiSquisite.App/Pages/GamePage.xaml`, trova:

```xml
            <Label Text="{Binding ConnectionBanner}" TextColor="OrangeRed"
                   IsVisible="{Binding ConnectionBanner, Converter={StaticResource NotEmpty}}" />
```

Sostituiscilo con:

```xml
            <Label Text="{Binding ConnectionBanner}" TextColor="OrangeRed"
                   IsVisible="{Binding ConnectionBanner, Converter={StaticResource NotEmpty}}" />
            <Button Text="Riconnetti" Style="{StaticResource SecondaryButton}"
                    Command="{Binding ReconnectCommand}"
                    IsVisible="{Binding ConnectionBanner, Converter={StaticResource NotEmpty}}" />
```

- [ ] **Step 6: Esegui tutta la suite di `App.Tests` e verifica che passi**

Run: `dotnet test tests/FrasiSquisite.App.Tests`
Expected: PASS — 131/131 (129 dal Task 2 + i 2 nuovi test di questo task;
il cambio a `GamePage.xaml` non ha test automatici, verifica manuale
sotto).

- [ ] **Step 7: Commit**

```bash
git add src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs src/FrasiSquisite.App/Pages/GamePage.xaml tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs
git commit -m "feat(connessione): bottone \"Riconnetti\" esplicito nel banner di connessione"
```

---

## Verifica manuale (fuori dal piano, per chi gioca dopo)

Non automatizzabile da qui: con l'app in esecuzione, disattivare il Wi-Fi
o attivare una VPN che blocchi il server a metà partita, poi:
1. Toccare un'azione di gioco (invia casella, vota) e verificare che, se
   la rete torna proprio in quel momento, l'azione vada comunque a buon
   fine senza dover premere due volte.
2. Con la rete giù, verificare che compaia il bottone "Riconnetti" accanto
   al banner, e che premerlo dopo aver ripristinato la rete faccia sparire
   il banner e riporti la partita in sincrono.
