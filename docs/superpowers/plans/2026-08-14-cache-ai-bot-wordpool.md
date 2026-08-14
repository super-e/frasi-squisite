# Cache AI per le parole dei bot — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Oggi i bot pescano sempre da `StaticWordPool`, il dizionario statico compilato nel binario. Questo piano aggiunge una seconda implementazione di `IWordPool` che serve da una cache popolata da chiamate AI (una per schema, sei schemi in tutto), con fallback su `StaticWordPool` quando una voce non c'è — coerente con `docs/superpowers/specs/2026-08-03-ai-design.md` §6 e `docs/superpowers/backlog.md` §3.

**Architettura:** `CachedAiWordPool` (nuova, in `FrasiSquisite.Server`) implementa `IWordPool` con una cache in memoria per ruolo e un `StaticWordPool` di fallback — `Take` resta **sincrono**, il motore non cambia di una riga. `BotWordPoolRunner` compone il prompt per uno schema (ruolo, prompt, esempio di ogni casella come contesto), chiama `IAiTextProvider.CompletaAsync` (lo stesso client già usato da `RefinementRunner`), legge la risposta e valida ogni parola con `SlotTextValidator` prima di restituirla — scartando quelle che sfondano il limite di 60 caratteri, esattamente il rischio che il backlog segnala perché `FillDisconnected` scrive le parole dei bot senza rivalidarle. `BotWordPoolWarmupService` (un `BackgroundService`) riempie la cache all'avvio, uno schema alla volta, e riprova ogni 30 minuti finché non ci riesce per tutti e sei.

**Tech Stack:** .NET 10, `Microsoft.Extensions.Hosting.BackgroundService`, `System.Text.Json`, xUnit.

## Global Constraints

- **Il motore (`FrasiSquisite.Domain`, in particolare `GameEngine.Writing.cs`) non va toccato.** Tutto il lavoro di questo piano vive in `FrasiSquisite.Server`, che implementa l'interfaccia `IWordPool` già esistente in `FrasiSquisite.Domain.Filling` — la stessa dipendenza che il motore già consuma oggi con `StaticWordPool`.
- `IWordPool.Take(string ruolo, IRandomSource random)` è sincrono per contratto (`src/FrasiSquisite.Domain/Filling/IWordPool.cs`): nessuna chiamata di rete può avvenire dentro `Take`. La cache va popolata **prima**, in sottofondo.
- Ogni parola che entra in cache deve passare per `FrasiSquisite.Shared.Validation.SlotTextValidator.Validate` (limite 60 caratteri dopo normalizzazione). Non è un'opzione: `FillDisconnected`, nel motore, scrive la parola del bot direttamente nella casella senza rivalidarla (a differenza dell'invio di un umano, che passa da `OnSlotSubmitted` → `SlotTextValidator.Validate`), quindi una voce che sfora il limite lo sforerebbe in silenzio anche in produzione.
- **Nota per chi esegue i piani di backlog fuori ordine:** se il piano `docs/superpowers/plans/2026-08-14-scala-max-tokens-rifinitura.md` è già stato eseguito prima di questo, `IAiTextProvider.CompletaAsync` avrà un quarto parametro obbligatorio `int maxTokens`. In quel caso, in `BotWordPoolRunner.GeneraAsync` (Task 2 sotto) passa una costante fissa come quarto argomento (es. `1500`, coerente con la scelta fatta per `IllustrationRunner` in quel piano) invece della firma a 3 argomenti mostrata qui.
- Se all'avvio il modello non risponde, il servizio di sottofondo riprova **ogni 30 minuti**, indefinitamente, finché la cache non è piena per tutti gli schemi — non è un'ottimizzazione da poter saltare: senza, un server acceso durante un disservizio AI resterebbe sul dizionario statico per sempre senza che nessuno se ne accorga (spec §6).

---

### Task 1: `CachedAiWordPool` — cache in memoria con fallback su `StaticWordPool`

**Files:**
- Create: `src/FrasiSquisite.Server/Ai/CachedAiWordPool.cs`
- Test: `tests/FrasiSquisite.Server.Tests/Ai/CachedAiWordPoolTests.cs`

