# AI — fondamenta e rifinitura — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Le caselle scritte dai giocatori vengono rifinite da un modello linguistico — solo tessuto connettivo, mai riscrittura — prima che il reveal le mostri.

**Architecture:** L'AI è un **effetto**, non una dipendenza del motore. Il motore entra in una fase `Refining`, emette l'effetto e resta puro e sincrono; `GameHost` lo esegue **senza attenderlo**, e il risultato rientra come evento. Le garanzie stanno in controlli meccanici sul valore di ritorno, non nel prompt.

**Tech Stack:** .NET 10, xUnit, SignalR, MAUI, `HttpClient` via `IHttpClientFactory`, API OpenAI-compatibile (ppq.ai).

**Spec:** [docs/superpowers/specs/2026-08-03-ai-design.md](../specs/2026-08-03-ai-design.md)

## Global Constraints

- **Lingua:** codice, commenti, nomi dei test e messaggi d'errore in **italiano**.
- **Il motore resta puro:** nessun I/O, nessun `async`, nessun orologio, nessun `Guid.NewGuid()`, nessuna casualità non iniettata in `FrasiSquisite.Domain`.
- **`FrasiSquisite.App` referenzia solo `Shared`**, mai `Domain`. Il progetto di test dell'App compila la cartella `ViewModels/` e `Services/IGameConnection.cs` da sorgente su `net10.0`: **nessun tipo MAUI in quei file**.
- **La chiave API non entra mai nel repository né nell'immagine Docker.** Nei test si usa sempre un provider finto. Nessun valore reale in `appsettings.json`, che finisce in git.
- Central package management (`Directory.Packages.props`): nessun `Version=` inline nei `.csproj`.
- **Prima di ogni `dotnet build`/`dotnet test`, verificare che il server non sia in esecuzione** (`Get-Process -Name "FrasiSquisite.Server"`): tiene aperta `FrasiSquisite.Shared.dll` e la build fallisce con `MSB3021`/`MSB3027`.
- **Commit firmati SSH via 1Password.** Se un commit fallisce con `failed to fill whole buffer`, 1Password è bloccato: **fermarsi e segnalarlo**, mai `--no-gpg-sign`.
- **Comando dei test:** `dotnet test FrasiSquisite.slnx --nologo -v q`. Baseline di partenza: **671 test verdi**.

---

## File Structure

**Creati:**

| File | Responsabilità |
|---|---|
| `src/FrasiSquisite.Domain/Refinement/RefinementGuard.cs` | I controlli puri sul valore di ritorno del modello |
| `src/FrasiSquisite.Server/Ai/AiOptions.cs` | Configurazione: indirizzo, modello, chiave, timeout |
| `src/FrasiSquisite.Server/Ai/IAiTextProvider.cs` | Il contratto: testo in, testo fuori |
| `src/FrasiSquisite.Server/Ai/OpenAiCompatibleTextProvider.cs` | L'implementazione HTTP |
| `src/FrasiSquisite.Server/Ai/DisabledAiTextProvider.cs` | Il degrado, come implementazione e non come `if` |
| `src/FrasiSquisite.Server/Ai/RefinementRunner.cs` | Compone il prompt, chiama, valida, produce l'evento |
| `src/FrasiSquisite.Domain/Engine/GameEngine.Refining.cs` | La fase nel motore |
| `tests/FrasiSquisite.Domain.Tests/Refinement/RefinementGuardTests.cs` | |
| `tests/FrasiSquisite.Domain.Tests/Engine/RifinituraTests.cs` | |
| `tests/FrasiSquisite.Server.Tests/Ai/RefinementRunnerTests.cs` | |
| `tests/FrasiSquisite.Server.Tests/Ai/FakeAiTextProvider.cs` | |

**Modificati:** `storia.json`, `RoomPhase.cs`, `GameState.cs`, `Effect.cs`, `GameEvent.cs`, `GameEngine.cs`, `GameEngine.Writing.cs`, `GameEngine.Room.cs`, `GameHost.cs`, `Program.cs`, `appsettings.json`, `ProtocolVersion.cs`, `GameSessionViewModel.cs`, `GamePage.xaml`, `docker-compose.yml`.

---

## Task 1: Il template dello schema di default

Preliminare e indipendente dall'AI: cambia il testo su cui la rifinitura lavorerà. Va per primo così i task successivi vedono già le formule definitive.

**Files:**
- Modify: `src/FrasiSquisite.Shared/Schemas/Data/storia.json`
- Test: `tests/FrasiSquisite.Shared.Tests/Schemas/EmbeddedSchemaCatalogTests.cs`

**Interfaces:**
- Consumes: niente.
- Produces: il template `"{0} {1} {2} {3}, {4}, dicendo: «{5}». La gente dice: «{6}», ed è andata a finire che {7}."`

- [ ] **Step 1: Aggiornare il test che fissa la frase composta**

In `EmbeddedSchemaCatalogTests.cs`, il test `LoSchemaDiDefaultIntercalaLeCongiunzioniDelTemplate` asserisce la frase composta esatta. Sostituire l'asserzione:

```csharp
        Assert.Equal(
            "Un pinguino in doppiopetto insieme al suo commercialista " +
            "nella sala d'attesa di un dentista monta una libreria svedese, " +
            "perché gliel'ha detto l'oroscopo, dicendo: «Non è colpa mia, io ho solo firmato». " +
            "La gente dice: «Si sapeva che finiva così», " +
            "ed è andata a finire che sono finiti tutti al telegiornale.",
            frase);
```

- [ ] **Step 2: Eseguire per verificare che fallisca**

```bash
dotnet test tests/FrasiSquisite.Shared.Tests --nologo --filter "LoSchemaDiDefaultIntercalaLeCongiunzioniDelTemplate"
```

Atteso: `Assert.Equal() Failure` con la vecchia frase come valore effettivo.

- [ ] **Step 3: Cambiare il template**

In `src/FrasiSquisite.Shared/Schemas/Data/storia.json`, sostituire la sola riga `"template"`:

```json
  "template": "{0} {1} {2} {3}, {4}, dicendo: «{5}». La gente dice: «{6}», ed è andata a finire che {7}.",
```

- [ ] **Step 4: Eseguire la suite completa**

```bash
dotnet test FrasiSquisite.slnx --nologo -v q
```

Atteso: `Non superati: 0`. Se fallisce un test **diverso** da quello dello Step 1, il template è citato in un altro posto: cercarlo con `grep -rn "Ultime parole" src tests` e aggiornarlo.

- [ ] **Step 5: Commit**

```bash
git add -A src tests && git commit -m "feat(schemi): congiunzioni piu' scorrevoli nello schema di default"
```

---

## Task 2: I controlli, come funzioni pure

**Files:**
- Create: `src/FrasiSquisite.Domain/Refinement/RefinementGuard.cs`
- Test: `tests/FrasiSquisite.Domain.Tests/Refinement/RefinementGuardTests.cs`

**Interfaces:**
- Consumes: niente.
- Produces:
  - `static IReadOnlyList<string> RefinementGuard.Applica(IReadOnlyList<string> grezze, IReadOnlyList<string>? rifinite, string template)`
    Restituisce, casella per casella, la rifinita se supera i controlli e la grezza altrimenti. Con `rifinite` nullo o di lunghezza diversa da `grezze`, restituisce `grezze`.
  - `const int RefinementGuard.MaxCaratteri = 200`

- [ ] **Step 1: Scrivere i test che falliscono**

Creare `tests/FrasiSquisite.Domain.Tests/Refinement/RefinementGuardTests.cs`:

