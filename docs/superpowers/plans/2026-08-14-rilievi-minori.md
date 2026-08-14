# Rilievi minori delle revisioni — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Chiudere i 7 rilievi minori elencati in `docs/superpowers/backlog.md` §4 — nessuno bloccante, ma "una decisione presa e non scritta diventa una svista". Quattro producono codice/test reali (test di regressione mancanti, un bug vero sul prefisso reverse-proxy, un tetto configurabile al costo AI); tre sono decisioni già corrette nel comportamento ma non ancora documentate dove serve, e vengono chiuse con un commento o una voce nel brain, non con codice che non farebbe nulla di diverso da oggi.

**Architettura:** Nessun tema architetturale comune — sono 7 rilievi indipendenti, ciascuno isolato nel proprio task. Un solo rilievo (il tetto alle illustrazioni) tocca il motore (`FrasiSquisite.Domain`); gli altri restano nel client (`FrasiSquisite.App`) o nella loro documentazione.

**Tech Stack:** .NET 10, xUnit, MAUI (client), `brain` CLI (per le decisioni non tecniche).

## Global Constraints

- **L'"apostrofi ASCII nei commenti" (ottava voce del backlog §4) è esplicitamente esclusa da questo piano.** Il backlog stesso lo dice: "Va fatta una passata in un commit suo su tutto il repository, non annegata in un lotto funzionale." Non toccarla qui.
- Ogni task è indipendente dagli altri: possono essere eseguiti in qualsiasi ordine, anche da rami/sessioni diverse.
- Per i task che usano il `brain` CLI, la skill `brain-page` (`~/.claude/skills/brain-page/SKILL.md`) è già installata: invocala per il comando esatto invece di modificare i file sotto `brain/` a mano.

---

### Task 1: Test di regressione — host retrocesso non vede più "Illustra"

**Files:**
- Modify: `tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs` (aggiunge un test dopo `UnHostPromossoDopoLaClassificaPuoIllustrare`, righe 1863-1882)

**Interfaccia:** nessuna — solo un test, nessun codice di produzione cambia. `GameSessionViewModel.OnIsHostChanged` (`src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs:1015-1033`) è già corretto: propaga `IsHost` a ogni riga di `FinalResults` a ogni cambio, in entrambe le direzioni. Manca solo il test che lo blocchi in caso di regressione futura.

- [ ] **Step 1: Scrivi il test — verso opposto di `UnHostPromossoDopoLaClassificaPuoIllustrare`**

Aggiungi in `tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs`, subito dopo `UnHostPromossoDopoLaClassificaPuoIllustrare` (riga 1882):

```csharp
    /// <summary>
    /// Gemello del test sopra, verso opposto: un host retrocesso (un altro
    /// giocatore diventa host al suo posto) non deve più vedere "Illustra".
    /// Il codice è già simmetrico e corretto (OnIsHostChanged propaga
    /// IsHost a ogni riga a ogni cambio, in entrambe le direzioni), ma senza
    /// questo test niente bloccherebbe chi "ottimizzasse"
    /// OnIsHostChanged con un `if (value)` che gestisse solo la promozione
    /// (backlog.md §4, rilievo 1).
    /// </summary>
    [Fact]
    public void UnHostRetrocessoDopoLaClassificaNonPuoPiuIllustrare()
    {
        var (vm, conn) = InFinale(ioSonoHost: true);
        var riga = vm.FinalResults[0];
        Assert.True(riga.CanRequest);

        // Anna (il giocatore locale) perde l'host: un altro giocatore lo
        // diventa, la stanza trasmette il nuovo stato - dopo la classifica
        // finale, come nel test gemello sopra.
        conn.Emit(new RoomStateMessage(
            "ABCD",
            "Lobby",
            [new PlayerView(Anna, "Anna", false, true, false), new PlayerView(Guid.NewGuid(), "Bruno", true, true, false)],
            "storia",
            8));

        Assert.False(vm.IsHost);
        Assert.False(riga.CanRequest);
    }
```

- [ ] **Step 2: Esegui il test e verifica che passi già (comportamento corretto, solo il test mancava)**

```bash
dotnet test tests/FrasiSquisite.App.Tests/FrasiSquisite.App.Tests.csproj --no-restore --filter "FullyQualifiedName~UnHostRetrocessoDopoLaClassificaNonPuoPiuIllustrare"
```

