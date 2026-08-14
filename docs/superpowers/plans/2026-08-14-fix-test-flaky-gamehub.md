# Fix del test flaky GameHubTests — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminare la flakiness intermittente di `GameHubTests` (timeout che scade solo quando gira la suite intera), causata da fame di thread nel pool.

**Architettura:** `GameHubTests` oggi costruisce un `WebApplicationFactory<Program>` nuovo per ognuno dei 22 metodi `[Fact]` (22 host ASP.NET completi per esecuzione della classe) e lo smaltisce con `.Dispose()` sincrono su un host `IAsyncDisposable`, bloccando un thread del pool a ogni test mentre le altre classi di test girano in parallelo (xUnit parallelizza le classi fino al numero di core, non c'è `xunit.runner.json`). Il fix procede in tre passi di costo/resa crescente, ciascuno verificato empiricamente prima di passare al successivo: (1) dispose realmente asincrono, (2) una sola `WebApplicationFactory` condivisa per tutta la classe invece di 22, (3) un tetto al parallelismo xUnit come rete di sicurezza.

**Tech Stack:** .NET 10, xUnit, `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory<T>`).

## Global Constraints

- Non toccare `WaitFor`/`WaitForCount` (righe interne a `GameHubTests.cs`): sono già attese su condizione con polling a 20ms, il tempo fisso è solo il tetto massimo — non è la causa della flakiness.
- Non introdurre nessun pacchetto NuGet aggiuntivo: `xunit.runner.visualstudio` (già referenziato) legge già `xunit.runner.json` a runtime se presente nella directory di output.
- Ogni fix va verificato per evidenza empirica (esecuzioni ripetute della suite), non solo per lettura del codice: la flakiness è intermittente, un singolo run verde non è prova di nulla.

---

### Task 1: Dispose asincrono di `WebApplicationFactory` in `GameHubTests`

**Files:**
- Modify: `tests/FrasiSquisite.Server.Tests/Realtime/GameHubTests.cs:32-35`

**Interfaccia toccata:** nessuna, cambia solo il corpo di `DisposeAsync()` — nessun altro file dipende da questo metodo.

Questo è il fix più economico, isolato, e va misurato e committato da solo prima di passare al Task 2 — anche se il Task 2 lo supererà strutturalmente, dà un punto di dati indipendente su quanto pesava da solo il dispose sincrono.

- [ ] **Step 1: Misura la baseline di flakiness**

Esegui la suite del progetto server 5 volte di fila (la flakiness si manifesta "solo quando gira la suite intera", quindi non filtrare a una singola classe):

```bash
for i in 1 2 3 4 5; do
  dotnet test tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore -v q 2>&1 | tail -5
done
```

Annota quante delle 5 esecuzioni falliscono e su quali test (di solito `GameHubTests`, secondo la diagnosi già fatta in `docs/superpowers/backlog.md` §2).

- [ ] **Step 2: Applica il fix**

In `tests/FrasiSquisite.Server.Tests/Realtime/GameHubTests.cs`, sostituisci:

```csharp
    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }
```

con:

```csharp
    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
```

- [ ] **Step 3: Ricompila e rimisura**

```bash
dotnet build tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore
```

Poi ripeti lo Step 1 (5 esecuzioni). Annota il nuovo tasso di fallimento.

- [ ] **Step 4: Commit**

```bash
git add tests/FrasiSquisite.Server.Tests/Realtime/GameHubTests.cs
git commit -m "fix(test): dispose asincrono di WebApplicationFactory in GameHubTests

Dispose() sincrono su un host IAsyncDisposable blocca un thread del
pool a ogni test; con 22 Fact nella classe è un candidato concreto
per la flakiness diagnosticata in docs/superpowers/backlog.md §2."
```

---

### Task 2: `WebApplicationFactory` condivisa per classe via `IClassFixture`

**Files:**
- Modify: `tests/FrasiSquisite.Server.Tests/Realtime/GameHubTests.cs:19-35` (rimuove `IAsyncLifetime`, aggiunge una factory dedicata)

**Interfaccia:**
- Consuma: il pattern già in uso e funzionante in `tests/FrasiSquisite.Server.Tests/Realtime/IllustrazioneEndpointTests.cs:9-10` (`IClassFixture<WebApplicationFactory<Program>>`).
- Produce: `_factory` resta un campo `WebApplicationFactory<Program>` leggibile da tutto il resto della classe esattamente come oggi (righe 187, 653, 719, 865, 981 lo usano già solo per leggerlo o derivarne una copia via `.WithWebHostBuilder(...)`, non lo mutano mai) — nessun'altra riga del file va toccata oltre a quelle di questo task.

