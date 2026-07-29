# Frasi Squisite — Fase 1 (Nucleo) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Una partita di "cadavere squisito" giocabile dall'ingresso in lobby fino al reveal finale, con più telefoni Android collegati a un server locale.

**Architecture:** Il motore di gioco (`FrasiSquisite.Domain`) è una funzione pura `(stato, evento) → (nuovo stato, effetti)`: non conosce rete, database né `async`. Un adapter nel server (`GameHost`) esegue gli effetti su SignalR. Il client MAUI non calcola nulla sullo stato di gioco: rende quello che riceve.

**Tech Stack:** .NET 10, ASP.NET Core + SignalR, MAUI (solo `net10.0-android`), xUnit 2.9.3, `Microsoft.Extensions.DependencyInjection`.

## Global Constraints

- **TargetFramework:** `net10.0` per `Shared`, `Domain`, `Server` e i test; `net10.0-android` per `App`. Nessun altro target.
- **Gestione pacchetti centralizzata:** ogni nuovo pacchetto va aggiunto come `<PackageVersion>` in `Directory.Packages.props`, e referenziato nel `.csproj` **senza** attributo `Version`.
- **`Nullable` e `ImplicitUsings` sono abilitati** per tutti i progetti da `Directory.Build.props`. Non ridichiararli nei singoli `.csproj`.
- **`FrasiSquisite.Domain` non può referenziare** ASP.NET Core, SignalR, Entity Framework, `System.Net.*`, né usare `async`/`Task`. Solo `FrasiSquisite.Shared` e la BCL.
- **Il motore non chiama mai** `DateTime.Now`, `DateTime.UtcNow` né `Task.Delay`: il tempo arriva da `TimeProvider`, la casualità da `IRandomSource`.
- **Segretezza (requisito centrale, spec §2.3):** nessun messaggio diretto a un giocatore può contenere il testo di una casella non ancora rivelata. Vale per ogni task che produce messaggi.
- **Versione di protocollo:** `ProtocolVersion.Current = 1` per tutta la Fase 1.
- **Lingua:** identificatori pubblici di dominio in italiano dove il dominio è italiano (`Casella`, `Schema`, `Ruolo`); nomi dei test in italiano. Il resto in inglese, come da convenzione .NET.
- **Fuori scope in questa fase:** AI, voto, persistenza, timer di round, riconnessione, QR, schemi multipli. Non anticiparli. Se un task sembra richiederli, si è capito male il task.
- **Messaggi della spec §4.2 non implementati qui, deliberatamente:** `RejoinRoom`, `AddBot`, `RemovePlayer`, `SetSchema`, `RequestSuggestion`, `CastVote`, `GetArchive`, `GetGame`, `VotePhase`, `Results`, `ImageReady`, `GameAborted`, `ProtocolRejectedMessage` (in Fase 1 l'incompatibilità di protocollo viaggia come `HubException`, non come messaggio). Il tipo `ProtocolRejectedMessage` viene comunque definito nel Task 3 perché fa parte del contratto; resta inutilizzato fino alla fase con la riconnessione.

---

## File Structure

**`src/FrasiSquisite.Shared`**
- `Schemas/Casella.cs` — una casella dello schema (ruolo, prompt, esempio)
- `Schemas/Schema.cs` — schema grammaticale e composizione della frase
- `Schemas/ISchemaCatalog.cs` — accesso agli schemi disponibili
- `Schemas/EmbeddedSchemaCatalog.cs` — implementazione che legge i JSON embedded
- `Schemas/Data/surrealista-classico.json` — lo schema di riferimento, come dato
- `Validation/SlotTextValidator.cs` — validazione del testo di una casella
- `Protocol/ProtocolVersion.cs` — costante di versione
- `Protocol/ClientMessages.cs` — DTO client → server
- `Protocol/ServerMessages.cs` — DTO server → client
- `Protocol/ProtocolJson.cs` — opzioni di serializzazione condivise

**`src/FrasiSquisite.Domain`**
- `Model/Player.cs`, `Model/Slot.cs`, `Model/Phrase.cs`, `Model/RoomPhase.cs`, `Model/GameState.cs`
- `Randomness/IRandomSource.cs`, `Randomness/SystemRandomSource.cs`, `Randomness/SeededRandomSource.cs`
- `Modes/SlotAssignment.cs`, `Modes/IGameMode.cs`, `Modes/RoleSchemaMode.cs`
- `Filling/IWordPool.cs`, `Filling/StaticWordPool.cs` — dizionario di riserva per il bot che subentra a chi abbandona
- `Engine/GameEvent.cs` — eventi in ingresso al motore
- `Engine/Effect.cs` — effetti in uscita dal motore
- `Engine/EngineResult.cs`
- `Engine/IGameEngine.cs`, `Engine/GameEngine.cs`

**`src/FrasiSquisite.Server`**
- `Rooms/RoomCodeGenerator.cs` — generazione codici stanza
- `Rooms/IRoomRegistry.cs`, `Rooms/RoomRegistry.cs` — stanze attive in memoria
- `Realtime/GameHub.cs` — hub SignalR
- `Realtime/GameHost.cs` — esecuzione degli effetti
- `Program.cs` — wiring DI (modifica)

**`src/FrasiSquisite.App`**
- `Services/IGameConnection.cs`, `Services/SignalRGameConnection.cs`
- `ViewModels/HomeViewModel.cs`, `LobbyViewModel.cs`, `WritingViewModel.cs`, `RevealViewModel.cs`
- `Pages/HomePage.xaml(.cs)`, `LobbyPage.xaml(.cs)`, `WritingPage.xaml(.cs)`, `RevealPage.xaml(.cs)`
- `MauiProgram.cs` — wiring DI (modifica)

**`tests/`**
- `FrasiSquisite.Shared.Tests/` — schemi, validazione, contratti
- `FrasiSquisite.Domain.Tests/` — il grosso della copertura
- `FrasiSquisite.Server.Tests/` — registro stanze, integrazione hub
- `FrasiSquisite.App.Tests/` — ViewModel con `FakeGameConnection` (creato nel Task 10)

---

## Task 1: Lo schema grammaticale come dato

**Files:**
- Create: `src/FrasiSquisite.Shared/Schemas/Casella.cs`
- Create: `src/FrasiSquisite.Shared/Schemas/Schema.cs`
- Create: `src/FrasiSquisite.Shared/Schemas/ISchemaCatalog.cs`
- Create: `src/FrasiSquisite.Shared/Schemas/EmbeddedSchemaCatalog.cs`
- Create: `src/FrasiSquisite.Shared/Schemas/Data/surrealista-classico.json`
- Modify: `src/FrasiSquisite.Shared/FrasiSquisite.Shared.csproj`
- Test: `tests/FrasiSquisite.Shared.Tests/Schemas/EmbeddedSchemaCatalogTests.cs`
- Delete: `tests/FrasiSquisite.Shared.Tests/UnitTest1.cs`

**Interfaces:**
- Consumes: niente (primo task)
- Produces:
  - `record Casella(string Ruolo, string Prompt, string Esempio)`
  - `record Schema(string Id, int Version, string Nome, IReadOnlyList<Casella> Caselle, string Template)` con `int SlotCount` e `string Compose(IReadOnlyList<string> valori)`
  - `interface ISchemaCatalog { IReadOnlyList<Schema> All { get; } Schema Get(string id); }`
  - `class EmbeddedSchemaCatalog : ISchemaCatalog` con costruttore senza parametri
  - Costante `Schema.DefaultId = "surrealista-classico"`

- [ ] **Step 1: Scrivere il test che fallisce**

Crea `tests/FrasiSquisite.Shared.Tests/Schemas/EmbeddedSchemaCatalogTests.cs`:

```csharp
using FrasiSquisite.Shared.Schemas;
using Xunit;

namespace FrasiSquisite.Shared.Tests.Schemas;

public class EmbeddedSchemaCatalogTests
{
    private readonly ISchemaCatalog _catalogo = new EmbeddedSchemaCatalog();

    [Fact]
    public void CaricaLoSchemaSurrealistaClassico()
    {
        var schema = _catalogo.Get(Schema.DefaultId);

        Assert.Equal("surrealista-classico", schema.Id);
        Assert.Equal("Surrealista classico", schema.Nome);
        Assert.Equal(5, schema.SlotCount);
    }

    [Fact]
    public void OgniCasellaHaRuoloPromptEdEsempioNonVuoti()
    {
        var schema = _catalogo.Get(Schema.DefaultId);

        Assert.All(schema.Caselle, casella =>
        {
            Assert.False(string.IsNullOrWhiteSpace(casella.Ruolo));
            Assert.False(string.IsNullOrWhiteSpace(casella.Prompt));
            Assert.False(string.IsNullOrWhiteSpace(casella.Esempio));
        });
    }

    [Fact]
    public void ComponeLaFraseSecondoIlTemplate()
    {
        var schema = _catalogo.Get(Schema.DefaultId);

        var frase = schema.Compose(["Il cadavere", "squisito", "berrà", "il vino", "nuovo"]);

        Assert.Equal("Il cadavere squisito berrà il vino nuovo", frase);
    }

    [Fact]
    public void ComporreConUnNumeroSbagliatoDiValoriFallisce()
    {
        var schema = _catalogo.Get(Schema.DefaultId);

        Assert.Throws<ArgumentException>(() => schema.Compose(["uno", "due"]));
    }

    [Fact]
    public void ChiedereUnoSchemaInesistenteFallisceConMessaggioUtile()
    {
        var eccezione = Assert.Throws<KeyNotFoundException>(() => _catalogo.Get("non-esiste"));

        Assert.Contains("non-esiste", eccezione.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IlCatalogoEspoheTuttiGliSchemiCaricati()
    {
        Assert.NotEmpty(_catalogo.All);
        Assert.Contains(_catalogo.All, s => s.Id == Schema.DefaultId);
    }
}
```

Elimina il test segnaposto del template:

```bash
rm tests/FrasiSquisite.Shared.Tests/UnitTest1.cs
```

- [ ] **Step 2: Eseguire il test e verificare che fallisca**

Run: `dotnet test tests/FrasiSquisite.Shared.Tests --nologo`

Expected: FAIL in compilazione — `CS0246: Impossibile trovare il tipo o il nome dello spazio dei nomi 'FrasiSquisite.Shared.Schemas'`.

- [ ] **Step 3: Creare i modelli**

`src/FrasiSquisite.Shared/Schemas/Casella.cs`:

```csharp
namespace FrasiSquisite.Shared.Schemas;

/// <summary>
/// Una casella dello schema grammaticale. Il <paramref name="Ruolo"/> è testo
/// libero e non un enum: aggiungere ruoli nuovi deve restare una modifica al
/// JSON, non al codice (spec §6).
/// </summary>
public sealed record Casella(string Ruolo, string Prompt, string Esempio);
```

`src/FrasiSquisite.Shared/Schemas/Schema.cs`:

```csharp
using System.Globalization;

namespace FrasiSquisite.Shared.Schemas;

public sealed record Schema(
    string Id,
    int Version,
    string Nome,
    IReadOnlyList<Casella> Caselle,
    string Template)
{
    public const string DefaultId = "surrealista-classico";

    public int SlotCount => Caselle.Count;

    /// <summary>
    /// Compone la frase finale. Il template usa segnaposto numerati, così una
    /// casella può in futuro comparire più volte o in ordine diverso da quello
    /// di scrittura senza cambiare il formato dei dati (spec §6).
    /// </summary>
    public string Compose(IReadOnlyList<string> valori)
    {
        ArgumentNullException.ThrowIfNull(valori);

        if (valori.Count != SlotCount)
        {
            throw new ArgumentException(
                $"Lo schema '{Id}' ha {SlotCount} caselle, ricevuti {valori.Count} valori.",
                nameof(valori));
        }

        var argomenti = new object[valori.Count];
        for (var i = 0; i < valori.Count; i++)
        {
            argomenti[i] = valori[i];
        }

        return string.Format(CultureInfo.InvariantCulture, Template, argomenti);
    }
}
```

`src/FrasiSquisite.Shared/Schemas/ISchemaCatalog.cs`:

```csharp
namespace FrasiSquisite.Shared.Schemas;

public interface ISchemaCatalog
{
    IReadOnlyList<Schema> All { get; }

    /// <exception cref="KeyNotFoundException">Se lo schema non esiste.</exception>
    Schema Get(string id);
}
```

- [ ] **Step 4: Creare il dato e il catalogo**

`src/FrasiSquisite.Shared/Schemas/Data/surrealista-classico.json`:

```json
{
  "id": "surrealista-classico",
  "version": 1,
  "nome": "Surrealista classico",
  "template": "{0} {1} {2} {3} {4}",
  "caselle": [
    { "ruolo": "Soggetto",    "prompt": "Un soggetto, con l'articolo", "esempio": "Il cadavere" },
    { "ruolo": "Aggettivo",   "prompt": "Un aggettivo",                "esempio": "squisito" },
    { "ruolo": "Verbo",       "prompt": "Un verbo coniugato",          "esempio": "berrà" },
    { "ruolo": "Complemento", "prompt": "Un complemento oggetto",      "esempio": "il vino" },
    { "ruolo": "Aggettivo",   "prompt": "Un altro aggettivo",          "esempio": "nuovo" }
  ]
}
```

`src/FrasiSquisite.Shared/Schemas/EmbeddedSchemaCatalog.cs`:

```csharp
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;

namespace FrasiSquisite.Shared.Schemas;

/// <summary>
/// Legge gli schemi dai JSON embedded nell'assembly. In una fase successiva
/// affiancherà (non sostituirà) un catalogo servito dal server.
/// </summary>
public sealed class EmbeddedSchemaCatalog : ISchemaCatalog
{
    private const string ResourcePrefix = "FrasiSquisite.Shared.Schemas.Data.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ImmutableDictionary<string, Schema> _perId;

    public EmbeddedSchemaCatalog()
    {
        var assembly = typeof(EmbeddedSchemaCatalog).Assembly;
        var schemi = assembly.GetManifestResourceNames()
            .Where(nome => nome.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                        && nome.EndsWith(".json", StringComparison.Ordinal))
            .Select(nome => Leggi(assembly, nome))
            .ToList();

        All = [.. schemi];
        _perId = schemi.ToImmutableDictionary(s => s.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<Schema> All { get; }

    public Schema Get(string id) =>
        _perId.TryGetValue(id, out var schema)
            ? schema
            : throw new KeyNotFoundException($"Schema '{id}' non trovato.");

    private static Schema Leggi(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Risorsa '{resourceName}' non leggibile.");

        return JsonSerializer.Deserialize<Schema>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Risorsa '{resourceName}' contiene JSON nullo.");
    }
}
```

Aggiungi l'embedding in `src/FrasiSquisite.Shared/FrasiSquisite.Shared.csproj`, subito prima di `</Project>`:

```xml
  <ItemGroup>
    <EmbeddedResource Include="Schemas\Data\*.json" />
  </ItemGroup>
```

- [ ] **Step 5: Eseguire i test e verificare che passino**

Run: `dotnet test tests/FrasiSquisite.Shared.Tests --nologo`

Expected: PASS, 6 test superati.

Se `CaricaLoSchemaSurrealistaClassico` fallisce con "Risorsa non leggibile" o con un catalogo vuoto, il nome della risorsa embedded non corrisponde a `ResourcePrefix`: verificalo con

```bash
dotnet build src/FrasiSquisite.Shared -v n 2>&1 | grep -i embedded
```

- [ ] **Step 6: Commit**

```bash
git add src/FrasiSquisite.Shared tests/FrasiSquisite.Shared.Tests
git commit -m "feat(shared): schema grammaticale come dato JSON embedded"
```

---

## Task 2: Validazione del testo di una casella

**Files:**
- Create: `src/FrasiSquisite.Shared/Validation/SlotTextValidator.cs`
- Test: `tests/FrasiSquisite.Shared.Tests/Validation/SlotTextValidatorTests.cs`

**Interfaces:**
- Consumes: niente
- Produces:
  - `readonly record struct SlotTextValidation(bool IsValid, string? Error, string Normalized)`
  - `static class SlotTextValidator` con `const int MaxLength = 60` e `static SlotTextValidation Validate(string? testo)`

La validazione vive in `Shared` perché serve **due volte**: il client la usa per dare un errore immediato, il server la riapplica perché non può fidarsi del client. Stesso codice, nessuna possibilità di divergenza.

- [ ] **Step 1: Scrivere il test che fallisce**

Crea `tests/FrasiSquisite.Shared.Tests/Validation/SlotTextValidatorTests.cs`:

```csharp
using FrasiSquisite.Shared.Validation;
using Xunit;

namespace FrasiSquisite.Shared.Tests.Validation;

public class SlotTextValidatorTests
{
    [Theory]
    [InlineData("Il cadavere")]
    [InlineData("squisito")]
    [InlineData("berrà l'acqua")]
    [InlineData("a")]
    public void AccettaTestoNormale(string testo)
    {
        var esito = SlotTextValidator.Validate(testo);

        Assert.True(esito.IsValid);
        Assert.Null(esito.Error);
        Assert.Equal(testo, esito.Normalized);
    }

    [Fact]
    public void RimuoveGliSpaziAiBordi()
    {
        var esito = SlotTextValidator.Validate("   squisito  ");

        Assert.True(esito.IsValid);
        Assert.Equal("squisito", esito.Normalized);
    }

    [Fact]
    public void CollassaGliSpaziInterni()
    {
        var esito = SlotTextValidator.Validate("il    vino     nuovo");

        Assert.True(esito.IsValid);
        Assert.Equal("il vino nuovo", esito.Normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void RifiutaTestoVuoto(string? testo)
    {
        var esito = SlotTextValidator.Validate(testo);

        Assert.False(esito.IsValid);
        Assert.NotNull(esito.Error);
    }

    [Fact]
    public void RifiutaTestoTroppoLungo()
    {
        var testo = new string('a', SlotTextValidator.MaxLength + 1);

        var esito = SlotTextValidator.Validate(testo);

        Assert.False(esito.IsValid);
        Assert.Contains(SlotTextValidator.MaxLength.ToString(), esito.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void AccettaTestoDiLunghezzaMassima()
    {
        var testo = new string('a', SlotTextValidator.MaxLength);

        Assert.True(SlotTextValidator.Validate(testo).IsValid);
    }

    [Theory]
    [InlineData("prima\nseconda")]
    [InlineData("prima\rseconda")]
    [InlineData("con\0nullo")]
    public void RifiutaCaratteriDiControllo(string testo)
    {
        var esito = SlotTextValidator.Validate(testo);

        Assert.False(esito.IsValid);
        Assert.NotNull(esito.Error);
    }
}
```

- [ ] **Step 2: Eseguire il test e verificare che fallisca**

Run: `dotnet test tests/FrasiSquisite.Shared.Tests --nologo --filter "FullyQualifiedName~SlotTextValidatorTests"`

Expected: FAIL in compilazione — `CS0246` su `FrasiSquisite.Shared.Validation`.

- [ ] **Step 3: Scrivere l'implementazione minima**

`src/FrasiSquisite.Shared/Validation/SlotTextValidator.cs`:

```csharp
using System.Text.RegularExpressions;

namespace FrasiSquisite.Shared.Validation;

public readonly record struct SlotTextValidation(bool IsValid, string? Error, string Normalized)
{
    public static SlotTextValidation Ok(string normalized) => new(true, null, normalized);

    public static SlotTextValidation Fail(string error) => new(false, error, string.Empty);
}

/// <summary>
/// Validazione del testo di una casella. Vive in Shared perché il client la usa
/// per il feedback immediato e il server la riapplica non potendosi fidare del
/// client: unico codice, nessuna divergenza possibile.
/// </summary>
public static partial class SlotTextValidator
{
    public const int MaxLength = 60;

    public static SlotTextValidation Validate(string? testo)
    {
        if (string.IsNullOrWhiteSpace(testo))
        {
            return SlotTextValidation.Fail("Scrivi qualcosa.");
        }

        if (testo.Any(char.IsControl))
        {
            return SlotTextValidation.Fail("Niente a capo o caratteri strani.");
        }

        var normalizzato = SpaziMultipli().Replace(testo.Trim(), " ");

        if (normalizzato.Length > MaxLength)
        {
            return SlotTextValidation.Fail($"Massimo {MaxLength} caratteri.");
        }

        return SlotTextValidation.Ok(normalizzato);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaziMultipli();
}
```

- [ ] **Step 4: Eseguire i test e verificare che passino**

Run: `dotnet test tests/FrasiSquisite.Shared.Tests --nologo`

Expected: PASS, 21 test superati (6 del Task 1 + 15 di questo).

- [ ] **Step 5: Commit**

```bash
git add src/FrasiSquisite.Shared tests/FrasiSquisite.Shared.Tests
git commit -m "feat(shared): validazione del testo di una casella"
```

---

## Task 3: Contratti di protocollo

**Files:**
- Create: `src/FrasiSquisite.Shared/Protocol/ProtocolVersion.cs`
- Create: `src/FrasiSquisite.Shared/Protocol/ProtocolJson.cs`
- Create: `src/FrasiSquisite.Shared/Protocol/ClientMessages.cs`
- Create: `src/FrasiSquisite.Shared/Protocol/ServerMessages.cs`
- Test: `tests/FrasiSquisite.Shared.Tests/Protocol/ProtocolContractTests.cs`

**Interfaces:**
- Consumes: `Casella` (Task 1)
- Produces (tutti in `FrasiSquisite.Shared.Protocol`):
  - `static class ProtocolVersion { const int Current = 1; }`
  - `static class ProtocolJson { static JsonSerializerOptions Options { get; } }`
  - Client → server: `CreateRoomRequest(int ProtocolVersion, Guid PlayerId, string Nickname)`, `JoinRoomRequest(int ProtocolVersion, Guid PlayerId, string Nickname, string RoomCode)`, `StartGameRequest(string RoomCode)`, `SubmitSlotRequest(string RoomCode, string Text)`
  - Server → client: `PlayerView(Guid Id, string Nickname, bool IsHost, bool IsConnected)`, `RoomStateMessage(string RoomCode, string Phase, IReadOnlyList<PlayerView> Players, string SchemaId, int SlotCount)`, `SlotRequestMessage(int Round, int TotalRounds, string Ruolo, string Prompt, string Esempio)`, `RoundProgressMessage(int Round, int Submitted, int Total)`, `RevealStepMessage(int PhraseIndex, int TotalPhrases, IReadOnlyList<string> RevealedSlots, bool PhraseComplete, IReadOnlyList<string> Authors)`, `GameFinishedMessage(IReadOnlyList<string> Phrases)`, `ErrorMessage(string Code, string Message)`, `ProtocolRejectedMessage(int ServerVersion, string Message)`

**Nota sul contenuto di `SlotRequestMessage`:** contiene solo round, ruolo, prompt ed esempio. **Non** contiene l'indice della frase su cui si sta scrivendo né alcun testo già inserito. È il vincolo di segretezza della spec §2.3 espresso nel tipo: se il campo non esiste, non può trapelare.

`RevealStepMessage.Authors` è vuoto finché `PhraseComplete` è `false` (spec §2.4: l'autore si rivela solo a frase completa).

- [ ] **Step 1: Scrivere il test che fallisce**

Crea `tests/FrasiSquisite.Shared.Tests/Protocol/ProtocolContractTests.cs`:

```csharp
using System.Text.Json;
using FrasiSquisite.Shared.Protocol;
using Xunit;

namespace FrasiSquisite.Shared.Tests.Protocol;

public class ProtocolContractTests
{
    [Fact]
    public void LaVersioneDiProtocolloDellaFaseUnoE1()
    {
        Assert.Equal(1, ProtocolVersion.Current);
    }

    [Fact]
    public void SlotRequestSiSerializzaInCamelCase()
    {
        var messaggio = new SlotRequestMessage(
            Round: 0,
            TotalRounds: 5,
            Ruolo: "Soggetto",
            Prompt: "Un soggetto, con l'articolo",
            Esempio: "Il cadavere");

        var json = JsonSerializer.Serialize(messaggio, ProtocolJson.Options);

        Assert.Contains("\"round\":0", json, StringComparison.Ordinal);
        Assert.Contains("\"totalRounds\":5", json, StringComparison.Ordinal);
        Assert.Contains("\"ruolo\":\"Soggetto\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Test di segretezza a livello di contratto: se un giorno qualcuno
    /// aggiungesse alla richiesta di casella un campo con il testo della frase,
    /// questo test fallirebbe. Vedi spec §2.3.
    /// </summary>
    [Fact]
    public void SlotRequestNonEspoheAlcunCampoDiTesto()
    {
        var proprieta = typeof(SlotRequestMessage).GetProperties().Select(p => p.Name).ToArray();

        Assert.Equal(
            ["Round", "TotalRounds", "Ruolo", "Prompt", "Esempio"],
            proprieta);
    }

    // Nota: i record che contengono liste non hanno uguaglianza strutturale
    // (i record confrontano le liste per riferimento), quindi il roundtrip si
    // verifica campo per campo. Le singole liste si confrontano con
    // Assert.Equal, che sulle collezioni confronta gli elementi.
    [Fact]
    public void RoundtripDiRoomState()
    {
        var originale = new RoomStateMessage(
            RoomCode: "ABCD",
            Phase: "Lobby",
            Players: [new PlayerView(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Enrico", true, true)],
            SchemaId: "surrealista-classico",
            SlotCount: 5);

        var json = JsonSerializer.Serialize(originale, ProtocolJson.Options);
        var ricostruito = JsonSerializer.Deserialize<RoomStateMessage>(json, ProtocolJson.Options);

        Assert.NotNull(ricostruito);
        Assert.Equal(originale.RoomCode, ricostruito.RoomCode);
        Assert.Equal(originale.Phase, ricostruito.Phase);
        Assert.Equal(originale.SchemaId, ricostruito.SchemaId);
        Assert.Equal(originale.SlotCount, ricostruito.SlotCount);
        Assert.Equal(originale.Players, ricostruito.Players);
    }

    [Fact]
    public void RoundtripDiRevealStep()
    {
        var originale = new RevealStepMessage(
            PhraseIndex: 0,
            TotalPhrases: 3,
            RevealedSlots: ["Il cadavere", "squisito"],
            PhraseComplete: false,
            Authors: []);

        var json = JsonSerializer.Serialize(originale, ProtocolJson.Options);
        var ricostruito = JsonSerializer.Deserialize<RevealStepMessage>(json, ProtocolJson.Options);

        Assert.NotNull(ricostruito);
        Assert.Equal(originale.PhraseIndex, ricostruito.PhraseIndex);
        Assert.Equal(originale.TotalPhrases, ricostruito.TotalPhrases);
        Assert.Equal(originale.RevealedSlots, ricostruito.RevealedSlots);
        Assert.Equal(originale.PhraseComplete, ricostruito.PhraseComplete);
        Assert.Equal(originale.Authors, ricostruito.Authors);
    }
}
```

- [ ] **Step 2: Eseguire il test e verificare che fallisca**

Run: `dotnet test tests/FrasiSquisite.Shared.Tests --nologo --filter "FullyQualifiedName~ProtocolContractTests"`

Expected: FAIL in compilazione — `CS0246` su `FrasiSquisite.Shared.Protocol`.

- [ ] **Step 3: Scrivere i contratti**

`src/FrasiSquisite.Shared/Protocol/ProtocolVersion.cs`:

```csharp
namespace FrasiSquisite.Shared.Protocol;

/// <summary>
/// Con distribuzione via APK i client sono sempre disallineati fra loro: il
/// server deve poter rifiutare esplicitamente una versione incompatibile invece
/// di fallire in modo oscuro (spec §4.1).
/// </summary>
public static class ProtocolVersion
{
    public const int Current = 1;

    public static bool IsCompatible(int clientVersion) => clientVersion == Current;
}
```

`src/FrasiSquisite.Shared/Protocol/ProtocolJson.cs`:

```csharp
using System.Text.Json;

namespace FrasiSquisite.Shared.Protocol;

public static class ProtocolJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
```

`src/FrasiSquisite.Shared/Protocol/ClientMessages.cs`:

```csharp
namespace FrasiSquisite.Shared.Protocol;

public sealed record CreateRoomRequest(int ProtocolVersion, Guid PlayerId, string Nickname);

public sealed record JoinRoomRequest(int ProtocolVersion, Guid PlayerId, string Nickname, string RoomCode);

public sealed record StartGameRequest(string RoomCode);

public sealed record SubmitSlotRequest(string RoomCode, string Text);
```

`src/FrasiSquisite.Shared/Protocol/ServerMessages.cs`:

```csharp
namespace FrasiSquisite.Shared.Protocol;

public sealed record PlayerView(Guid Id, string Nickname, bool IsHost, bool IsConnected);

public sealed record RoomStateMessage(
    string RoomCode,
    string Phase,
    IReadOnlyList<PlayerView> Players,
    string SchemaId,
    int SlotCount);

/// <summary>
/// Contiene esclusivamente il ruolo da riempire. Nessun campo trasporta testo
/// già scritto, e questa assenza è il modo in cui la segretezza del gioco è
/// garantita dal tipo e non dalla disciplina di chi scrive il codice
/// (spec §2.3, §4.2).
/// </summary>
public sealed record SlotRequestMessage(
    int Round,
    int TotalRounds,
    string Ruolo,
    string Prompt,
    string Esempio);

public sealed record RoundProgressMessage(int Round, int Submitted, int Total);

/// <summary>
/// <paramref name="Authors"/> resta vuoto finché <paramref name="PhraseComplete"/>
/// è false: sapere chi scrive la casella successiva ne anticiperebbe il
/// contenuto (spec §2.4).
/// </summary>
public sealed record RevealStepMessage(
    int PhraseIndex,
    int TotalPhrases,
    IReadOnlyList<string> RevealedSlots,
    bool PhraseComplete,
    IReadOnlyList<string> Authors);

public sealed record GameFinishedMessage(IReadOnlyList<string> Phrases);

public sealed record ErrorMessage(string Code, string Message);

public sealed record ProtocolRejectedMessage(int ServerVersion, string Message);
```

- [ ] **Step 4: Eseguire i test e verificare che passino**

Run: `dotnet test tests/FrasiSquisite.Shared.Tests --nologo`

Expected: PASS, 26 test superati.

- [ ] **Step 5: Commit**

```bash
git add src/FrasiSquisite.Shared tests/FrasiSquisite.Shared.Tests
git commit -m "feat(shared): contratti di protocollo e versione handshake"
```

---

## Task 4: Modello di stato, casualità e modalità di gioco

**Files:**
- Create: `src/FrasiSquisite.Domain/Model/RoomPhase.cs`
- Create: `src/FrasiSquisite.Domain/Model/Player.cs`
- Create: `src/FrasiSquisite.Domain/Model/Slot.cs`
- Create: `src/FrasiSquisite.Domain/Model/Phrase.cs`
- Create: `src/FrasiSquisite.Domain/Model/GameState.cs`
- Create: `src/FrasiSquisite.Domain/Randomness/IRandomSource.cs`
- Create: `src/FrasiSquisite.Domain/Randomness/SystemRandomSource.cs`
- Create: `src/FrasiSquisite.Domain/Randomness/SeededRandomSource.cs`
- Create: `src/FrasiSquisite.Domain/Modes/SlotAssignment.cs`
- Create: `src/FrasiSquisite.Domain/Modes/IGameMode.cs`
- Create: `src/FrasiSquisite.Domain/Modes/RoleSchemaMode.cs`
- Test: `tests/FrasiSquisite.Domain.Tests/Modes/RoleSchemaModeTests.cs`
- Test: `tests/FrasiSquisite.Domain.Tests/TestSchemas.cs`
- Delete: `tests/FrasiSquisite.Domain.Tests/UnitTest1.cs`

**Interfaces:**
- Consumes: `Schema`, `Casella` (Task 1)
- Produces:
  - `enum RoomPhase { Lobby, Writing, Reveal, Finished }`
  - `record Player(Guid Id, string Nickname, bool IsBot, long JoinOrder, bool IsConnected)`
  - `record Slot(Guid AuthorId, string Text)`
  - `record Phrase(int Index, IReadOnlyList<Slot?> Slots)` con `bool IsComplete`
  - `record GameState(...)` — firma completa nello Step 3
  - `interface IRandomSource { int Next(int maxExclusive); }`
  - `record struct SlotAssignment(int PhraseIndex, int SlotIndex)`
  - `interface IGameMode { string Id { get; } int PhraseCount(int playerCount, Schema schema); SlotAssignment AssignSlot(int round, int playerIndex, int playerCount, Schema schema); bool IsComplete(GameState state); }`
  - `class RoleSchemaMode : IGameMode`
  - Helper di test `TestSchemas.WithSlots(int k)`

- [ ] **Step 1: Scrivere il test che fallisce**

Crea `tests/FrasiSquisite.Domain.Tests/TestSchemas.cs`:

```csharp
using FrasiSquisite.Shared.Schemas;

namespace FrasiSquisite.Domain.Tests;

public static class TestSchemas
{
    /// <summary>Schema sintetico con K caselle, per i test di proprietà.</summary>
    public static Schema WithSlots(int k)
    {
        var caselle = Enumerable.Range(0, k)
            .Select(i => new Casella($"Ruolo{i}", $"Prompt {i}", $"Esempio {i}"))
            .ToList();

        var template = string.Join(" ", Enumerable.Range(0, k).Select(i => $"{{{i}}}"));

        return new Schema($"test-{k}", 1, $"Test {k}", caselle, template);
    }
}
```

Crea `tests/FrasiSquisite.Domain.Tests/Modes/RoleSchemaModeTests.cs`:

```csharp
using FrasiSquisite.Domain.Modes;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Modes;

public class RoleSchemaModeTests
{
    private readonly IGameMode _modalita = new RoleSchemaMode();

    public static TheoryData<int, int> GiocatoriECaselle()
    {
        var dati = new TheoryData<int, int>();
        for (var n = 2; n <= 12; n++)
        {
            for (var k = 3; k <= 8; k++)
            {
                dati.Add(n, k);
            }
        }

        return dati;
    }

    /// <summary>
    /// La proprietà su cui poggia l'intero gioco (spec §2.2). Se questo test
    /// fallisce, esistono frasi con caselle doppie o mancanti.
    /// </summary>
    [Theory]
    [MemberData(nameof(GiocatoriECaselle))]
    public void OgniFraseRiceveOgniCasellaEsattamenteUnaVolta(int n, int k)
    {
        var schema = TestSchemas.WithSlots(k);
        var conteggi = new int[n, k];

        for (var round = 0; round < k; round++)
        {
            for (var giocatore = 0; giocatore < n; giocatore++)
            {
                var assegnazione = _modalita.AssignSlot(round, giocatore, n, schema);
                conteggi[assegnazione.PhraseIndex, assegnazione.SlotIndex]++;
            }
        }

        for (var frase = 0; frase < n; frase++)
        {
            for (var casella = 0; casella < k; casella++)
            {
                Assert.Equal(1, conteggi[frase, casella]);
            }
        }
    }

    /// <summary>
    /// Ogni giocatore copre tutte le K caselle dello schema, una per round.
    /// Asserisce sulle assegnazioni restituite, non sul numero di iterazioni
    /// del ciclo — altrimenti il test passerebbe qualunque cosa restituisca
    /// AssignSlot.
    /// </summary>
    [Theory]
    [MemberData(nameof(GiocatoriECaselle))]
    public void OgniGiocatoreCopreTutteLeCaselleUnaVoltaCiascuna(int n, int k)
    {
        var schema = TestSchemas.WithSlots(k);

        for (var giocatore = 0; giocatore < n; giocatore++)
        {
            var caselleScritte = Enumerable.Range(0, k)
                .Select(round => _modalita.AssignSlot(round, giocatore, n, schema).SlotIndex)
                .OrderBy(i => i)
                .ToList();

            Assert.Equal(Enumerable.Range(0, k), caselleScritte);
        }
    }

    /// <summary>
    /// Finché le caselle non superano i giocatori, nessuno scrive due volte
    /// sulla stessa frase: è ciò che rende varie le frasi risultanti.
    /// </summary>
    [Theory]
    [MemberData(nameof(GiocatoriECaselle))]
    public void UnGiocatoreNonTornaSullaStessaFraseFinchePuo(int n, int k)
    {
        var schema = TestSchemas.WithSlots(k);
        var frasiDistinteAttese = Math.Min(n, k);

        for (var giocatore = 0; giocatore < n; giocatore++)
        {
            var frasi = Enumerable.Range(0, k)
                .Select(round => _modalita.AssignSlot(round, giocatore, n, schema).PhraseIndex)
                .Distinct()
                .Count();

            Assert.Equal(frasiDistinteAttese, frasi);
        }
    }

    [Theory]
    [MemberData(nameof(GiocatoriECaselle))]
    public void InUnDatoRoundDueGiocatoriNonScrivonoMaiSullaStessaFrase(int n, int k)
    {
        var schema = TestSchemas.WithSlots(k);

        for (var round = 0; round < k; round++)
        {
            var frasi = Enumerable.Range(0, n)
                .Select(p => _modalita.AssignSlot(round, p, n, schema).PhraseIndex)
                .ToList();

            Assert.Equal(n, frasi.Distinct().Count());
        }
    }

    [Fact]
    public void IlNumeroDiFrasiEQuelloDeiGiocatori()
    {
        var schema = TestSchemas.WithSlots(5);

        Assert.Equal(4, _modalita.PhraseCount(4, schema));
    }
}
```

Elimina il segnaposto:

```bash
rm tests/FrasiSquisite.Domain.Tests/UnitTest1.cs
```

- [ ] **Step 2: Eseguire il test e verificare che fallisca**

Run: `dotnet test tests/FrasiSquisite.Domain.Tests --nologo`

Expected: FAIL in compilazione — `CS0246` su `FrasiSquisite.Domain.Modes`.

- [ ] **Step 3: Creare il modello**

`src/FrasiSquisite.Domain/Model/RoomPhase.cs`:

```csharp
namespace FrasiSquisite.Domain.Model;

/// <summary>
/// Fasi della Fase 1. Voting e Results arrivano nella fase implementativa
/// successiva: non anticiparli qui (spec §13).
/// </summary>
public enum RoomPhase
{
    Lobby,
    Writing,
    Reveal,
    Finished,
}
```

`src/FrasiSquisite.Domain/Model/Player.cs`:

```csharp
namespace FrasiSquisite.Domain.Model;

/// <summary>
/// <paramref name="JoinOrder"/> è un contatore monotono, non un timestamp: il
/// motore non può leggere l'orologio, e per stabilire "chi è presente da più
/// tempo" (successione dell'host) un ordinale basta e avanza.
/// </summary>
public sealed record Player(Guid Id, string Nickname, bool IsBot, long JoinOrder, bool IsConnected);
```

`src/FrasiSquisite.Domain/Model/Slot.cs`:

```csharp
namespace FrasiSquisite.Domain.Model;

public sealed record Slot(Guid AuthorId, string Text);
```

`src/FrasiSquisite.Domain/Model/Phrase.cs`:

```csharp
namespace FrasiSquisite.Domain.Model;

/// <summary>Una casella a <c>null</c> non è ancora stata scritta.</summary>
public sealed record Phrase(int Index, IReadOnlyList<Slot?> Slots)
{
    public bool IsComplete => Slots.All(s => s is not null);

    public static Phrase Empty(int index, int slotCount) =>
        new(index, new Slot?[slotCount]);

    public Phrase With(int slotIndex, Slot slot)
    {
        var caselle = Slots.ToArray();
        caselle[slotIndex] = slot;
        return this with { Slots = caselle };
    }
}
```

`src/FrasiSquisite.Domain/Model/GameState.cs`:

```csharp
using FrasiSquisite.Shared.Schemas;

namespace FrasiSquisite.Domain.Model;

public sealed record GameState(
    string RoomCode,
    RoomPhase Phase,
    Guid HostId,
    IReadOnlyList<Player> Players,
    Schema Schema,
    long NextJoinOrder,
    int Round,
    IReadOnlyList<Phrase> Phrases,
    IReadOnlySet<Guid> SubmittedThisRound,
    int RevealPhraseIndex,
    int RevealSlotCount)
{
    public static GameState NewRoom(string roomCode, Schema schema) =>
        new(
            RoomCode: roomCode,
            Phase: RoomPhase.Lobby,
            HostId: Guid.Empty,
            Players: [],
            Schema: schema,
            NextJoinOrder: 0,
            Round: 0,
            Phrases: [],
            SubmittedThisRound: new HashSet<Guid>(),
            RevealPhraseIndex: 0,
            RevealSlotCount: 0);

    public Player? FindPlayer(Guid id) => Players.FirstOrDefault(p => p.Id == id);

    public int IndexOfPlayer(Guid id)
    {
        for (var i = 0; i < Players.Count; i++)
        {
            if (Players[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }
}
```

- [ ] **Step 4: Creare casualità e modalità**

`src/FrasiSquisite.Domain/Randomness/IRandomSource.cs`:

```csharp
namespace FrasiSquisite.Domain.Randomness;

/// <summary>
/// La casualità è una dipendenza per poter riprodurre una partita da seed
/// (spec §3.3).
/// </summary>
public interface IRandomSource
{
    int Next(int maxExclusive);
}
```

`src/FrasiSquisite.Domain/Randomness/SystemRandomSource.cs`:

```csharp
namespace FrasiSquisite.Domain.Randomness;

public sealed class SystemRandomSource : IRandomSource
{
    public int Next(int maxExclusive) => Random.Shared.Next(maxExclusive);
}
```

`src/FrasiSquisite.Domain/Randomness/SeededRandomSource.cs`:

```csharp
namespace FrasiSquisite.Domain.Randomness;

public sealed class SeededRandomSource(int seed) : IRandomSource
{
    private readonly Random _random = new(seed);

    public int Next(int maxExclusive) => _random.Next(maxExclusive);
}
```

`src/FrasiSquisite.Domain/Modes/SlotAssignment.cs`:

```csharp
namespace FrasiSquisite.Domain.Modes;

public readonly record struct SlotAssignment(int PhraseIndex, int SlotIndex);
```

`src/FrasiSquisite.Domain/Modes/IGameMode.cs`:

```csharp
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Shared.Schemas;

namespace FrasiSquisite.Domain.Modes;

/// <summary>
/// La logica va scritta comunque: dietro un'interfaccia costa quasi nulla e fa
/// sì che la variante "frase a catena" diventi in futuro una classe nuova
/// invece di una riscrittura del motore (spec §3.4).
/// </summary>
public interface IGameMode
{
    string Id { get; }

    int PhraseCount(int playerCount, Schema schema);

    SlotAssignment AssignSlot(int round, int playerIndex, int playerCount, Schema schema);

    bool IsComplete(GameState state);
}
```

`src/FrasiSquisite.Domain/Modes/RoleSchemaMode.cs`:

```csharp
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Shared.Schemas;

namespace FrasiSquisite.Domain.Modes;

/// <summary>
/// N frasi in parallelo, K round. Al round r il giocatore p riempie la casella
/// r della frase (p + r) mod N (spec §2.2).
/// </summary>
public sealed class RoleSchemaMode : IGameMode
{
    public string Id => "role-schema";

    public int PhraseCount(int playerCount, Schema schema) => playerCount;

    public SlotAssignment AssignSlot(int round, int playerIndex, int playerCount, Schema schema)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(round);
        ArgumentOutOfRangeException.ThrowIfNegative(playerIndex);
        ArgumentOutOfRangeException.ThrowIfLessThan(playerCount, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(playerIndex, playerCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(round, schema.SlotCount);

        return new SlotAssignment((playerIndex + round) % playerCount, round);
    }

    public bool IsComplete(GameState state) => state.Round >= state.Schema.SlotCount;
}
```

- [ ] **Step 5: Eseguire i test e verificare che passino**

Run: `dotnet test tests/FrasiSquisite.Domain.Tests --nologo`

Expected: PASS, 265 test superati (66 combinazioni × 4 test di proprietà + 1 fact).

- [ ] **Step 6: Commit**

```bash
git add src/FrasiSquisite.Domain tests/FrasiSquisite.Domain.Tests
git commit -m "feat(domain): modello di stato e modalità a schema di ruoli"
```

---

## Task 5: Motore — lobby

**Files:**
- Create: `src/FrasiSquisite.Domain/Engine/GameEvent.cs`
- Create: `src/FrasiSquisite.Domain/Engine/Effect.cs`
- Create: `src/FrasiSquisite.Domain/Engine/EngineResult.cs`
- Create: `src/FrasiSquisite.Domain/Engine/IGameEngine.cs`
- Create: `src/FrasiSquisite.Domain/Engine/GameEngine.cs`
- Test: `tests/FrasiSquisite.Domain.Tests/Engine/LobbyTests.cs`
- Test: `tests/FrasiSquisite.Domain.Tests/EngineTestExtensions.cs`

**Interfaces:**
- Consumes: `GameState`, `Player`, `RoomPhase` (Task 4); messaggi di `Shared.Protocol` (Task 3)
- Produces:
  - `abstract record GameEvent` con `PlayerJoined(Guid PlayerId, string Nickname)`, `PlayerLeft(Guid PlayerId)`, `GameStartRequested(Guid RequestedBy)`, `SlotSubmitted(Guid PlayerId, string Text)`, `RevealAdvanceRequested(Guid RequestedBy)`
  - `abstract record Effect` con `SendToPlayer(Guid PlayerId, object Message)`, `BroadcastToRoom(object Message)`
  - `record EngineResult(GameState State, IReadOnlyList<Effect> Effects)`
  - `interface IGameEngine { EngineResult Handle(GameState state, GameEvent evt); }`
  - `class GameEngine(IGameMode mode) : IGameEngine`
  - Estensioni di test: `Messages<T>()`, `MessagesTo<T>(Guid)`, `Broadcasts<T>()`

- [ ] **Step 1: Scrivere il test che fallisce**

Crea `tests/FrasiSquisite.Domain.Tests/EngineTestExtensions.cs`:

```csharp
using FrasiSquisite.Domain.Engine;

namespace FrasiSquisite.Domain.Tests;

public static class EngineTestExtensions
{
    public static IEnumerable<T> Broadcasts<T>(this EngineResult result) =>
        result.Effects.OfType<BroadcastToRoom>().Select(e => e.Message).OfType<T>();

    public static IEnumerable<T> MessagesTo<T>(this EngineResult result, Guid playerId) =>
        result.Effects.OfType<SendToPlayer>()
            .Where(e => e.PlayerId == playerId)
            .Select(e => e.Message)
            .OfType<T>();

    public static IEnumerable<object> AllMessages(this EngineResult result) =>
        result.Effects.Select(e => e switch
        {
            SendToPlayer s => s.Message,
            BroadcastToRoom b => b.Message,
            _ => throw new InvalidOperationException($"Effetto non gestito: {e.GetType().Name}"),
        });
}
```

Crea `tests/FrasiSquisite.Domain.Tests/Engine/LobbyTests.cs`:

```csharp
using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Shared.Protocol;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Engine;

public class LobbyTests
{
    private static readonly Guid Anna = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Bruno = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid Carla = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    private readonly IGameEngine _motore = new GameEngine(new RoleSchemaMode());

    private GameState StanzaVuota() => GameState.NewRoom("ABCD", TestSchemas.WithSlots(5));

    [Fact]
    public void IlPrimoGiocatoreCheEntraDiventaHost()
    {
        var risultato = _motore.Handle(StanzaVuota(), new PlayerJoined(Anna, "Anna"));

        Assert.Equal(Anna, risultato.State.HostId);
        Assert.Single(risultato.State.Players);
    }

    [Fact]
    public void EntrandoSiRicevonoLoStatoDellaStanzaETuttiLoVedono()
    {
        var risultato = _motore.Handle(StanzaVuota(), new PlayerJoined(Anna, "Anna"));

        var stato = Assert.Single(risultato.Broadcasts<RoomStateMessage>());
        Assert.Equal("ABCD", stato.RoomCode);
        Assert.Equal(nameof(RoomPhase.Lobby), stato.Phase);
        Assert.Equal("Anna", Assert.Single(stato.Players).Nickname);
        Assert.True(Assert.Single(stato.Players).IsHost);
    }

    [Fact]
    public void IlSecondoGiocatoreNonDiventaHost()
    {
        var stato = StanzaVuota();
        stato = _motore.Handle(stato, new PlayerJoined(Anna, "Anna")).State;

        var risultato = _motore.Handle(stato, new PlayerJoined(Bruno, "Bruno"));

        Assert.Equal(Anna, risultato.State.HostId);
        Assert.Equal(2, risultato.State.Players.Count);
    }

    [Fact]
    public void RientrareConLoStessoIdNonDuplicaIlGiocatore()
    {
        var stato = StanzaVuota();
        stato = _motore.Handle(stato, new PlayerJoined(Anna, "Anna")).State;

        var risultato = _motore.Handle(stato, new PlayerJoined(Anna, "Anna"));

        Assert.Single(risultato.State.Players);
    }

    [Fact]
    public void QuandoEsceLHostIlRuoloPassaAlPresenteDaPiuTempo()
    {
        var stato = StanzaVuota();
        stato = _motore.Handle(stato, new PlayerJoined(Anna, "Anna")).State;
        stato = _motore.Handle(stato, new PlayerJoined(Bruno, "Bruno")).State;
        stato = _motore.Handle(stato, new PlayerJoined(Carla, "Carla")).State;

        var risultato = _motore.Handle(stato, new PlayerLeft(Anna));

        Assert.Equal(Bruno, risultato.State.HostId);
        Assert.Equal(2, risultato.State.Players.Count);
    }

    [Fact]
    public void QuandoEsceUnNonHostLHostNonCambia()
    {
        var stato = StanzaVuota();
        stato = _motore.Handle(stato, new PlayerJoined(Anna, "Anna")).State;
        stato = _motore.Handle(stato, new PlayerJoined(Bruno, "Bruno")).State;

        var risultato = _motore.Handle(stato, new PlayerLeft(Bruno));

        Assert.Equal(Anna, risultato.State.HostId);
    }

    [Fact]
    public void SoloLHostPuoAvviareLaPartita()
    {
        var stato = StanzaVuota();
        stato = _motore.Handle(stato, new PlayerJoined(Anna, "Anna")).State;
        stato = _motore.Handle(stato, new PlayerJoined(Bruno, "Bruno")).State;

        var risultato = _motore.Handle(stato, new GameStartRequested(Bruno));

        Assert.Equal(RoomPhase.Lobby, risultato.State.Phase);
        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Bruno));
        Assert.Equal("NOT_HOST", errore.Code);
    }

    [Fact]
    public void NonSiPuoAvviareUnaPartitaConUnSoloGiocatore()
    {
        var stato = StanzaVuota();
        stato = _motore.Handle(stato, new PlayerJoined(Anna, "Anna")).State;

        var risultato = _motore.Handle(stato, new GameStartRequested(Anna));

        Assert.Equal(RoomPhase.Lobby, risultato.State.Phase);
        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Anna));
        Assert.Equal("TOO_FEW_PLAYERS", errore.Code);
    }
}
```

- [ ] **Step 2: Eseguire il test e verificare che fallisca**

Run: `dotnet test tests/FrasiSquisite.Domain.Tests --nologo --filter "FullyQualifiedName~LobbyTests"`

Expected: FAIL in compilazione — `CS0246` su `FrasiSquisite.Domain.Engine`.

- [ ] **Step 3: Definire eventi, effetti e risultato**

`src/FrasiSquisite.Domain/Engine/GameEvent.cs`:

```csharp
namespace FrasiSquisite.Domain.Engine;

public abstract record GameEvent;

public sealed record PlayerJoined(Guid PlayerId, string Nickname) : GameEvent;

public sealed record PlayerLeft(Guid PlayerId) : GameEvent;

public sealed record GameStartRequested(Guid RequestedBy) : GameEvent;

public sealed record SlotSubmitted(Guid PlayerId, string Text) : GameEvent;

public sealed record RevealAdvanceRequested(Guid RequestedBy) : GameEvent;
```

`src/FrasiSquisite.Domain/Engine/Effect.cs`:

```csharp
namespace FrasiSquisite.Domain.Engine;

/// <summary>
/// Il motore descrive cosa andrebbe fatto; è l'adapter nel server a farlo.
/// Un test può quindi asserire sui messaggi che <em>sarebbero</em> stati
/// inviati, senza mockare nulla di rete (spec §3.2).
/// </summary>
public abstract record Effect;

public sealed record SendToPlayer(Guid PlayerId, object Message) : Effect;

public sealed record BroadcastToRoom(object Message) : Effect;
```

`src/FrasiSquisite.Domain/Engine/EngineResult.cs`:

```csharp
using FrasiSquisite.Domain.Model;

namespace FrasiSquisite.Domain.Engine;

public sealed record EngineResult(GameState State, IReadOnlyList<Effect> Effects)
{
    public static EngineResult NoChange(GameState state) => new(state, []);
}
```

`src/FrasiSquisite.Domain/Engine/IGameEngine.cs`:

```csharp
using FrasiSquisite.Domain.Model;

namespace FrasiSquisite.Domain.Engine;

public interface IGameEngine
{
    EngineResult Handle(GameState state, GameEvent evt);
}
```

- [ ] **Step 4: Implementare il motore per la lobby**

`src/FrasiSquisite.Domain/Engine/GameEngine.cs`:

```csharp
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Shared.Protocol;

namespace FrasiSquisite.Domain.Engine;

public sealed class GameEngine(IGameMode mode) : IGameEngine
{
    public const int MinPlayers = 2;

    private readonly IGameMode _mode = mode;

    public EngineResult Handle(GameState state, GameEvent evt) => evt switch
    {
        PlayerJoined e => OnPlayerJoined(state, e),
        PlayerLeft e => OnPlayerLeft(state, e),
        GameStartRequested e => OnGameStartRequested(state, e),
        _ => EngineResult.NoChange(state),
    };

    private static EngineResult OnPlayerJoined(GameState state, PlayerJoined e)
    {
        if (state.Phase != RoomPhase.Lobby)
        {
            return Error(state, e.PlayerId, "GAME_IN_PROGRESS", "La partita è già iniziata.");
        }

        if (state.FindPlayer(e.PlayerId) is not null)
        {
            return new EngineResult(state, [new BroadcastToRoom(RoomState(state))]);
        }

        var giocatore = new Player(e.PlayerId, e.Nickname, IsBot: false, state.NextJoinOrder, IsConnected: true);

        var nuovo = state with
        {
            Players = [.. state.Players, giocatore],
            NextJoinOrder = state.NextJoinOrder + 1,
            HostId = state.Players.Count == 0 ? e.PlayerId : state.HostId,
        };

        return new EngineResult(nuovo, [new BroadcastToRoom(RoomState(nuovo))]);
    }

    private static EngineResult OnPlayerLeft(GameState state, PlayerLeft e)
    {
        if (state.FindPlayer(e.PlayerId) is null)
        {
            return EngineResult.NoChange(state);
        }

        var rimasti = state.Players.Where(p => p.Id != e.PlayerId).ToList();

        // Successione dell'host: il presente da più tempo. La partita non muore
        // con chi l'ha creata (spec §9).
        var nuovoHost = state.HostId == e.PlayerId
            ? rimasti.OrderBy(p => p.JoinOrder).FirstOrDefault()?.Id ?? Guid.Empty
            : state.HostId;

        var nuovo = state with { Players = rimasti, HostId = nuovoHost };

        return new EngineResult(nuovo, [new BroadcastToRoom(RoomState(nuovo))]);
    }

    private EngineResult OnGameStartRequested(GameState state, GameStartRequested e)
    {
        if (state.Phase != RoomPhase.Lobby)
        {
            return Error(state, e.RequestedBy, "GAME_IN_PROGRESS", "La partita è già iniziata.");
        }

        if (state.HostId != e.RequestedBy)
        {
            return Error(state, e.RequestedBy, "NOT_HOST", "Solo chi ha creato la stanza può avviare.");
        }

        if (state.Players.Count < MinPlayers)
        {
            return Error(state, e.RequestedBy, "TOO_FEW_PLAYERS", $"Servono almeno {MinPlayers} giocatori.");
        }

        return StartGame(state);
    }

    // Implementato nel Task 6.
    private EngineResult StartGame(GameState state) => EngineResult.NoChange(state);

    private static EngineResult Error(GameState state, Guid playerId, string code, string message) =>
        new(state, [new SendToPlayer(playerId, new ErrorMessage(code, message))]);

    private static RoomStateMessage RoomState(GameState state) =>
        new(
            state.RoomCode,
            state.Phase.ToString(),
            [.. state.Players.Select(p => new PlayerView(p.Id, p.Nickname, p.Id == state.HostId, p.IsConnected))],
            state.Schema.Id,
            state.Schema.SlotCount);
}
```

- [ ] **Step 5: Eseguire i test e verificare che passino**

Run: `dotnet test tests/FrasiSquisite.Domain.Tests --nologo`

Expected: PASS, 273 test superati.

- [ ] **Step 6: Commit**

```bash
git add src/FrasiSquisite.Domain tests/FrasiSquisite.Domain.Tests
git commit -m "feat(domain): motore per lobby, ingresso e successione host"
```

---

## Task 6: Motore — avvio partita, round e segretezza

**Files:**
- Modify: `src/FrasiSquisite.Domain/Engine/GameEngine.cs`
- Test: `tests/FrasiSquisite.Domain.Tests/Engine/RoundTests.cs`
- Test: `tests/FrasiSquisite.Domain.Tests/Engine/SecretezzaTests.cs`

**Interfaces:**
- Consumes: tutto dal Task 5
- Produces: nessun tipo nuovo. `GameEngine` gestisce `SlotSubmitted` e implementa `StartGame`, emettendo `SlotRequestMessage` e `RoundProgressMessage`.

- [ ] **Step 1: Scrivere il test dei round che fallisce**

Crea `tests/FrasiSquisite.Domain.Tests/Engine/RoundTests.cs`:

```csharp
using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Shared.Protocol;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Engine;

public class RoundTests
{
    private readonly IGameEngine _motore = new GameEngine(new RoleSchemaMode());

    private static Guid Giocatore(int i) => Guid.Parse($"00000000-0000-0000-0000-{i:D12}");

    /// <summary>Crea una stanza con n giocatori e la porta in partita.</summary>
    private GameState PartitaAvviata(int n, int k)
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(k));

        for (var i = 0; i < n; i++)
        {
            stato = _motore.Handle(stato, new PlayerJoined(Giocatore(i), $"G{i}")).State;
        }

        return _motore.Handle(stato, new GameStartRequested(Giocatore(0))).State;
    }

    [Fact]
    public void AllAvvioSiCreanoNFrasiVuote()
    {
        var stato = PartitaAvviata(n: 4, k: 5);

        Assert.Equal(RoomPhase.Writing, stato.Phase);
        Assert.Equal(4, stato.Phrases.Count);
        Assert.All(stato.Phrases, f => Assert.Equal(5, f.Slots.Count));
        Assert.All(stato.Phrases, f => Assert.All(f.Slots, Assert.Null));
    }

    [Fact]
    public void AllAvvioOgniGiocatoreRiceveLaPropriaRichiestaDiCasella()
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(5));
        for (var i = 0; i < 3; i++)
        {
            stato = _motore.Handle(stato, new PlayerJoined(Giocatore(i), $"G{i}")).State;
        }

        var risultato = _motore.Handle(stato, new GameStartRequested(Giocatore(0)));

        for (var i = 0; i < 3; i++)
        {
            var richiesta = Assert.Single(risultato.MessagesTo<SlotRequestMessage>(Giocatore(i)));
            Assert.Equal(0, richiesta.Round);
            Assert.Equal(5, richiesta.TotalRounds);
            Assert.Equal("Ruolo0", richiesta.Ruolo);
        }
    }

    [Fact]
    public void InviareUnaCasellaLaRegistraSullaFraseAssegnata()
    {
        var stato = PartitaAvviata(n: 3, k: 5);

        // Round 0, giocatore 1 → frase (1 + 0) % 3 = 1, casella 0.
        var risultato = _motore.Handle(stato, new SlotSubmitted(Giocatore(1), "Il cadavere"));

        var slot = risultato.State.Phrases[1].Slots[0];
        Assert.NotNull(slot);
        Assert.Equal("Il cadavere", slot.Text);
        Assert.Equal(Giocatore(1), slot.AuthorId);
    }

    [Fact]
    public void IlTestoInviatoVieneNormalizzato()
    {
        var stato = PartitaAvviata(n: 3, k: 5);

        var risultato = _motore.Handle(stato, new SlotSubmitted(Giocatore(1), "  il   vino  "));

        Assert.Equal("il vino", risultato.State.Phrases[1].Slots[0]!.Text);
    }

    [Fact]
    public void UnTestoNonValidoVieneRifiutatoConErrore()
    {
        var stato = PartitaAvviata(n: 3, k: 5);

        var risultato = _motore.Handle(stato, new SlotSubmitted(Giocatore(1), "   "));

        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(1)));
        Assert.Equal("INVALID_TEXT", errore.Code);
        Assert.All(risultato.State.Phrases, f => Assert.All(f.Slots, Assert.Null));
    }

    [Fact]
    public void InviareDueVolteNelloStessoRoundVieneRifiutato()
    {
        var stato = PartitaAvviata(n: 3, k: 5);
        stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(1), "primo")).State;

        var risultato = _motore.Handle(stato, new SlotSubmitted(Giocatore(1), "secondo"));

        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(1)));
        Assert.Equal("ALREADY_SUBMITTED", errore.Code);
        Assert.Equal("primo", risultato.State.Phrases[1].Slots[0]!.Text);
    }

    [Fact]
    public void DopoOgniInvioTuttiVedonoIlProgressoDelRound()
    {
        var stato = PartitaAvviata(n: 3, k: 5);

        var risultato = _motore.Handle(stato, new SlotSubmitted(Giocatore(0), "uno"));

        var progresso = Assert.Single(risultato.Broadcasts<RoundProgressMessage>());
        Assert.Equal(0, progresso.Round);
        Assert.Equal(1, progresso.Submitted);
        Assert.Equal(3, progresso.Total);
    }

    [Fact]
    public void QuandoTuttiHannoInviatoSiPassaAlRoundSuccessivo()
    {
        var stato = PartitaAvviata(n: 3, k: 5);
        stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(0), "uno")).State;
        stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(1), "due")).State;

        var risultato = _motore.Handle(stato, new SlotSubmitted(Giocatore(2), "tre"));

        Assert.Equal(1, risultato.State.Round);
        Assert.Empty(risultato.State.SubmittedThisRound);

        for (var i = 0; i < 3; i++)
        {
            var richiesta = Assert.Single(risultato.MessagesTo<SlotRequestMessage>(Giocatore(i)));
            Assert.Equal(1, richiesta.Round);
            Assert.Equal("Ruolo1", richiesta.Ruolo);
        }
    }

    [Fact]
    public void DopoLUltimoRoundSiEntraInReveal()
    {
        var stato = PartitaAvviata(n: 3, k: 3);

        for (var round = 0; round < 3; round++)
        {
            for (var g = 0; g < 3; g++)
            {
                stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(g), $"r{round}g{g}")).State;
            }
        }

        Assert.Equal(RoomPhase.Reveal, stato.Phase);
        Assert.All(stato.Phrases, f => Assert.True(f.IsComplete));
    }

    [Fact]
    public void UnaPartitaCompletaRiempieOgniCasellaDiOgniFrase()
    {
        const int n = 5;
        const int k = 4;
        var stato = PartitaAvviata(n, k);

        for (var round = 0; round < k; round++)
        {
            for (var g = 0; g < n; g++)
            {
                stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(g), $"r{round}g{g}")).State;
            }
        }

        Assert.Equal(n, stato.Phrases.Count);
        Assert.All(stato.Phrases, f => Assert.All(f.Slots, s => Assert.NotNull(s)));
    }
}
```

- [ ] **Step 2: Scrivere il test di segretezza che fallisce**

Crea `tests/FrasiSquisite.Domain.Tests/Engine/SecretezzaTests.cs`:

```csharp
using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Modes;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Engine;

/// <summary>
/// Il requisito centrale del gioco (spec §2.3): nessun giocatore deve poter
/// vedere il contenuto di una casella non ancora rivelata. Questi test lo
/// verificano ispezionando ogni singolo messaggio prodotto dal motore.
/// </summary>
public class SecretezzaTests
{
    private readonly IGameEngine _motore = new GameEngine(new RoleSchemaMode());

    private static Guid Giocatore(int i) => Guid.Parse($"00000000-0000-0000-0000-{i:D12}");

    [Fact]
    public void NessunMessaggioDuranteLaScritturaContieneTestoScrittoDaAltri()
    {
        const int n = 4;
        const int k = 5;
        var testiSegreti = new List<string>();

        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(k));
        for (var i = 0; i < n; i++)
        {
            stato = _motore.Handle(stato, new PlayerJoined(Giocatore(i), $"G{i}")).State;
        }

        stato = _motore.Handle(stato, new GameStartRequested(Giocatore(0))).State;

        for (var round = 0; round < k; round++)
        {
            for (var g = 0; g < n; g++)
            {
                var segreto = $"SEGRETO-r{round}-g{g}";
                var risultato = _motore.Handle(stato, new SlotSubmitted(Giocatore(g), segreto));
                stato = risultato.State;

                // Ogni messaggio emesso finché siamo in scrittura non deve
                // contenere nessuno dei testi già inviati.
                if (stato.Phase == RoomPhase.Writing)
                {
                    var serializzati = risultato.AllMessages()
                        .Select(m => System.Text.Json.JsonSerializer.Serialize(m))
                        .ToList();

                    foreach (var precedente in testiSegreti)
                    {
                        Assert.All(serializzati, s =>
                            Assert.DoesNotContain(precedente, s, StringComparison.Ordinal));
                    }
                }

                testiSegreti.Add(segreto);
            }
        }
    }
}
```

- [ ] **Step 3: Eseguire i test e verificare che falliscano**

Run: `dotnet test tests/FrasiSquisite.Domain.Tests --nologo --filter "FullyQualifiedName~RoundTests|FullyQualifiedName~SecretezzaTests"`

Expected: FAIL — `AllAvvioSiCreanoNFrasiVuote` fallisce perché `StartGame` restituisce ancora `NoChange`.

- [ ] **Step 4: Implementare avvio, invio e avanzamento**

In `src/FrasiSquisite.Domain/Engine/GameEngine.cs`, aggiungi `SlotSubmitted` allo `switch` di `Handle`:

```csharp
    public EngineResult Handle(GameState state, GameEvent evt) => evt switch
    {
        PlayerJoined e => OnPlayerJoined(state, e),
        PlayerLeft e => OnPlayerLeft(state, e),
        GameStartRequested e => OnGameStartRequested(state, e),
        SlotSubmitted e => OnSlotSubmitted(state, e),
        _ => EngineResult.NoChange(state),
    };
```

Sostituisci il segnaposto `StartGame` con:

```csharp
    private EngineResult StartGame(GameState state)
    {
        var frasi = Enumerable
            .Range(0, _mode.PhraseCount(state.Players.Count, state.Schema))
            .Select(i => Phrase.Empty(i, state.Schema.SlotCount))
            .ToList();

        var nuovo = state with
        {
            Phase = RoomPhase.Writing,
            Round = 0,
            Phrases = frasi,
            SubmittedThisRound = new HashSet<Guid>(),
        };

        List<Effect> effetti = [new BroadcastToRoom(RoomState(nuovo))];
        effetti.AddRange(SlotRequests(nuovo));

        return new EngineResult(nuovo, effetti);
    }
```

Aggiungi i metodi seguenti alla classe:

```csharp
    private EngineResult OnSlotSubmitted(GameState state, SlotSubmitted e)
    {
        if (state.Phase != RoomPhase.Writing)
        {
            return Error(state, e.PlayerId, "NOT_WRITING", "Non è il momento di scrivere.");
        }

        var indice = state.IndexOfPlayer(e.PlayerId);
        if (indice < 0)
        {
            return Error(state, e.PlayerId, "NOT_IN_ROOM", "Non sei in questa stanza.");
        }

        if (state.SubmittedThisRound.Contains(e.PlayerId))
        {
            return Error(state, e.PlayerId, "ALREADY_SUBMITTED", "Hai già inviato per questo round.");
        }

        // Il server riapplica la validazione: non può fidarsi del client.
        var esito = SlotTextValidator.Validate(e.Text);
        if (!esito.IsValid)
        {
            return Error(state, e.PlayerId, "INVALID_TEXT", esito.Error!);
        }

        var assegnazione = _mode.AssignSlot(state.Round, indice, state.Players.Count, state.Schema);

        var frasi = state.Phrases.ToArray();
        frasi[assegnazione.PhraseIndex] = frasi[assegnazione.PhraseIndex]
            .With(assegnazione.SlotIndex, new Slot(e.PlayerId, esito.Normalized));

        var inviati = new HashSet<Guid>(state.SubmittedThisRound) { e.PlayerId };

        var nuovo = state with { Phrases = frasi, SubmittedThisRound = inviati };

        if (inviati.Count < nuovo.Players.Count)
        {
            return new EngineResult(nuovo, [
                new BroadcastToRoom(new RoundProgressMessage(nuovo.Round, inviati.Count, nuovo.Players.Count)),
            ]);
        }

        return AdvanceRound(nuovo);
    }

    private EngineResult AdvanceRound(GameState state)
    {
        var prossimo = state with
        {
            Round = state.Round + 1,
            SubmittedThisRound = new HashSet<Guid>(),
        };

        if (_mode.IsComplete(prossimo))
        {
            var reveal = prossimo with
            {
                Phase = RoomPhase.Reveal,
                RevealPhraseIndex = 0,
                RevealSlotCount = 0,
            };

            return new EngineResult(reveal, [new BroadcastToRoom(RoomState(reveal))]);
        }

        List<Effect> effetti = [
            new BroadcastToRoom(new RoundProgressMessage(state.Round, state.Players.Count, state.Players.Count)),
        ];
        effetti.AddRange(SlotRequests(prossimo));

        return new EngineResult(prossimo, effetti);
    }

    private IEnumerable<Effect> SlotRequests(GameState state)
    {
        for (var i = 0; i < state.Players.Count; i++)
        {
            var assegnazione = _mode.AssignSlot(state.Round, i, state.Players.Count, state.Schema);
            var casella = state.Schema.Caselle[assegnazione.SlotIndex];

            // Nota: PhraseIndex resta deliberatamente fuori dal messaggio.
            yield return new SendToPlayer(
                state.Players[i].Id,
                new SlotRequestMessage(
                    state.Round,
                    state.Schema.SlotCount,
                    casella.Ruolo,
                    casella.Prompt,
                    casella.Esempio));
        }
    }
```

Aggiungi in cima al file l'using per la validazione:

```csharp
using FrasiSquisite.Shared.Validation;
```

- [ ] **Step 5: Eseguire i test e verificare che passino**

Run: `dotnet test tests/FrasiSquisite.Domain.Tests --nologo`

Expected: PASS, 284 test superati.

- [ ] **Step 6: Commit**

```bash
git add src/FrasiSquisite.Domain tests/FrasiSquisite.Domain.Tests
git commit -m "feat(domain): avvio partita, avanzamento round e test di segretezza"
```

---

## Task 6b: Abbandono durante la partita — il bot subentra

**Files:**
- Create: `src/FrasiSquisite.Domain/Filling/IWordPool.cs`
- Create: `src/FrasiSquisite.Domain/Filling/StaticWordPool.cs`
- Modify: `src/FrasiSquisite.Domain/Engine/GameEngine.cs`
- Modify: `tests/FrasiSquisite.Domain.Tests/Engine/SecretezzaTests.cs`
- Modify: `tests/FrasiSquisite.Domain.Tests/Engine/LobbyTests.cs`, `RoundTests.cs` (solo la costruzione del motore)
- Test: `tests/FrasiSquisite.Domain.Tests/Engine/AbbandonoTests.cs`

**Interfaces:**
- Consumes: `GameState`, `Player`, `IGameMode`, `IRandomSource`, `SlotTextValidator`
- Produces:
  - `interface IWordPool { string Take(string ruolo, IRandomSource random); }`
  - `class StaticWordPool : IWordPool`
  - `GameEngine` cambia costruttore: `GameEngine(IGameMode mode, IWordPool pool, IRandomSource random)`

**Perché.** La spec §2.2 stabilisce che N è fissato a `StartGame` e non cambia. L'implementazione del Task 6 invece rimuove il giocatore uscito da `Players`, e questo produce tre guasti reali: il round avanza pur mancando qualcuno lasciando caselle `null` per sempre; le assegnazioni si rimappano su un N diverso e un giocatore può sovrascrivere una casella già scritta; con due giocatori l'uscita di uno fa lanciare `ArgumentOutOfRangeException` dal `ThrowIfLessThan(playerCount, 2)` di `AssignSlot`.

La soluzione: **chi si disconnette mantiene il posto e un bot gioca per lui.** In `Lobby` uscire rimuove ancora il giocatore — lì nessuna partita è in corso e N non è ancora fissato.

Il dizionario statico è quello che la spec §8.2 prevede come riserva quando l'AI è irraggiungibile. Arriva qui in anticipo rispetto alla fase 4 perché il bot ne ha bisogno adesso; in fase 4 l'`IAiProvider` diventa semplicemente un'altra implementazione dietro la stessa idea, senza che il motore cambi.

- [ ] **Step 1: Scrivere i test che falliscono**

Crea `tests/FrasiSquisite.Domain.Tests/Engine/AbbandonoTests.cs`:

```csharp
using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Shared.Protocol;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Engine;

public class AbbandonoTests
{
    private readonly IGameEngine _motore =
        new GameEngine(new RoleSchemaMode(), new StaticWordPool(), new SeededRandomSource(1));

    private static Guid Giocatore(int i) => Guid.Parse($"00000000-0000-0000-0000-{i:D12}");

    private GameState PartitaAvviata(int n, int k)
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(k));

        for (var i = 0; i < n; i++)
        {
            stato = _motore.Handle(stato, new PlayerJoined(Giocatore(i), $"G{i}")).State;
        }

        return _motore.Handle(stato, new GameStartRequested(Giocatore(0))).State;
    }

    [Fact]
    public void InLobbyUscireRimuoveIlGiocatore()
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(5));
        stato = _motore.Handle(stato, new PlayerJoined(Giocatore(0), "G0")).State;
        stato = _motore.Handle(stato, new PlayerJoined(Giocatore(1), "G1")).State;

        var risultato = _motore.Handle(stato, new PlayerLeft(Giocatore(1)));

        Assert.Single(risultato.State.Players);
    }

    [Fact]
    public void InPartitaUscireNonRimuoveIlGiocatoreMaLoMarcaDisconnesso()
    {
        var stato = PartitaAvviata(n: 3, k: 5);

        var risultato = _motore.Handle(stato, new PlayerLeft(Giocatore(1)));

        Assert.Equal(3, risultato.State.Players.Count);
        Assert.False(risultato.State.FindPlayer(Giocatore(1))!.IsConnected);
    }

    [Fact]
    public void IlNumeroDiFrasiNonCambiaQuandoQualcunoAbbandona()
    {
        var stato = PartitaAvviata(n: 3, k: 5);

        var risultato = _motore.Handle(stato, new PlayerLeft(Giocatore(1)));

        Assert.Equal(3, risultato.State.Phrases.Count);
    }

    [Fact]
    public void LaCasellaDiChiAbbandonaVieneRiempitaDalBot()
    {
        var stato = PartitaAvviata(n: 3, k: 5);

        // Round 0, giocatore 1 -> frase (1 + 0) % 3 = 1, casella 0.
        var risultato = _motore.Handle(stato, new PlayerLeft(Giocatore(1)));

        var slot = risultato.State.Phrases[1].Slots[0];
        Assert.NotNull(slot);
        Assert.False(string.IsNullOrWhiteSpace(slot.Text));
        Assert.Equal(Giocatore(1), slot.AuthorId);
    }

    [Fact]
    public void ChiAbbandonaNonBloccaLAvanzamentoDelRound()
    {
        var stato = PartitaAvviata(n: 3, k: 5);
        stato = _motore.Handle(stato, new PlayerLeft(Giocatore(1))).State;

        stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(0), "uno")).State;
        var risultato = _motore.Handle(stato, new SlotSubmitted(Giocatore(2), "due"));

        Assert.Equal(1, risultato.State.Round);
    }

    [Fact]
    public void IlBotRiempieAncheINuoviRoundSenzaCheNessunoAspetti()
    {
        const int n = 3;
        const int k = 4;
        var stato = PartitaAvviata(n, k);
        stato = _motore.Handle(stato, new PlayerLeft(Giocatore(1))).State;

        for (var round = 0; round < k; round++)
        {
            foreach (var g in (int[])[0, 2])
            {
                stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(g), $"r{round}g{g}")).State;
            }
        }

        Assert.Equal(RoomPhase.Reveal, stato.Phase);
        Assert.All(stato.Phrases, f => Assert.True(f.IsComplete));
    }

    [Fact]
    public void ConDueGiocatoriLUscitaDiUnoNonFaEsplodereIlMotore()
    {
        var stato = PartitaAvviata(n: 2, k: 3);
        stato = _motore.Handle(stato, new PlayerLeft(Giocatore(1))).State;

        var risultato = _motore.Handle(stato, new SlotSubmitted(Giocatore(0), "sopravvissuto"));

        Assert.Equal(1, risultato.State.Round);
        Assert.Empty(risultato.MessagesTo<ErrorMessage>(Giocatore(0)));
    }

    [Fact]
    public void SeAbbandonaLHostIlRuoloPassaAUnConnesso()
    {
        var stato = PartitaAvviata(n: 3, k: 5);

        var risultato = _motore.Handle(stato, new PlayerLeft(Giocatore(0)));

        Assert.NotEqual(Giocatore(0), risultato.State.HostId);
        Assert.True(risultato.State.FindPlayer(risultato.State.HostId)!.IsConnected);
    }

    [Fact]
    public void ChiEGiaDisconnessoNonVieneRiempitoDueVolte()
    {
        var stato = PartitaAvviata(n: 3, k: 5);
        stato = _motore.Handle(stato, new PlayerLeft(Giocatore(1))).State;
        var testoBot = stato.Phrases[1].Slots[0]!.Text;

        var risultato = _motore.Handle(stato, new PlayerLeft(Giocatore(1)));

        Assert.Equal(testoBot, risultato.State.Phrases[1].Slots[0]!.Text);
    }
}
```

Crea inoltre `tests/FrasiSquisite.Domain.Tests/Filling/StaticWordPoolTests.cs`:

```csharp
using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Shared.Validation;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Filling;

public class StaticWordPoolTests
{
    private readonly IWordPool _pool = new StaticWordPool();

    [Theory]
    [InlineData("Soggetto")]
    [InlineData("Aggettivo")]
    [InlineData("Verbo")]
    [InlineData("Complemento")]
    public void RestituisceUnaParolaPerIRuoliNoti(string ruolo)
    {
        var parola = _pool.Take(ruolo, new SeededRandomSource(1));

        Assert.False(string.IsNullOrWhiteSpace(parola));
    }

    [Fact]
    public void PerUnRuoloSconosciutoRicadeSuUnaListaGenerica()
    {
        var parola = _pool.Take("RuoloCheNonEsiste", new SeededRandomSource(1));

        Assert.False(string.IsNullOrWhiteSpace(parola));
    }

    /// <summary>
    /// Il motore riapplica la validazione a ogni casella: se il dizionario
    /// contenesse una parola non valida, il riempimento del bot fallirebbe in
    /// partita e non qui.
    /// </summary>
    [Theory]
    [InlineData("Soggetto")]
    [InlineData("Aggettivo")]
    [InlineData("Verbo")]
    [InlineData("Complemento")]
    [InlineData("RuoloCheNonEsiste")]
    public void OgniParolaDelDizionarioSuperaLaValidazione(string ruolo)
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var parola = _pool.Take(ruolo, new SeededRandomSource(seed));

            Assert.True(SlotTextValidator.Validate(parola).IsValid, $"parola non valida: '{parola}'");
        }
    }

    [Fact]
    public void ConLoStessoSeedRestituisceLaStessaParola()
    {
        Assert.Equal(
            _pool.Take("Soggetto", new SeededRandomSource(42)),
            _pool.Take("Soggetto", new SeededRandomSource(42)));
    }
}
```

- [ ] **Step 2: Correggere l'ordinamento del test di segretezza**

In `tests/FrasiSquisite.Domain.Tests/Engine/SecretezzaTests.cs`, `testiSegreti.Add(segreto)` viene eseguito **dopo** il blocco di asserzioni, quindi i messaggi prodotti da una chiamata vengono confrontati solo con i segreti dei giri precedenti — mai con quello appena inviato. La fuga più probabile in assoluto, un messaggio che rimbalza in broadcast il testo appena scritto, comparirebbe esattamente e solo lì. Sposta l'aggiunta **prima** delle asserzioni:

```csharp
                var segreto = $"SEGRETO-r{round}-g{g}";
                var risultato = _motore.Handle(stato, new SlotSubmitted(Giocatore(g), segreto));
                stato = risultato.State;

                testiSegreti.Add(segreto);

                // Ogni messaggio emesso finché siamo in scrittura non deve
                // contenere nessuno dei testi inviati, incluso quello appena
                // scritto: la fuga tipica è un broadcast che lo rimbalza.
                if (stato.Phase == RoomPhase.Writing)
                {
                    var serializzati = risultato.AllMessages()
                        .Select(m => System.Text.Json.JsonSerializer.Serialize(m))
                        .ToList();

                    foreach (var precedente in testiSegreti)
                    {
                        Assert.All(serializzati, s =>
                            Assert.DoesNotContain(precedente, s, StringComparison.Ordinal));
                    }
                }
```

- [ ] **Step 3: Eseguire i test e verificare che falliscano**

Run: `dotnet test tests/FrasiSquisite.Domain.Tests --nologo`

Expected: FAIL in compilazione — `CS0246` su `FrasiSquisite.Domain.Filling`.

- [ ] **Step 4: Creare il dizionario di riserva**

`src/FrasiSquisite.Domain/Filling/IWordPool.cs`:

```csharp
using FrasiSquisite.Domain.Randomness;

namespace FrasiSquisite.Domain.Filling;

/// <summary>
/// Fonte di parole per riempire la casella di chi non è in grado di scriverla.
/// In fase 4 l'AI diventerà un'altra implementazione di questa idea, senza che
/// il motore cambi (spec §5, §8.2).
/// </summary>
public interface IWordPool
{
    string Take(string ruolo, IRandomSource random);
}
```

`src/FrasiSquisite.Domain/Filling/StaticWordPool.cs`:

```csharp
using System.Collections.Frozen;
using FrasiSquisite.Domain.Randomness;

namespace FrasiSquisite.Domain.Filling;

/// <summary>
/// Dizionario compilato nel binario. Deve funzionare senza rete e senza AI:
/// è la garanzia che una partita non si blocchi mai (spec §8.5).
/// </summary>
public sealed class StaticWordPool : IWordPool
{
    private static readonly FrozenDictionary<string, string[]> PerRuolo =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Soggetto"] = ["Il notaio", "La pantofola", "Un tram", "Il vescovo", "La zuppa", "Un ombrello"],
            ["Aggettivo"] = ["distratto", "elettrico", "sbilenco", "solenne", "tiepido", "invisibile"],
            ["Verbo"] = ["divora", "sussurra", "scavalca", "dimentica", "corteggia", "rimpiange"],
            ["Complemento"] = ["il tramonto", "una scala", "il silenzio", "tre valigie", "la domenica", "un lampione"],
            ["Avverbio"] = ["lentamente", "di nascosto", "per sbaglio", "controvoglia", "all'improvviso"],
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] Generico =
        ["qualcosa", "un tale", "altrove", "comunque", "una cosa", "chissà"];

    public string Take(string ruolo, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);

        var parole = PerRuolo.TryGetValue(ruolo, out var perRuolo) ? perRuolo : Generico;

        return parole[random.Next(parole.Length)];
    }
}
```

- [ ] **Step 5: Modificare il motore**

In `src/FrasiSquisite.Domain/Engine/GameEngine.cs`, cambia il costruttore e aggiungi gli using:

```csharp
using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Randomness;
```

```csharp
public sealed class GameEngine(IGameMode mode, IWordPool pool, IRandomSource random) : IGameEngine
{
    public const int MinPlayers = 2;

    private readonly IGameMode _mode = mode;
    private readonly IWordPool _pool = pool;
    private readonly IRandomSource _random = random;
```

Sostituisci `OnPlayerLeft` (che era `static`; ora non può esserlo più) con:

```csharp
    private EngineResult OnPlayerLeft(GameState state, PlayerLeft e)
    {
        var uscente = state.FindPlayer(e.PlayerId);
        if (uscente is null)
        {
            return EngineResult.NoChange(state);
        }

        // In lobby si esce davvero: N non è ancora fissato.
        if (state.Phase == RoomPhase.Lobby)
        {
            var rimasti = state.Players.Where(p => p.Id != e.PlayerId).ToList();

            var host = state.HostId == e.PlayerId
                ? rimasti.OrderBy(p => p.JoinOrder).FirstOrDefault()?.Id ?? Guid.Empty
                : state.HostId;

            var senza = state with { Players = rimasti, HostId = host };

            return new EngineResult(senza, [new BroadcastToRoom(RoomState(senza))]);
        }

        // A partita iniziata il posto resta occupato: N è fissato (spec §2.2).
        // Il giocatore viene marcato disconnesso e da qui in poi gioca il bot.
        if (!uscente.IsConnected)
        {
            return EngineResult.NoChange(state);
        }

        var giocatori = state.Players
            .Select(p => p.Id == e.PlayerId ? p with { IsConnected = false } : p)
            .ToList();

        var nuovoHost = state.HostId == e.PlayerId
            ? giocatori.Where(p => p.IsConnected).OrderBy(p => p.JoinOrder).FirstOrDefault()?.Id ?? state.HostId
            : state.HostId;

        var aggiornato = state with { Players = giocatori, HostId = nuovoHost };

        List<Effect> effetti = [new BroadcastToRoom(RoomState(aggiornato))];

        if (aggiornato.Phase == RoomPhase.Writing)
        {
            return FillDisconnected(aggiornato, effetti);
        }

        return new EngineResult(aggiornato, effetti);
    }
```

Aggiungi i due metodi di riempimento:

```csharp
    /// <summary>
    /// Riempie con il bot la casella di ogni giocatore disconnesso che non ha
    /// ancora inviato in questo round, e fa avanzare il round se con questo
    /// tutti hanno una casella. Nessuno resta mai in attesa di chi non c'è.
    /// </summary>
    private EngineResult FillDisconnected(GameState state, List<Effect> effetti)
    {
        var corrente = state;

        foreach (var giocatore in state.Players.Where(p => !p.IsConnected))
        {
            if (corrente.SubmittedThisRound.Contains(giocatore.Id))
            {
                continue;
            }

            corrente = ApplySlot(corrente, giocatore.Id, BotWord(corrente, giocatore.Id));
        }

        if (corrente.SubmittedThisRound.Count < corrente.Players.Count)
        {
            effetti.Add(new BroadcastToRoom(new RoundProgressMessage(
                corrente.Round, corrente.SubmittedThisRound.Count, corrente.Players.Count)));

            return new EngineResult(corrente, effetti);
        }

        var avanzato = AdvanceRound(corrente);

        return new EngineResult(avanzato.State, [.. effetti, .. avanzato.Effects]);
    }

    private string BotWord(GameState state, Guid playerId)
    {
        var indice = state.IndexOfPlayer(playerId);
        var assegnazione = _mode.AssignSlot(state.Round, indice, state.Players.Count, state.Schema);

        return _pool.Take(state.Schema.Caselle[assegnazione.SlotIndex].Ruolo, _random);
    }
```

Estrai da `OnSlotSubmitted` la scrittura della casella in un metodo riusabile, e usalo da entrambe le strade:

```csharp
    private GameState ApplySlot(GameState state, Guid playerId, string testoNormalizzato)
    {
        var indice = state.IndexOfPlayer(playerId);
        var assegnazione = _mode.AssignSlot(state.Round, indice, state.Players.Count, state.Schema);

        var frasi = state.Phrases.ToArray();
        frasi[assegnazione.PhraseIndex] = frasi[assegnazione.PhraseIndex]
            .With(assegnazione.SlotIndex, new Slot(playerId, testoNormalizzato));

        return state with
        {
            Phrases = frasi,
            SubmittedThisRound = new HashSet<Guid>(state.SubmittedThisRound) { playerId },
        };
    }
```

Il corpo di `OnSlotSubmitted`, dopo la validazione, diventa:

```csharp
        var nuovo = ApplySlot(state, e.PlayerId, esito.Normalized);

        if (nuovo.SubmittedThisRound.Count < nuovo.Players.Count)
        {
            return new EngineResult(nuovo, [
                new BroadcastToRoom(new RoundProgressMessage(
                    nuovo.Round, nuovo.SubmittedThisRound.Count, nuovo.Players.Count)),
            ]);
        }

        return AdvanceRound(nuovo);
```

Infine, in `AdvanceRound`, dopo aver costruito lo stato del round successivo e prima di emettere le richieste di casella, riempi subito chi è già disconnesso — altrimenti al round nuovo la partita tornerebbe ad aspettarlo:

```csharp
    private EngineResult AdvanceRound(GameState state)
    {
        var prossimo = state with
        {
            Round = state.Round + 1,
            SubmittedThisRound = new HashSet<Guid>(),
        };

        if (_mode.IsComplete(prossimo))
        {
            var reveal = prossimo with
            {
                Phase = RoomPhase.Reveal,
                RevealPhraseIndex = 0,
                RevealSlotCount = 0,
            };

            return new EngineResult(reveal, [new BroadcastToRoom(RoomState(reveal))]);
        }

        List<Effect> effetti = [
            new BroadcastToRoom(new RoundProgressMessage(state.Round, state.Players.Count, state.Players.Count)),
        ];
        effetti.AddRange(SlotRequests(prossimo));

        if (prossimo.Players.Any(p => !p.IsConnected))
        {
            return FillDisconnected(prossimo, effetti);
        }

        return new EngineResult(prossimo, effetti);
    }
```

`SlotRequests` continua a inviare la richiesta a tutti, disconnessi inclusi: il messaggio semplicemente non raggiunge nessuno, e mantenere il ciclo uniforme evita un ramo condizionale in più nel punto più delicato del motore.

- [ ] **Step 6: Aggiornare la costruzione del motore nei test esistenti**

In `LobbyTests.cs`, `RoundTests.cs` e `SecretezzaTests.cs` sostituisci

```csharp
    private readonly IGameEngine _motore = new GameEngine(new RoleSchemaMode());
```

con

```csharp
    private readonly IGameEngine _motore =
        new GameEngine(new RoleSchemaMode(), new StaticWordPool(), new SeededRandomSource(1));
```

aggiungendo gli using `FrasiSquisite.Domain.Filling` e `FrasiSquisite.Domain.Randomness`.

Attenzione a `LobbyTests.QuandoEsceLHostIlRuoloPassaAlPresenteDaPiuTempo` e `QuandoEsceUnNonHostLHostNonCambia`: restano validi perché operano in `Lobby`, dove uscire rimuove ancora davvero il giocatore.

- [ ] **Step 7: Eseguire i test e verificare che passino**

Run: `dotnet test tests/FrasiSquisite.Domain.Tests --nologo`

Expected: PASS, 304 test superati (284 precedenti + 9 `AbbandonoTests` + 11 `StaticWordPoolTests`).

- [ ] **Step 8: Commit**

```bash
git add src/FrasiSquisite.Domain tests/FrasiSquisite.Domain.Tests
git commit -m "fix(domain): il bot subentra a chi abbandona invece di rimuoverlo"
```

---

## Task 7: Motore — reveal

**Files:**
- Modify: `src/FrasiSquisite.Domain/Engine/GameEngine.cs`
- Test: `tests/FrasiSquisite.Domain.Tests/Engine/RevealTests.cs`

**Interfaces:**
- Consumes: tutto dai Task 5 e 6
- Produces: nessun tipo nuovo. `GameEngine` gestisce `RevealAdvanceRequested`, emettendo `RevealStepMessage` e infine `GameFinishedMessage`.

Il reveal è pilotato dall'host: ogni `RevealAdvanceRequested` scopre una casella. Quando una frase è completa il messaggio porta anche gli autori; quando tutte le frasi sono scoperte si passa a `Finished` con le frasi composte.

- [ ] **Step 1: Scrivere il test che fallisce**

Crea `tests/FrasiSquisite.Domain.Tests/Engine/RevealTests.cs`:

```csharp
using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Shared.Protocol;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Engine;

public class RevealTests
{
    private const int N = 3;
    private const int K = 3;

    private readonly IGameEngine _motore =
        new GameEngine(new RoleSchemaMode(), new StaticWordPool(), new SeededRandomSource(1));

    private static Guid Giocatore(int i) => Guid.Parse($"00000000-0000-0000-0000-{i:D12}");

    private GameState PartitaConclusa()
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(K));
        for (var i = 0; i < N; i++)
        {
            stato = _motore.Handle(stato, new PlayerJoined(Giocatore(i), $"G{i}")).State;
        }

        stato = _motore.Handle(stato, new GameStartRequested(Giocatore(0))).State;

        for (var round = 0; round < K; round++)
        {
            for (var g = 0; g < N; g++)
            {
                stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(g), $"p{round}{g}")).State;
            }
        }

        return stato;
    }

    [Fact]
    public void OgniAvanzamentoScopreUnaCasellaInPiu()
    {
        var stato = PartitaConclusa();

        var primo = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0)));
        var passo = Assert.Single(primo.Broadcasts<RevealStepMessage>());

        Assert.Equal(0, passo.PhraseIndex);
        Assert.Equal(N, passo.TotalPhrases);
        Assert.Single(passo.RevealedSlots);
        Assert.False(passo.PhraseComplete);
    }

    [Fact]
    public void GliAutoriRestanoNascostiFinoAFraseCompleta()
    {
        var stato = PartitaConclusa();

        for (var i = 0; i < K - 1; i++)
        {
            var parziale = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0)));
            var passo = Assert.Single(parziale.Broadcasts<RevealStepMessage>());

            Assert.False(passo.PhraseComplete);
            Assert.Empty(passo.Authors);

            stato = parziale.State;
        }

        var ultimo = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0)));
        var completo = Assert.Single(ultimo.Broadcasts<RevealStepMessage>());

        Assert.True(completo.PhraseComplete);
        Assert.Equal(K, completo.Authors.Count);
        Assert.Equal(K, completo.RevealedSlots.Count);
    }

    [Fact]
    public void DopoUnaFraseSiPassaAllaSuccessiva()
    {
        var stato = PartitaConclusa();

        for (var i = 0; i < K; i++)
        {
            stato = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0))).State;
        }

        var risultato = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0)));
        var passo = Assert.Single(risultato.Broadcasts<RevealStepMessage>());

        Assert.Equal(1, passo.PhraseIndex);
        Assert.Single(passo.RevealedSlots);
    }

    [Fact]
    public void SoloLHostPuoFarAvanzareIlReveal()
    {
        var stato = PartitaConclusa();

        var risultato = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(1)));

        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(1)));
        Assert.Equal("NOT_HOST", errore.Code);
        Assert.Empty(risultato.Broadcasts<RevealStepMessage>());
    }

    [Fact]
    public void ScopertaLUltimaFraseLaPartitaEConclusa()
    {
        var stato = PartitaConclusa();

        EngineResult risultato = null!;
        for (var i = 0; i < N * K; i++)
        {
            risultato = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0)));
            stato = risultato.State;
        }

        Assert.Equal(RoomPhase.Finished, stato.Phase);

        var finale = Assert.Single(risultato.Broadcasts<GameFinishedMessage>());
        Assert.Equal(N, finale.Phrases.Count);
        Assert.All(finale.Phrases, f => Assert.False(string.IsNullOrWhiteSpace(f)));
    }
}
```

- [ ] **Step 2: Eseguire il test e verificare che fallisca**

Run: `dotnet test tests/FrasiSquisite.Domain.Tests --nologo --filter "FullyQualifiedName~RevealTests"`

Expected: FAIL — `OgniAvanzamentoScopreUnaCasellaInPiu` fallisce con "The collection was expected to contain a single element, but it was empty": `RevealAdvanceRequested` non è ancora gestito.

- [ ] **Step 3: Implementare il reveal**

In `src/FrasiSquisite.Domain/Engine/GameEngine.cs`, aggiungi il caso allo `switch`:

```csharp
        RevealAdvanceRequested e => OnRevealAdvance(state, e),
```

e aggiungi il metodo:

```csharp
    private static EngineResult OnRevealAdvance(GameState state, RevealAdvanceRequested e)
    {
        if (state.Phase != RoomPhase.Reveal)
        {
            return Error(state, e.RequestedBy, "NOT_REVEALING", "Non è il momento del reveal.");
        }

        if (state.HostId != e.RequestedBy)
        {
            return Error(state, e.RequestedBy, "NOT_HOST", "Solo chi ha creato la stanza fa avanzare il reveal.");
        }

        var scoperte = state.RevealSlotCount + 1;
        var frase = state.Phrases[state.RevealPhraseIndex];
        var completa = scoperte >= frase.Slots.Count;

        var testi = frase.Slots.Take(scoperte).Select(s => s!.Text).ToList();

        // Gli autori compaiono solo a frase completa (spec §2.4).
        var autori = completa
            ? frase.Slots.Select(s => state.FindPlayer(s!.AuthorId)?.Nickname ?? "?").ToList()
            : [];

        var passo = new RevealStepMessage(
            state.RevealPhraseIndex,
            state.Phrases.Count,
            testi,
            completa,
            autori);

        if (!completa)
        {
            return new EngineResult(
                state with { RevealSlotCount = scoperte },
                [new BroadcastToRoom(passo)]);
        }

        var prossimaFrase = state.RevealPhraseIndex + 1;

        if (prossimaFrase < state.Phrases.Count)
        {
            return new EngineResult(
                state with { RevealPhraseIndex = prossimaFrase, RevealSlotCount = 0 },
                [new BroadcastToRoom(passo)]);
        }

        var finito = state with { Phase = RoomPhase.Finished };

        var frasiComposte = finito.Phrases
            .Select(f => finito.Schema.Compose([.. f.Slots.Select(s => s!.Text)]))
            .ToList();

        return new EngineResult(finito, [
            new BroadcastToRoom(passo),
            new BroadcastToRoom(new GameFinishedMessage(frasiComposte)),
        ]);
    }
```

- [ ] **Step 4: Eseguire i test e verificare che passino**

Run: `dotnet test tests/FrasiSquisite.Domain.Tests --nologo`

Expected: PASS, 309 test superati.

- [ ] **Step 5: Commit**

```bash
git add src/FrasiSquisite.Domain tests/FrasiSquisite.Domain.Tests
git commit -m "feat(domain): reveal una casella alla volta con autori a frase completa"
```

---

## Task 8: Registro delle stanze

**Files:**
- Create: `src/FrasiSquisite.Server/Rooms/RoomCodeGenerator.cs`
- Create: `src/FrasiSquisite.Server/Rooms/IRoomRegistry.cs`
- Create: `src/FrasiSquisite.Server/Rooms/RoomRegistry.cs`
- Test: `tests/FrasiSquisite.Server.Tests/Rooms/RoomRegistryTests.cs`
- Delete: `tests/FrasiSquisite.Server.Tests/UnitTest1.cs`
- Modify: `tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj`

**Interfaces:**
- Consumes: `GameState`, `IRandomSource`, `ISchemaCatalog`
- Produces:
  - `class RoomCodeGenerator(IRandomSource random)` con `string Next()`
  - `interface IRoomRegistry` con `GameState Create()`, `bool TryGet(string code, out GameState state)`, `void Set(string code, GameState state)`, `void Remove(string code)`, `IReadOnlyCollection<string> Codes { get; }`
  - `class RoomRegistry : IRoomRegistry` — thread-safe, basato su `ConcurrentDictionary`

Il registro deve essere thread-safe: SignalR serve più connessioni in parallelo. `Set` usa un confronto ottimistico non è necessario in Fase 1 — l'accesso allo stato di una stanza è serializzato dall'hub tramite un lock per stanza (Task 9).

- [ ] **Step 1: Scrivere il test che fallisce**

Aggiungi al `tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj`, dentro un `<ItemGroup>`:

```xml
    <ProjectReference Include="..\..\src\FrasiSquisite.Domain\FrasiSquisite.Domain.csproj" />
```

Crea `tests/FrasiSquisite.Server.Tests/Rooms/RoomRegistryTests.cs`:

```csharp
using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Server.Rooms;
using FrasiSquisite.Shared.Schemas;
using Xunit;

namespace FrasiSquisite.Server.Tests.Rooms;

public class RoomCodeGeneratorTests
{
    [Fact]
    public void GeneraCodiciDellaLunghezzaAttesa()
    {
        var generatore = new RoomCodeGenerator(new SeededRandomSource(42));

        Assert.Equal(RoomCodeGenerator.CodeLength, generatore.Next().Length);
    }

    [Fact]
    public void UsaSoloCaratteriDellAlfabetoSenzaAmbiguita()
    {
        var generatore = new RoomCodeGenerator(new SeededRandomSource(7));

        for (var i = 0; i < 200; i++)
        {
            Assert.All(generatore.Next(), c => Assert.Contains(c, RoomCodeGenerator.Alphabet));
        }
    }

    [Fact]
    public void ConLoStessoSeedProduceLaStessaSequenza()
    {
        var uno = new RoomCodeGenerator(new SeededRandomSource(99));
        var due = new RoomCodeGenerator(new SeededRandomSource(99));

        Assert.Equal(uno.Next(), due.Next());
    }
}

public class RoomRegistryTests
{
    private static RoomRegistry Registro(int seed = 1) =>
        new(new RoomCodeGenerator(new SeededRandomSource(seed)), new EmbeddedSchemaCatalog());

    [Fact]
    public void CreaUnaStanzaInLobbyConLoSchemaPredefinito()
    {
        var stato = Registro().Create();

        Assert.Equal(Schema.DefaultId, stato.Schema.Id);
        Assert.Empty(stato.Players);
    }

    [Fact]
    public void LaStanzaCreataERecuperabilePerCodice()
    {
        var registro = Registro();
        var creata = registro.Create();

        Assert.True(registro.TryGet(creata.RoomCode, out var trovata));
        Assert.Equal(creata.RoomCode, trovata.RoomCode);
    }

    [Fact]
    public void UnCodiceInesistenteNonSiTrova()
    {
        Assert.False(Registro().TryGet("ZZZZ", out _));
    }

    [Fact]
    public void IlCodiceSiCercaSenzaDistinzioneDiMaiuscole()
    {
        var registro = Registro();
        var creata = registro.Create();

        Assert.True(registro.TryGet(creata.RoomCode.ToLowerInvariant(), out _));
    }

    [Fact]
    public void SetSostituisceLoStatoDellaStanza()
    {
        var registro = Registro();
        var creata = registro.Create();

        registro.Set(creata.RoomCode, creata with { HostId = Guid.NewGuid() });

        Assert.True(registro.TryGet(creata.RoomCode, out var aggiornata));
        Assert.NotEqual(Guid.Empty, aggiornata.HostId);
    }

    [Fact]
    public void RemoveEliminaLaStanza()
    {
        var registro = Registro();
        var creata = registro.Create();

        registro.Remove(creata.RoomCode);

        Assert.False(registro.TryGet(creata.RoomCode, out _));
    }

    [Fact]
    public void CreareTanteStanzeNonProduceCodiciDuplicati()
    {
        var registro = Registro();

        var codici = Enumerable.Range(0, 500).Select(_ => registro.Create().RoomCode).ToList();

        Assert.Equal(codici.Count, codici.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
```

Elimina il segnaposto:

```bash
rm tests/FrasiSquisite.Server.Tests/UnitTest1.cs
```

- [ ] **Step 2: Eseguire il test e verificare che fallisca**

Run: `dotnet test tests/FrasiSquisite.Server.Tests --nologo`

Expected: FAIL in compilazione — `CS0246` su `FrasiSquisite.Server.Rooms`.

- [ ] **Step 3: Implementare generatore e registro**

`src/FrasiSquisite.Server/Rooms/RoomCodeGenerator.cs`:

```csharp
using FrasiSquisite.Domain.Randomness;

namespace FrasiSquisite.Server.Rooms;

public sealed class RoomCodeGenerator(IRandomSource random)
{
    public const int CodeLength = 4;

    /// <summary>
    /// Niente 0/O né 1/I/L: il codice si detta a voce o si legge da un altro
    /// telefono, e le ambiguità costano tentativi falliti.
    /// </summary>
    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public string Next()
    {
        return string.Create(CodeLength, random, static (span, rnd) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = Alphabet[rnd.Next(Alphabet.Length)];
            }
        });
    }
}
```

`src/FrasiSquisite.Server/Rooms/IRoomRegistry.cs`:

```csharp
using FrasiSquisite.Domain.Model;

namespace FrasiSquisite.Server.Rooms;

/// <summary>
/// Le stanze attive vivono solo in memoria: un riavvio del server le perde, ed
/// è un limite accettato consapevolmente per la v1 (spec §7.1).
/// </summary>
public interface IRoomRegistry
{
    GameState Create();

    bool TryGet(string code, out GameState state);

    void Set(string code, GameState state);

    void Remove(string code);

    IReadOnlyCollection<string> Codes { get; }
}
```

`src/FrasiSquisite.Server/Rooms/RoomRegistry.cs`:

```csharp
using System.Collections.Concurrent;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Shared.Schemas;

namespace FrasiSquisite.Server.Rooms;

public sealed class RoomRegistry(RoomCodeGenerator codes, ISchemaCatalog schemas) : IRoomRegistry
{
    private const int MaxCodeAttempts = 100;

    private readonly ConcurrentDictionary<string, GameState> _rooms =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Codes => _rooms.Keys.ToList();

    public GameState Create()
    {
        var schema = schemas.Get(Schema.DefaultId);

        for (var tentativo = 0; tentativo < MaxCodeAttempts; tentativo++)
        {
            var codice = codes.Next();
            var stato = GameState.NewRoom(codice, schema);

            if (_rooms.TryAdd(codice, stato))
            {
                return stato;
            }
        }

        throw new InvalidOperationException(
            $"Impossibile generare un codice stanza libero dopo {MaxCodeAttempts} tentativi.");
    }

    public bool TryGet(string code, out GameState state) => _rooms.TryGetValue(code, out state!);

    public void Set(string code, GameState state) => _rooms[code] = state;

    public void Remove(string code) => _rooms.TryRemove(code, out _);
}
```

- [ ] **Step 4: Eseguire i test e verificare che passino**

Run: `dotnet test tests/FrasiSquisite.Server.Tests --nologo`

Expected: PASS, 10 test superati.

Se `CreareTanteStanzeNonProduceCodiciDuplicati` fallisce, l'alfabeto è troppo piccolo per 500 stanze con codici da 4 caratteri: 31⁴ ≈ 923.000 combinazioni, quindi il test deve passare — un fallimento indica che `TryAdd` non sta rilevando le collisioni.

- [ ] **Step 5: Commit**

```bash
git add src/FrasiSquisite.Server tests/FrasiSquisite.Server.Tests
git commit -m "feat(server): registro stanze in memoria e generazione codici"
```

---

## Task 9: Hub SignalR e adapter degli effetti

**Files:**
- Create: `src/FrasiSquisite.Server/Realtime/GameHost.cs`
- Create: `src/FrasiSquisite.Server/Realtime/GameHub.cs`
- Modify: `src/FrasiSquisite.Server/Program.cs`
- Modify: `src/FrasiSquisite.Server/FrasiSquisite.Server.csproj`
- Modify: `Directory.Packages.props`
- Modify: `tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj`
- Test: `tests/FrasiSquisite.Server.Tests/Realtime/GameHubTests.cs`

**Interfaces:**
- Consumes: `IGameEngine`, `IRoomRegistry`, messaggi di `Shared.Protocol`
- Produces:
  - `class GameHost(IGameEngine engine, IRoomRegistry rooms, IHubContext<GameHub> hub)` con `Task DispatchAsync(string roomCode, GameEvent evt)`
  - `class GameHub : Hub` con i metodi `CreateRoom`, `JoinRoom`, `StartGame`, `SubmitSlot`, `AdvanceReveal`
  - Il client riceve i messaggi via `ReceiveMessage(string type, object payload)`

**Nota sulla serializzazione:** l'hub invia `(nomeTipo, payload)` invece di usare un metodo per messaggio. Un solo punto di ingresso lato client rende banale il `FakeGameConnection` del Task 10 e non obbliga a toccare l'hub ogni volta che si aggiunge un messaggio.

**Nota sulla concorrenza:** l'accesso allo stato di una stanza è serializzato da un `SemaphoreSlim` per codice stanza dentro `GameHost`. Senza, due invii simultanei possono leggere lo stesso stato e sovrascriversi a vicenda — e il bug si manifesterebbe solo con giocatori veri che premono insieme.

- [ ] **Step 1: Aggiungere i pacchetti di test**

In `Directory.Packages.props`, dentro `<ItemGroup Label="Test">`:

```xml
    <PackageVersion Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.10" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.10" />
```

In `tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj`, nell'`<ItemGroup>` dei pacchetti:

```xml
    <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
```

Perché `WebApplicationFactory<T>` trovi il punto di ingresso, aggiungi in fondo a `src/FrasiSquisite.Server/Program.cs`:

```csharp
public partial class Program;
```

- [ ] **Step 2: Scrivere il test di integrazione che fallisce**

Crea `tests/FrasiSquisite.Server.Tests/Realtime/GameHubTests.cs`:

```csharp
using System.Text.Json;
using FrasiSquisite.Shared.Protocol;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace FrasiSquisite.Server.Tests.Realtime;

public sealed class GameHubTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private sealed class Client(HubConnection connection) : IAsyncDisposable
    {
        private readonly List<(string Type, JsonElement Payload)> _received = [];

        public HubConnection Connection { get; } = connection;

        public IReadOnlyList<(string Type, JsonElement Payload)> Received => _received;

        public void Listen() =>
            Connection.On<string, JsonElement>("ReceiveMessage", (type, payload) =>
            {
                lock (_received)
                {
                    _received.Add((type, payload));
                }
            });

        public T Last<T>() where T : class
        {
            lock (_received)
            {
                var nome = typeof(T).Name;
                for (var i = _received.Count - 1; i >= 0; i--)
                {
                    if (_received[i].Type == nome)
                    {
                        return _received[i].Payload.Deserialize<T>(ProtocolJson.Options)!;
                    }
                }
            }

            throw new InvalidOperationException($"Nessun messaggio di tipo {typeof(T).Name} ricevuto.");
        }

        public async Task WaitFor<T>(TimeSpan timeout)
        {
            var scadenza = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < scadenza)
            {
                lock (_received)
                {
                    if (_received.Any(m => m.Type == typeof(T).Name))
                    {
                        return;
                    }
                }

                await Task.Delay(20);
            }

            throw new TimeoutException($"Nessun {typeof(T).Name} entro {timeout}.");
        }

        public ValueTask DisposeAsync() => Connection.DisposeAsync();
    }

    private async Task<Client> ConnettiAsync()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(_factory.Server.BaseAddress, "hubs/game"),
                options => options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler())
            .Build();

        var client = new Client(connection);
        client.Listen();
        await connection.StartAsync();
        return client;
    }

    [Fact]
    public async Task CreareUnaStanzaRestituisceUnCodiceEDiventaHost()
    {
        await using var anna = await ConnettiAsync();

        var codice = await anna.Connection.InvokeAsync<string>(
            "CreateRoom",
            new CreateRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "Anna"));

        Assert.False(string.IsNullOrWhiteSpace(codice));

        await anna.WaitFor<RoomStateMessage>(TimeSpan.FromSeconds(5));
        var stato = anna.Last<RoomStateMessage>();

        Assert.Equal(codice, stato.RoomCode);
        Assert.True(Assert.Single(stato.Players).IsHost);
    }

    [Fact]
    public async Task UnaVersioneDiProtocolloSbagliataVieneRifiutata()
    {
        await using var anna = await ConnettiAsync();

        await Assert.ThrowsAsync<HubException>(() =>
            anna.Connection.InvokeAsync<string>(
                "CreateRoom",
                new CreateRoomRequest(ProtocolVersion.Current + 1, Guid.NewGuid(), "Anna")));
    }

    [Fact]
    public async Task DueClientGiocanoUnaPartitaFinoAlReveal()
    {
        await using var anna = await ConnettiAsync();
        await using var bruno = await ConnettiAsync();

        var annaId = Guid.NewGuid();
        var brunoId = Guid.NewGuid();

        var codice = await anna.Connection.InvokeAsync<string>(
            "CreateRoom", new CreateRoomRequest(ProtocolVersion.Current, annaId, "Anna"));

        await bruno.Connection.InvokeAsync(
            "JoinRoom", new JoinRoomRequest(ProtocolVersion.Current, brunoId, "Bruno", codice));

        await bruno.WaitFor<RoomStateMessage>(TimeSpan.FromSeconds(5));

        await anna.Connection.InvokeAsync("StartGame", new StartGameRequest(codice));
        await anna.WaitFor<SlotRequestMessage>(TimeSpan.FromSeconds(5));
        await bruno.WaitFor<SlotRequestMessage>(TimeSpan.FromSeconds(5));

        var richiesta = anna.Last<SlotRequestMessage>();
        Assert.Equal(5, richiesta.TotalRounds);

        // Cinque round con due giocatori.
        for (var round = 0; round < 5; round++)
        {
            await anna.Connection.InvokeAsync("SubmitSlot", new SubmitSlotRequest(codice, $"anna{round}"));
            await bruno.Connection.InvokeAsync("SubmitSlot", new SubmitSlotRequest(codice, $"bruno{round}"));
        }

        await anna.Connection.InvokeAsync("AdvanceReveal", codice);
        await anna.WaitFor<RevealStepMessage>(TimeSpan.FromSeconds(5));

        var passo = anna.Last<RevealStepMessage>();
        Assert.Equal(0, passo.PhraseIndex);
        Assert.Equal(2, passo.TotalPhrases);
        Assert.Single(passo.RevealedSlots);
    }
}
```

- [ ] **Step 3: Eseguire il test e verificare che fallisca**

Run: `dotnet test tests/FrasiSquisite.Server.Tests --nologo --filter "FullyQualifiedName~GameHubTests"`

Expected: FAIL in compilazione — `CS0246` su `FrasiSquisite.Server.Realtime`, oppure `Program` non accessibile.

- [ ] **Step 4: Implementare hub e adapter**

`src/FrasiSquisite.Server/Realtime/GameHost.cs`:

```csharp
using System.Collections.Concurrent;
using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Server.Rooms;
using Microsoft.AspNetCore.SignalR;

namespace FrasiSquisite.Server.Realtime;

/// <summary>
/// Esegue gli effetti prodotti dal motore. È l'unico punto del server che
/// conosce sia il dominio sia SignalR: il motore resta ignaro della rete
/// (spec §3.2).
/// </summary>
public sealed class GameHost(
    IGameEngine engine,
    IRoomRegistry rooms,
    IHubContext<GameHub> hub)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Serializza gli eventi per stanza: due invii simultanei leggerebbero lo
    /// stesso stato e si sovrascriverebbero a vicenda.
    /// </summary>
    public async Task DispatchAsync(string roomCode, GameEvent evt, CancellationToken ct = default)
    {
        var gate = Locks.GetOrAdd(roomCode, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(ct);
        try
        {
            if (!rooms.TryGet(roomCode, out var stato))
            {
                return;
            }

            var risultato = engine.Handle(stato, evt);
            rooms.Set(roomCode, risultato.State);

            foreach (var effetto in risultato.Effects)
            {
                await EseguiAsync(roomCode, effetto, ct);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private Task EseguiAsync(string roomCode, Effect effetto, CancellationToken ct) => effetto switch
    {
        BroadcastToRoom b => hub.Clients.Group(roomCode)
            .SendAsync("ReceiveMessage", b.Message.GetType().Name, b.Message, ct),

        SendToPlayer s => hub.Clients.Group(PlayerGroup(s.PlayerId))
            .SendAsync("ReceiveMessage", s.Message.GetType().Name, s.Message, ct),

        _ => throw new InvalidOperationException($"Effetto non gestito: {effetto.GetType().Name}"),
    };

    public static string PlayerGroup(Guid playerId) => $"player:{playerId}";
}
```

`src/FrasiSquisite.Server/Realtime/GameHub.cs`:

```csharp
using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Server.Rooms;
using FrasiSquisite.Shared.Protocol;
using Microsoft.AspNetCore.SignalR;

namespace FrasiSquisite.Server.Realtime;

public sealed class GameHub(GameHost host, IRoomRegistry rooms) : Hub
{
    private const string RoomKey = "room";
    private const string PlayerKey = "player";

    public async Task<string> CreateRoom(CreateRoomRequest request)
    {
        RichiediProtocolloCompatibile(request.ProtocolVersion);

        var stanza = rooms.Create();
        await EntraAsync(stanza.RoomCode, request.PlayerId);
        await host.DispatchAsync(stanza.RoomCode, new PlayerJoined(request.PlayerId, request.Nickname));

        return stanza.RoomCode;
    }

    public async Task JoinRoom(JoinRoomRequest request)
    {
        RichiediProtocolloCompatibile(request.ProtocolVersion);

        if (!rooms.TryGet(request.RoomCode, out var stanza))
        {
            throw new HubException("Stanza non trovata.");
        }

        await EntraAsync(stanza.RoomCode, request.PlayerId);
        await host.DispatchAsync(stanza.RoomCode, new PlayerJoined(request.PlayerId, request.Nickname));
    }

    public Task StartGame(StartGameRequest request) =>
        host.DispatchAsync(request.RoomCode, new GameStartRequested(GiocatoreCorrente()));

    public Task SubmitSlot(SubmitSlotRequest request) =>
        host.DispatchAsync(request.RoomCode, new SlotSubmitted(GiocatoreCorrente(), request.Text));

    public Task AdvanceReveal(string roomCode) =>
        host.DispatchAsync(roomCode, new RevealAdvanceRequested(GiocatoreCorrente()));

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(RoomKey, out var room) && room is string roomCode &&
            Context.Items.TryGetValue(PlayerKey, out var player) && player is Guid playerId)
        {
            await host.DispatchAsync(roomCode, new PlayerLeft(playerId));
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task EntraAsync(string roomCode, Guid playerId)
    {
        Context.Items[RoomKey] = roomCode;
        Context.Items[PlayerKey] = playerId;

        await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);
        await Groups.AddToGroupAsync(Context.ConnectionId, GameHost.PlayerGroup(playerId));
    }

    private Guid GiocatoreCorrente() =>
        Context.Items.TryGetValue(PlayerKey, out var player) && player is Guid id
            ? id
            : throw new HubException("Non sei in una stanza.");

    private static void RichiediProtocolloCompatibile(int clientVersion)
    {
        if (!ProtocolVersion.IsCompatible(clientVersion))
        {
            throw new HubException(
                $"Versione dell'app non compatibile: il server parla la versione {ProtocolVersion.Current}. Aggiorna l'app.");
        }
    }
}
```

Sostituisci `src/FrasiSquisite.Server/Program.cs` con:

```csharp
using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Server.Realtime;
using FrasiSquisite.Server.Rooms;
using FrasiSquisite.Shared.Schemas;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();

builder.Services.AddSingleton<ISchemaCatalog, EmbeddedSchemaCatalog>();
builder.Services.AddSingleton<IRandomSource, SystemRandomSource>();
builder.Services.AddSingleton<IWordPool, StaticWordPool>();
builder.Services.AddSingleton<IGameMode, RoleSchemaMode>();
builder.Services.AddSingleton<IGameEngine, GameEngine>();
builder.Services.AddSingleton<RoomCodeGenerator>();
builder.Services.AddSingleton<IRoomRegistry, RoomRegistry>();
builder.Services.AddSingleton<GameHost>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapHub<GameHub>("/hubs/game");

app.Run();

public partial class Program;
```

- [ ] **Step 5: Eseguire i test e verificare che passino**

Run: `dotnet test tests/FrasiSquisite.Server.Tests --nologo`

Expected: PASS, 13 test superati.

Se `DueClientGiocanoUnaPartitaFinoAlReveal` va in `TimeoutException` su `SlotRequestMessage`, il gruppo per giocatore non è stato registrato: verifica che `EntraAsync` sia chiamato prima di `DispatchAsync` in `CreateRoom` e `JoinRoom`.

- [ ] **Step 6: Commit**

```bash
git add src/FrasiSquisite.Server tests/FrasiSquisite.Server.Tests Directory.Packages.props
git commit -m "feat(server): hub SignalR e adapter di esecuzione degli effetti"
```

---

## Task 10: Connessione astratta lato client

**Files:**
- Create: `tests/FrasiSquisite.App.Tests/FrasiSquisite.App.Tests.csproj`
- Create: `src/FrasiSquisite.App/Services/IGameConnection.cs`
- Create: `src/FrasiSquisite.App/Services/SignalRGameConnection.cs`
- Create: `tests/FrasiSquisite.App.Tests/FakeGameConnection.cs`
- Create: `tests/FrasiSquisite.App.Tests/Services/FakeGameConnectionTests.cs`
- Modify: `Directory.Packages.props`
- Modify: `src/FrasiSquisite.App/FrasiSquisite.App.csproj`
- Modify: `FrasiSquisite.slnx`

**Interfaces:**
- Consumes: messaggi di `Shared.Protocol`
- Produces:
  - `interface IGameConnection` con `event Action<object>? MessageReceived`, `Task ConnectAsync(string serverUrl, CancellationToken ct)`, `Task<string> CreateRoomAsync(Guid playerId, string nickname)`, `Task JoinRoomAsync(Guid playerId, string nickname, string roomCode)`, `Task StartGameAsync(string roomCode)`, `Task SubmitSlotAsync(string roomCode, string text)`, `Task AdvanceRevealAsync(string roomCode)`, `Task DisconnectAsync()`
  - `class SignalRGameConnection : IGameConnection`
  - `class FakeGameConnection : IGameConnection` (nel progetto di test) con `void Emit(object message)` e `IReadOnlyList<string> Calls`

**Perché i sorgenti si linkano invece di referenziare il progetto.** `FrasiSquisite.App` ha come target `net10.0-android` e non è caricabile da un test runner desktop, quindi il progetto di test non può referenziarlo. Duplicare l'interfaccia nel progetto di test sarebbe peggio: due definizioni che divergono al primo cambiamento. La soluzione è compilare gli stessi file sorgente in entrambi i progetti tramite `<Compile Include>` con percorso relativo — un solo file, due compilazioni. È l'approccio standard per testare codice MAUI senza emulatore, e vincola i file linkati a non dipendere da tipi MAUI (motivo per cui `IGameConnection` e le ViewModel non usano `Page`, `Dispatcher` o `SecureStorage`).

- [ ] **Step 1: Creare il progetto di test e l'interfaccia**

```bash
dotnet new xunit -o tests/FrasiSquisite.App.Tests -f net10.0
rm tests/FrasiSquisite.App.Tests/UnitTest1.cs
dotnet add tests/FrasiSquisite.App.Tests reference src/FrasiSquisite.Shared
dotnet sln add tests/FrasiSquisite.App.Tests
```

`src/FrasiSquisite.App/Services/IGameConnection.cs`:

```csharp
namespace FrasiSquisite.App.Services;

/// <summary>
/// Le ViewModel dipendono da questa interfaccia e mai da HubConnection: così
/// l'intero flusso di schermate si prova a server spento (spec §5).
/// </summary>
public interface IGameConnection
{
    event Action<object>? MessageReceived;

    bool IsConnected { get; }

    Task ConnectAsync(string serverUrl, CancellationToken ct = default);

    Task<string> CreateRoomAsync(Guid playerId, string nickname);

    Task JoinRoomAsync(Guid playerId, string nickname, string roomCode);

    Task StartGameAsync(string roomCode);

    Task SubmitSlotAsync(string roomCode, string text);

    Task AdvanceRevealAsync(string roomCode);

    Task DisconnectAsync();
}
```

Aggiungi al `tests/FrasiSquisite.App.Tests/FrasiSquisite.App.Tests.csproj`, dentro un `<ItemGroup>`, il link ai sorgenti condivisi:

```xml
    <Compile Include="..\..\src\FrasiSquisite.App\Services\IGameConnection.cs" Link="Linked\IGameConnection.cs" />
    <Compile Include="..\..\src\FrasiSquisite.App\ViewModels\**\*.cs" LinkBase="Linked\ViewModels" />
```

- [ ] **Step 2: Scrivere il test che fallisce**

Crea `tests/FrasiSquisite.App.Tests/FakeGameConnection.cs`:

```csharp
using FrasiSquisite.App.Services;

namespace FrasiSquisite.App.Tests;

/// <summary>
/// Implementazione in memoria di <see cref="IGameConnection"/>: registra le
/// chiamate e permette di simulare i messaggi in arrivo dal server.
/// </summary>
public sealed class FakeGameConnection : IGameConnection
{
    private readonly List<string> _calls = [];

    public event Action<object>? MessageReceived;

    public bool IsConnected { get; private set; }

    public IReadOnlyList<string> Calls => _calls;

    public string NextRoomCode { get; set; } = "ABCD";

    public void Emit(object message) => MessageReceived?.Invoke(message);

    public Task ConnectAsync(string serverUrl, CancellationToken ct = default)
    {
        _calls.Add($"Connect({serverUrl})");
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task<string> CreateRoomAsync(Guid playerId, string nickname)
    {
        _calls.Add($"CreateRoom({nickname})");
        return Task.FromResult(NextRoomCode);
    }

    public Task JoinRoomAsync(Guid playerId, string nickname, string roomCode)
    {
        _calls.Add($"JoinRoom({nickname},{roomCode})");
        return Task.CompletedTask;
    }

    public Task StartGameAsync(string roomCode)
    {
        _calls.Add($"StartGame({roomCode})");
        return Task.CompletedTask;
    }

    public Task SubmitSlotAsync(string roomCode, string text)
    {
        _calls.Add($"SubmitSlot({roomCode},{text})");
        return Task.CompletedTask;
    }

    public Task AdvanceRevealAsync(string roomCode)
    {
        _calls.Add($"AdvanceReveal({roomCode})");
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        _calls.Add("Disconnect()");
        IsConnected = false;
        return Task.CompletedTask;
    }
}
```

Crea `tests/FrasiSquisite.App.Tests/Services/FakeGameConnectionTests.cs`:

```csharp
using FrasiSquisite.Shared.Protocol;
using Xunit;

namespace FrasiSquisite.App.Tests.Services;

public class FakeGameConnectionTests
{
    [Fact]
    public async Task RegistraLeChiamateEffettuate()
    {
        var connessione = new FakeGameConnection();

        await connessione.ConnectAsync("http://localhost:5000");
        await connessione.CreateRoomAsync(Guid.NewGuid(), "Anna");

        Assert.Equal(["Connect(http://localhost:5000)", "CreateRoom(Anna)"], connessione.Calls);
        Assert.True(connessione.IsConnected);
    }

    [Fact]
    public void EmetteIMessaggiVersoGliIscritti()
    {
        var connessione = new FakeGameConnection();
        object? ricevuto = null;
        connessione.MessageReceived += m => ricevuto = m;

        var atteso = new RoundProgressMessage(0, 1, 3);
        connessione.Emit(atteso);

        Assert.Same(atteso, ricevuto);
    }
}
```

- [ ] **Step 3: Eseguire il test e verificare che fallisca**

Run: `dotnet test tests/FrasiSquisite.App.Tests --nologo`

Expected: FAIL in compilazione — il `<Compile Include>` delle ViewModel punta a una cartella inesistente. Crea il segnaposto per farla esistere:

```bash
mkdir -p src/FrasiSquisite.App/ViewModels
```

Rilancia: ora fallisce con `CS0246` su `FrasiSquisite.App.Services` se il primo `<Compile Include>` non è stato aggiunto correttamente.

- [ ] **Step 4: Implementare la connessione SignalR**

In `Directory.Packages.props`, dentro `<ItemGroup Label="App (MAUI)">`:

```xml
    <PackageVersion Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.10" />
    <PackageVersion Include="CommunityToolkit.Mvvm" Version="8.4.2" />
```

In `src/FrasiSquisite.App/FrasiSquisite.App.csproj`, nell'`<ItemGroup>` dei pacchetti:

```xml
    <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" />
    <PackageReference Include="CommunityToolkit.Mvvm" />
```

`src/FrasiSquisite.App/Services/SignalRGameConnection.cs`:

```csharp
using System.Text.Json;
using FrasiSquisite.Shared.Protocol;
using Microsoft.AspNetCore.SignalR.Client;

namespace FrasiSquisite.App.Services;

public sealed class SignalRGameConnection : IGameConnection, IAsyncDisposable
{
    private HubConnection? _connection;

    public event Action<object>? MessageReceived;

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public async Task ConnectAsync(string serverUrl, CancellationToken ct = default)
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _connection = new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri(serverUrl), "hubs/game"))
            .WithAutomaticReconnect()
            .Build();

        _connection.On<string, JsonElement>("ReceiveMessage", (type, payload) =>
        {
            if (Deserializza(type, payload) is { } messaggio)
            {
                MessageReceived?.Invoke(messaggio);
            }
        });

        await _connection.StartAsync(ct);
    }

    public Task<string> CreateRoomAsync(Guid playerId, string nickname) =>
        Hub.InvokeAsync<string>("CreateRoom", new CreateRoomRequest(ProtocolVersion.Current, playerId, nickname));

    public Task JoinRoomAsync(Guid playerId, string nickname, string roomCode) =>
        Hub.InvokeAsync("JoinRoom", new JoinRoomRequest(ProtocolVersion.Current, playerId, nickname, roomCode));

    public Task StartGameAsync(string roomCode) =>
        Hub.InvokeAsync("StartGame", new StartGameRequest(roomCode));

    public Task SubmitSlotAsync(string roomCode, string text) =>
        Hub.InvokeAsync("SubmitSlot", new SubmitSlotRequest(roomCode, text));

    public Task AdvanceRevealAsync(string roomCode) =>
        Hub.InvokeAsync("AdvanceReveal", roomCode);

    public async Task DisconnectAsync()
    {
        if (_connection is not null)
        {
            await _connection.StopAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    private HubConnection Hub =>
        _connection ?? throw new InvalidOperationException("Connessione non stabilita.");

    private static object? Deserializza(string type, JsonElement payload) => type switch
    {
        nameof(RoomStateMessage) => payload.Deserialize<RoomStateMessage>(ProtocolJson.Options),
        nameof(SlotRequestMessage) => payload.Deserialize<SlotRequestMessage>(ProtocolJson.Options),
        nameof(RoundProgressMessage) => payload.Deserialize<RoundProgressMessage>(ProtocolJson.Options),
        nameof(RevealStepMessage) => payload.Deserialize<RevealStepMessage>(ProtocolJson.Options),
        nameof(GameFinishedMessage) => payload.Deserialize<GameFinishedMessage>(ProtocolJson.Options),
        nameof(ErrorMessage) => payload.Deserialize<ErrorMessage>(ProtocolJson.Options),
        _ => null,
    };
}
```

- [ ] **Step 5: Eseguire i test e verificare che passino**

Run: `dotnet test tests/FrasiSquisite.App.Tests --nologo`

Expected: PASS, 2 test superati.

Run: `dotnet build src/FrasiSquisite.App --nologo`

Expected: 0 errori.

- [ ] **Step 6: Commit**

```bash
git add src/FrasiSquisite.App tests/FrasiSquisite.App.Tests Directory.Packages.props FrasiSquisite.slnx
git commit -m "feat(app): connessione di gioco astratta e fake per i test"
```

---

## Task 11: ViewModel e schermate

**Files:**
- Create: `src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs`
- Create: `src/FrasiSquisite.App/Pages/GamePage.xaml`
- Create: `src/FrasiSquisite.App/Pages/GamePage.xaml.cs`
- Modify: `src/FrasiSquisite.App/AppShell.xaml`
- Modify: `src/FrasiSquisite.App/MauiProgram.cs`
- Delete: `src/FrasiSquisite.App/MainPage.xaml`, `src/FrasiSquisite.App/MainPage.xaml.cs`
- Test: `tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs`

**Interfaces:**
- Consumes: `IGameConnection` (Task 10), messaggi di `Shared.Protocol`
- Produces:
  - `enum ScreenState { Home, Lobby, Writing, Waiting, Reveal, Finished }`
  - `partial class GameSessionViewModel : ObservableObject` con proprietà osservabili `Screen`, `Nickname`, `RoomCode`, `Players`, `IsHost`, `Ruolo`, `Prompt`, `Esempio`, `Round`, `TotalRounds`, `SlotText`, `SubmittedCount`, `PlayerCount`, `RevealedSlots`, `RevealAuthors`, `FinalPhrases`, `ErrorText`; comandi `CreateRoomCommand`, `JoinRoomCommand`, `StartGameCommand`, `SubmitSlotCommand`, `AdvanceRevealCommand`

Una sola ViewModel per l'intera sessione invece di cinque: le schermate condividono lo stesso stato e le stesse transizioni, e spezzarle costringerebbe a passarsi lo stato fra ViewModel. `Screen` decide cosa la pagina mostra.

- [ ] **Step 1: Scrivere il test che fallisce**

Crea `tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs`:

```csharp
using FrasiSquisite.App.ViewModels;
using FrasiSquisite.Shared.Protocol;
using Xunit;

namespace FrasiSquisite.App.Tests.ViewModels;

public class GameSessionViewModelTests
{
    private static readonly Guid Anna = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static (GameSessionViewModel Vm, FakeGameConnection Conn) Crea()
    {
        var connessione = new FakeGameConnection();
        var vm = new GameSessionViewModel(connessione, Anna) { ServerUrl = "http://test" };
        return (vm, connessione);
    }

    [Fact]
    public void AllAvvioSiEAllaSchermataIniziale()
    {
        var (vm, _) = Crea();

        Assert.Equal(ScreenState.Home, vm.Screen);
    }

    [Fact]
    public async Task CreareUnaStanzaChiamaLaConnessioneEMemorizzaIlCodice()
    {
        var (vm, conn) = Crea();
        conn.NextRoomCode = "WXYZ";
        vm.Nickname = "Anna";

        await vm.CreateRoomCommand.ExecuteAsync(null);

        Assert.Contains("CreateRoom(Anna)", conn.Calls);
        Assert.Equal("WXYZ", vm.RoomCode);
    }

    [Fact]
    public void RicevereLoStatoDellaStanzaPortaInLobbyEPopolaIGiocatori()
    {
        var (vm, conn) = Crea();

        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [new PlayerView(Anna, "Anna", true, true), new PlayerView(Guid.NewGuid(), "Bruno", false, true)],
            "surrealista-classico", 5));

        Assert.Equal(ScreenState.Lobby, vm.Screen);
        Assert.Equal(2, vm.Players.Count);
        Assert.True(vm.IsHost);
    }

    [Fact]
    public void RicevereUnaRichiestaDiCasellaPortaInScritturaEMostraIlRuolo()
    {
        var (vm, conn) = Crea();

        conn.Emit(new SlotRequestMessage(0, 5, "Soggetto", "Un soggetto, con l'articolo", "Il cadavere"));

        Assert.Equal(ScreenState.Writing, vm.Screen);
        Assert.Equal("Soggetto", vm.Ruolo);
        Assert.Equal("Un soggetto, con l'articolo", vm.Prompt);
        Assert.Equal("Il cadavere", vm.Esempio);
        Assert.Equal(1, vm.Round);
        Assert.Equal(5, vm.TotalRounds);
    }

    [Fact]
    public async Task InviareUnaCasellaSvuotaIlCampoEPortaInAttesa()
    {
        var (vm, conn) = Crea();
        vm.RoomCode = "ABCD";
        conn.Emit(new SlotRequestMessage(0, 5, "Soggetto", "prompt", "esempio"));
        vm.SlotText = "Il cadavere";

        await vm.SubmitSlotCommand.ExecuteAsync(null);

        Assert.Contains("SubmitSlot(ABCD,Il cadavere)", conn.Calls);
        Assert.Equal(string.Empty, vm.SlotText);
        Assert.Equal(ScreenState.Waiting, vm.Screen);
    }

    [Fact]
    public async Task NonSiPuoInviareUnTestoNonValido()
    {
        var (vm, conn) = Crea();
        vm.RoomCode = "ABCD";
        conn.Emit(new SlotRequestMessage(0, 5, "Soggetto", "prompt", "esempio"));
        vm.SlotText = "   ";

        await vm.SubmitSlotCommand.ExecuteAsync(null);

        Assert.DoesNotContain(conn.Calls, c => c.StartsWith("SubmitSlot", StringComparison.Ordinal));
        Assert.False(string.IsNullOrEmpty(vm.ErrorText));
    }

    [Fact]
    public void IlProgressoDelRoundAggiornaIlConteggio()
    {
        var (vm, conn) = Crea();

        conn.Emit(new RoundProgressMessage(0, 2, 4));

        Assert.Equal(2, vm.SubmittedCount);
        Assert.Equal(4, vm.PlayerCount);
    }

    [Fact]
    public void IlPassoDiRevealPopolaCaselleEAutori()
    {
        var (vm, conn) = Crea();

        conn.Emit(new RevealStepMessage(0, 3, ["Il cadavere", "squisito"], false, []));

        Assert.Equal(ScreenState.Reveal, vm.Screen);
        Assert.Equal(2, vm.RevealedSlots.Count);
        Assert.Empty(vm.RevealAuthors);

        conn.Emit(new RevealStepMessage(0, 3, ["Il cadavere", "squisito", "berrà"], true, ["Anna", "Bruno", "Carla"]));

        Assert.Equal(3, vm.RevealAuthors.Count);
    }

    [Fact]
    public void LaFinePartitaMostraLeFrasiComposte()
    {
        var (vm, conn) = Crea();

        conn.Emit(new GameFinishedMessage(["Il cadavere squisito berrà il vino nuovo"]));

        Assert.Equal(ScreenState.Finished, vm.Screen);
        Assert.Single(vm.FinalPhrases);
    }

    [Fact]
    public void UnErroreDalServerVieneMostrato()
    {
        var (vm, conn) = Crea();

        conn.Emit(new ErrorMessage("NOT_HOST", "Solo chi ha creato la stanza può avviare."));

        Assert.Equal("Solo chi ha creato la stanza può avviare.", vm.ErrorText);
    }
}
```

- [ ] **Step 2: Eseguire il test e verificare che fallisca**

Run: `dotnet test tests/FrasiSquisite.App.Tests --nologo`

Expected: FAIL in compilazione — `CS0246` su `FrasiSquisite.App.ViewModels`.

- [ ] **Step 3: Aggiungere CommunityToolkit.Mvvm al progetto di test**

Il codice linkato usa i generatori del toolkit, quindi serve anche nel progetto di test. In `tests/FrasiSquisite.App.Tests/FrasiSquisite.App.Tests.csproj`:

```xml
    <PackageReference Include="CommunityToolkit.Mvvm" />
```

- [ ] **Step 4: Scrivere la ViewModel**

`src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrasiSquisite.App.Services;
using FrasiSquisite.Shared.Protocol;
using FrasiSquisite.Shared.Validation;

namespace FrasiSquisite.App.ViewModels;

public enum ScreenState
{
    Home,
    Lobby,
    Writing,
    Waiting,
    Reveal,
    Finished,
}

/// <summary>
/// Una sola ViewModel per l'intera sessione: le schermate condividono lo stesso
/// stato e le stesse transizioni, e separarle costringerebbe a passarselo.
/// Non contiene logica di gioco: reagisce ai messaggi del server (spec §3.1).
/// </summary>
public partial class GameSessionViewModel : ObservableObject
{
    private readonly IGameConnection _connection;
    private readonly Guid _playerId;

    public GameSessionViewModel(IGameConnection connection, Guid playerId)
    {
        _connection = connection;
        _playerId = playerId;

        // Sottoscrizione nel costruttore: la ViewModel deve reagire ai messaggi
        // fin dal primo istante, anche prima che l'utente tocchi qualcosa.
        _connection.MessageReceived += OnMessage;
    }

    [ObservableProperty]
    private ScreenState _screen = ScreenState.Home;

    [ObservableProperty]
    private string _serverUrl = "http://10.0.2.2:5000";

    [ObservableProperty]
    private string _nickname = string.Empty;

    [ObservableProperty]
    private string _roomCode = string.Empty;

    [ObservableProperty]
    private string _joinCode = string.Empty;

    [ObservableProperty]
    private bool _isHost;

    [ObservableProperty]
    private string _ruolo = string.Empty;

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    private string _esempio = string.Empty;

    [ObservableProperty]
    private int _round;

    [ObservableProperty]
    private int _totalRounds;

    [ObservableProperty]
    private string _slotText = string.Empty;

    [ObservableProperty]
    private int _submittedCount;

    [ObservableProperty]
    private int _playerCount;

    [ObservableProperty]
    private string _errorText = string.Empty;

    public ObservableCollection<PlayerView> Players { get; } = [];

    public ObservableCollection<string> RevealedSlots { get; } = [];

    public ObservableCollection<string> RevealAuthors { get; } = [];

    public ObservableCollection<string> FinalPhrases { get; } = [];

    [RelayCommand]
    private async Task CreateRoomAsync()
    {
        ErrorText = string.Empty;
        await EnsureConnectedAsync();
        RoomCode = await _connection.CreateRoomAsync(_playerId, Nickname);
    }

    [RelayCommand]
    private async Task JoinRoomAsync()
    {
        ErrorText = string.Empty;
        await EnsureConnectedAsync();
        RoomCode = JoinCode.Trim().ToUpperInvariant();
        await _connection.JoinRoomAsync(_playerId, Nickname, RoomCode);
    }

    [RelayCommand]
    private Task StartGameAsync() => _connection.StartGameAsync(RoomCode);

    [RelayCommand]
    private async Task SubmitSlotAsync()
    {
        // Stesso validatore che riapplica il server: feedback immediato senza
        // che le due regole possano divergere.
        var esito = SlotTextValidator.Validate(SlotText);
        if (!esito.IsValid)
        {
            ErrorText = esito.Error!;
            return;
        }

        ErrorText = string.Empty;
        await _connection.SubmitSlotAsync(RoomCode, esito.Normalized);
        SlotText = string.Empty;
        Screen = ScreenState.Waiting;
    }

    [RelayCommand]
    private Task AdvanceRevealAsync() => _connection.AdvanceRevealAsync(RoomCode);

    private async Task EnsureConnectedAsync()
    {
        if (!_connection.IsConnected)
        {
            await _connection.ConnectAsync(ServerUrl);
        }
    }

    private void OnMessage(object message)
    {
        switch (message)
        {
            case RoomStateMessage stato:
                RoomCode = stato.RoomCode;
                Players.Clear();
                foreach (var giocatore in stato.Players)
                {
                    Players.Add(giocatore);
                }

                IsHost = stato.Players.Any(p => p.Id == _playerId && p.IsHost);
                PlayerCount = stato.Players.Count;

                if (stato.Phase == "Lobby")
                {
                    Screen = ScreenState.Lobby;
                }

                break;

            case SlotRequestMessage richiesta:
                Ruolo = richiesta.Ruolo;
                Prompt = richiesta.Prompt;
                Esempio = richiesta.Esempio;
                Round = richiesta.Round + 1;
                TotalRounds = richiesta.TotalRounds;
                SlotText = string.Empty;
                ErrorText = string.Empty;
                Screen = ScreenState.Writing;
                break;

            case RoundProgressMessage progresso:
                SubmittedCount = progresso.Submitted;
                PlayerCount = progresso.Total;
                break;

            case RevealStepMessage passo:
                RevealedSlots.Clear();
                foreach (var testo in passo.RevealedSlots)
                {
                    RevealedSlots.Add(testo);
                }

                RevealAuthors.Clear();
                foreach (var autore in passo.Authors)
                {
                    RevealAuthors.Add(autore);
                }

                Screen = ScreenState.Reveal;
                break;

            case GameFinishedMessage finale:
                FinalPhrases.Clear();
                foreach (var frase in finale.Phrases)
                {
                    FinalPhrases.Add(frase);
                }

                Screen = ScreenState.Finished;
                break;

            case ErrorMessage errore:
                ErrorText = errore.Message;
                break;
        }
    }
}
```

- [ ] **Step 5: Eseguire i test e verificare che passino**

Run: `dotnet test tests/FrasiSquisite.App.Tests --nologo`

Expected: PASS, 12 test superati.

- [ ] **Step 6: Scrivere la pagina**

`src/FrasiSquisite.App/Pages/GamePage.xaml`:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:FrasiSquisite.App.ViewModels"
             xmlns:proto="clr-namespace:FrasiSquisite.Shared.Protocol;assembly=FrasiSquisite.Shared"
             xmlns:sys="clr-namespace:System;assembly=netstandard"
             x:Class="FrasiSquisite.App.Pages.GamePage"
             x:DataType="vm:GameSessionViewModel"
             Title="Frasi Squisite">

    <ScrollView>
        <VerticalStackLayout Padding="24" Spacing="16">

            <Label Text="{Binding ErrorText}" TextColor="OrangeRed"
                   IsVisible="{Binding ErrorText, Converter={StaticResource NotEmpty}}" />

            <!-- Home -->
            <VerticalStackLayout Spacing="12" IsVisible="{Binding Screen, Converter={StaticResource IsScreen}, ConverterParameter=Home}">
                <Label Text="Frasi Squisite" FontSize="32" HorizontalOptions="Center" />
                <Entry Placeholder="Server" Text="{Binding ServerUrl}" />
                <Entry Placeholder="Il tuo nome" Text="{Binding Nickname}" />
                <Button Text="Crea una stanza" Command="{Binding CreateRoomCommand}" />
                <Entry Placeholder="Codice stanza" Text="{Binding JoinCode}" MaxLength="4" />
                <Button Text="Entra" Command="{Binding JoinRoomCommand}" />
            </VerticalStackLayout>

            <!-- Lobby -->
            <VerticalStackLayout Spacing="12" IsVisible="{Binding Screen, Converter={StaticResource IsScreen}, ConverterParameter=Lobby}">
                <Label Text="{Binding RoomCode}" FontSize="48" HorizontalOptions="Center" />
                <Label Text="Dettate questo codice agli altri" HorizontalOptions="Center" />
                <CollectionView ItemsSource="{Binding Players}">
                    <CollectionView.ItemTemplate>
                        <DataTemplate x:DataType="proto:PlayerView">
                            <Label Text="{Binding Nickname}" FontSize="20" />
                        </DataTemplate>
                    </CollectionView.ItemTemplate>
                </CollectionView>
                <Button Text="Comincia" Command="{Binding StartGameCommand}" IsVisible="{Binding IsHost}" />
            </VerticalStackLayout>

            <!-- Scrittura -->
            <VerticalStackLayout Spacing="12" IsVisible="{Binding Screen, Converter={StaticResource IsScreen}, ConverterParameter=Writing}">
                <Label FontSize="14">
                    <Label.FormattedText>
                        <FormattedString>
                            <Span Text="Round " />
                            <Span Text="{Binding Round}" />
                            <Span Text=" di " />
                            <Span Text="{Binding TotalRounds}" />
                        </FormattedString>
                    </Label.FormattedText>
                </Label>
                <Label Text="{Binding Ruolo}" FontSize="28" />
                <Label Text="{Binding Prompt}" FontSize="16" />
                <Label Text="{Binding Esempio}" FontSize="14" TextColor="Gray" />
                <Entry Placeholder="Scrivi qui" Text="{Binding SlotText}" MaxLength="60" />
                <Button Text="Invia" Command="{Binding SubmitSlotCommand}" />
            </VerticalStackLayout>

            <!-- Attesa -->
            <VerticalStackLayout Spacing="12" IsVisible="{Binding Screen, Converter={StaticResource IsScreen}, ConverterParameter=Waiting}">
                <Label Text="Aspettiamo gli altri…" FontSize="24" HorizontalOptions="Center" />
                <Label HorizontalOptions="Center">
                    <Label.FormattedText>
                        <FormattedString>
                            <Span Text="{Binding SubmittedCount}" />
                            <Span Text=" di " />
                            <Span Text="{Binding PlayerCount}" />
                        </FormattedString>
                    </Label.FormattedText>
                </Label>
            </VerticalStackLayout>

            <!-- Reveal -->
            <VerticalStackLayout Spacing="12" IsVisible="{Binding Screen, Converter={StaticResource IsScreen}, ConverterParameter=Reveal}">
                <CollectionView ItemsSource="{Binding RevealedSlots}">
                    <CollectionView.ItemTemplate>
                        <DataTemplate x:DataType="sys:String">
                            <Label Text="{Binding .}" FontSize="24" />
                        </DataTemplate>
                    </CollectionView.ItemTemplate>
                </CollectionView>
                <CollectionView ItemsSource="{Binding RevealAuthors}">
                    <CollectionView.ItemTemplate>
                        <DataTemplate x:DataType="sys:String">
                            <Label Text="{Binding .}" FontSize="14" TextColor="Gray" />
                        </DataTemplate>
                    </CollectionView.ItemTemplate>
                </CollectionView>
                <Button Text="Avanti" Command="{Binding AdvanceRevealCommand}" IsVisible="{Binding IsHost}" />
            </VerticalStackLayout>

            <!-- Fine -->
            <VerticalStackLayout Spacing="12" IsVisible="{Binding Screen, Converter={StaticResource IsScreen}, ConverterParameter=Finished}">
                <Label Text="Ecco cosa è venuto fuori" FontSize="24" />
                <CollectionView ItemsSource="{Binding FinalPhrases}">
                    <CollectionView.ItemTemplate>
                        <DataTemplate x:DataType="sys:String">
                            <Label Text="{Binding .}" FontSize="20" Margin="0,8" />
                        </DataTemplate>
                    </CollectionView.ItemTemplate>
                </CollectionView>
            </VerticalStackLayout>

        </VerticalStackLayout>
    </ScrollView>
</ContentPage>
```

`src/FrasiSquisite.App/Pages/GamePage.xaml.cs`:

```csharp
using FrasiSquisite.App.ViewModels;

namespace FrasiSquisite.App.Pages;

public partial class GamePage : ContentPage
{
    public GamePage(GameSessionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
```

Crea i due converter usati dal XAML, in `src/FrasiSquisite.App/Converters/UiConverters.cs`:

```csharp
using System.Globalization;
using FrasiSquisite.App.ViewModels;

namespace FrasiSquisite.App.Converters;

public sealed class IsScreenConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ScreenState screen
        && parameter is string atteso
        && string.Equals(screen.ToString(), atteso, StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !string.IsNullOrWhiteSpace(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

Registrali in `src/FrasiSquisite.App/App.xaml`, dentro `<Application.Resources><ResourceDictionary>`:

```xml
            <converters:IsScreenConverter x:Key="IsScreen" />
            <converters:NotEmptyConverter x:Key="NotEmpty" />
```

aggiungendo al tag `<Application>` il namespace:

```xml
             xmlns:converters="clr-namespace:FrasiSquisite.App.Converters"
```

- [ ] **Step 7: Collegare il wiring DI e rimuovere la pagina del template**

Sostituisci `src/FrasiSquisite.App/MauiProgram.cs` con:

```csharp
using FrasiSquisite.App.Pages;
using FrasiSquisite.App.Services;
using FrasiSquisite.App.ViewModels;
using Microsoft.Extensions.Logging;

namespace FrasiSquisite.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // La ViewModel deve ricevere LA connessione del container, non una
        // nuova: due istanze significherebbero una ViewModel iscritta a una
        // connessione diversa da quella che parla col server.
        builder.Services.AddSingleton<IGameConnection, SignalRGameConnection>();
        builder.Services.AddSingleton(sp => new GameSessionViewModel(
            sp.GetRequiredService<IGameConnection>(),
            PlayerIdentity.Current()));
        builder.Services.AddSingleton<GamePage>();

        return builder.Build();
    }
}

/// <summary>
/// Identità del giocatore: un GUID generato al primo avvio e conservato in
/// SecureStorage. Nessun account (spec §9).
/// </summary>
public static class PlayerIdentity
{
    private const string Key = "player-id";

    public static Guid Current()
    {
        var salvato = SecureStorage.Default.GetAsync(Key).GetAwaiter().GetResult();

        if (Guid.TryParse(salvato, out var esistente))
        {
            return esistente;
        }

        var nuovo = Guid.NewGuid();
        SecureStorage.Default.SetAsync(Key, nuovo.ToString()).GetAwaiter().GetResult();
        return nuovo;
    }
}
```

Sostituisci `src/FrasiSquisite.App/AppShell.xaml` con:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<Shell xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
       xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
       xmlns:pages="clr-namespace:FrasiSquisite.App.Pages"
       x:Class="FrasiSquisite.App.AppShell"
       Shell.FlyoutBehavior="Disabled">

    <ShellContent Title="Frasi Squisite"
                  ContentTemplate="{DataTemplate pages:GamePage}"
                  Route="GamePage" />
</Shell>
```

Elimina la pagina del template:

```bash
rm src/FrasiSquisite.App/MainPage.xaml src/FrasiSquisite.App/MainPage.xaml.cs
```

- [ ] **Step 8: Verificare la compilazione e i test**

Run: `dotnet build src/FrasiSquisite.App --nologo`

Expected: 0 errori. Se `App.xaml` non trova i converter, controlla il namespace `xmlns:converters`.

Run: `dotnet test tests/FrasiSquisite.App.Tests --nologo`

Expected: PASS, 12 test superati.

- [ ] **Step 9: Verifica manuale end-to-end**

Avvia il server:

```bash
dotnet run --project src/FrasiSquisite.Server --urls http://0.0.0.0:5000
```

In un secondo terminale, verifica che risponda:

```bash
curl http://localhost:5000/health
```

Expected: `{"status":"ok"}`

Installa l'APK su due dispositivi (o un dispositivo e un emulatore) e gioca una partita completa: crea stanza, entra col codice, avvia, cinque round, reveal. Da emulatore Android l'host della macchina è `10.0.2.2`; da telefono fisico serve l'IP del PC sulla LAN.

- [ ] **Step 10: Commit**

```bash
git add src/FrasiSquisite.App tests/FrasiSquisite.App.Tests
git commit -m "feat(app): sessione di gioco completa dalla home al reveal"
```

---

## Verifica finale della Fase 1

- [ ] **Tutti i test passano**

Run: `dotnet test FrasiSquisite.slnx --nologo`

Expected: 0 fallimenti su tutti e quattro i progetti di test.

- [ ] **Tutto compila senza avvisi**

Run: `dotnet build FrasiSquisite.slnx --nologo`

Expected: `Avvisi: 0`, `Errori: 0`.

- [ ] **Aggiornare lo stato nel README**

In `README.md`, sostituisci la sezione `## Stato` con:

```markdown
## Stato

Fase 1 completata: partita giocabile dalla lobby al reveal, con più dispositivi
Android collegati a un server locale. Senza timer, voto, persistenza né AI —
arrivano nelle fasi successive (vedi spec §13).
```

```bash
git add README.md
git commit -m "docs: stato del progetto dopo la fase 1"
```