**Interfaccia:**
- Consuma: `IWordPool` (`FrasiSquisite.Domain.Filling`), `IRandomSource` (`FrasiSquisite.Domain.Randomness`), `StaticWordPool` (`FrasiSquisite.Domain.Filling`).
- Produce: `CachedAiWordPool(StaticWordPool fallback) : IWordPool` con un metodo pubblico in più, `void Popola(string ruolo, IReadOnlyList<string> parole)`, che il Task 3 (`BotWordPoolWarmupService`) chiama per riempire la cache.

- [ ] **Step 1: Scrivi il test che la cache viene preferita al fallback**

Crea `tests/FrasiSquisite.Server.Tests/Ai/CachedAiWordPoolTests.cs`:

```csharp
using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Server.Ai;
using Xunit;

namespace FrasiSquisite.Server.Tests.Ai;

public class CachedAiWordPoolTests
{
    [Fact]
    public void UnRuoloPresenteInCacheRestituisceUnaVoceDellaCache()
    {
        var pool = new CachedAiWordPool(new StaticWordPool());
        pool.Popola("Soggetto", ["Il notaio col paracadute"]);

        var parola = pool.Take("Soggetto", new SeededRandomSource(1));

        Assert.Equal("Il notaio col paracadute", parola);
    }
}
```

- [ ] **Step 2: Esegui il test e verifica che fallisca**

```bash
dotnet test tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~CachedAiWordPoolTests"
```

Atteso: FAIL — `CachedAiWordPool` non esiste ancora, errore di compilazione.

- [ ] **Step 3: Implementazione minima**

Crea `src/FrasiSquisite.Server/Ai/CachedAiWordPool.cs`:

```csharp
using System.Collections.Concurrent;
using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Randomness;

namespace FrasiSquisite.Server.Ai;

/// <summary>
/// Seconda implementazione di IWordPool (spec AI §6): una cache popolata in
/// sottofondo da BotWordPoolWarmupService, con fallback su StaticWordPool
/// quando un ruolo non è ancora (o non è mai stato) messo in cache. Take
/// resta sincrono: il motore lo chiama così, e non sa nulla dell'AI dietro.
/// </summary>
public sealed class CachedAiWordPool(StaticWordPool fallback) : IWordPool
{
    private readonly ConcurrentDictionary<string, string[]> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Chiamato solo da BotWordPoolWarmupService. Ignora liste vuote: un
    /// ruolo senza parole valide resta al dizionario statico, non a una
    /// voce di cache vuota che farebbe esplodere Take.
    /// </summary>
    public void Popola(string ruolo, IReadOnlyList<string> parole)
    {
        if (parole.Count > 0)
        {
            _cache[ruolo] = [.. parole];
        }
    }

    public string Take(string ruolo, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);

        return _cache.TryGetValue(ruolo, out var parole)
            ? parole[random.Next(parole.Length)]
            : fallback.Take(ruolo, random);
    }
}
```

- [ ] **Step 4: Esegui il test e verifica che passi**

```bash
dotnet test tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~CachedAiWordPoolTests"
```

Atteso: PASS.

- [ ] **Step 5: Aggiungi i casi di fallback e sovrascrittura**

Aggiungi a `CachedAiWordPoolTests.cs`:

```csharp
    [Fact]
    public void UnRuoloAssenteDallaCacheRicadeSulFallback()
    {
        var pool = new CachedAiWordPool(new StaticWordPool());

        var parola = pool.Take("Soggetto", new SeededRandomSource(1));

        Assert.Contains(parola, ["Il notaio", "La pantofola", "Un tram", "Il vescovo", "La zuppa", "Un ombrello"]);
    }

    [Fact]
    public void PopolareConUnaListaVuotaNonSostituisceIlFallback()
    {
        var pool = new CachedAiWordPool(new StaticWordPool());
        pool.Popola("Soggetto", []);

        var parola = pool.Take("Soggetto", new SeededRandomSource(1));

        Assert.Contains(parola, ["Il notaio", "La pantofola", "Un tram", "Il vescovo", "La zuppa", "Un ombrello"]);
    }

    [Fact]
    public void PopolareDiNuovoLoStessoRuoloSostituisceLaVoceDiCachePrecedente()
    {
        var pool = new CachedAiWordPool(new StaticWordPool());
        pool.Popola("Soggetto", ["prima voce"]);
        pool.Popola("Soggetto", ["seconda voce"]);

        var parola = pool.Take("Soggetto", new SeededRandomSource(1));

        Assert.Equal("seconda voce", parola);
    }
```