Con `IClassFixture<T>`, xUnit costruisce **un'unica istanza** di `T` per tutta la classe (i 22 `[Fact]`), non una per test. Perché quell'istanza porti già la sovrascrittura di `IGracePeriodTimer` (oggi applicata in `InitializeAsync` via `.WithWebHostBuilder(...)`), serve una sottoclasse dedicata di `WebApplicationFactory<Program>` che la applichi in `ConfigureWebHost` — la fixture grezza `WebApplicationFactory<Program>` non la porterebbe. La sottoclasse implementa anche `Xunit.IAsyncLifetime` esplicitamente, così xUnit smaltisce la fixture con un vero `await`, non con l'`IDisposable.Dispose()` sincrono che il framework userebbe altrimenti di default a fine classe (un solo dispose sincrono a fine classe sarebbe già un miglioramento enorme rispetto a 22, ma resta evitabile del tutto).

**Nota sui test che derivano una factory propria** (righe 653, 719, 981): usano già `_factory.WithWebHostBuilder(builder => ...)` per sovrascrivere un servizio specifico (es. `TimerControllabilePerTest` alla riga 719) e la smaltiscono con `await using`. `WithWebHostBuilder` restituisce sempre una **nuova** istanza di factory senza toccare quella condivisa: questo pattern continua a funzionare identico dopo il fix, non richiede modifiche.

**Nota sui singleton condivisi** (`IRoomRegistry`, `ImageStore`, entrambi registrati come singleton in `src/FrasiSquisite.Server/Program.cs:26,31`): con una factory condivisa, questi diventano condivisi fra tutti i 22 test della classe invece che isolati per test. Non è un problema nuovo introdotto da questo task — ogni stanza usa un codice a 4 caratteri generato casualmente (`RoomCodeGenerator`), quindi le collisioni fra test sono trascurabili quanto lo sono già in produzione fra partite concorrenti reali. Il test alla riga 667 che chiama `factory.Services.GetRequiredService<IRoomRegistry>()` per rimuovere una stanza direttamente dal registro continua a funzionare: opera su un codice stanza specifico di quel test.

- [ ] **Step 1: Estrai `VelocizzaGraziaTimer` e crea `GameHubTestsFactory`**

In `tests/FrasiSquisite.Server.Tests/Realtime/GameHubTests.cs`, aggiungi in cima al file, subito dopo il blocco `using` e prima di `namespace` (nessun using aggiuntivo serve: `IWebHostBuilder` arriva già transitivamente da `Microsoft.AspNetCore.Mvc.Testing`, verificato dal fatto che `builder.ConfigureServices` è già usato oggi nello stesso file senza using espliciti per `IWebHostBuilder`):

```csharp
namespace FrasiSquisite.Server.Tests.Realtime;

/// <summary>
/// Un'unica istanza per tutta la classe (via IClassFixture sotto), non una
/// per test: 22 host ASP.NET completi per esecuzione della classe erano un
/// candidato concreto per la flakiness intermittente diagnosticata in
/// docs/superpowers/backlog.md §2. Implementa Xunit.IAsyncLifetime
/// esplicitamente perché xUnit smaltisca la fixture con un vero await
/// invece del Dispose() sincrono che userebbe di default su un
/// IAsyncDisposable a fine classe.
/// </summary>
public sealed class GameHubTestsFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
            services.AddSingleton<IGracePeriodTimer>(new VelocizzaGraziaTimer()));

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync() => await base.DisposeAsync();

    /// <summary>
    /// Il periodo di grazia di produzione dura 30s reali. Qui si accorcia a
    /// una manciata di millisecondi — abbastanza perché un rientro immediato
    /// lo batta ancora sul tempo, ma senza aspettare per davvero i 30s di
    /// produzione in ogni test che tocca una disconnessione.
    /// </summary>
    private sealed class VelocizzaGraziaTimer : IGracePeriodTimer
    {
        public Task DelayAsync(TimeSpan durata, CancellationToken ct) => Task.Delay(TimeSpan.FromMilliseconds(200), ct);
    }
}
```

- [ ] **Step 2: Converti `GameHubTests` a `IClassFixture<GameHubTestsFactory>`**

Sostituisci la dichiarazione della classe e la rimozione del ciclo di vita manuale:

```csharp
public sealed class GameHubTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton<IGracePeriodTimer>(new VelocizzaGraziaTimer())));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
```

con:

```csharp
public sealed class GameHubTests(GameHubTestsFactory fabbricaCondivisa) : IClassFixture<GameHubTestsFactory>
{
    private readonly WebApplicationFactory<Program> _factory = fabbricaCondivisa;
```

- [ ] **Step 3: Rimuovi la classe `VelocizzaGraziaTimer` originale dal corpo di `GameHubTests`**

Cerca la definizione a riga ~162 (dentro `GameHubTests`, non quella appena creata in `GameHubTestsFactory`):

```csharp
    /// <summary>
    /// Il periodo di grazia di produzione dura 30s reali. Qui si accorcia a
    /// una manciata di millisecondi — abbastanza perché un rientro immediato
    /// lo batta ancora sul tempo, ma senza aspettare per davvero i 30s di
    /// produzione in ogni test che tocca una disconnessione.
    /// </summary>
    private sealed class VelocizzaGraziaTimer : IGracePeriodTimer
    {
        public Task DelayAsync(TimeSpan durata, CancellationToken ct) => Task.Delay(TimeSpan.FromMilliseconds(200), ct);
    }
```