```csharp
using FrasiSquisite.Domain.Refinement;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Refinement;

public class RefinementGuardTests
{
    private const string Template =
        "{0} {1} {2} {3}, {4}, dicendo: «{5}». La gente dice: «{6}», ed è andata a finire che {7}.";

    private const string Semplice = "{0} {1}";

    [Fact]
    public void UnaRifinituraValidaVieneAccettata()
    {
        var esito = RefinementGuard.Applica(["la nonna", "la mamma"], ["la nonna", "con la mamma"], Semplice);

        Assert.Equal(["la nonna", "con la mamma"], esito);
    }

    [Fact]
    public void UnaCasellaLasciataIdenticaVaBene()
    {
        var esito = RefinementGuard.Applica(["balla", "male"], ["balla", "è finita male"], Semplice);

        Assert.Equal(["balla", "è finita male"], esito);
    }

    /// <summary>
    /// Il controllo che protegge il gioco: il divertimento vive degli
    /// incidenti dei giocatori, e un modello che trasforma "il cadavere
    /// squisito" in "il defunto elegante" lo ucciderebbe. La casella
    /// riscritta torna grezza; le altre passano lo stesso.
    /// </summary>
    [Fact]
    public void UnaCasellaRiscrittaTornaGrezzaSenzaTrascinareLeAltre()
    {
        var esito = RefinementGuard.Applica(
            ["il cadavere squisito", "la mamma"],
            ["il defunto elegante", "con la mamma"],
            Semplice);

        Assert.Equal(["il cadavere squisito", "con la mamma"], esito);
    }

    [Fact]
    public void IlContenimentoIgnoraMaiuscoleESpaziDoppi()
    {
        var esito = RefinementGuard.Applica(["la  nonna"], ["Con La Nonna"], "{0}");

        Assert.Equal(["Con La Nonna"], esito);
    }

    /// <summary>
    /// Il template mette gia' "ed è andata a finire che" davanti alla casella
    /// 7: se il modello lo ripete, la frase composta diventa "ed è andata a
    /// finire che ed è andata a finire che male".
    /// </summary>
    [Fact]
    public void UnaCasellaCheRipeteIlLetteraleDelTemplateTornaGrezza()
    {
        var grezze = new[] { "a", "b", "c", "d", "e", "f", "g", "male" };
        var rifinite = new[] { "a", "b", "c", "d", "e", "f", "g", "ed è andata a finire che è finita male" };

        var esito = RefinementGuard.Applica(grezze, rifinite, Template);

        Assert.Equal("male", esito[7]);
        Assert.Equal("a", esito[0]);
    }

    [Fact]
    public void UnNumeroDiCaselleDiversoScartaTuttaLaFrase()
    {
        var esito = RefinementGuard.Applica(["uno", "due"], ["uno"], Semplice);

        Assert.Equal(["uno", "due"], esito);
    }

    [Fact]
    public void SenzaRifinituraSiTengonoLeGrezze()
    {
        var esito = RefinementGuard.Applica(["uno", "due"], null, Semplice);

        Assert.Equal(["uno", "due"], esito);
    }

    /// <summary>
    /// Il limite di 60 caratteri della validazione vale per l'input umano e
    /// non qui, perche' rifinire per definizione allunga. Resta un tetto piu'
    /// largo perche' il modello non possa restituire un paragrafo.
    /// </summary>
    [Fact]
    public void UnaCasellaSmisurataTornaGrezza()
    {
        var lunga = "male " + new string('x', RefinementGuard.MaxCaratteri);

        var esito = RefinementGuard.Applica(["male"], [lunga], "{0}");

        Assert.Equal(["male"], esito);
    }

    [Fact]
    public void UnaCasellaRifinitaVuotaTornaGrezza()
    {
        var esito = RefinementGuard.Applica(["male"], ["   "], "{0}");

        Assert.Equal(["male"], esito);
    }
}
```

- [ ] **Step 2: Eseguire per verificare che fallisca**

```bash
dotnet test tests/FrasiSquisite.Domain.Tests --nologo --filter "RefinementGuardTests"
```

Atteso: errore di compilazione, `The type or namespace name 'Refinement' does not exist`.

- [ ] **Step 3: Scrivere l'implementazione**

Creare `src/FrasiSquisite.Domain/Refinement/RefinementGuard.cs`:

```csharp
using System.Text.RegularExpressions;

namespace FrasiSquisite.Domain.Refinement;

/// <summary>
/// Decide, casella per casella, se fidarsi di quello che il modello ha
/// restituito. Un prompt e' una preghiera: la garanzia sta qui, in codice
/// puro e provabile, non nelle istruzioni che si mandano (spec §4.3).
/// </summary>
public static partial class RefinementGuard
{
    /// <summary>
    /// Tetto di sicurezza, non la validazione dell'input umano: quella e' a
    /// 60 caratteri e non si applica qui, perche' rifinire allunga sempre.
    /// Serve solo a impedire che il modello restituisca un paragrafo.
    /// </summary>
    public const int MaxCaratteri = 200;

    /// <param name="grezze">Le caselle come le hanno scritte i giocatori.</param>
    /// <param name="rifinite">Quelle tornate dal modello, o null se non e' tornato nulla.</param>
    /// <param name="template">Il template dello schema: dice cosa precede ogni segnaposto.</param>
    public static IReadOnlyList<string> Applica(
        IReadOnlyList<string> grezze,
        IReadOnlyList<string>? rifinite,
        string template)
    {
        ArgumentNullException.ThrowIfNull(grezze);
        ArgumentNullException.ThrowIfNull(template);

        // Un numero diverso di caselle non e' recuperabile a pezzi: non si sa
        // piu' quale corrisponde a quale, quindi si scarta tutto.
        if (rifinite is null || rifinite.Count != grezze.Count)
        {
            return grezze;
        }

        var precedenti = LetteraliPrecedenti(template, grezze.Count);
        var esito = new string[grezze.Count];

        for (var i = 0; i < grezze.Count; i++)
        {
            esito[i] = Accettabile(grezze[i], rifinite[i], precedenti[i]) ? rifinite[i] : grezze[i];
        }

        return esito;
    }

    private static bool Accettabile(string grezza, string rifinita, string precedente)
    {
        if (string.IsNullOrWhiteSpace(rifinita) || rifinita.Length > MaxCaratteri)
        {
            return false;
        }

        var r = Normalizza(rifinita);

        // Il modello non puo' riscrivere: le parole del giocatore devono
        // ricomparire dentro la casella rifinita.
        if (!r.Contains(Normalizza(grezza), StringComparison.Ordinal))
        {
            return false;
        }

        // E non puo' ripetere cio' che il template gli mette gia' davanti.
        return string.IsNullOrEmpty(precedente)
            || !r.StartsWith(Normalizza(precedente), StringComparison.Ordinal);
    }

    private static string Normalizza(string testo) =>
        SpaziMultipli().Replace(testo.Trim(), " ").ToLowerInvariant();

    /// <summary>
    /// Per ogni segnaposto, il testo fisso che lo precede nel template. Per
    /// "{6}», ed è andata a finire che {7}." il precedente di 7 e'
    /// "», ed è andata a finire che": basta e avanza per accorgersi che il
    /// modello lo sta ripetendo, perche' il confronto e' su come INIZIA la
    /// casella rifinita.
    /// </summary>
    private static string[] LetteraliPrecedenti(string template, int caselle)
    {
        var esito = new string[caselle];

        for (var i = 0; i < caselle; i++)
        {
            var posizione = template.IndexOf($"{{{i}}}", StringComparison.Ordinal);
            if (posizione < 0)
            {
                esito[i] = string.Empty;
                continue;
            }

            var prima = template[..posizione];
            var precedente = template.LastIndexOf('}', Math.Max(posizione - 1, 0));

            esito[i] = precedente >= 0 && precedente < posizione
                ? prima[(precedente + 1)..].Trim()
                : prima.Trim();
        }

        return esito;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaziMultipli();
}
```

> **Attenzione al caso del primo segnaposto:** per `{0}` non c'e' nessun `}` prima, quindi `LastIndexOf` torna -1 e il precedente e' tutto cio' che sta prima — di solito la stringa vuota. Il test `UnaCasellaRiscrittaTornaGrezzaSenzaTrascinareLeAltre` lo esercita.

- [ ] **Step 4: Eseguire i test**

```bash
dotnet test tests/FrasiSquisite.Domain.Tests --nologo --filter "RefinementGuardTests"
```

Atteso: `Superato! - Non superati: 0. Superati: 9`.

- [ ] **Step 5: Commit**

```bash
git add src/FrasiSquisite.Domain/Refinement/ tests/FrasiSquisite.Domain.Tests/Refinement/ && git commit -m "feat(ai): controlli puri sul valore di ritorno della rifinitura"
```

---

## Task 3: Il contratto AI, la configurazione e il degrado