(`"Soggetto"` con quelle sei voci è il ruolo `["Soggetto"]` di `StaticWordPool`, verificato in `src/FrasiSquisite.Domain/Filling/StaticWordPool.cs:22`.)

- [ ] **Step 6: Esegui tutti i test della classe**

```bash
dotnet test tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~CachedAiWordPoolTests"
```

Atteso: 4 test, tutti PASS.

- [ ] **Step 7: Commit**

```bash
git add src/FrasiSquisite.Server/Ai/CachedAiWordPool.cs tests/FrasiSquisite.Server.Tests/Ai/CachedAiWordPoolTests.cs
git commit -m "feat(ai): CachedAiWordPool, seconda implementazione di IWordPool

Cache in memoria per ruolo con fallback su StaticWordPool. Take
resta sincrono (contratto di IWordPool): la popolazione della cache
è compito di un servizio in sottofondo separato (prossimo commit)."
```

---

### Task 2: `BotWordPoolRunner` — genera e valida le parole per uno schema

**Files:**
- Create: `src/FrasiSquisite.Server/Ai/BotWordPoolRunner.cs`
- Test: `tests/FrasiSquisite.Server.Tests/Ai/BotWordPoolRunnerTests.cs`

**Interfaccia:**
- Consuma: `IAiTextProvider` (`src/FrasiSquisite.Server/Ai/IAiTextProvider.cs`), `Schema`/`Casella` (`FrasiSquisite.Shared.Schemas`), `SlotTextValidator` (`FrasiSquisite.Shared.Validation`), `FakeAiTextProvider` (`tests/FrasiSquisite.Server.Tests/Ai/FakeAiTextProvider.cs`) nei test.
- Produce: `BotWordPoolRunner.GeneraAsync(Schema schema, CancellationToken ct) : Task<IReadOnlyDictionary<string, IReadOnlyList<string>>?>` — il Task 3 (`BotWordPoolWarmupService`) consuma questo metodo: `null` se l'AI non ha risposto o la risposta non si legge affatto, altrimenti un dizionario ruolo→parole **già validate**, con solo i ruoli per cui è rimasta almeno una parola valida.

- [ ] **Step 1: Scrivi il test del caso felice**

Crea `tests/FrasiSquisite.Server.Tests/Ai/BotWordPoolRunnerTests.cs`:

```csharp
using FrasiSquisite.Server.Ai;
using FrasiSquisite.Shared.Schemas;
using Xunit;

namespace FrasiSquisite.Server.Tests.Ai;

public class BotWordPoolRunnerTests
{
    // Schema compatto apposta per test leggibili: 3 caselle, non le 8 di
    // "storia". Corrisponde a src/FrasiSquisite.Shared/Schemas/Data/proverbio.json.
    private static readonly Schema Proverbio = new EmbeddedSchemaCatalog().Get("proverbio");

    private static BotWordPoolRunner Crea(FakeAiTextProvider ai) => new(ai);

    [Fact]
    public async Task UnaRispostaBenFormataDiventaParolePerRuolo()
    {
        var ai = new FakeAiTextProvider
        {
            Risposta = """
                {"ruoli": [
                    {"ruolo": "Premessa", "parole": ["Chi corre troppo", "Chi tace sempre"]},
                    {"ruolo": "Conseguenza", "parole": ["inciampa due volte"]},
                    {"ruolo": "Rincaro", "parole": ["e nessuno se ne accorge"]}
                ]}
                """,
        };

        var esito = await Crea(ai).GeneraAsync(Proverbio, CancellationToken.None);

        Assert.NotNull(esito);
        Assert.Equal(["Chi corre troppo", "Chi tace sempre"], esito["Premessa"]);
        Assert.Equal(["inciampa due volte"], esito["Conseguenza"]);
        Assert.Equal(["e nessuno se ne accorge"], esito["Rincaro"]);
    }
}
```