Atteso: PASS al primo colpo — se fallisse, sarebbe una regressione reale da investigare, non un test da aggiustare per farlo passare.

- [ ] **Step 3: Esegui l'intera suite del progetto app**

```bash
dotnet test tests/FrasiSquisite.App.Tests/FrasiSquisite.App.Tests.csproj --no-restore
```

Atteso: tutti i test passano.

- [ ] **Step 4: Commit**

```bash
git add tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs
git commit -m "test: regressione sulla retrocessione dell'host e il pulsante Illustra

Gemello di UnHostPromossoDopoLaClassificaPuoIllustrare: il codice era
già corretto in entrambe le direzioni, mancava solo il test che
blocchi una futura ottimizzazione asimmetrica di OnIsHostChanged
(backlog.md §4, rilievo 1)."
```

---

### Task 2: Test di regressione — un esito di illustrazione tardivo non tocca una partita nuova

**Files:**
- Modify: `tests/FrasiSquisite.Domain.Tests/Engine/IllustrazioneTests.cs`

**Interfaccia:** nessuna — solo un test. `GameEngine.Illustration.cs:47-50` (`OnIllustrationFinished`) già scarta ogni esito che arriva mentre `state.Phase != RoomPhase.Finished`, che è esattamente la guardia contro un esito tardivo dopo che una nuova partita è iniziata. Manca il test che lo dimostri (backlog.md §4, rilievo 3: "da rivedere solo se il reveal diventasse automatico" — nessuna correzione richiesta ora, solo la prova che la guardia c'è).

- [ ] **Step 1: Scrivi il test**

Aggiungi in `tests/FrasiSquisite.Domain.Tests/Engine/IllustrazioneTests.cs`, dopo `UnAltraFraseSiPuoChiedereLoStesso`:

```csharp
    /// <summary>
    /// Se nel frattempo la stanza è tornata in Lobby (nuova partita, o
    /// ritorno alla lobby), un esito di illustrazione arrivato in ritardo
    /// per la partita precedente non deve avere alcun effetto: non è un
    /// errore da segnalare a nessuno (nessun giocatore l'ha chiesto in
    /// questo momento), è solo un evento interno da ignorare
    /// (backlog.md §4, rilievo 3).
    /// </summary>
    [Fact]
    public void UnEsitoTardivoDopoIlRitornoInLobbyNonHaEffetto()
    {
        var chiesta = _motore.Handle(AllaClassifica(), new IllustrationRequested(Giocatore(0), 1)).State;
        var nuovaPartita = _motore.Handle(chiesta, new BackToLobbyRequested(Giocatore(0))).State;

        var risultato = _motore.Handle(nuovaPartita, new IllustrationFinished(1, "/illustrazioni/tardiva"));

        Assert.Equal(nuovaPartita, risultato.State);
        Assert.Empty(risultato.Effects);
    }
```

Se `BackToLobbyRequested` non è il nome esatto dell'evento usato per "Torna alla lobby" nel motore, cercalo con `grep -rn "BackToLobby" src/FrasiSquisite.Domain/` prima di scrivere il test — il nome esatto è confermato dai test esistenti in `tests/FrasiSquisite.Domain.Tests/Engine/NuovaPartitaTests.cs`, che copre lo stesso scenario "torna alla lobby dopo la classifica" usato qui.

- [ ] **Step 2: Esegui il test e verifica che passi già**

```bash
dotnet test tests/FrasiSquisite.Domain.Tests/FrasiSquisite.Domain.Tests.csproj --no-restore --filter "FullyQualifiedName~UnEsitoTardivoDopoIlRitornoInLobbyNonHaEffetto"
```

Atteso: PASS al primo colpo — la guardia esiste già, questo è un test di caratterizzazione, non un fix.

- [ ] **Step 3: Esegui l'intera suite del progetto domain**

```bash
dotnet test tests/FrasiSquisite.Domain.Tests/FrasiSquisite.Domain.Tests.csproj --no-restore
```

Atteso: tutti i test passano.

- [ ] **Step 4: Commit**

```bash
git add tests/FrasiSquisite.Domain.Tests/Engine/IllustrazioneTests.cs
git commit -m "test: un esito di illustrazione tardivo dopo il ritorno in lobby non ha effetto

La guardia Phase != Finished in OnIllustrationFinished la gestisce
già; mancava solo la prova esplicita (backlog.md §4, rilievo 3)."
```

---

### Task 3: Fix — il percorso dell'immagine ignora un prefisso reverse-proxy

**Files:**
- Modify: `src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs:919-931`
- Test: `tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs`

**Interfaccia:** nessuna cambia — solo il corpo del case `IllustrationReadyMessage` nello switch dei messaggi in arrivo.

Bug reale: `new Uri(new Uri(ServerUrl), pronta.Path)` con `pronta.Path` che inizia per `/` (es. `/illustrazioni/ab12`) **scarta qualsiasi path già presente in `ServerUrl`** — comportamento standard di `Uri`: un riferimento che inizia con `/` sostituisce l'intero path dell'URI base. Funziona oggi solo perché il deployment in uso (Caddy a sottodominio) non mette mai un path davanti a `ServerUrl`. Con un reverse proxy a path-prefix (es. `ServerUrl = "https://host/frasi"`), il risultato sarebbe `https://host/illustrazioni/ab12` invece di `https://host/frasi/illustrazioni/ab12` (backlog.md §4, rilievo 5).

- [ ] **Step 1: Scrivi il test che riproduce il bug**

Aggiungi in `tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs`, subito dopo `LIndirizzoRelativoDiventaAssolutoConIlServerUrl` (riga 1904-1914):

```csharp
    /// <summary>
    /// Come sopra, ma con un ServerUrl che ha già un path (un reverse proxy
    /// a path-prefix, non a sottodominio come Caddy in produzione oggi): il
    /// prefisso va conservato, non scartato (backlog.md §4, rilievo 5).
    /// </summary>
    [Fact]
    public void UnPrefissoDiPercorsoNelServerUrlVieneMantenuto()
    {
        var (vm, conn) = InFinale();
        vm.ServerUrl = "http://test/frasi";

        conn.Emit(new IllustrationReadyMessage(1, "/illustrazioni/ab12"));

        Assert.Equal("http://test/frasi/illustrazioni/ab12", vm.FinalResults[0].ImageUrl);
    }
```

- [ ] **Step 2: Esegui il test e verifica che fallisca**

```bash
dotnet test tests/FrasiSquisite.App.Tests/FrasiSquisite.App.Tests.csproj --no-restore --filter "FullyQualifiedName~UnPrefissoDiPercorsoNelServerUrlVieneMantenuto"
```

Atteso: FAIL — il risultato oggi è `http://test/illustrazioni/ab12`, senza `/frasi`.

- [ ] **Step 3: Applica il fix**

`src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs`, sostituisci (righe 919-931):

```csharp
                case IllustrationReadyMessage pronta:
                    if (RigaDiFrase(pronta.PhraseIndex) is { } rigaPronta)
                    {
                        rigaPronta.IsWaiting = false;
                        // L'indirizzo arriva relativo perché il server non sa sotto
                        // quale nome è raggiunto: davanti c'è un reverse proxy.
                        // ServerUrl è l'unico posto dove quell'informazione c'è.
                        rigaPronta.ImageUrl = new Uri(new Uri(ServerUrl), pronta.Path).ToString();
                    }
                    break;
```

con:

```csharp
                case IllustrationReadyMessage pronta:
                    if (RigaDiFrase(pronta.PhraseIndex) is { } rigaPronta)
                    {
                        rigaPronta.IsWaiting = false;
                        // L'indirizzo arriva relativo perché il server non sa sotto
                        // quale nome è raggiunto: davanti c'è un reverse proxy.
                        // ServerUrl è l'unico posto dove quell'informazione c'è.
                        //
                        // pronta.Path inizia sempre per '/' (percorso assoluto dal
                        // server): combinato così com'è con Uri, scarterebbe
                        // qualunque path già presente in ServerUrl (es. un
                        // reverse proxy a path-prefix, non a sottodominio come
                        // quello in uso oggi). Si forza ServerUrl a finire con
                        // '/' e si toglie lo '/' iniziale dal percorso, cosi'
                        // Uri li unisce invece di sostituire.
                        var baseConSlash = ServerUrl.EndsWith('/') ? ServerUrl : ServerUrl + "/";
                        rigaPronta.ImageUrl = new Uri(new Uri(baseConSlash), pronta.Path.TrimStart('/')).ToString();
                    }
                    break;
```

- [ ] **Step 4: Esegui il test e verifica che passi**

```bash
dotnet test tests/FrasiSquisite.App.Tests/FrasiSquisite.App.Tests.csproj --no-restore --filter "FullyQualifiedName~UnPrefissoDiPercorsoNelServerUrlVieneMantenuto"
```

Atteso: PASS.

- [ ] **Step 5: Esegui anche il test esistente che copre il caso senza prefisso**

```bash
dotnet test tests/FrasiSquisite.App.Tests/FrasiSquisite.App.Tests.csproj --no-restore --filter "FullyQualifiedName~LIndirizzoRelativoDiventaAssolutoConIlServerUrl"
```

Atteso: PASS — deve continuare a restituire `http://test/illustrazioni/ab12` esattamente come prima; il fix non deve cambiare il comportamento nel caso senza prefisso.

- [ ] **Step 6: Esegui l'intera suite del progetto app**

```bash
dotnet test tests/FrasiSquisite.App.Tests/FrasiSquisite.App.Tests.csproj --no-restore
```

Atteso: tutti i test passano.

- [ ] **Step 7: Commit**

```bash
git add src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs
git commit -m "fix(app): il percorso dell'illustrazione rispetta il prefisso di ServerUrl

new Uri(base, path) con path che inizia per '/' scartava qualunque
path già presente in ServerUrl - innocuo con Caddy a sottodominio
(il deployment in uso oggi), ma sbagliato con un reverse proxy a
path-prefix (backlog.md §4, rilievo 5)."
```

---

### Task 4: Tetto configurabile alle illustrazioni per stanza

**Files:**
- Modify: `src/FrasiSquisite.Domain/Engine/GameEngine.cs:10-28`
- Modify: `src/FrasiSquisite.Domain/Engine/GameEngine.Illustration.cs:12-40`
- Modify: `src/FrasiSquisite.Server/Ai/AiOptions.cs`
- Modify: `src/FrasiSquisite.Server/Program.cs`
- Test: `tests/FrasiSquisite.Domain.Tests/Engine/IllustrazioneTests.cs`

**Interfaccia:**
- Produce: `GameEngine(IGameMode mode, IWordPool pool, IRandomSource random, int massimoIllustrazioniPerStanza = int.MaxValue)` — il quarto parametro ha un default che **non cambia il comportamento odierno** per nessuno dei 13 punti del repo che costruiscono `GameEngine` senza specificarlo (12 file di test più `Program.cs`, verificato con `grep -rn "new GameEngine(" tests/ src/`).

Backlog.md §4, rilievo 7: "l'host può illustrare una frase per riga, a circa nove centesimi l'una. Se in una serata diventasse un problema, il posto dove metterlo è il motore... non il client, che non è la fonte della verità." Il contatore giusto esiste già: `state.IllustrationsRequested.Count` (`GameState.IllustrationsRequested`, `src/FrasiSquisite.Domain/Model/GameState.cs:20`) — l'indice resta nell'insieme dopo un successo apposta per impedire un secondo pagamento (commento in `src/FrasiSquisite.Server/Realtime/GameHost.cs:193-196`), quindi in fase `Finished` rappresenta esattamente "quante illustrazioni ha già pagato questa partita".

**Scelta di scoping deliberata:** `IllustrationsRequested` si azzera a ogni nuova partita (`GameEngine.Room.cs:98`, dentro la creazione di un nuovo `GameState`) — quindi questo tetto limita le illustrazioni **per singola partita conclusa**, non "per stanza per tutta la serata" attraverso più partite consecutive. Un tetto che sopravvivesse a "Nuova partita" richiederebbe un nuovo campo persistente in `GameState` distinto da `IllustrationsRequested`, una modifica di modello dati più ampia di quanto il backlog descriva concretamente. Il default `int.MaxValue` (nessun tetto, comportamento identico a oggi) rende il cambiamento un'aggiunta di meccanismo, non un cambio di comportamento silenzioso: un operatore che vuole davvero il tetto lo configura esplicitamente.

- [ ] **Step 1: Scrivi il test sul tetto raggiunto**

Aggiungi in `tests/FrasiSquisite.Domain.Tests/Engine/IllustrazioneTests.cs`, dopo `UnAltraFraseSiPuoChiedereLoStesso`:

```csharp
    /// <summary>
    /// Il tetto è per partita conclusa (IllustrationsRequested si azzera a
    /// ogni nuova partita): con un tetto di 1, la seconda richiesta - anche
    /// su una frase diversa dalla prima - viene rifiutata (backlog.md §4,
    /// rilievo 7).
    /// </summary>
    [Fact]
    public void OltreIlTettoConfiguratoLeRichiesteVengonoRifiutate()
    {
        var motoreConTetto = new GameEngine(new RoleSchemaMode(), new StaticWordPool(), new SeededRandomSource(1), massimoIllustrazioniPerStanza: 1);
        var statoConUna = motoreConTetto.Handle(AllaClassifica(), new IllustrationRequested(Giocatore(0), 1)).State;

        var risultato = motoreConTetto.Handle(statoConUna, new IllustrationRequested(Giocatore(0), 0));

        Assert.Empty(risultato.Effects.OfType<RequestIllustration>());
        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(0)));
        Assert.Equal("ILLUSTRATION_LIMIT_REACHED", errore.Code);
    }

    /// <summary>
    /// Senza specificare il parametro, il comportamento resta quello di
    /// sempre: nessun tetto. AllaClassifica() con N = K = 3 produce 3
    /// frasi, indici 0-2: tre richieste sullo stesso stato, tutte accettate,
    /// nessun tetto di default a fermarle.
    /// </summary>
    [Fact]
    public void SenzaConfigurazioneNonCESunTettoDiDefault()
    {
        var stato = AllaClassifica();
        for (var i = 0; i < 3; i++)
        {
            stato = _motore.Handle(stato, new IllustrationRequested(Giocatore(0), i)).State;
        }

        Assert.Equal(3, stato.IllustrationsRequested.Count);
    }
```

- [ ] **Step 2: Esegui i test e verifica che falliscano**

```bash
dotnet test tests/FrasiSquisite.Domain.Tests/FrasiSquisite.Domain.Tests.csproj --no-restore --filter "FullyQualifiedName~OltreIlTettoConfiguratoLeRichiesteVengonoRifiutate|FullyQualifiedName~SenzaConfigurazioneNonCESunTettoDiDefault"
```

Atteso: `OltreIlTettoConfiguratoLeRichiesteVengonoRifiutate` FAIL con errore di compilazione (`GameEngine` non ha un costruttore a 4 argomenti); `SenzaConfigurazioneNonCESunTettoDiDefault` passerebbe già oggi (nessun cambiamento necessario per farlo passare), ma non compila finché il primo non lo fa.

- [ ] **Step 3: Aggiungi il quarto parametro al costruttore primario**

`src/FrasiSquisite.Domain/Engine/GameEngine.cs`, sostituisci la riga 10:

```csharp
public sealed partial class GameEngine(IGameMode mode, IWordPool pool, IRandomSource random) : IGameEngine
```

con:

```csharp
public sealed partial class GameEngine(
    IGameMode mode,
    IWordPool pool,
    IRandomSource random,
    int massimoIllustrazioniPerStanza = int.MaxValue) : IGameEngine
```

e, subito dopo i campi `_mode`/`_pool`/`_random` (righe 26-28), aggiungi:

```csharp
    private readonly int _massimoIllustrazioniPerStanza = massimoIllustrazioniPerStanza;
```

- [ ] **Step 4: Applica il tetto in `OnIllustrationRequested`**

`src/FrasiSquisite.Domain/Engine/GameEngine.Illustration.cs`, sostituisci la firma (riga 12):

```csharp
    private static EngineResult OnIllustrationRequested(GameState state, IllustrationRequested e)
```

con (diventa un metodo d'istanza: deve leggere `_massimoIllustrazioniPerStanza`):

```csharp
    private EngineResult OnIllustrationRequested(GameState state, IllustrationRequested e)
```

e aggiungi il controllo subito dopo quello su `ILLUSTRATION_ALREADY_REQUESTED` (dopo la riga 32):

```csharp
        if (state.IllustrationsRequested.Contains(e.PhraseIndex))
        {
            return Error(state, e.RequestedBy, "ILLUSTRATION_ALREADY_REQUESTED", "Quella frase ce l'ha già.");
        }

        if (state.IllustrationsRequested.Count >= _massimoIllustrazioniPerStanza)
        {
            return Error(state, e.RequestedBy, "ILLUSTRATION_LIMIT_REACHED", "Limite di illustrazioni raggiunto per questa partita.");
        }
```

Nessuna modifica serve al sito di chiamata `GameEngine.cs:50` (`IllustrationRequested e => OnIllustrationRequested(state, e),`): resta valido sia per un metodo statico sia per uno d'istanza, dato che `Handle` è già un metodo d'istanza.

- [ ] **Step 5: Compila ed esegui i test del progetto domain**

```bash
dotnet build src/FrasiSquisite.Domain/FrasiSquisite.Domain.csproj --no-restore
dotnet test tests/FrasiSquisite.Domain.Tests/FrasiSquisite.Domain.Tests.csproj --no-restore
```

Atteso: nessun errore di compilazione (i 12 test file con `new GameEngine(...)` a 3 argomenti continuano a compilare grazie al default), tutti i test passano incluso i due nuovi.

- [ ] **Step 6: Aggiungi l'opzione configurabile**

`src/FrasiSquisite.Server/Ai/AiOptions.cs`, aggiungi dopo `Abilitato` (riga 69):

```csharp

    /// <summary>
    /// Tetto alle illustrazioni per singola partita conclusa (si azzera a
    /// ogni nuova partita, non "per serata": vedi la nota di scoping nel
    /// piano che ha introdotto questo campo). Default int.MaxValue: nessun
    /// tetto, comportamento identico a prima che questo campo esistesse.
    /// Ogni illustrazione costa circa nove centesimi (spec AI); un
    /// operatore che vuole limitare il costo lo configura esplicitamente
    /// (backlog.md §4, rilievo 7).
    /// </summary>
    public int MassimoIllustrazioniPerStanza { get; set; } = int.MaxValue;
```

- [ ] **Step 7: Cablala nella registrazione DI del motore**

`src/FrasiSquisite.Server/Program.cs`, sostituisci la riga (circa 24):

```csharp
builder.Services.AddSingleton<IGameEngine, GameEngine>();
```

con:

```csharp
builder.Services.AddSingleton<IGameEngine>(sp => new GameEngine(
    sp.GetRequiredService<IGameMode>(),
    sp.GetRequiredService<IWordPool>(),
    sp.GetRequiredService<IRandomSource>(),
    sp.GetRequiredService<IOptions<AiOptions>>().Value.MassimoIllustrazioniPerStanza));
```

Se `Microsoft.Extensions.Options` non è già importato in `Program.cs` (necessario per `IOptions<AiOptions>`), aggiungi in cima al file:

```csharp
using Microsoft.Extensions.Options;
```

- [ ] **Step 8: Compila e avvia il server**

```bash
dotnet build src/FrasiSquisite.Server/FrasiSquisite.Server.csproj --no-restore
dotnet run --project src/FrasiSquisite.Server/FrasiSquisite.Server.csproj --no-build &
sleep 5
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5000/illustrazioni/inventato
kill %1
```

Atteso: il server risponde (nessun errore di risoluzione DI — la registrazione di `IGameEngine` ora dipende da `IOptions<AiOptions>`, che è già configurato prima nel file con `builder.Services.Configure<AiOptions>(...)`, ma essendo una factory lazy l'ordine di registrazione fra le due righe non conta).

- [ ] **Step 9: Esegui l'intera suite del progetto server**

```bash
dotnet test tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore
```

Atteso: tutti i test passano (incluso `LHostChiedeLIllustrazioneEArrivaLImmaginePronta` in `GameHubTests.cs`, che passa dal `GameEngine` reale costruito dalla DI del server con il nuovo default `int.MaxValue`).

- [ ] **Step 10: Commit**

```bash
git add src/FrasiSquisite.Domain/Engine/GameEngine.cs src/FrasiSquisite.Domain/Engine/GameEngine.Illustration.cs src/FrasiSquisite.Server/Ai/AiOptions.cs src/FrasiSquisite.Server/Program.cs tests/FrasiSquisite.Domain.Tests/Engine/IllustrazioneTests.cs
git commit -m "feat(motore): tetto configurabile alle illustrazioni per partita

Default int.MaxValue: nessun cambio di comportamento finché non
configurato esplicitamente. Il contatore riusa IllustrationsRequested,
già usato per impedire un doppio pagamento sulla stessa frase
(backlog.md §4, rilievo 7)."
```

---

### Task 5: Documentare le tre decisioni prese e non scritte

**Files:**
- Modify: `src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs:966-968` (case `ErrorMessage`)
- Brain: due voci via `brain` CLI

Tre rilievi del backlog (§4, voci 2, 4, 6) descrivono comportamenti **già corretti** nel codice, dove l'unica cosa mancante è scrivere da qualche parte *perché* sono così — altrimenti, come dice il backlog, "una decisione presa e non scritta diventa una svista" che qualcuno rimette in discussione senza sapere che è già stata valutata.

- [ ] **Step 1: Commento sul perché `IsWaiting` non si spegne su un `ErrorMessage` generico (rilievo 4)**

`src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs`, il case `ErrorMessage` (righe 966-968) oggi è:

```csharp
                case ErrorMessage errore:
                    ErrorText = errore.Message;
                    break;
```

Sostituiscilo con:

```csharp
                case ErrorMessage errore:
                    // ErrorMessage non porta l'indice della frase, quindi non
                    // c'è modo di sapere quale riga di FinalResults spegnere:
                    // oggi comunque irraggiungibile (le guardie del motore su
                    // IllustrationRequested rispondono tutte con un codice
                    // specifico). Lasciato così di proposito - spegnere
                    // IsWaiting su un errore qualunque riabiliterebbe il
                    // pulsante "Illustra" mentre una generazione è ancora in
                    // corso altrove, che è peggio di una rotellina che gira
                    // un po' più a lungo del dovuto (backlog.md §4, rilievo 4).
                    ErrorText = errore.Message;
                    break;
```

- [ ] **Step 2: Compila per verificare che il commento non abbia rotto la sintassi**

```bash
dotnet build src/FrasiSquisite.App/FrasiSquisite.App.csproj --no-restore
```

Atteso: nessun errore.

- [ ] **Step 3: Commit del commento**

```bash
git add src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs
git commit -m "docs: perche' IsWaiting non si spegne su un ErrorMessage generico

Decisione gia' corretta nel codice, non ancora scritta da nessuna
parte (backlog.md §4, rilievo 4)."
```

- [ ] **Step 4: Registra nel brain la decisione di rimandare `TreatWarningsAsErrors` repo-wide (rilievo 2)**

Il rilievo 2 del backlog (`ImageStore.Salva` torna `string?`, nullable non imposto come errore) non ha un fix puntuale da applicare: il chiamante esistente (`GameHost.AvviaIllustrazione`, `src/FrasiSquisite.Server/Realtime/GameHost.cs:210-215`) gestisce già correttamente il caso `null`, ed è già commentato lì. Il rimedio vero — `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in `Directory.Build.props` — il backlog stesso lo definisce "una decisione di repository, non di un singolo ramo": attivarlo qui, alla cieca, potrebbe far esplodere la build su warning nullable preesistenti altrove nel repository, mai misurati. Registra la decisione di rimandarlo invece di implementarlo silenziosamente:

Invoca la skill `brain-page` per farti confermare il percorso esatto del bundle installato (varia da macchina a macchina), poi definisci la funzione di shell come documentato lì:

```bash
brain() { node "<percorso-del-bundle-brain-page>/bin/brain.mjs" "$@"; }
brain create-page --id treatwarningsaserrors-rimandato --category decision \
  --title "TreatWarningsAsErrors repo-wide: rimandato, non rifiutato" \
  --tags nullable,build,repository --status active \
  --source "docs/superpowers/backlog.md §4 rilievo 2; piano 2026-08-14-rilievi-minori"
```

Poi popola il compiled_truth (heredoc, non `echo` fra apici singoli: il testo contiene apostrofi italiani veri, che andrebbero preservati esattamente — `un'altra`, `l'ImageStore`, ecc. — e un `echo '...'` in apici singoli non può contenerli senza spezzare la stringa di shell):

```bash
brain update-truth --id treatwarningsaserrors-rimandato \
  --summary "Decisione iniziale: rimandato, non rifiutato" \
  --source "docs/superpowers/backlog.md §4 rilievo 2" <<'EOF'
**Cosa:** `ImageStore.Salva` torna `string?` e il nullable non è imposto
come errore di build in nessun progetto del repository (nessun
`TreatWarningsAsErrors`/`WarningsAsErrors` in `Directory.Build.props` o
nei singoli `.csproj`, verificato con una ricerca esaustiva il
2026-08-14).

**Perché non è stato attivato qui:** il chiamante esistente di `Salva`
(`GameHost.AvviaIllustrazione`) gestisce già correttamente il caso
`null` — non è un bug in produzione, è mancanza di una rete di
sicurezza a livello di compilatore. Attivare `TreatWarningsAsErrors`
è per costruzione una modifica repository-wide (vive in
`Directory.Build.props`, non in un singolo progetto): può far
emergere warning nullable preesistenti altrove, mai misurati, e
farebbe fallire la build in un punto imprevedibile per un rilievo
minore isolato.

**Quando riconsiderarlo:** la prossima volta che si tocca
`Directory.Build.props` per un altro motivo, o come iniziativa a sé
stante con la sua build di verifica dedicata — non dentro un lotto di
rilievi minori.
EOF
```

- [ ] **Step 5: Registra nel brain il trade-off accettato sull'eviction FIFO globale (rilievo 6)**

```bash
brain create-page --id imagestore-eviction-fifo-globale --category decision \
  --title "ImageStore: eviction FIFO globale fra stanze, accettata" \
  --tags immagini,imagestore,eviction --status active \
  --source "docs/superpowers/backlog.md §4 rilievo 6; piano 2026-08-14-rilievi-minori"
```

```bash
brain update-truth --id imagestore-eviction-fifo-globale \
  --summary "Decisione iniziale: FIFO globale accettata, non per-stanza" \
  --source "docs/superpowers/backlog.md §4 rilievo 6" <<'EOF'
**Cosa:** `ImageStore` (`src/FrasiSquisite.Server/Images/ImageStore.cs`) è un
unico singleton condiviso da tutte le stanze, con una coda FIFO e un
budget in byte **globali** (default 75MB), non per-stanza. Quando il
budget sfora, `Salva` sfratta le immagini più vecchie in assoluto,
indipendentemente da quale stanza le ha prodotte: il traffico di una
partita attiva può far sfrattare l'immagine ancora visibile di
un'altra partita conclusa in un'altra stanza.

**Perché è accettato così:** renderlo per-stanza richiederebbe
riservare una fetta di budget a ogni stanza attiva (o un limite per
stanza sopra a quello globale), il che a sua volta richiede più
memoria di ricambio per non sprecare budget con stanze inattive — il
backlog stima ~75MB in più per eliminare del tutto la contesa. Non è
stato implementato: il traffico reale (poche stanze contemporanee, in
un contesto amicale) rende la contesa fra stanze concorrenti rara
nella pratica.

**Quando riconsiderarlo:** se il numero di stanze concorrenti crescesse
davvero (uso non più solo fra amici), o se uno sfratto cross-stanza
venisse osservato giocando, non solo dedotto dal codice.
EOF
```

- [ ] **Step 6: Verifica i link del brain**

```bash
brain lint-links
```

Atteso: nessun link rotto.

Non serve alcun commit git per gli Step 4-5: le pagine del brain vivono sotto `brain/` e sono tracciate da git come ogni altro file del repository — verifica con `git status` che i nuovi file `brain/pages/treatwarningsaserrors-rimandato.md` e `brain/pages/imagestore-eviction-fifo-globale.md` (più `brain/index.md` rigenerato) compaiano, poi:

```bash
git add brain/
git commit -m "docs(brain): due decisioni sui rilievi minori del backlog

TreatWarningsAsErrors repo-wide e l'eviction FIFO globale di
ImageStore sono comportamenti gia' corretti/accettati, non bug da
correggere: la voce nel brain e' cio' che manca perche' non tornino
in discussione senza sapere che sono gia' state valutate
(backlog.md §4, rilievi 2 e 6)."
```