**Files:**
- Create: `src/FrasiSquisite.Server/Ai/AiOptions.cs`
- Create: `src/FrasiSquisite.Server/Ai/IAiTextProvider.cs`
- Create: `src/FrasiSquisite.Server/Ai/DisabledAiTextProvider.cs`
- Create: `src/FrasiSquisite.Server/Ai/OpenAiCompatibleTextProvider.cs`
- Modify: `src/FrasiSquisite.Server/Program.cs`
- Modify: `src/FrasiSquisite.Server/appsettings.json`
- Create: `tests/FrasiSquisite.Server.Tests/Ai/FakeAiTextProvider.cs`
- Test: `tests/FrasiSquisite.Server.Tests/Ai/AiConfigurazioneTests.cs`

**Interfaces:**
- Consumes: niente.
- Produces:
  - `sealed class AiOptions { string BaseUrl; string ApiKey; string TextModel; int TimeoutSeconds; bool Abilitato => !string.IsNullOrWhiteSpace(ApiKey); }` con `const string Sezione = "Ai"`
  - `interface IAiTextProvider { Task<string?> CompletaAsync(string sistema, string utente, CancellationToken ct); }` — restituisce `null` per qualunque fallimento.
  - `sealed class DisabledAiTextProvider : IAiTextProvider`
  - `sealed class OpenAiCompatibleTextProvider : IAiTextProvider`

- [ ] **Step 1: Scrivere il test che fallisce**

Creare `tests/FrasiSquisite.Server.Tests/Ai/AiConfigurazioneTests.cs`:

```csharp
using FrasiSquisite.Server.Ai;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FrasiSquisite.Server.Tests.Ai;

public class AiConfigurazioneTests
{
    [Fact]
    public void SenzaChiaveLaConfigurazioneRisultaDisabilitata()
    {
        var opzioni = new AiOptions { ApiKey = "" };

        Assert.False(opzioni.Abilitato);
    }

    [Fact]
    public void ConLaChiaveLaConfigurazioneRisultaAbilitata()
    {
        var opzioni = new AiOptions { ApiKey = "sk-qualcosa" };

        Assert.True(opzioni.Abilitato);
    }

    /// <summary>
    /// Il degrado non e' un ramo condizionale sparso nel codice ma la scelta
    /// di quale implementazione registrare (spec §7). Il server di test non
    /// ha chiave configurata, quindi deve risolvere quella disabilitata.
    /// </summary>
    [Fact]
    public void SenzaChiaveIlContainerRisolveIlProviderDisabilitato()
    {
        using var factory = new WebApplicationFactory<Program>();

        var provider = factory.Services.GetRequiredService<IAiTextProvider>();

        Assert.IsType<DisabledAiTextProvider>(provider);
    }

    [Fact]
    public async Task IlProviderDisabilitatoRestituisceSempreNull()
    {
        var provider = new DisabledAiTextProvider();

        Assert.Null(await provider.CompletaAsync("sistema", "utente", TestContext.Current.CancellationToken));
    }
}
```

> Se `TestContext.Current.CancellationToken` non esiste nella versione di xUnit in uso, passare `CancellationToken.None`. Controllare come fanno gli altri test del progetto prima di scegliere.

- [ ] **Step 2: Eseguire per verificare che fallisca**

```bash
dotnet test tests/FrasiSquisite.Server.Tests --nologo --filter "AiConfigurazioneTests"
```

Atteso: errore di compilazione, `The type or namespace name 'Ai' does not exist`.

- [ ] **Step 3: Scrivere configurazione e contratto**

Creare `src/FrasiSquisite.Server/Ai/AiOptions.cs`:

```csharp
namespace FrasiSquisite.Server.Ai;

/// <summary>
/// Indirizzo, modello e chiave vengono dalla configurazione e non dal codice:
/// ppq.ai e OpenRouter espongono entrambi /chat/completions in formato
/// OpenAI, quindi cambiare fornitore e' una variabile d'ambiente e non una
/// modifica al codice (spec §7).
/// </summary>
public sealed class AiOptions
{
    public const string Sezione = "Ai";

    public string BaseUrl { get; set; } = "https://api.ppq.ai";

    /// <summary>
    /// Mai in appsettings.json, che finisce in git: arriva come variabile
    /// d'ambiente (Ai__ApiKey) dal file .env del container.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    public string TextModel { get; set; } = "glm-5.2";

    /// <summary>
    /// Oltre questo, si prosegue con le caselle grezze. Non e' un'ottimizzazione:
    /// e' cio' che impedisce a una partita di restare appesa (spec §4.4).
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// L'unico interruttore: senza chiave l'AI e' spenta e il gioco resta
    /// interamente giocabile (spec §7).
    /// </summary>
    public bool Abilitato => !string.IsNullOrWhiteSpace(ApiKey);
}
```

Creare `src/FrasiSquisite.Server/Ai/IAiTextProvider.cs`:

```csharp
namespace FrasiSquisite.Server.Ai;

/// <summary>
/// Testo dentro, testo fuori. <c>null</c> significa "non disponibile", per
/// qualunque motivo: chiave assente, rete giu', timeout, risposta illeggibile.
/// Chi chiama non deve distinguere i casi, perche' la reazione e' la stessa.
/// </summary>
public interface IAiTextProvider
{
    Task<string?> CompletaAsync(string sistema, string utente, CancellationToken ct);
}
```

Creare `src/FrasiSquisite.Server/Ai/DisabledAiTextProvider.cs`:

```csharp
namespace FrasiSquisite.Server.Ai;

/// <summary>
/// Il degrado come implementazione e non come <c>if</c> (spec §5 del design
/// generale). Quando la chiave non c'e', il container risolve questo: il
/// resto del codice non sa nemmeno che l'AI e' spenta.
/// </summary>
public sealed class DisabledAiTextProvider : IAiTextProvider
{
    public Task<string?> CompletaAsync(string sistema, string utente, CancellationToken ct) =>
        Task.FromResult<string?>(null);
}
```

- [ ] **Step 4: Scrivere l'implementazione HTTP**

Creare `src/FrasiSquisite.Server/Ai/OpenAiCompatibleTextProvider.cs`:

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FrasiSquisite.Server.Ai;

/// <summary>
/// Una sola implementazione per tutti i fornitori compatibili OpenAI.
/// Non lancia mai: qualunque guasto diventa <c>null</c>, perche' il chiamante
/// ha gia' una strada per quel caso e un'eccezione lo costringerebbe a
/// duplicarla in un catch.
/// </summary>
public sealed class OpenAiCompatibleTextProvider(
    HttpClient http,
    IOptions<AiOptions> opzioni,
    ILogger<OpenAiCompatibleTextProvider> logger) : IAiTextProvider
{
    private readonly AiOptions _opzioni = opzioni.Value;

    public async Task<string?> CompletaAsync(string sistema, string utente, CancellationToken ct)
    {
        try
        {
            var richiesta = new
            {
                model = _opzioni.TextModel,
                messages = new[]
                {
                    new { role = "system", content = sistema },
                    new { role = "user", content = utente },
                },
                // GLM-5.2 e' un modello di ragionamento: senza questo spende
                // token nascosti prima di rispondere, e per una correzione di
                // bozze e' sproporzionato in tempo e in denaro (spec §4.2).
                reasoning_effort = "low",
                max_tokens = 2000,
            };

            using var risposta = await http.PostAsJsonAsync("/chat/completions", richiesta, ct);

            if (!risposta.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Il fornitore AI ha risposto {Codice}: si prosegue senza rifinitura.",
                    (int)risposta.StatusCode);
                return null;
            }

            using var documento = JsonDocument.Parse(await risposta.Content.ReadAsStringAsync(ct));

            return documento.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or IndexOutOfRangeException)
        {
            // Rete giu', timeout, o una risposta di forma inattesa: per il
            // gioco sono lo stesso caso, e nessuno di questi deve far cadere
            // una partita.
            logger.LogWarning(ex, "Chiamata al fornitore AI fallita: si prosegue senza rifinitura.");
            return null;
        }
    }
}
```

- [ ] **Step 5: Registrare nel container**

In `src/FrasiSquisite.Server/Program.cs`, prima di `var app = builder.Build();`:

```csharp
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.Sezione));

// Quale implementazione registrare e' L'UNICO punto in cui si decide se l'AI
// e' accesa. Da qui in poi nessun altro file conosce quella distinzione.
var aiOptions = builder.Configuration.GetSection(AiOptions.Sezione).Get<AiOptions>() ?? new AiOptions();