- [ ] **Step 2: Esegui il test e verifica che fallisca**

```bash
dotnet test tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~BotWordPoolRunnerTests"
```

Atteso: FAIL — `BotWordPoolRunner` non esiste ancora, errore di compilazione.

- [ ] **Step 3: Implementazione**

Crea `src/FrasiSquisite.Server/Ai/BotWordPoolRunner.cs`:

```csharp
using System.Text.Json;
using FrasiSquisite.Shared.Schemas;
using FrasiSquisite.Shared.Validation;

namespace FrasiSquisite.Server.Ai;

/// <summary>
/// Compone il prompt per uno schema, chiama il modello, legge la risposta
/// e valida ogni parola prima di restituirla. A differenza di
/// RefinementRunner (che lascia la fiducia a RefinementGuard, nel motore),
/// qui la validazione è definitiva: le parole dei bot finiscono nelle
/// caselle senza che il motore le rivalidi (FillDisconnected le scrive
/// direttamente), quindi ogni voce va passata per SlotTextValidator prima
/// di entrare in cache — non dopo (backlog.md §3).
/// </summary>
public sealed class BotWordPoolRunner(IAiTextProvider ai)
{
    private const string Sistema = """
        Generi parole o brevi frasi per riempire le caselle di un gioco
        surreale, quando un giocatore non è presente per scriverle da sé.

        Ricevi lo schema di un gioco: per ogni casella, il suo ruolo
        grammaticale/narrativo, un prompt che descrive cosa dovrebbe
        contenere, e un esempio già scritto per un'altra casella dello
        stesso ruolo.

        Per ciascun ruolo, genera una decina di alternative diverse fra
        loro, nello stesso stile e nello stesso registro dell'esempio dato:
        brevi, surreali, concrete, mai generiche. Non ripetere l'esempio
        stesso fra le tue proposte.

        REGOLE INDEROGABILI
        - Ogni voce sta da sola in una casella: non fare riferimento al
          resto della frase, che non conosci.
        - Massimo una manciata di parole per voce — non frasi lunghe.
        - Rispondi solo con JSON, senza commenti e senza blocchi di codice:
          {"ruoli": [{"ruolo": "...", "parole": ["...", "..."]}, ...]}
          Un elemento per ogni ruolo ricevuto, nello stesso ordine.
        """;

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>?> GeneraAsync(Schema schema, CancellationToken ct)
    {
        var utente = JsonSerializer.Serialize(new
        {
            schema = schema.Nome,
            caselle = schema.Caselle.Select(c => new { c.Ruolo, c.Prompt, c.Esempio }),
        });

        var risposta = await ai.CompletaAsync(Sistema, utente, ct);

        if (risposta is null)
        {
            return null;
        }

        var letto = Leggi(risposta);

        if (letto is null)
        {
            return null;
        }

        var validato = new Dictionary<string, IReadOnlyList<string>>();

        foreach (var (ruolo, parole) in letto)
        {
            var valide = parole
                .Select(SlotTextValidator.Validate)
                .Where(v => v.IsValid)
                .Select(v => v.Normalized)
                .ToList();

            if (valide.Count > 0)
            {
                validato[ruolo] = valide;
            }
        }

        return validato;
    }

    /// <summary>
    /// Stesso schema di ricerca del primo oggetto JSON usato in
    /// RefinementRunner.Leggi: i modelli incorniciano spesso la risposta in
    /// un blocco markdown o ci mettono una frase davanti.
    /// </summary>
    private static Dictionary<string, List<string>>? Leggi(string risposta)
    {
        try
        {
            var inizio = risposta.IndexOf('{');
            var fine = risposta.LastIndexOf('}');

            if (inizio < 0 || fine <= inizio)
            {
                return null;
            }

            using var documento = JsonDocument.Parse(risposta[inizio..(fine + 1)]);

            var ruoli = documento.RootElement.GetProperty("ruoli");
            var esito = new Dictionary<string, List<string>>();

            foreach (var voce in ruoli.EnumerateArray())
            {
                var ruolo = voce.GetProperty("ruolo").GetString();

                if (ruolo is null)
                {
                    continue;
                }

                esito[ruolo] = [.. voce.GetProperty("parole").EnumerateArray()
                    .Select(p => p.GetString())
                    .Where(p => p is not null)
                    .Select(p => p!)];
            }

            return esito;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
```