Rimuovila per intero: ora vive solo dentro `GameHubTestsFactory` (Step 1). `TimerControllabilePerTest`, subito sotto nel file originale, resta dov'è — è usata da un test specifico (riga 718) tramite una factory derivata via `WithWebHostBuilder`, non dalla fixture condivisa.

- [ ] **Step 4: Compila**

```bash
dotnet build tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore
```

Atteso: nessun errore. Se il compilatore segnala `IWebHostBuilder` non trovato, aggiungi `using Microsoft.AspNetCore.Hosting;` in cima al file.

- [ ] **Step 5: Esegui l'intera classe una volta e verifica che passi tutta**

```bash
dotnet test tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~GameHubTests"
```

Atteso: 22 test, 0 falliti.

- [ ] **Step 6: Rimisura la flakiness sull'intera suite (5 esecuzioni, come nel Task 1)**

```bash
for i in 1 2 3 4 5; do
  dotnet test tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore -v q 2>&1 | tail -5
done
```

Confronta col tasso di fallimento misurato dopo il Task 1. Se è a zero su tutte e 5 le esecuzioni, il Task 3 diventa una rete di sicurezza opzionale ma va comunque implementato (è a costo pressoché nullo e protegge da regressioni future se la suite cresce).

- [ ] **Step 7: Commit**

```bash
git add tests/FrasiSquisite.Server.Tests/Realtime/GameHubTests.cs
git commit -m "fix(test): una sola WebApplicationFactory condivisa per GameHubTests

22 host ASP.NET completi per esecuzione della classe (uno per Fact)
erano il principale candidato per la flakiness intermittente. Segue
lo stesso pattern IClassFixture già in uso e funzionante in
IllustrazioneEndpointTests."
```

---

### Task 3: Tetto al parallelismo xUnit come rete di sicurezza

**Files:**
- Create: `tests/FrasiSquisite.Server.Tests/xunit.runner.json`

**Interfaccia:** nessuna — file di configurazione letto a runtime da `xunit.runner.visualstudio` (già referenziato in `tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj`), non tocca codice.

Anche con la factory condivisa (Task 2), xUnit continua a far girare le classi di test in parallelo fino al numero di core della macchina (nessun `xunit.runner.json` esiste oggi nel repo, confermato). Questo task mette un tetto esplicito e moderato, indipendentemente dal fatto che il Task 2 abbia già azzerato la flakiness: è economia da poco e previene lo stesso sintomo se in futuro si aggiungono altre classi di test pesanti.

- [ ] **Step 1: Crea il file di configurazione**

```json
{
  "$schema": "https://xunit.net/schema/current/xunit.runner.schema.json",
  "parallelizeAssembly": false,
  "parallelizeTestCollections": true,
  "maxParallelThreads": 4
}
```

- [ ] **Step 2: Assicurati che venga copiato nell'output di build**

Apri `tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj` e verifica se `xunit.runner.json` compare già automaticamente nell'output (`bin/Debug/net10.0/`) dopo un build — l'SDK xUnit di solito lo raccoglie da solo se il file si chiama esattamente così ed è nella root del progetto. Verifica con:

```bash
dotnet build tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore
ls tests/FrasiSquisite.Server.Tests/bin/Debug/net10.0/xunit.runner.json
```

Se il file **non** compare nell'output, aggiungi esplicitamente in `tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj`, dentro un `<ItemGroup>` esistente o uno nuovo:

```xml
  <ItemGroup>
    <None Update="xunit.runner.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
```

e ricompila per confermare che ora compare.

- [ ] **Step 3: Rimisura la flakiness un'ultima volta (5 esecuzioni)**

```bash
for i in 1 2 3 4 5; do
  dotnet test tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore -v q 2>&1 | tail -5
done
```

- [ ] **Step 4: Commit**

```bash
git add tests/FrasiSquisite.Server.Tests/xunit.runner.json tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj
git commit -m "test: tetto al parallelismo xUnit del progetto server

Rete di sicurezza oltre al fix della factory condivisa: nessun
xunit.runner.json esisteva, quindi le classi giravano in parallelo
fino al numero di core della macchina senza alcun limite esplicito."
```

---

## Note per chi esegue

- Se dopo il Task 1 la flakiness è già sparita su 5/5 esecuzioni, procedi comunque al Task 2: la riduzione da 22 host a 1 per classe è un miglioramento di prestazioni dei test indipendente dalla flakiness, e resta il fix strutturalmente corretto raccomandato dalla diagnosi in `docs/superpowers/backlog.md` §2.
- Se dopo il Task 2 la flakiness persiste, prima di procedere al Task 3 rileggi `AiConfigurazioneTests.cs` (`tests/FrasiSquisite.Server.Tests/Ai/AiConfigurazioneTests.cs`): costruisce 5 istanze proprie di `WebApplicationFactory<Program>` inline per `[Fact]` (pattern diverso, non convertito da questo piano) e potrebbe essere una fonte residua di pressione sul pool di thread durante l'esecuzione parallela della suite.