if (aiOptions.Abilitato)
{
    builder.Services.AddHttpClient<IAiTextProvider, OpenAiCompatibleTextProvider>(c =>
    {
        c.BaseAddress = new Uri(aiOptions.BaseUrl);
        c.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", aiOptions.ApiKey);
        c.Timeout = TimeSpan.FromSeconds(aiOptions.TimeoutSeconds);
    });
}
else
{
    builder.Services.AddSingleton<IAiTextProvider, DisabledAiTextProvider>();
}
```

e l'using in cima: `using FrasiSquisite.Server.Ai;`

In `src/FrasiSquisite.Server/appsettings.json`, aggiungere la sezione **senza chiave**:

```json
  "Ai": {
    "BaseUrl": "https://api.ppq.ai",
    "TextModel": "glm-5.2",
    "TimeoutSeconds": 10
  },
```

> **Non aggiungere `ApiKey` qui.** Questo file finisce in git. La chiave arriva come variabile d'ambiente `Ai__ApiKey`.

- [ ] **Step 6: Scrivere il provider finto per i test**

Creare `tests/FrasiSquisite.Server.Tests/Ai/FakeAiTextProvider.cs`:

```csharp
using FrasiSquisite.Server.Ai;

namespace FrasiSquisite.Server.Tests.Ai;

/// <summary>
/// Provider in memoria: nessuna rete nei test. Permette di simulare la
/// risposta, il fallimento e la lentezza, che sono i tre casi che il gioco
/// deve saper reggere.
/// </summary>
public sealed class FakeAiTextProvider : IAiTextProvider
{
    public string? Risposta { get; set; }

    /// <summary>Se impostato, la chiamata attende questo prima di rispondere.</summary>
    public TimeSpan Ritardo { get; set; } = TimeSpan.Zero;

    public int Chiamate { get; private set; }

    public string? UltimoSistema { get; private set; }

    public string? UltimoUtente { get; private set; }

    public async Task<string?> CompletaAsync(string sistema, string utente, CancellationToken ct)
    {
        Chiamate++;
        UltimoSistema = sistema;
        UltimoUtente = utente;

        if (Ritardo > TimeSpan.Zero)
        {
            await Task.Delay(Ritardo, ct);
        }

        return Risposta;
    }
}
```

- [ ] **Step 7: Eseguire i test**

```bash
dotnet test tests/FrasiSquisite.Server.Tests --nologo --filter "AiConfigurazioneTests"
```

Atteso: `Non superati: 0. Superati: 4`.

- [ ] **Step 8: Commit**

```bash
git add -A src tests && git commit -m "feat(ai): provider configurabile e degrado come implementazione"
```

---

## Task 4: La fase `Refining` nel motore

**Files:**
- Modify: `src/FrasiSquisite.Domain/Model/RoomPhase.cs`
- Modify: `src/FrasiSquisite.Domain/Model/GameState.cs`
- Modify: `src/FrasiSquisite.Domain/Engine/Effect.cs`
- Modify: `src/FrasiSquisite.Domain/Engine/GameEvent.cs`
- Modify: `src/FrasiSquisite.Domain/Engine/GameEngine.cs`
- Modify: `src/FrasiSquisite.Domain/Engine/GameEngine.Writing.cs`
- Modify: `src/FrasiSquisite.Domain/Engine/GameEngine.Room.cs`
- Create: `src/FrasiSquisite.Domain/Engine/GameEngine.Refining.cs`
- Test: `tests/FrasiSquisite.Domain.Tests/Engine/RifinituraTests.cs`

**Interfaces:**
- Consumes: `RefinementGuard.Applica` (Task 2).
- Produces:
  - `RoomPhase.Refining` fra `Writing` e `Reveal`
  - `sealed record RequestRefinement(IReadOnlyList<IReadOnlyList<string>> Frasi, string Template) : Effect`
  - `sealed record RefinementFinished(IReadOnlyList<IReadOnlyList<string>>? Frasi) : GameEvent` — `null` significa fallimento.
  - `GameState.Phrases` invariato: la rifinitura **sostituisce il testo delle caselle**, mantenendo gli autori.

- [ ] **Step 1: Scrivere i test che falliscono**

Creare `tests/FrasiSquisite.Domain.Tests/Engine/RifinituraTests.cs`:

```csharp
using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Shared.Protocol;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Engine;

public class RifinituraTests
{
    private const int N = 2;
    private const int K = 2;

    private readonly IGameEngine _motore =
        new GameEngine(new RoleSchemaMode(), new StaticWordPool(), new SeededRandomSource(1));

    private static Guid Giocatore(int i) => Guid.Parse($"00000000-0000-0000-0000-{i:D12}");

    /// <summary>Partita con tutte le caselle scritte: e' il momento in cui si entra in Refining.</summary>
    private (GameState Stato, EngineResult Ultimo) ScritturaConclusa()
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(K));
        for (var i = 0; i < N; i++)
        {
            stato = _motore.Handle(stato, new PlayerJoined(Giocatore(i), $"G{i}")).State;
        }

        stato = _motore.Handle(stato, new GameStartRequested(Giocatore(0))).State;

        EngineResult ultimo = null!;
        for (var round = 0; round < K; round++)
        {
            for (var g = 0; g < N; g++)
            {
                ultimo = _motore.Handle(stato, new SlotSubmitted(Giocatore(g), $"p{round}{g}"));
                stato = ultimo.State;
            }
        }

        return (stato, ultimo);
    }

    [Fact]
    public void FinitaLaScritturaSiEntraInRefiningENonInReveal()
    {
        var (stato, _) = ScritturaConclusa();

        Assert.Equal(RoomPhase.Refining, stato.Phase);
    }

    [Fact]
    public void EntrandoInRefiningVieneChiestaLaRifinitura()
    {
        var (_, ultimo) = ScritturaConclusa();

        var richiesta = Assert.Single(ultimo.Effects.OfType<RequestRefinement>());

        Assert.Equal(N, richiesta.Frasi.Count);
        Assert.All(richiesta.Frasi, f => Assert.Equal(K, f.Count));
        Assert.False(string.IsNullOrWhiteSpace(richiesta.Template));
    }

    [Fact]
    public void LaRifinituraRiuscitaSostituisceIlTestoEPortaAlReveal()
    {
        var (stato, _) = ScritturaConclusa();

        var rifinite = stato.Phrases
            .Select(f => (IReadOnlyList<string>)[.. f.Slots.Select(s => "con " + s!.Text)])
            .ToList();

        var risultato = _motore.Handle(stato, new RefinementFinished(rifinite));

        Assert.Equal(RoomPhase.Reveal, risultato.State.Phase);
        Assert.Equal("con p00", risultato.State.Phrases[0].Slots[0]!.Text);
        Assert.Single(risultato.Broadcasts<RevealStepMessage>());
    }

    /// <summary>
    /// Gli autori non devono cambiare: la rifinitura tocca il testo, non chi
    /// l'ha scritto, e la classifica finale li mostra.
    /// </summary>
    [Fact]
    public void LaRifinituraNonTocccaGliAutori()
    {
        var (stato, _) = ScritturaConclusa();
        var autoriPrima = stato.Phrases[0].Slots.Select(s => s!.AuthorId).ToList();

        var rifinite = stato.Phrases
            .Select(f => (IReadOnlyList<string>)[.. f.Slots.Select(s => "con " + s!.Text)])
            .ToList();

        var risultato = _motore.Handle(stato, new RefinementFinished(rifinite));

        Assert.Equal(autoriPrima, risultato.State.Phrases[0].Slots.Select(s => s!.AuthorId));
    }

    [Fact]
    public void LaRifinituraFallitaPortaComunqueAlReveal()
    {
        var (stato, _) = ScritturaConclusa();

        var risultato = _motore.Handle(stato, new RefinementFinished(null));

        Assert.Equal(RoomPhase.Reveal, risultato.State.Phase);
        Assert.Equal("p00", risultato.State.Phrases[0].Slots[0]!.Text);
        Assert.Single(risultato.Broadcasts<RevealStepMessage>());
    }

    /// <summary>
    /// Una casella che il modello ha riscritto torna grezza, senza che le
    /// altre ne risentano: e' RefinementGuard, applicato dal motore.
    /// </summary>
    [Fact]
    public void UnaCasellaRiscrittaDalModelloTornaGrezza()
    {
        var (stato, _) = ScritturaConclusa();

        var rifinite = stato.Phrases
            .Select((f, i) => (IReadOnlyList<string>)[.. f.Slots.Select((s, j) =>
                i == 0 && j == 0 ? "tutt'altro" : "con " + s!.Text)])
            .ToList();

        var risultato = _motore.Handle(stato, new RefinementFinished(rifinite));

        Assert.Equal("p00", risultato.State.Phrases[0].Slots[0]!.Text);
        Assert.Equal("con p01", risultato.State.Phrases[0].Slots[1]!.Text);
    }

    /// <summary>
    /// Se la stanza e' uscita da Refining - l'host ha ricominciato, o e'
    /// tornato in lobby - applicare quelle caselle sovrascriverebbe una
    /// partita nuova con i resti di quella vecchia (spec §3).
    /// </summary>
    [Fact]
    public void UnaRifinituraInRitardoVieneIgnorata()
    {
        var (stato, _) = ScritturaConclusa();
        var inLobby = stato with { Phase = RoomPhase.Lobby };

        var risultato = _motore.Handle(inLobby, new RefinementFinished(null));

        Assert.Equal(RoomPhase.Lobby, risultato.State.Phase);
        Assert.Empty(risultato.Effects);
    }

    [Fact]
    public void TornareAllaLobbyAzzeraLaFaseDiRifinitura()
    {
        var (stato, _) = ScritturaConclusa();
        var finito = stato with { Phase = RoomPhase.Finished };

        var azzerato = _motore.Handle(finito, new BackToLobbyRequested(Giocatore(0))).State;

        Assert.Equal(RoomPhase.Lobby, azzerato.Phase);
    }
}
```

- [ ] **Step 2: Eseguire per verificare che fallisca**

```bash
dotnet test tests/FrasiSquisite.Domain.Tests --nologo --filter "RifinituraTests"
```

Atteso: errore di compilazione, `'RoomPhase' does not contain a definition for 'Refining'`.

- [ ] **Step 3: Aggiungere la fase**

In `src/FrasiSquisite.Domain/Model/RoomPhase.cs`, inserire `Refining` **fra `Writing` e `Reveal`**:

```csharp
public enum RoomPhase
{
    Lobby,
    Writing,
    Refining,
    Reveal,
    Voting,
    Finished,
}
```

- [ ] **Step 4: Aggiungere effetto ed evento**

In `src/FrasiSquisite.Domain/Engine/Effect.cs`, in fondo:

```csharp
/// <summary>
/// Chiede che le caselle vengano rifinite. Porta i dati e nient'altro: il
/// motore non sa se dietro ci sia un modello, un dizionario o niente
/// (spec §3).
/// </summary>
public sealed record RequestRefinement(
    IReadOnlyList<IReadOnlyList<string>> Frasi,
    string Template) : Effect;