- [ ] **Step 4: Esegui il test e verifica che passi**

```bash
dotnet test tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~BotWordPoolRunnerTests"
```

Atteso: PASS.

- [ ] **Step 5: Scrivi il test sulla validazione — il punto critico del backlog**

Aggiungi a `BotWordPoolRunnerTests.cs`:

```csharp
    [Fact]
    public async Task UnaParolaTroppoLungaVieneScartataMaLeAltreRestano()
    {
        var parolaTroppoLunga = new string('x', 61); // SlotTextValidator.MaxLength = 60
        var ai = new FakeAiTextProvider
        {
            Risposta = $$"""
                {"ruoli": [
                    {"ruolo": "Premessa", "parole": ["Chi corre troppo", "{{parolaTroppoLunga}}"]}
                ]}
                """,
        };

        var esito = await Crea(ai).GeneraAsync(Proverbio, CancellationToken.None);

        Assert.NotNull(esito);
        Assert.Equal(["Chi corre troppo"], esito["Premessa"]);
    }

    [Fact]
    public async Task UnRuoloConSoleParoleNonValideNonCompareNelRisultato()
    {
        var parolaTroppoLunga = new string('x', 61);
        var ai = new FakeAiTextProvider
        {
            Risposta = $$"""
                {"ruoli": [
                    {"ruolo": "Premessa", "parole": ["{{parolaTroppoLunga}}"]},
                    {"ruolo": "Conseguenza", "parole": ["inciampa due volte"]}
                ]}
                """,
        };

        var esito = await Crea(ai).GeneraAsync(Proverbio, CancellationToken.None);

        Assert.NotNull(esito);
        Assert.False(esito.ContainsKey("Premessa"));
        Assert.True(esito.ContainsKey("Conseguenza"));
    }

    [Fact]
    public async Task SenzaRispostaDalModelloSiRestituisceNull()
    {
        var ai = new FakeAiTextProvider { Risposta = null };

        Assert.Null(await Crea(ai).GeneraAsync(Proverbio, CancellationToken.None));
    }

    [Fact]
    public async Task UnaRispostaIllegibileNonFaEsplodereNiente()
    {
        var ai = new FakeAiTextProvider { Risposta = "non sono JSON" };

        Assert.Null(await Crea(ai).GeneraAsync(Proverbio, CancellationToken.None));
    }

    /// <summary>
    /// I modelli incorniciano spesso il JSON in un blocco markdown: scartarlo
    /// per questo sarebbe buttare via una risposta buona (stesso principio
    /// verificato per RefinementRunner).
    /// </summary>
    [Fact]
    public async Task UnJsonAvvoltoInUnBloccoMarkdownVieneComunqueLetto()
    {
        var ai = new FakeAiTextProvider
        {
            Risposta = "```json\n{\"ruoli\": [{\"ruolo\": \"Premessa\", \"parole\": [\"Chi corre troppo\"]}]}\n```",
        };

        var esito = await Crea(ai).GeneraAsync(Proverbio, CancellationToken.None);

        Assert.NotNull(esito);
        Assert.Equal(["Chi corre troppo"], esito["Premessa"]);
    }

    [Fact]
    public async Task IlPromptDiSistemaFiniceNellaChiamataAlModello()
    {
        var ai = new FakeAiTextProvider { Risposta = """{"ruoli": []}""" };

        await Crea(ai).GeneraAsync(Proverbio, CancellationToken.None);

        Assert.NotNull(ai.UltimoSistema);
        Assert.Contains("ruoli", ai.UltimoUtente!, StringComparison.Ordinal);
    }
```

- [ ] **Step 6: Esegui tutti i test della classe e verifica che passino**

```bash
dotnet test tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~BotWordPoolRunnerTests"
```

Atteso: 7 test, tutti PASS.

- [ ] **Step 7: Commit**

```bash
git add src/FrasiSquisite.Server/Ai/BotWordPoolRunner.cs tests/FrasiSquisite.Server.Tests/Ai/BotWordPoolRunnerTests.cs
git commit -m "feat(ai): BotWordPoolRunner genera e valida parole bot per schema