```

In `src/FrasiSquisite.Domain/Engine/GameEvent.cs`, in fondo:

```csharp
/// <summary>
/// L'esito della rifinitura. <paramref name="Frasi"/> nullo significa che non
/// e' arrivata: rete giu', timeout, chiave assente. Il motore tratta i due
/// casi allo stesso modo — prosegue — quindi non servono due eventi.
/// </summary>
public sealed record RefinementFinished(IReadOnlyList<IReadOnlyList<string>>? Frasi) : GameEvent;
```

- [ ] **Step 5: Creare `GameEngine.Refining.cs`**

```csharp
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Refinement;

namespace FrasiSquisite.Domain.Engine;

/// <summary>
/// La fase fra la scrittura e il reveal, in cui si aspetta che le caselle
/// tornino rifinite. Il motore non chiama nessuno: emette un effetto e
/// aspetta l'evento di ritorno (spec §3).
/// </summary>
public sealed partial class GameEngine
{
    /// <summary>
    /// Ingresso nella fase, chiamato quando l'ultima casella e' stata scritta.
    /// </summary>
    private static EngineResult EntraInRifinitura(GameState state, List<Effect> effetti)
    {
        var rifinendo = state with { Phase = RoomPhase.Refining };

        var frasi = rifinendo.Phrases
            .Select(f => (IReadOnlyList<string>)[.. f.Slots.Select(s => s!.Text)])
            .ToList();

        effetti.Add(new BroadcastToRoom(RoomState(rifinendo)));
        effetti.Add(new RequestRefinement(frasi, rifinendo.Schema.Template));

        return new EngineResult(rifinendo, effetti);
    }

    private static EngineResult OnRefinementFinished(GameState state, RefinementFinished e)
    {
        // Se la stanza e' andata avanti - nuova partita, ritorno in lobby -
        // applicare queste caselle sovrascriverebbe la partita nuova con i
        // resti di quella vecchia. Nessun errore verso il client: non l'ha
        // chiesto nessun giocatore, e' un evento interno del server.
        if (state.Phase != RoomPhase.Refining)
        {
            return EngineResult.NoChange(state);
        }

        var applicato = e.Frasi is null ? state : ApplicaRifinitura(state, e.Frasi);

        return AvviaReveal(applicato);
    }

    /// <summary>
    /// Sostituisce il testo delle caselle lasciando intatti gli autori: la
    /// rifinitura tocca cosa c'e' scritto, non chi l'ha scritto.
    /// </summary>
    private static GameState ApplicaRifinitura(
        GameState state,
        IReadOnlyList<IReadOnlyList<string>> rifinite)
    {
        if (rifinite.Count != state.Phrases.Count)
        {
            return state;
        }

        var frasi = state.Phrases.ToArray();

        for (var i = 0; i < frasi.Length; i++)
        {
            var grezze = frasi[i].Slots.Select(s => s!.Text).ToList();
            var accettate = RefinementGuard.Applica(grezze, rifinite[i], state.Schema.Template);

            var caselle = frasi[i].Slots.ToArray();
            for (var j = 0; j < caselle.Length; j++)
            {
                caselle[j] = caselle[j]! with { Text = accettate[j] };
            }

            frasi[i] = frasi[i] with { Slots = caselle };
        }

        return state with { Phrases = frasi };
    }
}
```

- [ ] **Step 6: Far entrare la scrittura in rifinitura invece che nel reveal**

In `src/FrasiSquisite.Domain/Engine/GameEngine.Writing.cs`, dentro `AdvanceRound`, il ramo `if (_mode.IsComplete(prossimo))` costruisce oggi lo stato di reveal e i suoi tre broadcast. Sostituirlo con:

```csharp
        if (_mode.IsComplete(prossimo))
        {
            // Il RoundProgressMessage saturo evita che l'attesa resti bloccata
            // a N-1/N mentre si passa alla rifinitura.
            return EntraInRifinitura(prossimo, [
                new BroadcastToRoom(new RoundProgressMessage(state.Round, state.Players.Count, state.Players.Count)),
            ]);
        }
```

Estrarre in `GameEngine.Refining.cs` il pezzo che prima stava qui, come metodo condiviso — e' quello che `OnRefinementFinished` richiama:

```csharp
    /// <summary>
    /// Porta la stanza nel reveal. Chiamato solo dalla fine della rifinitura:
    /// prima ci si arrivava direttamente dalla fine dell'ultimo round.
    ///
    /// La RevealStepMessage iniziale - vuota, nessuna casella scoperta - e'
    /// cio' che porta tutti sulla schermata di reveal: senza, ogni client
    /// resterebbe fermo dov'era, perche' non arriva piu' nessuna
    /// SlotRequestMessage e Screen non cambia mai da solo.
    /// </summary>
    private static EngineResult AvviaReveal(GameState state)
    {
        var reveal = state with
        {
            Phase = RoomPhase.Reveal,
            RevealPhraseIndex = 0,
            RevealSlotCount = 0,
        };

        return new EngineResult(reveal, [
            new BroadcastToRoom(RoomState(reveal)),
            new BroadcastToRoom(new RevealStepMessage(0, reveal.Phrases.Count, [], false)),
        ]);
    }
```

> Aggiungere `using FrasiSquisite.Shared.Protocol;` in cima a `GameEngine.Refining.cs`.

- [ ] **Step 7: Registrare l'evento nel dispatch**

In `src/FrasiSquisite.Domain/Engine/GameEngine.cs`, dentro `Handle`, prima di `_ => EngineResult.NoChange(state)`:

```csharp
        RefinementFinished e => OnRefinementFinished(state, e),