Ogni parola passa per SlotTextValidator prima di essere restituita:
FillDisconnected, nel motore, scrive le parole dei bot senza
rivalidarle, quindi la validazione qui è definitiva, non opzionale
(backlog.md §3)."
```

---

### Task 3: `BotWordPoolWarmupService` — riempie la cache all'avvio, riprova ogni 30 minuti

**Files:**
- Create: `src/FrasiSquisite.Server/Ai/BotWordPoolWarmupService.cs`
- Test: `tests/FrasiSquisite.Server.Tests/Ai/BotWordPoolWarmupServiceTests.cs`

**Interfaccia:**
- Consuma: `ISchemaCatalog` (`FrasiSquisite.Shared.Schemas`), `BotWordPoolRunner.GeneraAsync` (Task 2), `CachedAiWordPool.Popola` (Task 1).
- Produce: `BotWordPoolWarmupService : BackgroundService` registrato come hosted service (Task 4); espone `EseguiUnGiroAsync(HashSet<string> daRiempire, CancellationToken ct) : Task<HashSet<string>>` pubblico apposta per essere testabile senza dover guidare l'intero ciclo di vita di un `BackgroundService` nei test.

- [ ] **Step 1: Scrivi il test di un giro che riesce per tutti gli schemi**

Crea `tests/FrasiSquisite.Server.Tests/Ai/BotWordPoolWarmupServiceTests.cs`:

```csharp
using FrasiSquisite.Server.Ai;
using FrasiSquisite.Shared.Schemas;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FrasiSquisite.Server.Tests.Ai;

public class BotWordPoolWarmupServiceTests
{
    private static readonly ISchemaCatalog Catalogo = new EmbeddedSchemaCatalog();

    private static BotWordPoolWarmupService Crea(FakeAiTextProvider ai, CachedAiWordPool cache) =>
        new(Catalogo, new BotWordPoolRunner(ai), cache, NullLogger<BotWordPoolWarmupService>.Instance);

    [Fact]
    public async Task UnGiroCheRisponeSempreSvuotaGliSchemiDaRiempire()
    {
        var ai = new FakeAiTextProvider { Risposta = """{"ruoli": [{"ruolo": "Soggetto", "parole": ["prova"]}]}""" };
        var cache = new CachedAiWordPool(new StaticWordPool());
        var servizio = Crea(ai, cache);
        var daRiempire = new HashSet<string>(Catalogo.All.Select(s => s.Id));

        var restano = await servizio.EseguiUnGiroAsync(daRiempire, CancellationToken.None);

        Assert.Empty(restano);
    }
}
```

Serve `using FrasiSquisite.Domain.Filling;` in cima al file per `StaticWordPool` (namespace `FrasiSquisite.Domain.Filling`, non `FrasiSquisite.Server.Ai`).

- [ ] **Step 2: Esegui il test e verifica che fallisca**

```bash
dotnet test tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~BotWordPoolWarmupServiceTests"
```

Atteso: FAIL — `BotWordPoolWarmupService` non esiste ancora, errore di compilazione.

- [ ] **Step 3: Implementazione**

Crea `src/FrasiSquisite.Server/Ai/BotWordPoolWarmupService.cs`:

```csharp
using FrasiSquisite.Shared.Schemas;

namespace FrasiSquisite.Server.Ai;