```

- [ ] **Step 8: Eseguire i test del dominio**

```bash
dotnet test tests/FrasiSquisite.Domain.Tests --nologo -v q
```

Atteso: `RifinituraTests` verdi. **Molti test esistenti falliranno**: tutti quelli che, finita la scrittura, si aspettavano di essere in `Reveal`. È atteso — la fase in mezzo è nuova.

- [ ] **Step 9: Aggiornare i test esistenti che saltavano la fase**

Nei file `RevealTests.cs`, `VotoTests.cs`, `NuovaPartitaTests.cs`, `BotTests.cs`, `AbbandonoTests.cs`, gli aiutanti che portano una partita fino al reveal devono ora attraversare la rifinitura. Dopo l'ultimo `SlotSubmitted`, aggiungere:

```csharp
        // Dalla fine della scrittura si passa per la rifinitura: nei test del
        // motore la si conclude senza modifiche, perche' il modello non c'e'.
        stato = _motore.Handle(stato, new RefinementFinished(null)).State;
```

Non toccare le asserzioni: solo l'attraversamento della fase.

- [ ] **Step 10: Eseguire la suite completa**

```bash
dotnet test FrasiSquisite.slnx --nologo -v q
```

Atteso: `Non superati: 0` su Domain e Shared. **`Server.Tests` fallirà**: i test d'integrazione portano una partita fino in fondo e ora si fermano in `Refining`, perché nessuno esegue ancora l'effetto. È il Task 5.

- [ ] **Step 11: Commit**

```bash
git add -A src tests && git commit -m "feat(ai): fase di rifinitura nel motore, come effetto e evento"
```

---

## Task 5: `GameHost` esegue l'effetto — senza attenderlo

Il punto più delicato del lotto.

**Files:**
- Create: `src/FrasiSquisite.Server/Ai/RefinementRunner.cs`
- Modify: `src/FrasiSquisite.Server/Realtime/GameHost.cs`
- Modify: `src/FrasiSquisite.Server/Program.cs`
- Test: `tests/FrasiSquisite.Server.Tests/Ai/RefinementRunnerTests.cs`

**Interfaces:**
- Consumes: `IAiTextProvider` (Task 3), `RequestRefinement` e `RefinementFinished` (Task 4), `RefinementGuard.MaxCaratteri` (Task 2).
- Produces: `sealed class RefinementRunner` con `Task<IReadOnlyList<IReadOnlyList<string>>?> RifinisciAsync(IReadOnlyList<IReadOnlyList<string>> frasi, string template, CancellationToken ct)`.

- [ ] **Step 1: Capire perché l'effetto non si può attendere**

Leggere `src/FrasiSquisite.Server/Realtime/GameHost.cs`. `DispatchAsync` prende un lucchetto per stanza e lo tiene **per tutta l'esecuzione degli effetti**.

Se l'effetto AI venisse eseguito dentro quel ciclo, due cose andrebbero storte insieme:

1. Il lucchetto resterebbe preso per tutta la durata della chiamata al modello: nessun altro evento di quella stanza passerebbe.
2. Il risultato deve rientrare come **evento**, cioè con un'altra `DispatchAsync` sulla **stessa** stanza — che aspetterebbe lo stesso lucchetto, ancora preso da lei stessa. **Stallo.**

L'effetto va quindi avviato e non atteso, e il risultato rientra da una `DispatchAsync` nuova, a lucchetto già rilasciato.

- [ ] **Step 2: Scrivere i test che falliscono**

Creare `tests/FrasiSquisite.Server.Tests/Ai/RefinementRunnerTests.cs`:

```csharp
using FrasiSquisite.Server.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FrasiSquisite.Server.Tests.Ai;

public class RefinementRunnerTests
{
    private const string Template = "{0} {1}";

    private static RefinementRunner Crea(FakeAiTextProvider ai, int timeoutSecondi = 10) =>
        new(ai, Options.Create(new AiOptions { TimeoutSeconds = timeoutSecondi }), NullLogger<RefinementRunner>.Instance);

    [Fact]
    public async Task UnaRispostaBenFormataDiventaCaselleRifinite()
    {
        var ai = new FakeAiTextProvider
        {
            Risposta = """{"frasi": [{"caselle": ["la nonna", "con la mamma"]}]}""",
        };

        var esito = await Crea(ai).RifinisciAsync([["la nonna", "la mamma"]], Template, CancellationToken.None);

        Assert.NotNull(esito);
        Assert.Equal(["la nonna", "con la mamma"], Assert.Single(esito));
    }

    [Fact]
    public async Task IlTemplateFinisceNelMessaggioMandatoAlModello()
    {
        var ai = new FakeAiTextProvider { Risposta = """{"frasi": [{"caselle": ["a", "b"]}]}""" };

        await Crea(ai).RifinisciAsync([["a", "b"]], Template, CancellationToken.None);

        Assert.Contains(Template, ai.UltimoUtente!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IlContenutoDelleCaselleFinisceNelMessaggio()
    {
        var ai = new FakeAiTextProvider { Risposta = """{"frasi": [{"caselle": ["a", "b"]}]}""" };

        await Crea(ai).RifinisciAsync([["primo", "secondo"]], Template, CancellationToken.None);

        Assert.Contains("primo", ai.UltimoUtente!, StringComparison.Ordinal);
        Assert.Contains("secondo", ai.UltimoUtente!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SenzaRispostaDalModelloSiRestituisceNull()
    {
        var ai = new FakeAiTextProvider { Risposta = null };

        Assert.Null(await Crea(ai).RifinisciAsync([["a", "b"]], Template, CancellationToken.None));
    }

    [Fact]
    public async Task UnaRispostaIllegibileNonFaEsplodereNiente()
    {
        var ai = new FakeAiTextProvider { Risposta = "non sono JSON" };

        Assert.Null(await Crea(ai).RifinisciAsync([["a", "b"]], Template, CancellationToken.None));
    }

    /// <summary>
    /// I modelli incorniciano spesso il JSON in un blocco markdown: scartarlo
    /// per questo sarebbe buttare via una risposta buona.
    /// </summary>
    [Fact]
    public async Task UnJsonAvvoltoInUnBloccoMarkdownVieneComunqueLetto()
    {
        var ai = new FakeAiTextProvider
        {
            Risposta = "```json\n{\"frasi\": [{\"caselle\": [\"a\", \"con b\"]}]}\n```",
        };

        var esito = await Crea(ai).RifinisciAsync([["a", "b"]], Template, CancellationToken.None);

        Assert.NotNull(esito);
        Assert.Equal(["a", "con b"], Assert.Single(esito));
    }

    [Fact]
    public async Task OltreIlTimeoutSiRestituisceNull()
    {
        var ai = new FakeAiTextProvider
        {
            Risposta = """{"frasi": [{"caselle": ["a", "b"]}]}""",
            Ritardo = TimeSpan.FromSeconds(5),
        };

        var esito = await Crea(ai, timeoutSecondi: 1)
            .RifinisciAsync([["a", "b"]], Template, CancellationToken.None);

        Assert.Null(esito);
    }

    [Fact]
    public async Task UnaChiamataSolaPerTutteLeFrasi()
    {
        var ai = new FakeAiTextProvider
        {
            Risposta = """{"frasi": [{"caselle": ["a", "b"]}, {"caselle": ["c", "d"]}]}""",
        };

        await Crea(ai).RifinisciAsync([["a", "b"], ["c", "d"]], Template, CancellationToken.None);

        Assert.Equal(1, ai.Chiamate);
    }
}
```

- [ ] **Step 3: Eseguire per verificare che fallisca**

```bash
dotnet test tests/FrasiSquisite.Server.Tests --nologo --filter "RefinementRunnerTests"
```

Atteso: errore di compilazione, `The type or namespace name 'RefinementRunner' could not be found`.

- [ ] **Step 4: Scrivere il runner**

Creare `src/FrasiSquisite.Server/Ai/RefinementRunner.cs`:

```csharp
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FrasiSquisite.Server.Ai;

/// <summary>
/// Compone il prompt, chiama il modello, legge la risposta. Non decide se
/// fidarsi di quello che torna: quello e' compito di RefinementGuard, dentro
/// il motore, dove e' provabile senza rete.
/// </summary>
public sealed class RefinementRunner(
    IAiTextProvider ai,
    IOptions<AiOptions> opzioni,
    ILogger<RefinementRunner> logger)
{
    private readonly AiOptions _opzioni = opzioni.Value;

    private const string Sistema = """
        Sei un correttore di bozze per un gioco di frasi surreali.
        Ricevi le caselle di una frase, scritte da giocatori diversi che non
        si sono visti fra loro. Il tuo compito è UNICO: aggiungere il minimo
        tessuto connettivo perché la frase si legga — preposizioni, articoli,
        congiunzioni, accordi.

        REGOLE INDEROGABILI
        - Non sostituire le parole scelte dai giocatori. Devono comparire
          tutte, invariate, dentro la casella corrispondente.
        - Non riordinare le caselle e non spostarne il contenuto.
        - Non aggiungere idee, aggettivi o dettagli tuoi.
        - Se una casella si legge già bene, restituiscila identica.
        - Il template della frase contiene già del testo fisso: non ripeterlo,
          e non ripeterne nemmeno il senso.

        L'assurdo è voluto. Non renderlo sensato: rendilo leggibile.

        Rispondi solo con JSON, senza commenti e senza blocchi di codice:
        {"frasi": [{"caselle": ["...", "..."]}, ...]}
        Tante frasi quante ne ricevi, tante caselle quante ne ha ciascuna,
        nello stesso ordine.
        """;

    public async Task<IReadOnlyList<IReadOnlyList<string>>?> RifinisciAsync(
        IReadOnlyList<IReadOnlyList<string>> frasi,
        string template,
        CancellationToken ct)
    {
        using var scadenza = CancellationTokenSource.CreateLinkedTokenSource(ct);
        scadenza.CancelAfter(TimeSpan.FromSeconds(_opzioni.TimeoutSeconds));

        try
        {
            var utente = JsonSerializer.Serialize(new
            {
                template,
                frasi = frasi.Select(f => new { caselle = f }),
            });

            var risposta = await ai.CompletaAsync(Sistema, utente, scadenza.Token);

            return risposta is null ? null : Leggi(risposta, frasi.Count);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Rifinitura scaduta dopo {Secondi}s: si prosegue con le caselle grezze.", _opzioni.TimeoutSeconds);
            return null;
        }
    }

    /// <summary>
    /// I modelli incorniciano spesso il JSON in un blocco markdown, o ci
    /// mettono una frase davanti. Scartare una risposta buona per questo
    /// sarebbe uno spreco, quindi si cerca il primo oggetto JSON.
    /// </summary>
    private IReadOnlyList<IReadOnlyList<string>>? Leggi(string risposta, int atteso)
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

            var frasi = documento.RootElement.GetProperty("frasi");
            var esito = new List<IReadOnlyList<string>>();

            foreach (var frase in frasi.EnumerateArray())
            {
                esito.Add([.. frase.GetProperty("caselle").EnumerateArray().Select(c => c.GetString() ?? string.Empty)]);
            }

            // Il conteggio sbagliato lo gestisce comunque RefinementGuard, ma
            // fermarsi qui evita di portarsi dietro una risposta inutile.
            return esito.Count == atteso ? esito : null;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Risposta del modello illeggibile: si prosegue con le caselle grezze.");
            return null;
        }
    }
}
```

> **Una forma sola, sempre `{"frasi": [{"caselle": [...]}]}`**, anche quando la frase è una. Accettarne due renderebbe più difficile spiegarla al modello e raddoppierebbe i casi da validare.

- [ ] **Step 5: Far eseguire l'effetto a `GameHost`**

In `src/FrasiSquisite.Server/Realtime/GameHost.cs`, aggiungere `RefinementRunner runner` ai parametri del costruttore primario, e nel `switch` di `EseguiAsync`, prima del ramo di default:

```csharp
        // NON si attende, ed e' deliberato. DispatchAsync tiene il lucchetto
        // della stanza per tutta l'esecuzione degli effetti: aspettare qui
        // terrebbe fuori ogni altro evento per tutta la durata della chiamata
        // al modello, e - peggio - il risultato deve rientrare come EVENTO,
        // cioe' con un'altra DispatchAsync sulla stessa stanza, che
        // aspetterebbe quello stesso lucchetto. Stallo.
        RequestRefinement r => AvviaRifinitura(roomCode, r),
```

e il metodo, in fondo alla classe:

```csharp
    /// <summary>
    /// Avvia la rifinitura in sottofondo e ritorna subito. Il risultato
    /// rientra come evento, quando il lucchetto della stanza e' gia' stato
    /// rilasciato.
    /// </summary>
    private Task AvviaRifinitura(string roomCode, RequestRefinement richiesta)
    {
        _ = Task.Run(async () =>
        {
            IReadOnlyList<IReadOnlyList<string>>? rifinite = null;

            try
            {
                rifinite = await runner.RifinisciAsync(richiesta.Frasi, richiesta.Template, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Il runner non dovrebbe lanciare, ma questo e' un task
                // slegato: un'eccezione non osservata qui lascerebbe la
                // stanza in Refining per sempre, e nessuno lo saprebbe.
                logger.LogError(ex, "Rifinitura fallita per la stanza {RoomCode}.", roomCode);
            }

            try
            {
                await DispatchAsync(roomCode, new RefinementFinished(rifinite));
            }
            catch (Exception ex)
            {
                // La stanza puo' essere sparita nel frattempo (riavvio, o
                // tutti usciti): non c'e' piu' nessuno a cui importi, ma
                // resta l'unica traccia osservabile.
                logger.LogWarning(ex, "Esito della rifinitura non consegnabile alla stanza {RoomCode}.", roomCode);
            }
        });

        return Task.CompletedTask;
    }
```

In `src/FrasiSquisite.Server/Program.cs`, accanto agli altri servizi:

```csharp
builder.Services.AddSingleton<RefinementRunner>();
```

- [ ] **Step 6: Scrivere il test del degrado**

La spec §9 lo chiede esplicitamente, ed è il requisito §8.5 del design generale: *"il gioco è interamente giocabile senza AI"*, verificato e non sperato. Oggi lo si otterrebbe **per caso** — i test d'integrazione girano senza chiave — ma nessuno lo afferma, quindi un domani si potrebbe rendere l'AI obbligatoria senza che un test rosso lo segnali.

In `tests/FrasiSquisite.Server.Tests/Ai/AiConfigurazioneTests.cs`, aggiungere:

```csharp
    /// <summary>
    /// Il requisito §8.5 del design generale: senza AI il gioco arriva in
    /// fondo. Non e' un test sull'AI ma sulla sua ASSENZA, e va scritto
    /// perche' altrimenti la garanzia riposa sul fatto che nessuno abbia
    /// configurato una chiave nei test - cioe' su un caso, non su una scelta.
    /// </summary>
    [Fact]
    public async Task SenzaChiaveUnaPartitaArrivaDallaLobbyAllaClassifica()
    {
        await using var factory = new WebApplicationFactory<Program>();

        Assert.IsType<DisabledAiTextProvider>(factory.Services.GetRequiredService<IAiTextProvider>());

        // Il percorso completo e' gia' coperto da
        // GameHubTests.DueClientVotanoERicevonoLaClassifica, che gira contro
        // questa stessa configurazione: qui si inchioda il presupposto su cui
        // quel test poggia senza dirlo.
    }
```

> Se dopo averlo scritto ti accorgi che duplica soltanto un test esistente senza aggiungere un'affermazione nuova, **dillo nel report invece di tenerlo**: un test che non può fallire da solo è rumore, e in quel caso la cosa giusta è aggiungere l'asserzione sul provider dentro `GameHubTests` e cancellare questo.

- [ ] **Step 7: Eseguire la suite completa**

```bash
dotnet test FrasiSquisite.slnx --nologo -v q
```

Atteso: `Non superati: 0` su tutti e quattro i progetti. Nei test d'integrazione l'AI è disabilitata (nessuna chiave), quindi il provider restituisce subito `null` e la rifinitura conclude senza modifiche: le partite arrivano in fondo come prima.

- [ ] **Step 8: Commit**

```bash
git add -A src tests && git commit -m "feat(ai): GameHost esegue la rifinitura senza tenere il lucchetto"
```

---

## Task 6: Il client — schermata d'attesa e protocollo v6

**Files:**
- Modify: `src/FrasiSquisite.Shared/Protocol/ProtocolVersion.cs`
- Modify: `src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs`
- Modify: `src/FrasiSquisite.App/Pages/GamePage.xaml`
- Test: `tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs`
- Test: `tests/FrasiSquisite.Shared.Tests/Protocol/ProtocolContractTests.cs`

**Interfaces:**
- Consumes: la fase `Refining`, che arriva come stringa dentro `RoomStateMessage.Phase`.
- Produces: `ScreenState.Refining`.

- [ ] **Step 1: Scrivere i test che falliscono**

In `tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs`:

```csharp
    /// <summary>
    /// Eccezione consapevole alla regola stabilita col voto: "Writing" e
    /// "Voting" NON si mappano, perche' uno stato di stanza che arriva a
    /// partita in corso strapperebbe dalla schermata d'attesa chi ha gia'
    /// inviato o gia' votato. Qui e' l'opposto: durante la rifinitura nessuno
    /// ha niente da fare e tutti devono andare sulla schermata di passaggio.
    /// </summary>
    [Fact]
    public void LoStatoDiStanzaInRifinituraPortaTuttiSullaSchermataDiAttesa()
    {
        var (vm, conn) = Crea();

        conn.Emit(new RoomStateMessage(
            "ABCD",
            "Refining",
            [new PlayerView(Anna, "Anna", true, true, false)],
            "storia",
            8));

        Assert.Equal(ScreenState.Refining, vm.Screen);
    }

    [Fact]
    public void DallaRifinituraSiEsceQuandoArrivaIlPrimoPassoDiReveal()
    {
        var (vm, conn) = Crea();
        conn.Emit(new RoomStateMessage(
            "ABCD", "Refining", [new PlayerView(Anna, "Anna", true, true, false)], "storia", 8));

        conn.Emit(new RevealStepMessage(0, 2, [], false));

        Assert.Equal(ScreenState.Reveal, vm.Screen);
    }
```

In `tests/FrasiSquisite.Shared.Tests/Protocol/ProtocolContractTests.cs`, aggiornare l'asserzione sulla versione corrente da 5 a 6 e allungare la catena delle versioni incompatibili, seguendo la convenzione già documentata nel file.

- [ ] **Step 2: Eseguire per verificare che fallisca**

```bash
dotnet test tests/FrasiSquisite.App.Tests --nologo --filter "Rifinitura"
```

Atteso: errore di compilazione, `'ScreenState' does not contain a definition for 'Refining'`.

- [ ] **Step 3: Portare il protocollo a 6**

In `src/FrasiSquisite.Shared/Protocol/ProtocolVersion.cs`:

```csharp
    public const int Current = 6;
```

- [ ] **Step 4: Aggiungere lo stato di schermata**

In `src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs`, nell'enum, fra `Waiting` e `Reveal`:

```csharp
public enum ScreenState
{
    Home,
    Settings,
    Lobby,
    Writing,
    Waiting,
    Refining,
    Reveal,
    Voting,
    Finished,
}
```

e nel `case RoomStateMessage`, accanto ai rami esistenti:

```csharp
                // "Refining" SI mappa, a differenza di "Writing" e "Voting":
                // durante la rifinitura nessuno ha niente da fare e tutti
                // devono andare sulla schermata di passaggio, quindi qui la
                // mappatura e' esattamente cio' che serve.
                else if (stato.Phase == "Refining")
                {
                    Screen = ScreenState.Refining;
                }
```

- [ ] **Step 5: Aggiungere la schermata**

In `src/FrasiSquisite.App/Pages/GamePage.xaml`, **prima** della sezione Reveal:

```xml
            <!-- ================= Rifinitura ================= -->
            <VerticalStackLayout Spacing="20" IsVisible="{Binding Screen, Converter={StaticResource IsScreen}, ConverterParameter=Refining}">
                <Label Text="Il cadavere si ricompone…" Style="{StaticResource H2Label}"
                       HorizontalOptions="Center" HorizontalTextAlignment="Center" />
                <ActivityIndicator IsRunning="True" HorizontalOptions="Center" />
                <Label Style="{StaticResource FootnoteLabel}" HorizontalOptions="Center"
                       HorizontalTextAlignment="Center"
                       Text="Stiamo rimettendo a posto le preposizioni. Le parole restano vostre." />
            </VerticalStackLayout>
```

> Verificare `H2Label` e `FootnoteLabel` in `src/FrasiSquisite.App/Resources/Styles/Styles.xaml` prima di usarli: nel lotto precedente uno stile citato da un brief non esisteva.

- [ ] **Step 6: Compilare l'app ed eseguire la suite**

```bash
dotnet build src/FrasiSquisite.App -f net10.0-android --nologo
dotnet test FrasiSquisite.slnx --nologo -v q
```

Atteso: entrambi puliti.

- [ ] **Step 7: Commit**

```bash
git add -A src tests && git commit -m "feat(ai): schermata d'attesa della rifinitura, protocollo v6"
```

---

## Task 7: Chiave sul container, prova reale, APK

**Files:**
- Modify: `docker-compose.yml`
- Modify: `.superpowers/sdd/progress.md`

- [ ] **Step 1: Passare la variabile al container**

In `docker-compose.yml`, sotto `environment:`:

```yaml
      ASPNETCORE_ENVIRONMENT: Production
      # La chiave NON sta qui: arriva dal file .env accanto a questo compose,
      # che non e' in git. Docker Compose lo legge da solo.
      Ai__ApiKey: ${AI_API_KEY:-}
```

> `:-` fa sì che senza `.env` la variabile resti vuota e l'AI resti spenta, invece di far fallire l'avvio. È il degrado, applicato anche al deployment.

- [ ] **Step 2: Creare il `.env` sul container**

**Questo passo lo esegue Enrico, non un agente: la chiave non deve passare per la chat.**

```bash
ssh enrico@192.168.86.115
cd ~/apps/frasi-squisite
printf 'AI_API_KEY=%s\n' 'LA-CHIAVE' > .env
chmod 600 .env
```

Verificare che `.env` sia ignorato da git: `git check-ignore -v .env` deve stampare una riga. Se non lo è, aggiungerlo a `.gitignore` **prima** di scriverci dentro la chiave.

- [ ] **Step 3: Aggiornare e riavviare il servizio**

```bash
ssh enrico@192.168.86.115 'cd ~/apps/frasi-squisite && git pull && docker compose up -d --build'
```

- [ ] **Step 4: Verificare che l'AI risulti accesa**

```bash
ssh enrico@192.168.86.115 'docker logs frasi-squisite --tail 30'
```

Non deve comparire nessun errore all'avvio. Per una prova diretta, giocare una partita e osservare nei log l'assenza di avvisi `si prosegue senza rifinitura`.

- [ ] **Step 5: Costruire e installare l'APK**

```bash
dotnet build src/FrasiSquisite.App -c Debug -f net10.0-android -p:EmbedAssembliesIntoApk=true --nologo
"C:\Users\Enrico\AppData\Local\Android\Sdk\platform-tools\adb.exe" install -r "src\FrasiSquisite.App\bin\Debug\net10.0-android\com.supere.frasisquisite-Signed.apk"
```

`EmbedAssembliesIntoApk=true` non è opzionale: senza, il Fast Deployment produce un APK guscio che si apre e muore. Il protocollo è a v6, quindi **il vecchio APK viene rifiutato** e va reinstallato.

- [ ] **Step 6: Provare una partita vera**

Con un dispositivo e un bot:

1. Finito l'ultimo round, deve comparire **"Il cadavere si ricompone…"** per qualche secondo.
2. Il reveal scopre caselle **rifinite**: dove serviva, con la preposizione davanti.
3. Le parole scritte dai giocatori devono esserci ancora, invariate. Se una è stata sostituita, il controllo di contenimento ha un buco: annotarlo.
4. La frase composta al voto deve leggersi senza ripetizioni (`ed è andata a finire che è andata a finire`).
5. **Con la chiave rimossa dal `.env` e il servizio riavviato**, la partita deve arrivare in fondo lo stesso, con le caselle grezze e senza attese lunghe.

- [ ] **Step 7: Aggiornare il ledger e committare**

In `.superpowers/sdd/progress.md` registrare: lotto completato, il totale dei test, e cosa ha fatto davvero il modello sulle frasi vere — è l'unica cosa che nessun test può dire.

```bash
git add -A && git commit -m "docs: primo pezzo del lotto AI completato"
```