/// <summary>
/// Riempie CachedAiWordPool all'avvio, uno schema alla volta: una chiamata
/// per schema, sei schemi in tutto (spec AI §6). Se il modello non
/// risponde, riprova ogni 30 minuti finché la cache non è piena per tutti
/// — non è un'ottimizzazione: senza, un server acceso durante un
/// disservizio AI resterebbe sul dizionario statico per sempre senza che
/// nessuno se ne accorga.
/// </summary>
public sealed class BotWordPoolWarmupService(
    ISchemaCatalog catalogo,
    BotWordPoolRunner runner,
    CachedAiWordPool cache,
    ILogger<BotWordPoolWarmupService> logger) : BackgroundService
{
    private static readonly TimeSpan IntervalloRitentativo = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var daRiempire = new HashSet<string>(catalogo.All.Select(s => s.Id), StringComparer.Ordinal);

        while (daRiempire.Count > 0 && !stoppingToken.IsCancellationRequested)
        {
            daRiempire = await EseguiUnGiroAsync(daRiempire, stoppingToken);

            if (daRiempire.Count > 0)
            {
                logger.LogWarning(
                    "Cache bot non completa per {Schemi} schemi: nuovo tentativo tra {Minuti} minuti.",
                    daRiempire.Count, IntervalloRitentativo.TotalMinutes);

                await Task.Delay(IntervalloRitentativo, stoppingToken);
            }
        }
    }

    /// <summary>
    /// Un giro: prova a generare le parole per ogni schema ancora da
    /// riempire, popola la cache per quelli che rispondono, restituisce
    /// l'insieme di quelli rimasti. Pubblico e a parte da ExecuteAsync
    /// apposta per essere testabile senza guidare l'intero ciclo di vita
    /// di un BackgroundService.
    /// </summary>
    public async Task<HashSet<string>> EseguiUnGiroAsync(HashSet<string> daRiempire, CancellationToken ct)
    {
        var restano = new HashSet<string>(daRiempire, StringComparer.Ordinal);

        foreach (var schema in catalogo.All.Where(s => daRiempire.Contains(s.Id)))
        {
            var esito = await runner.GeneraAsync(schema, ct);

            if (esito is null)
            {
                continue;
            }

            foreach (var (ruolo, parole) in esito)
            {
                cache.Popola(ruolo, parole);
            }

            restano.Remove(schema.Id);
        }

        return restano;
    }
}
```

- [ ] **Step 4: Esegui il test e verifica che passi**

```bash
dotnet test tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~BotWordPoolWarmupServiceTests"
```

Atteso: PASS.

- [ ] **Step 5: Scrivi i test sui casi di fallimento parziale e sulla cache effettivamente popolata**

Aggiungi a `BotWordPoolWarmupServiceTests.cs`:

```csharp
    [Fact]
    public async Task UnGiroCheNonRisponeMaiLasciaTuttiGliSchemiDaRiempire()
    {
        var ai = new FakeAiTextProvider { Risposta = null };
        var cache = new CachedAiWordPool(new StaticWordPool());
        var servizio = Crea(ai, cache);
        var daRiempire = new HashSet<string>(Catalogo.All.Select(s => s.Id));

        var restano = await servizio.EseguiUnGiroAsync(daRiempire, CancellationToken.None);

        Assert.Equal(daRiempire, restano);
    }

    [Fact]
    public async Task UnGiroCheRisponePopolaDavveroLaCache()
    {
        var ai = new FakeAiTextProvider { Risposta = """{"ruoli": [{"ruolo": "Soggetto", "parole": ["voce di prova dalla cache"]}]}""" };
        var cache = new CachedAiWordPool(new StaticWordPool());
        var servizio = Crea(ai, cache);

        await servizio.EseguiUnGiroAsync(new HashSet<string>(Catalogo.All.Select(s => s.Id)), CancellationToken.None);

        Assert.Equal("voce di prova dalla cache", cache.Take("Soggetto", new SeededRandomSource(1)));
    }

    [Fact]
    public async Task UnaSolaChiamataPerSchema()
    {
        var ai = new FakeAiTextProvider { Risposta = """{"ruoli": []}""" };
        var cache = new CachedAiWordPool(new StaticWordPool());
        var servizio = Crea(ai, cache);

        await servizio.EseguiUnGiroAsync(new HashSet<string>(Catalogo.All.Select(s => s.Id)), CancellationToken.None);

        Assert.Equal(Catalogo.All.Count, ai.Chiamate);
    }
```

Serve `using FrasiSquisite.Domain.Randomness;` in cima al file per `SeededRandomSource`.

- [ ] **Step 6: Esegui tutti i test della classe e verifica che passino**

```bash
dotnet test tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~BotWordPoolWarmupServiceTests"
```

Atteso: 4 test, tutti PASS.

- [ ] **Step 7: Commit**

```bash
git add src/FrasiSquisite.Server/Ai/BotWordPoolWarmupService.cs tests/FrasiSquisite.Server.Tests/Ai/BotWordPoolWarmupServiceTests.cs
git commit -m "feat(ai): BotWordPoolWarmupService riempie la cache bot all'avvio

Una chiamata per schema, sei schemi in tutto. Se il modello non
risponde, EseguiUnGiroAsync lascia lo schema nell'insieme da
riempire e ExecuteAsync riprova ogni 30 minuti, indefinitamente
(spec AI §6)."
```

---

### Task 4: Cablaggio in DI

**Files:**
- Modify: `src/FrasiSquisite.Server/Program.cs:22`

**Interfaccia:** nessuna nuova — collega quanto prodotto dai Task 1-3 al contenitore DI del server. Nessun test dedicato: è cablaggio, verificato dall'avvio effettivo dell'app (Step 3) e dalla suite esistente che già passa da `WebApplicationFactory<Program>` (`GameHubTests`, `IllustrazioneEndpointTests`, `AiConfigurazioneTests`).

- [ ] **Step 1: Sostituisci la registrazione di `IWordPool`**

In `src/FrasiSquisite.Server/Program.cs`, sostituisci la riga 22:

```csharp
builder.Services.AddSingleton<IWordPool, StaticWordPool>();
```

con:

```csharp
builder.Services.AddSingleton<StaticWordPool>();
builder.Services.AddSingleton<CachedAiWordPool>();
builder.Services.AddSingleton<IWordPool>(sp => sp.GetRequiredService<CachedAiWordPool>());
builder.Services.AddSingleton<BotWordPoolRunner>();
builder.Services.AddHostedService<BotWordPoolWarmupService>();
```

Nessun `using` aggiuntivo serve: `FrasiSquisite.Server.Ai` è già importato in cima al file (riga 5), e tutte e quattro le nuove classi vivono lì.

Nota: la registrazione **non** va dentro il blocco `if (aiOptions.Abilitato)` (righe 39-104) — segue lo stesso schema di `RefinementRunner`/`IllustrationRunner`/`ImageStore` (righe 29-31), registrati sempre. Quando l'AI è spenta, `DisabledAiTextProvider.CompletaAsync` restituisce `null` immediatamente (nessuna chiamata di rete), quindi `BotWordPoolWarmupService` riprova ogni 30 minuti senza mai riuscire, a costo trascurabile — coerente con la filosofia del progetto per cui il degrado no-AI è un percorso di codice reale, non un `if` scritto apposta per evitarlo.

- [ ] **Step 2: Compila**

```bash
dotnet build src/FrasiSquisite.Server/FrasiSquisite.Server.csproj --no-restore
```

Atteso: nessun errore.

- [ ] **Step 3: Avvia il server e verifica che parta senza eccezioni**

```bash
dotnet run --project src/FrasiSquisite.Server/FrasiSquisite.Server.csproj --no-build &
sleep 5
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5000/illustrazioni/inventato
kill %1
```

Atteso: il server risponde (qualunque codice HTTP, anche 404 — l'importante è che risponda, prova che l'avvio non è esploso su un errore di DI come "unable to resolve service for type IWordPool"). Se la porta differisce da 5000, controlla `src/FrasiSquisite.Server/Properties/launchSettings.json` per quella configurata.

- [ ] **Step 4: Esegui l'intera suite del progetto server**

```bash
dotnet test tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore
```

Atteso: tutti i test passano, incluso ogni test che passa da `WebApplicationFactory<Program>` e quindi costruisce l'intero container DI del server.

- [ ] **Step 5: Commit**

```bash
git add src/FrasiSquisite.Server/Program.cs
git commit -m "feat(ai): cablaggio DI della cache AI per i bot

CachedAiWordPool sostituisce StaticWordPool come IWordPool attivo;
StaticWordPool resta registrato come singleton a sé, usato da
CachedAiWordPool come fallback. BotWordPoolWarmupService parte
sempre, anche con AI spenta (stesso schema di RefinementRunner e
IllustrationRunner): con AI spenta riprova ogni 30 minuti a costo
trascurabile, senza bisogno di un if dedicato."
```
