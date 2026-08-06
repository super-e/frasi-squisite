# Il reveal si legge a scatti — Piano di implementazione

> **Per chi esegue:** SOTTO-SKILL RICHIESTA: usare superpowers:subagent-driven-development (consigliata) o superpowers:executing-plans, un task alla volta. I passi usano caselle (`- [ ]`).

**Obiettivo:** durante il reveal, la frase si legge con lo stesso tessuto connettivo del template (`dicendo:`, virgolette, punteggiatura) che oggi compare solo nella pagina del voto; e le caselle con testo lungo vanno a capo invece di essere tagliate. Backlog, voce 1.

**Architettura:** `Schema` impara a spezzare il proprio `Template` in una sequenza ordinata di segmenti — letterali e caselle — invece di comporlo tutto insieme con `string.Format`. Il motore usa quei segmenti a ogni avanzamento del reveal per costruire una lista di frammenti (`RevealFragment`): il testo fisso è sempre presente, le caselle scoperte portano il loro testo, quelle non ancora scoperte arrivano comunque ma vuote (`IsRevealed = false`) — il client sceglie da solo il segnaposto "···". Il server non manda mai il contenuto di una casella coperta: stesso vincolo di oggi, un campo in più con cui rispettarlo. Il client mostra i frammenti letterali come testo semplice, senza riquadro, intercalati agli stessi riquadri di oggi per le caselle — la corrispondenza 1:1 riquadro↔casella resta intatta, com'è "il cuore del gioco" (backlog).

**Perché fuori dai riquadri e non dentro:** il backlog lascia la scelta indifferente, ma tecnicamente "dentro" è ambiguo ogni volta che il template ha testo fisso sia prima del primo segnaposto che dopo l'ultimo (è il caso dello schema di default `storia`) — andrebbe deciso arbitrariamente a quale casella "appartiene" quel testo. "Fuori" non ha quest'ambiguità, si implementa con la stessa `FlexLayout` di oggi aggiungendo elementi non bordati accanto a quelli bordati, e mantiene la corrispondenza 1:1 che il backlog stesso indica come vincolo da preservare.

**Tech Stack:** .NET 10, MAUI (`net10.0-android`), SignalR, xUnit 2.9.3.

**Riferimento:** [backlog](../backlog.md), voce 1.

## Vincoli globali

Valgono per ogni task, senza ripeterli.

- **Il motore resta puro.** Niente I/O, niente `async`, niente orologio, niente casualità non iniettata dentro `FrasiSquisite.Domain`.
- **Il server non manda mai il testo di una casella non ancora scoperta.** È il vincolo che già vale oggi (voto cieco a valle, spec §3) e non deve regredire: una casella coperta arriva con `Text` vuoto e `IsRevealed = false`, mai col contenuto vero.
- **Il reveal scopre una casella alla volta.** Nessuna soluzione deve mostrare in anticipo il testo di una casella non ancora rivelata dall'host.
- **Protocollo: 7 → 8**, uguaglianza stretta (`ProtocolVersion.IsCompatible`): un client vecchio verrebbe rifiutato esplicitamente, non fallirebbe in modo oscuro.
- **Lingua**: codice, commenti, messaggi di commit e testo a schermo in italiano, come il resto del progetto. I commenti spiegano il *perché*, non il *cosa*.
- **Firma dei commit**: `commit.gpgsign` è attivo. Se 1Password è bloccato, **fermarsi e segnalarlo**; mai `--no-gpg-sign`.
- **Comando dei test**: `dotnet test FrasiSquisite.slnx` (estensione `.slnx`, non `.sln`). Fra il Task 2 e il Task 3 questo comando risulta rosso: `RevealStepMessage` cambia forma nel Task 2 e solo il Task 3 aggiorna il client MAUI che la consuma. Nel frattempo si verifica progetto per progetto (comandi indicati in ogni task) — è la stessa sequenza che affronterebbe chi lavora dal vivo su un contratto condiviso fra più progetti.
- **Punto di partenza**: 791 test verdi (Shared 77, Domain 509, App 104, Server 101).
- **Nessun test di markup/XAML esiste in questo repository** (i ViewModel MAUI si testano in isolamento, le pagine `.xaml` no): il Task 3 include passi di verifica manuale esplicitamente non automatizzati, non un buco silenzioso nella copertura.

---

## Struttura dei file

**Shared (contratto e composizione)**
- `src/FrasiSquisite.Shared/Schemas/Schema.cs` — nuovo tipo `TemplateSegment` e proprietà `Segments` che spezza `Template` in letterali e caselle.
- `src/FrasiSquisite.Shared/Protocol/ServerMessages.cs` — nuovo record `RevealFragment`; `RevealStepMessage.RevealedSlots` (`IReadOnlyList<string>`) diventa `RevealStepMessage.Fragments` (`IReadOnlyList<RevealFragment>`).
- `src/FrasiSquisite.Shared/Protocol/ProtocolVersion.cs` — `Current` da 7 a 8.

**Domain (motore puro)**
- `src/FrasiSquisite.Domain/Engine/GameEngine.Reveal.cs` — nuovo metodo privato `FrammentiReveal`; `OnRevealAdvance` lo usa per costruire `RevealStepMessage.Fragments` invece della lista di soli testi.
- `src/FrasiSquisite.Domain/Engine/GameEngine.Refining.cs` — `AvviaReveal` manda i frammenti iniziali (tutto il template, nessuna casella scoperta) invece di una lista vuota.

**App (client MAUI)**
- `src/FrasiSquisite.App/ViewModels/RevealSlotView.cs` → rinominato `RevealFragmentView.cs`: il record guadagna `IsSlot` e le proprietà computate che la UI usa per scegliere il ramo di rendering.
- `src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs` — `RevealSlots` diventa `RevealFragments`; l'handler di `RevealStepMessage` mappa `Fragments` invece di riempire per `SlotCount` (la logica di riempimento locale sparisce: la fa già il server).
- `src/FrasiSquisite.App/Pages/GamePage.xaml` — la `FlexLayout` del reveal lega a `RevealFragments`; il `DataTemplate` sceglie fra testo fisso senza riquadro, riquadro scoperto, riquadro coperto.
- `src/FrasiSquisite.App/Resources/Styles/Styles.xaml` — nuovo stile `RevealLiteralText`; `RevealSlotRevealedText`/`RevealSlotCoveredText` guadagnano un `LineBreakMode` esplicito e un `MaximumWidthRequest`, per non tagliare più le caselle con frasi lunghe.

**Test**
- `tests/FrasiSquisite.Shared.Tests/Schemas/SchemaSegmentsTests.cs` — nuovo file, copre `Schema.Segments`.
- `tests/FrasiSquisite.Shared.Tests/Protocol/ProtocolContractTests.cs` — roundtrip di `RevealFragment`, roundtrip e forma di `RevealStepMessage` aggiornati, versione di protocollo 8.
- `tests/FrasiSquisite.Domain.Tests/TestSchemas.cs` — nuovo helper `WithTemplate`.
- `tests/FrasiSquisite.Domain.Tests/Engine/RevealTests.cs` — asserzioni aggiornate su `Fragments`, nuovo test sull'intercalazione del testo fisso.
- `tests/FrasiSquisite.Server.Tests/Realtime/GameHubTests.cs` — un'asserzione aggiornata su `Fragments`.
- `tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs` — chiamate a `RevealStepMessage` aggiornate, `RevealSlots` → `RevealFragments`, un test riscritto.

---

### Task 1: `Schema` spezza il template in segmenti

**File:**
- Modificare: `src/FrasiSquisite.Shared/Schemas/Schema.cs`
- Test: `tests/FrasiSquisite.Shared.Tests/Schemas/SchemaSegmentsTests.cs` (nuovo)

**Interfacce:**
- Produce: `TemplateSegment` — `bool IsSlot`, `string Literal` (testo fisso se `!IsSlot`, altrimenti stringa vuota), `int SlotIndex` (indice della casella se `IsSlot`, altrimenti -1); fabbriche statiche `TemplateSegment.OfLiteral(string)` e `TemplateSegment.OfSlot(int)`. `Schema.Segments` (`IReadOnlyList<TemplateSegment>`), nell'ordine di lettura del `Template`.

Task isolato: nessun altro progetto tocca `Segments` finché non arriva il Task 2, quindi l'intera suite (`dotnet test FrasiSquisite.slnx`) resta verde per tutto questo task.

- [ ] **Passo 1: Scrivere il test che fallisce**

Creare `tests/FrasiSquisite.Shared.Tests/Schemas/SchemaSegmentsTests.cs`:

```csharp
using FrasiSquisite.Shared.Schemas;
using Xunit;

namespace FrasiSquisite.Shared.Tests.Schemas;

public class SchemaSegmentsTests
{
    private static Schema ConCaselle(int k, string template) =>
        new(
            "test",
            1,
            "Test",
            [.. Enumerable.Range(0, k).Select(i => new Casella($"Ruolo{i}", $"Prompt{i}", $"Esempio{i}"))],
            template);

    [Fact]
    public void UnTemplateDiSoleCaselleProduceSoloSegmentiDiCasella()
    {
        var schema = ConCaselle(3, "{0}{1}{2}");

        Assert.Equal(3, schema.Segments.Count);
        Assert.All(schema.Segments, s => Assert.True(s.IsSlot));
        Assert.Equal([0, 1, 2], schema.Segments.Select(s => s.SlotIndex));
    }

    [Fact]
    public void UnTemplateConSpaziIntercalaSegmentiLetteraliFraLeCaselle()
    {
        var schema = ConCaselle(2, "{0} {1}");

        Assert.Equal(3, schema.Segments.Count);
        Assert.Equal((true, 0), (schema.Segments[0].IsSlot, schema.Segments[0].SlotIndex));
        Assert.Equal((false, " "), (schema.Segments[1].IsSlot, schema.Segments[1].Literal));
        Assert.Equal((true, 1), (schema.Segments[2].IsSlot, schema.Segments[2].SlotIndex));
    }

    [Fact]
    public void IlTestoFissoPrimaDellaPrimaCasellaEDopoLUltimaDiventaUnSegmentoLetterale()
    {
        var schema = ConCaselle(1, "Dice: «{0}».");

        Assert.Equal(3, schema.Segments.Count);
        Assert.Equal((false, "Dice: «"), (schema.Segments[0].IsSlot, schema.Segments[0].Literal));
        Assert.Equal((true, 0), (schema.Segments[1].IsSlot, schema.Segments[1].SlotIndex));
        Assert.Equal((false, "»."), (schema.Segments[2].IsSlot, schema.Segments[2].Literal));
    }

    [Fact]
    public void LoSchemaDiDefaultProduceIlTessutoConnettivoAtteso()
    {
        var catalogo = new EmbeddedSchemaCatalog();
        var schema = catalogo.Get(Schema.DefaultId);

        var letterali = schema.Segments.Where(s => !s.IsSlot).Select(s => s.Literal).ToList();

        Assert.Contains(", dicendo: «", letterali);
        Assert.Contains("». La gente dice: «", letterali);
        Assert.Contains("», ed è andata a finire che ", letterali);
        Assert.Equal(8, schema.Segments.Count(s => s.IsSlot));
    }
}
```

- [ ] **Passo 2: Eseguire il test e verificare che fallisca**

Run: `dotnet test tests/FrasiSquisite.Shared.Tests --filter "FullyQualifiedName~SchemaSegmentsTests"`
Expected: FAIL — `Schema` non ha una proprietà `Segments` (errore di compilazione).

- [ ] **Passo 3: Implementare `TemplateSegment` e `Schema.Segments`**

Sostituire il contenuto di `src/FrasiSquisite.Shared/Schemas/Schema.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace FrasiSquisite.Shared.Schemas;

/// <summary>
/// Un pezzo del <see cref="Schema.Template"/>, nell'ordine di lettura: testo
/// fisso (<see cref="IsSlot"/> false) o casella (<see cref="IsSlot"/> true,
/// <see cref="SlotIndex"/> l'indice del segnaposto). Serve al reveal
/// (backlog #1) per intercalare il tessuto connettivo del template alle
/// caselle già scoperte, senza comporre l'intera frase in anticipo.
/// </summary>
public sealed record TemplateSegment
{
    public bool IsSlot { get; }
    public string Literal { get; }
    public int SlotIndex { get; }

    private TemplateSegment(bool isSlot, string literal, int slotIndex)
    {
        IsSlot = isSlot;
        Literal = literal;
        SlotIndex = slotIndex;
    }

    public static TemplateSegment OfLiteral(string text) => new(false, text, -1);

    public static TemplateSegment OfSlot(int slotIndex) => new(true, string.Empty, slotIndex);
}

public sealed record Schema(
    string Id,
    int Version,
    string Nome,
    IReadOnlyList<Casella> Caselle,
    string Template)
{
    public const string DefaultId = "storia";

    private static readonly Regex PosizioneSegnaposto = new(@"\{(\d+)\}", RegexOptions.Compiled);

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

    /// <summary>
    /// Il <see cref="Template"/> spezzato in ordine di lettura fra testo
    /// fisso e caselle (backlog #1): a differenza di <see cref="Compose"/>,
    /// che produce la frase intera in un colpo solo, questa scomposizione
    /// permette di mostrare il tessuto connettivo anche quando non tutte le
    /// caselle sono ancora state scoperte.
    /// </summary>
    public IReadOnlyList<TemplateSegment> Segments
    {
        get
        {
            var segmenti = new List<TemplateSegment>();
            var cursore = 0;

            foreach (Match m in PosizioneSegnaposto.Matches(Template))
            {
                if (m.Index > cursore)
                {
                    segmenti.Add(TemplateSegment.OfLiteral(Template[cursore..m.Index]));
                }

                segmenti.Add(TemplateSegment.OfSlot(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)));
                cursore = m.Index + m.Length;
            }

            if (cursore < Template.Length)
            {
                segmenti.Add(TemplateSegment.OfLiteral(Template[cursore..]));
            }

            return segmenti;
        }
    }
}
```

- [ ] **Passo 4: Eseguire il test e verificare che passi**

Run: `dotnet test tests/FrasiSquisite.Shared.Tests --filter "FullyQualifiedName~SchemaSegmentsTests"`
Expected: PASS — 4 test verdi.

Run anche l'intera suite Shared, per essere certi di non aver rotto `Compose`: `dotnet test tests/FrasiSquisite.Shared.Tests`
Expected: PASS — 81 test verdi (77 + 4 nuovi).

- [ ] **Passo 5: Commit**

```bash
git add src/FrasiSquisite.Shared/Schemas/Schema.cs tests/FrasiSquisite.Shared.Tests/Schemas/SchemaSegmentsTests.cs
git commit -m "feat(reveal): Schema espone il template spezzato in segmenti letterali e caselle"
```

---

### Task 2: Il motore manda il tessuto connettivo insieme alle caselle scoperte

**File:**
- Modificare: `src/FrasiSquisite.Shared/Protocol/ServerMessages.cs`
- Modificare: `src/FrasiSquisite.Shared/Protocol/ProtocolVersion.cs`
- Modificare: `src/FrasiSquisite.Domain/Engine/GameEngine.Reveal.cs`
- Modificare: `src/FrasiSquisite.Domain/Engine/GameEngine.Refining.cs`
- Modificare: `tests/FrasiSquisite.Domain.Tests/TestSchemas.cs`
- Test: `tests/FrasiSquisite.Shared.Tests/Protocol/ProtocolContractTests.cs`, `tests/FrasiSquisite.Domain.Tests/Engine/RevealTests.cs`, `tests/FrasiSquisite.Server.Tests/Realtime/GameHubTests.cs`

**Interfacce:**
- Consuma: `Schema.Segments`, `TemplateSegment` (Task 1).
- Produce: `RevealFragment(bool IsSlot, string Text, bool IsRevealed)` in `FrasiSquisite.Shared.Protocol`. `RevealStepMessage(int PhraseIndex, int TotalPhrases, IReadOnlyList<RevealFragment> Fragments, bool PhraseComplete)` — `Fragments` sostituisce `RevealedSlots`.

Questo task rompe la compilazione di `FrasiSquisite.App` (e quindi di `dotnet test FrasiSquisite.slnx` per intero): `GameSessionViewModel.cs` referenzia ancora `RevealedSlots`, che da qui in poi non esiste più. È atteso — il Task 3 lo sistema. In questo task si verifica progetto per progetto (Shared, Domain, Server), mai con l'intera soluzione.

- [ ] **Passo 1: Scrivere il test che fallisce, sul contratto**

In `tests/FrasiSquisite.Shared.Tests/Protocol/ProtocolContractTests.cs`, sostituire il blocco (righe 14-18):

```csharp
    [Fact]
    public void LaVersioneDelProtocolloE7()
    {
        Assert.Equal(7, ProtocolVersion.Current);
    }
```

con:

```csharp
    [Fact]
    public void LaVersioneDelProtocolloE8()
    {
        Assert.Equal(8, ProtocolVersion.Current);
    }
```

E sostituire il blocco (righe 140-178, i due test `RoundtripDiRevealStep` e `IlPassoDiRevealNonHaUnCampoAutori`):

```csharp
    [Fact]
    public void RoundtripDiRevealFragment()
    {
        var originale = new RevealFragment(IsSlot: true, Text: "Il cadavere", IsRevealed: true);

        var json = JsonSerializer.Serialize(originale, ProtocolJson.Options);
        var ricostruito = JsonSerializer.Deserialize<RevealFragment>(json, ProtocolJson.Options);

        Assert.Equal(originale, ricostruito);
    }

    [Fact]
    public void RoundtripDiRevealStep()
    {
        var originale = new RevealStepMessage(
            PhraseIndex: 0,
            TotalPhrases: 3,
            Fragments:
            [
                new RevealFragment(true, "Il cadavere", true),
                new RevealFragment(false, " ", true),
                new RevealFragment(true, string.Empty, false),
            ],
            PhraseComplete: false);

        var json = JsonSerializer.Serialize(originale, ProtocolJson.Options);
        var ricostruito = JsonSerializer.Deserialize<RevealStepMessage>(json, ProtocolJson.Options);

        Assert.NotNull(ricostruito);
        Assert.Equal(originale.PhraseIndex, ricostruito.PhraseIndex);
        Assert.Equal(originale.TotalPhrases, ricostruito.TotalPhrases);
        Assert.Equal(originale.Fragments, ricostruito.Fragments);
        Assert.Equal(originale.PhraseComplete, ricostruito.PhraseComplete);
    }

    /// <summary>
    /// Il tipo non deve avere un campo per gli autori: è così che la
    /// segretezza è garantita dal tipo e non dalla disciplina (spec §3).
    /// Come <see cref="SlotRequestNonEspoheAlcunCampoDiTesto"/>, l'elenco
    /// completo delle proprietà: cercare solo "Authors" per nome (come
    /// faceva questo test) lascerebbe passare indisturbato un campo
    /// rimesso con un altro nome (es. "Autori", "AuthorNames",
    /// "AuthorIds") - una regressione che riaprirebbe la fuga senza che
    /// nessun altro test se ne accorga, perché il valore sarebbe comunque
    /// vuoto nei casi provati.
    /// </summary>
    [Fact]
    public void IlPassoDiRevealNonHaUnCampoAutori()
    {
        var proprieta = typeof(RevealStepMessage).GetProperties().Select(p => p.Name).ToArray();

        Assert.Equal(
            ["PhraseIndex", "TotalPhrases", "Fragments", "PhraseComplete"],
            proprieta);
    }
```

- [ ] **Passo 2: Eseguire i test e verificare che falliscano**

Run: `dotnet test tests/FrasiSquisite.Shared.Tests --filter "FullyQualifiedName~ProtocolContractTests"`
Expected: FAIL — `RevealFragment` non esiste, `RevealStepMessage` non ha `Fragments`, `ProtocolVersion.Current` vale ancora 7 (errori di compilazione finché non si tocca il codice di produzione).

- [ ] **Passo 3: Aggiornare i messaggi di protocollo**

In `src/FrasiSquisite.Shared/Protocol/ServerMessages.cs`, sostituire il blocco (righe 49-59):

```csharp
/// <summary>
/// Il passo di scoprimento non porta gli autori: il voto che segue è cieco
/// (spec §3). Il campo non è vuoto — non esiste, così come
/// <see cref="SlotRequestMessage"/> non ha un campo per il testo già scritto.
/// Una fuga durante il reveal non è una regressione possibile.
/// </summary>
public sealed record RevealStepMessage(
    int PhraseIndex,
    int TotalPhrases,
    IReadOnlyList<string> RevealedSlots,
    bool PhraseComplete);
```

con:

```csharp
/// <summary>
/// Un pezzo della frase mostrata nel reveal, nell'ordine di lettura: testo
/// fisso del template (sempre presente, non rivela nulla su nessuno) o una
/// casella. Le caselle non ancora scoperte arrivano comunque, con
/// <see cref="IsRevealed"/> false e <see cref="Text"/> vuoto — così il
/// client conosce da subito la lunghezza e la punteggiatura della frase
/// intera, come già succede nella pagina del voto (backlog #1), senza che
/// il server mandi mai il contenuto di una casella coperta.
/// </summary>
public sealed record RevealFragment(bool IsSlot, string Text, bool IsRevealed);

/// <summary>
/// Il passo di scoprimento non porta gli autori: il voto che segue è cieco
/// (spec §3). Il campo non è vuoto — non esiste, così come
/// <see cref="SlotRequestMessage"/> non ha un campo per il testo già scritto.
/// Una fuga durante il reveal non è una regressione possibile.
/// </summary>
public sealed record RevealStepMessage(
    int PhraseIndex,
    int TotalPhrases,
    IReadOnlyList<RevealFragment> Fragments,
    bool PhraseComplete);
```

In `src/FrasiSquisite.Shared/Protocol/ProtocolVersion.cs`, sostituire:

```csharp
    public const int Current = 7;
```

con:

```csharp
    public const int Current = 8;
```

- [ ] **Passo 4: Eseguire i test e verificare che passino**

Run: `dotnet test tests/FrasiSquisite.Shared.Tests`
Expected: PASS — 83 test verdi (81 del Task 1 + `RoundtripDiRevealFragment` nuovo, `LaVersioneDelProtocolloE7` rinominato non aggiunge un test).

- [ ] **Passo 5: Scrivere il test che fallisce, sul motore**

In `tests/FrasiSquisite.Domain.Tests/TestSchemas.cs`, aggiungere dopo `WithSlots`:

```csharp
    /// <summary>
    /// Schema sintetico con K caselle e un template esplicito, per
    /// verificare l'intercalazione del testo fisso nel reveal (backlog #1).
    /// </summary>
    public static Schema WithTemplate(int k, string template)
    {
        var caselle = Enumerable.Range(0, k)
            .Select(i => new Casella($"Ruolo{i}", $"Prompt {i}", $"Esempio {i}"))
            .ToList();

        return new Schema($"test-{k}-template", 1, $"Test {k}", caselle, template);
    }
```

In `tests/FrasiSquisite.Domain.Tests/Engine/RevealTests.cs`, sostituire i tre corpi che leggono `RevealedSlots`:

riga 56, dentro `OgniAvanzamentoScopreUnaCasellaInPiu`:

```csharp
        Assert.Single(passo.RevealedSlots);
```

con:

```csharp
        Assert.Equal(1, passo.Fragments.Count(f => f.IsSlot && f.IsRevealed));
```

riga 84, dentro `IlPassoDiRevealNonPortaMaiGliAutori`:

```csharp
        Assert.Equal(K, completo.RevealedSlots.Count);
```

con:

```csharp
        Assert.Equal(K, completo.Fragments.Count(f => f.IsSlot && f.IsRevealed));
```

riga 101, dentro `DopoUnaFraseSiPassaAllaSuccessiva`:

```csharp
        Assert.Single(passo.RevealedSlots);
```

con:

```csharp
        Assert.Equal(1, passo.Fragments.Count(f => f.IsSlot && f.IsRevealed));
```

E aggiungere, dopo `DopoUnaFraseSiPassaAllaSuccessiva` (dopo la riga che ora chiude a `}` circa riga 103), il nuovo test:

```csharp
    [Fact]
    public void IlTestoFissoDelTemplateArrivaSubitoEIntercalatoAlleCaselleScoperte()
    {
        var motore = new GameEngine(new RoleSchemaMode(), new StaticWordPool(), new SeededRandomSource(1));
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithTemplate(K, "Dice: «{0}» e poi «{1}» e infine «{2}»."));

        for (var i = 0; i < N; i++)
        {
            stato = motore.Handle(stato, new PlayerJoined(Giocatore(i), $"G{i}")).State;
        }

        stato = motore.Handle(stato, new GameStartRequested(Giocatore(0))).State;

        for (var round = 0; round < K; round++)
        {
            for (var g = 0; g < N; g++)
            {
                stato = motore.Handle(stato, new SlotSubmitted(Giocatore(g), $"p{round}{g}")).State;
            }
        }

        stato = motore.Handle(stato, new RefinementFinished(null)).State;

        var testoPrimaCasella = stato.Phrases[0].Slots[0]!.Text;

        var primo = motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0)));
        var passo = Assert.Single(primo.Broadcasts<RevealStepMessage>());

        Assert.Equal(7, passo.Fragments.Count);
        Assert.Equal((false, "Dice: «", true), (passo.Fragments[0].IsSlot, passo.Fragments[0].Text, passo.Fragments[0].IsRevealed));
        Assert.Equal((true, testoPrimaCasella, true), (passo.Fragments[1].IsSlot, passo.Fragments[1].Text, passo.Fragments[1].IsRevealed));
        Assert.Equal((false, "» e poi «", true), (passo.Fragments[2].IsSlot, passo.Fragments[2].Text, passo.Fragments[2].IsRevealed));
        Assert.Equal((true, string.Empty, false), (passo.Fragments[3].IsSlot, passo.Fragments[3].Text, passo.Fragments[3].IsRevealed));
        Assert.Equal((false, "» e infine «", true), (passo.Fragments[4].IsSlot, passo.Fragments[4].Text, passo.Fragments[4].IsRevealed));
        Assert.Equal((true, string.Empty, false), (passo.Fragments[5].IsSlot, passo.Fragments[5].Text, passo.Fragments[5].IsRevealed));
        Assert.Equal((false, "»."), (passo.Fragments[6].IsSlot, passo.Fragments[6].Text));
    }
```

- [ ] **Passo 6: Eseguire i test e verificare che falliscano**

Run: `dotnet test tests/FrasiSquisite.Domain.Tests --filter "FullyQualifiedName~RevealTests"`
Expected: FAIL — `passo.Fragments` non esiste ancora (`OnRevealAdvance` costruisce ancora `RevealedSlots`, che nel frattempo non esiste più nel tipo: errore di compilazione).

- [ ] **Passo 7: Implementare `FrammentiReveal` nel motore**

Sostituire il contenuto di `src/FrasiSquisite.Domain/Engine/GameEngine.Reveal.cs`:

```csharp
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Voting;
using FrasiSquisite.Shared.Protocol;
using FrasiSquisite.Shared.Schemas;

namespace FrasiSquisite.Domain.Engine;

/// <summary>Scoprimento casella per casella, guidato dall'host.</summary>
public sealed partial class GameEngine
{
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

        var passo = new RevealStepMessage(
            state.RevealPhraseIndex,
            state.Phrases.Count,
            FrammentiReveal(state.Schema, frase, scoperte),
            completa);

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

        return EntraInVoto(state, [new BroadcastToRoom(passo)]);
    }

    /// <summary>
    /// Il tessuto connettivo del template intercalato alle caselle scoperte
    /// (backlog #1): è come si legge la pagina del voto, dove
    /// <see cref="Schema.Compose"/> fa lo stesso lavoro in un colpo solo.
    /// Le caselle non ancora scoperte arrivano comunque (per punteggiatura e
    /// posizione corrette lato client) ma senza testo — il voto che segue è
    /// cieco, e questo è il punto che non deve mai regredire.
    /// </summary>
    private static IReadOnlyList<RevealFragment> FrammentiReveal(Schema schema, Phrase frase, int scoperte) =>
        [.. schema.Segments.Select(s => s.IsSlot
            ? new RevealFragment(true, s.SlotIndex < scoperte ? frase.Slots[s.SlotIndex]!.Text : string.Empty, s.SlotIndex < scoperte)
            : new RevealFragment(false, s.Literal, true))];

    /// <summary>Le frasi composte secondo il template dello schema.</summary>
    private static IReadOnlyList<string> FrasiComposte(GameState state) =>
        [.. state.Phrases.Select(f => state.Schema.Compose([.. f.Slots.Select(s => s!.Text)]))];

    /// <summary>
    /// La classifica pronta da mandare. Con nessun voto — il voto chiuso
    /// prima che qualcuno esprimesse una preferenza — produce tutte le frasi
    /// a zero e nessuna vincitrice, che è esattamente il significato giusto.
    /// </summary>
    private static IReadOnlyList<PhraseResultView> Classifica(
        GameState state,
        IReadOnlyDictionary<Guid, int> voti)
    {
        var frasi = FrasiComposte(state);

        return [.. VoteTally.From(voti, state.Phrases.Count).Ranking
            .Select(r => new PhraseResultView(
                r.PhraseIndex,
                frasi[r.PhraseIndex],
                // Chi ha scritto la frase, non chi ha riempito ogni casella:
                // con otto caselle e due giocatori l'elenco per casella
                // ripeteva ogni nome quattro volte, e l'ordine posizionale
                // diceva pure quale casella fosse di chi — che il voto cieco
                // non prevede di rivelare. La deduplica e' sull'identita' e
                // non sul nome: due omonimi restano due persone.
                [.. state.Phrases[r.PhraseIndex].Slots
                    .Select(s => s!.AuthorId)
                    .Distinct()
                    .Select(id => state.FindPlayer(id)?.Nickname ?? "?")],
                r.Votes,
                r.IsWinner))];
    }
}
```

In `src/FrasiSquisite.Domain/Engine/GameEngine.Refining.cs`, sostituire il blocco (righe 79-101):

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

con:

```csharp
    /// <summary>
    /// Porta la stanza nel reveal. Chiamato solo dalla fine della rifinitura:
    /// prima ci si arrivava direttamente dalla fine dell'ultimo round.
    ///
    /// La RevealStepMessage iniziale - nessuna casella ancora scoperta, ma
    /// già col tessuto connettivo del template (backlog #1) - e' cio' che
    /// porta tutti sulla schermata di reveal: senza, ogni client resterebbe
    /// fermo dov'era, perche' non arriva piu' nessuna SlotRequestMessage e
    /// Screen non cambia mai da solo.
    /// </summary>
    private static EngineResult AvviaReveal(GameState state)
    {
        var reveal = state with
        {
            Phase = RoomPhase.Reveal,
            RevealPhraseIndex = 0,
            RevealSlotCount = 0,
        };

        var frammenti = FrammentiReveal(reveal.Schema, reveal.Phrases[0], scoperte: 0);

        return new EngineResult(reveal, [
            new BroadcastToRoom(RoomState(reveal)),
            new BroadcastToRoom(new RevealStepMessage(0, reveal.Phrases.Count, frammenti, false)),
        ]);
    }
```

- [ ] **Passo 8: Eseguire i test e verificare che passino**

Run: `dotnet test tests/FrasiSquisite.Domain.Tests --filter "FullyQualifiedName~RevealTests"`
Expected: PASS — 6 test verdi (5 esistenti aggiornati + 1 nuovo).

Run l'intera suite Domain: `dotnet test tests/FrasiSquisite.Domain.Tests`
Expected: PASS — 510 test verdi (509 + 1 nuovo).

- [ ] **Passo 9: Aggiornare il test d'integrazione del server**

In `tests/FrasiSquisite.Server.Tests/Realtime/GameHubTests.cs`, sostituire (riga 291):

```csharp
        Assert.Single(passo.RevealedSlots);
```

con:

```csharp
        Assert.Single(passo.Fragments.Where(f => f.IsSlot && f.IsRevealed));
```

Nello stesso file, per accuratezza del commento (righe 269-270 e 278, che oggi descrivono la `RevealStepMessage` iniziale come "vuota" — non lo è più, porta il tessuto connettivo): sostituire

```
        // una RevealStepMessage iniziale e vuota (nessuna casella ancora
        // scoperta), proprio per portare tutti sulla schermata di reveal
```

con

```
        // una RevealStepMessage iniziale, con nessuna casella ancora
        // scoperta, proprio per portare tutti sulla schermata di reveal
```

e sostituire

```
        // cieca - lo stesso pattern di GiocaFinoAllaFineAsync più sotto: se
        // la chiamata viene respinta, la RevealStepMessage iniziale e vuota
        // fa comunque salire il conteggio, e il giro successivo la riprova
```

con

```
        // cieca - lo stesso pattern di GiocaFinoAllaFineAsync più sotto: se
        // la chiamata viene respinta, la RevealStepMessage iniziale, priva
        // di caselle scoperte, fa comunque salire il conteggio, e il giro
        // successivo la riprova
```

e sostituire (nel commento XML sopra `GiocaFinoAllaFineAsync`, circa riga 304-306)

```
    /// qui invece, se la chiamata viene respinta, la RevealStepMessage
    /// iniziale e vuota che chiude comunque la rifinitura fa salire il
    /// conteggio lo stesso, e il giro successivo la riprova.
```

con

```
    /// qui invece, se la chiamata viene respinta, la RevealStepMessage
    /// iniziale, priva di caselle scoperte, che chiude comunque la
    /// rifinitura fa salire il conteggio lo stesso, e il giro successivo la
    /// riprova.
```

- [ ] **Passo 10: Eseguire i test e verificare che passino**

Run: `dotnet test tests/FrasiSquisite.Server.Tests --filter "FullyQualifiedName~GameHubTests"`
Expected: PASS.

Run l'intera suite Server: `dotnet test tests/FrasiSquisite.Server.Tests`
Expected: PASS — 101 test verdi (nessun test nuovo in questo progetto).

- [ ] **Passo 11: Commit**

```bash
git add src/FrasiSquisite.Shared/Protocol/ServerMessages.cs \
        src/FrasiSquisite.Shared/Protocol/ProtocolVersion.cs \
        src/FrasiSquisite.Domain/Engine/GameEngine.Reveal.cs \
        src/FrasiSquisite.Domain/Engine/GameEngine.Refining.cs \
        tests/FrasiSquisite.Shared.Tests/Protocol/ProtocolContractTests.cs \
        tests/FrasiSquisite.Domain.Tests/TestSchemas.cs \
        tests/FrasiSquisite.Domain.Tests/Engine/RevealTests.cs \
        tests/FrasiSquisite.Server.Tests/Realtime/GameHubTests.cs
git commit -m "feat(reveal): il motore manda il tessuto connettivo del template col reveal, protocollo v8"
```

---

### Task 3: Il client mostra il tessuto connettivo e non taglia più le caselle lunghe

**File:**
- Rinominare: `src/FrasiSquisite.App/ViewModels/RevealSlotView.cs` → `src/FrasiSquisite.App/ViewModels/RevealFragmentView.cs`
- Modificare: `src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs`
- Modificare: `src/FrasiSquisite.App/Pages/GamePage.xaml`
- Modificare: `src/FrasiSquisite.App/Resources/Styles/Styles.xaml`
- Test: `tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs`

**Interfacce:**
- Consuma: `RevealFragment`, `RevealStepMessage.Fragments` (Task 2).
- Produce: `RevealFragmentView(bool IsSlot, string Text, bool IsRevealed)` con le proprietà computate `IsLiteral`, `ShowAsRevealedSlot`, `ShowAsCoveredSlot`. `GameSessionViewModel.RevealFragments` (`ObservableCollection<RevealFragmentView>`) sostituisce `RevealSlots`.

Questo task riporta l'intera soluzione al verde: è l'ultimo a toccare codice di produzione.

- [ ] **Passo 1: Scrivere i test che falliscono**

In `tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs`, aggiungere tre helper privati subito dopo il metodo `Crea()` (circa riga 18, prima del prossimo metodo):

```csharp
    private static RevealFragment Rivelata(string testo) => new(true, testo, true);

    private static RevealFragment Coperta() => new(true, string.Empty, false);

    private static RevealFragment Letterale(string testo) => new(false, testo, true);
```

Sostituire il blocco del test esistente (righe 261-278, `IlPassoDiRevealPopolaLeCaselleScoperteELascaCoperteLeRestanti`):

```csharp
    [Fact]
    public void IlPassoDiRevealPopolaLeCaselleScoperteELascaCoperteLeRestanti()
    {
        var (vm, conn) = Crea();

        conn.Emit(new RoomStateMessage(
            "ABCD", "Reveal",
            [new PlayerView(Anna, "Anna", true, true, false)],
            "surrealista-classico", 3));

        conn.Emit(new RevealStepMessage(0, 1, ["Il cadavere", "squisito"], false));

        Assert.Equal(ScreenState.Reveal, vm.Screen);
        Assert.Equal(3, vm.RevealSlots.Count);
        Assert.Equal(("Il cadavere", true), (vm.RevealSlots[0].Text, vm.RevealSlots[0].IsRevealed));
        Assert.Equal(("squisito", true), (vm.RevealSlots[1].Text, vm.RevealSlots[1].IsRevealed));
        Assert.Equal(("···", false), (vm.RevealSlots[2].Text, vm.RevealSlots[2].IsRevealed));
    }
```

con:

```csharp
    [Fact]
    public void IlPassoDiRevealMostraIFrammentiScopertiECopreLeCaselleRestanti()
    {
        var (vm, conn) = Crea();

        conn.Emit(new RevealStepMessage(0, 1, [
            Rivelata("Il cadavere"),
            Letterale(" "),
            Rivelata("squisito"),
            Letterale(" "),
            Coperta(),
        ], false));

        Assert.Equal(ScreenState.Reveal, vm.Screen);
        Assert.Equal(5, vm.RevealFragments.Count);
        Assert.Equal((true, "Il cadavere", true), (vm.RevealFragments[0].IsSlot, vm.RevealFragments[0].Text, vm.RevealFragments[0].IsRevealed));
        Assert.Equal((false, " "), (vm.RevealFragments[1].IsSlot, vm.RevealFragments[1].Text));
        Assert.Equal((true, "squisito", true), (vm.RevealFragments[2].IsSlot, vm.RevealFragments[2].Text, vm.RevealFragments[2].IsRevealed));
        Assert.Equal((false, " "), (vm.RevealFragments[3].IsSlot, vm.RevealFragments[3].Text));
        Assert.Equal((true, "···", false), (vm.RevealFragments[4].IsSlot, vm.RevealFragments[4].Text, vm.RevealFragments[4].IsRevealed));
    }
```

Aggiornare gli altri call-site che costruiscono `RevealStepMessage` con liste di stringhe. Riga 150:

```csharp
        conn.Emit(new RevealStepMessage(0, 2, [], false));
```

resta invariata (`[]` si adatta al nuovo tipo dell'elemento da sola).

Riga 229:

```csharp
        conn.MessaggioDuranteInvio = new RevealStepMessage(0, 2, ["Il cadavere"], false);
```

diventa:

```csharp
        conn.MessaggioDuranteInvio = new RevealStepMessage(0, 2, [Rivelata("Il cadavere")], false);
```

Righe 293 e 299:

```csharp
        conn.Emit(new RevealStepMessage(0, 2, ["Il cadavere"], false));
        Assert.Equal("Rivela la prossima parola", vm.RevealButtonLabel);

        await vm.AdvanceRevealCommand.ExecuteAsync(null);
        Assert.Contains("AdvanceReveal(ABCD)", conn.Calls);

        conn.Emit(new RevealStepMessage(0, 2, ["Il cadavere", "squisito"], true));
```

diventano:

```csharp
        conn.Emit(new RevealStepMessage(0, 2, [Rivelata("Il cadavere")], false));
        Assert.Equal("Rivela la prossima parola", vm.RevealButtonLabel);

        await vm.AdvanceRevealCommand.ExecuteAsync(null);
        Assert.Contains("AdvanceReveal(ABCD)", conn.Calls);

        conn.Emit(new RevealStepMessage(0, 2, [Rivelata("Il cadavere"), Rivelata("squisito")], true));
```

Riga 311:

```csharp
        conn.Emit(new RevealStepMessage(1, 3, ["berrà"], false));
```

diventa:

```csharp
        conn.Emit(new RevealStepMessage(1, 3, [Rivelata("berrà")], false));
```

Righe 397 e 403:

```csharp
        conn.Emit(new RevealStepMessage(0, 3, ["Il cadavere"], false));
        Assert.Equal(ScreenState.Reveal, vm.Screen);

        conn.Emit(new ErrorMessage("TIMEOUT", "Richiesta scaduta, riprova."));
        Assert.False(string.IsNullOrEmpty(vm.ErrorText));

        conn.Emit(new RevealStepMessage(0, 3, ["Il cadavere", "squisito"], false));
```

diventano:

```csharp
        conn.Emit(new RevealStepMessage(0, 3, [Rivelata("Il cadavere")], false));
        Assert.Equal(ScreenState.Reveal, vm.Screen);

        conn.Emit(new ErrorMessage("TIMEOUT", "Richiesta scaduta, riprova."));
        Assert.False(string.IsNullOrEmpty(vm.ErrorText));

        conn.Emit(new RevealStepMessage(0, 3, [Rivelata("Il cadavere"), Rivelata("squisito")], false));
```

Riga 1185:

```csharp
        conn.Emit(new RevealStepMessage(0, 1, ["Il cadavere"], true));
```

diventa:

```csharp
        conn.Emit(new RevealStepMessage(0, 1, [Rivelata("Il cadavere")], true));
```

E, nello stesso test (`TornareAllaLobbySvuotaLeCollezioniDellaPartitaConclusa`, righe 1193 e 1202):

```csharp
        Assert.NotEmpty(vm.RevealSlots);
```

diventa

```csharp
        Assert.NotEmpty(vm.RevealFragments);
```

e

```csharp
        Assert.Empty(vm.RevealSlots);
```

diventa

```csharp
        Assert.Empty(vm.RevealFragments);
```

- [ ] **Passo 2: Eseguire i test e verificare che falliscano**

Run: `dotnet test tests/FrasiSquisite.App.Tests --filter "FullyQualifiedName~GameSessionViewModelTests"`
Expected: FAIL — `RevealSlots`/`RevealSlotView` non esistono più nel tipo atteso, `RevealFragments` non esiste ancora sul ViewModel (errori di compilazione).

- [ ] **Passo 3: Rinominare e riscrivere `RevealSlotView`**

```bash
git mv src/FrasiSquisite.App/ViewModels/RevealSlotView.cs src/FrasiSquisite.App/ViewModels/RevealFragmentView.cs
```

Sostituire il contenuto di `src/FrasiSquisite.App/ViewModels/RevealFragmentView.cs`:

```csharp
namespace FrasiSquisite.App.ViewModels;

/// <summary>
/// Un pezzo della frase nella schermata di reveal, 1:1 col
/// <c>RevealFragment</c> mandato dal server: testo fisso del template
/// (nessun riquadro, sempre visibile) o una casella (riquadro pieno se già
/// scoperta, tratteggiato con "···" se non ancora). Le tre proprietà
/// computate sono quelle che la UI usa per scegliere il ramo di rendering,
/// senza binding multipli nel markup (backlog #1).
/// </summary>
public sealed record RevealFragmentView(bool IsSlot, string Text, bool IsRevealed)
{
    public bool IsLiteral => !IsSlot;

    public bool ShowAsRevealedSlot => IsSlot && IsRevealed;

    public bool ShowAsCoveredSlot => IsSlot && !IsRevealed;
}
```

- [ ] **Passo 4: Aggiornare `GameSessionViewModel`**

In `src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs`, sostituire il blocco (righe 234-239):

```csharp
    /// <summary>
    /// Lunga sempre <see cref="SlotCount"/>: le caselle non ancora scoperte ci
    /// sono già, come segnaposto "···" (<see cref="RevealSlotView.IsRevealed"/>
    /// false), invece di apparire solo quando arrivano.
    /// </summary>
    public ObservableCollection<RevealSlotView> RevealSlots { get; } = [];
```

con:

```csharp
    /// <summary>
    /// Riflette 1:1 i frammenti mandati dal server per il passo di reveal
    /// corrente: testo fisso del template e caselle, scoperte o coperte con
    /// "···" (<see cref="RevealFragmentView.IsRevealed"/> false). Il
    /// riempimento delle caselle non ancora arrivate lo fa già il server:
    /// qui non c'è più bisogno di completare la lista con <c>SlotCount</c>
    /// (backlog #1).
    /// </summary>
    public ObservableCollection<RevealFragmentView> RevealFragments { get; } = [];
```

Sostituire il blocco (righe 724-745):

```csharp
            case RevealStepMessage passo:
                PhraseNumber = passo.PhraseIndex + 1;
                TotalPhrases = passo.TotalPhrases;

                // SlotCount arriva con la RoomStateMessage che precede sempre
                // il reveal nel flusso reale; il fallback alle sole caselle
                // ricevute copre solo il caso (di solo test) in cui non sia
                // ancora nota, senza inventare segnaposto in più.
                var totaleCaselle = SlotCount > 0 ? SlotCount : passo.RevealedSlots.Count;
                RevealSlots.Clear();
                for (var i = 0; i < totaleCaselle; i++)
                {
                    RevealSlots.Add(i < passo.RevealedSlots.Count
                        ? new RevealSlotView(passo.RevealedSlots[i], true)
                        : new RevealSlotView("···", false));
                }

                _fraseCompleta = passo.PhraseComplete;
                AggiornaEtichettaRevealButton();

                Screen = ScreenState.Reveal;
                break;
```

con:

```csharp
            case RevealStepMessage passo:
                PhraseNumber = passo.PhraseIndex + 1;
                TotalPhrases = passo.TotalPhrases;

                RevealFragments.Clear();
                foreach (var frammento in passo.Fragments)
                {
                    RevealFragments.Add(frammento.IsSlot
                        ? new RevealFragmentView(true, frammento.IsRevealed ? frammento.Text : "···", frammento.IsRevealed)
                        : new RevealFragmentView(false, frammento.Text, true));
                }

                _fraseCompleta = passo.PhraseComplete;
                AggiornaEtichettaRevealButton();

                Screen = ScreenState.Reveal;
                break;
```

Sostituire (riga 848):

```csharp
        RevealSlots.Clear();
```

con:

```csharp
        RevealFragments.Clear();
```

- [ ] **Passo 5: Eseguire i test e verificare che passino**

Run: `dotnet test tests/FrasiSquisite.App.Tests --filter "FullyQualifiedName~GameSessionViewModelTests"`
Expected: PASS.

Run l'intera suite App: `dotnet test tests/FrasiSquisite.App.Tests`
Expected: PASS — 104 test verdi (1 rinominato, nessun conteggio netto nuovo).

Run l'intera soluzione, per la prima volta di nuovo verde dal Task 2: `dotnet test FrasiSquisite.slnx`
Expected: PASS — 797 test verdi in tutto (Shared 82, Domain 510, App 104, Server 101), "Non superati: 0" su tutti e quattro i progetti.

- [ ] **Passo 6: Commit del codice testato**

```bash
git add src/FrasiSquisite.App/ViewModels/RevealFragmentView.cs \
        src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs \
        tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs
git commit -m "refactor(reveal): il client rende i frammenti del reveal, RevealSlotView diventa RevealFragmentView"
```

- [ ] **Passo 7: Aggiornare il markup del reveal (nessun test automatico — vedi Vincoli globali)**

In `src/FrasiSquisite.App/Pages/GamePage.xaml`, sostituire il blocco (righe 360-381):

```xml
                <FlexLayout Wrap="Wrap" JustifyContent="Center" AlignItems="Center"
                            BindableLayout.ItemsSource="{Binding RevealSlots}">
                    <BindableLayout.ItemTemplate>
                        <DataTemplate x:DataType="vm:RevealSlotView">
                            <!-- Due Border alternativi (invece di uno con trigger)
                                 perché "coperta" ha un bordo tratteggiato e
                                 "rivelata" uno pieno: sovrascrivere lo
                                 StrokeDashArray a runtime è più fragile che
                                 scegliere fra due stili già corretti. -->
                            <Grid>
                                <Border Margin="4" Style="{StaticResource RevealSlotRevealedFrame}"
                                        IsVisible="{Binding IsRevealed}">
                                    <Label Text="{Binding Text}" Style="{StaticResource RevealSlotRevealedText}" />
                                </Border>
                                <Border Margin="4" Style="{StaticResource RevealSlotCoveredFrame}"
                                        IsVisible="{Binding IsRevealed, Converter={StaticResource InvertBool}}">
                                    <Label Text="{Binding Text}" Style="{StaticResource RevealSlotCoveredText}" />
                                </Border>
                            </Grid>
                        </DataTemplate>
                    </BindableLayout.ItemTemplate>
                </FlexLayout>
```

con:

```xml
                <FlexLayout Wrap="Wrap" JustifyContent="Center" AlignItems="Center"
                            BindableLayout.ItemsSource="{Binding RevealFragments}">
                    <BindableLayout.ItemTemplate>
                        <DataTemplate x:DataType="vm:RevealFragmentView">
                            <!-- Tre rami alternativi (invece di trigger sullo
                                 stesso elemento): il testo fisso del template
                                 non ha bordo, "coperta" ha un bordo
                                 tratteggiato e "rivelata" uno pieno —
                                 sovrascrivere lo StrokeDashArray a runtime è
                                 più fragile che scegliere fra rami già
                                 corretti (backlog #1). -->
                            <Grid>
                                <Label Text="{Binding Text}" Style="{StaticResource RevealLiteralText}"
                                       IsVisible="{Binding IsLiteral}" VerticalOptions="Center" />
                                <Border Margin="4" Style="{StaticResource RevealSlotRevealedFrame}"
                                        IsVisible="{Binding ShowAsRevealedSlot}">
                                    <Label Text="{Binding Text}" Style="{StaticResource RevealSlotRevealedText}" />
                                </Border>
                                <Border Margin="4" Style="{StaticResource RevealSlotCoveredFrame}"
                                        IsVisible="{Binding ShowAsCoveredSlot}">
                                    <Label Text="{Binding Text}" Style="{StaticResource RevealSlotCoveredText}" />
                                </Border>
                            </Grid>
                        </DataTemplate>
                    </BindableLayout.ItemTemplate>
                </FlexLayout>
```

- [ ] **Passo 8: Aggiungere lo stile del testo fisso e correggere il taglio delle caselle lunghe**

In `src/FrasiSquisite.App/Resources/Styles/Styles.xaml`, sostituire il blocco (righe 651-679):

```xml
    <!-- Caselle del reveal: "rivelata" (scoperta) e "coperta" (ancora ···). -->
    <Style x:Key="RevealSlotRevealedFrame" TargetType="Border">
        <Setter Property="BackgroundColor" Value="{DynamicResource ThemeSurface}" />
        <Setter Property="Stroke" Value="{DynamicResource ThemeInk15Brush}" />
        <Setter Property="StrokeThickness" Value="1" />
        <Setter Property="StrokeShape" Value="{DynamicResource ThemeRadiusSoftShape}" />
        <Setter Property="Shadow" Value="{DynamicResource CardShadow}" />
        <Setter Property="Padding" Value="14,10" />
    </Style>

    <Style x:Key="RevealSlotRevealedText" TargetType="Label">
        <Setter Property="FontFamily" Value="{DynamicResource ThemeHeadFont}" />
        <Setter Property="FontSize" Value="19" />
        <Setter Property="TextColor" Value="{DynamicResource ThemeInk}" />
    </Style>

    <Style x:Key="RevealSlotCoveredFrame" TargetType="Border">
        <Setter Property="BackgroundColor" Value="{DynamicResource ThemeSurfaceAlt}" />
        <Setter Property="Stroke" Value="{DynamicResource ThemeInk33Brush}" />
        <Setter Property="StrokeThickness" Value="1" />
        <Setter Property="StrokeDashArray" Value="4,4" />
        <Setter Property="StrokeShape" Value="{DynamicResource ThemeRadiusSoftShape}" />
        <Setter Property="Padding" Value="14,10" />
    </Style>

    <Style x:Key="RevealSlotCoveredText" TargetType="Label">
        <Setter Property="FontSize" Value="19" />
        <Setter Property="TextColor" Value="{DynamicResource ThemeInkMuted}" />
    </Style>

</ResourceDictionary>
```

con:

```xml
    <!-- Caselle del reveal: "rivelata" (scoperta) e "coperta" (ancora ···).
         MaximumWidthRequest + LineBreakMode esplicito: FlexLayout Wrap non
         vincola la larghezza dei figli durante la misurazione, quindi senza
         un tetto una Label con una frase intera in una sola casella viene
         tagliata invece di andare a capo (backlog #1). -->
    <Style x:Key="RevealSlotRevealedFrame" TargetType="Border">
        <Setter Property="BackgroundColor" Value="{DynamicResource ThemeSurface}" />
        <Setter Property="Stroke" Value="{DynamicResource ThemeInk15Brush}" />
        <Setter Property="StrokeThickness" Value="1" />
        <Setter Property="StrokeShape" Value="{DynamicResource ThemeRadiusSoftShape}" />
        <Setter Property="Shadow" Value="{DynamicResource CardShadow}" />
        <Setter Property="Padding" Value="14,10" />
    </Style>

    <Style x:Key="RevealSlotRevealedText" TargetType="Label">
        <Setter Property="FontFamily" Value="{DynamicResource ThemeHeadFont}" />
        <Setter Property="FontSize" Value="19" />
        <Setter Property="TextColor" Value="{DynamicResource ThemeInk}" />
        <Setter Property="LineBreakMode" Value="WordWrap" />
        <Setter Property="MaximumWidthRequest" Value="320" />
    </Style>

    <Style x:Key="RevealSlotCoveredFrame" TargetType="Border">
        <Setter Property="BackgroundColor" Value="{DynamicResource ThemeSurfaceAlt}" />
        <Setter Property="Stroke" Value="{DynamicResource ThemeInk33Brush}" />
        <Setter Property="StrokeThickness" Value="1" />
        <Setter Property="StrokeDashArray" Value="4,4" />
        <Setter Property="StrokeShape" Value="{DynamicResource ThemeRadiusSoftShape}" />
        <Setter Property="Padding" Value="14,10" />
    </Style>

    <Style x:Key="RevealSlotCoveredText" TargetType="Label">
        <Setter Property="FontSize" Value="19" />
        <Setter Property="TextColor" Value="{DynamicResource ThemeInkMuted}" />
        <Setter Property="LineBreakMode" Value="WordWrap" />
        <Setter Property="MaximumWidthRequest" Value="320" />
    </Style>

    <!-- Tessuto connettivo fisso del template nel reveal, senza riquadro:
         mai bordato, per non farlo scambiare per una casella (backlog #1). -->
    <Style x:Key="RevealLiteralText" TargetType="Label">
        <Setter Property="FontFamily" Value="{DynamicResource ThemeHeadFont}" />
        <Setter Property="FontSize" Value="19" />
        <Setter Property="TextColor" Value="{DynamicResource ThemeInkMuted}" />
        <Setter Property="LineBreakMode" Value="WordWrap" />
    </Style>

</ResourceDictionary>
```

- [ ] **Passo 9: Verifica manuale**

Nessun test automatico copre `.xaml`/markup in questo repository (vedi Vincoli globali) — questo passo è manuale e va segnalato come tale, non spacciato per una prova automatizzata.

Run: `dotnet build src/FrasiSquisite.App -f net10.0-android` (o la piattaforma disponibile in locale)
Expected: build senza errori.

Avviare l'app, creare una stanza con lo schema di default ("Storia in otto atti"), giocare una partita completa con almeno due giocatori (anche con dei bot, backlog voce 4 non ancora fatta quindi con `StaticWordPool`) fino al reveal, e verificare a occhio:

1. Alla prima schermata di reveal compaiono già le virgolette e la punteggiatura del template (es. `dicendo: «`), non solo le caselle.
2. A ogni tocco di "Rivela la prossima parola" una casella in più mostra il proprio testo, il tessuto connettivo resta sempre visibile.
3. Nessuna casella non ancora scoperta mostra il proprio testo in anticipo.
4. Una casella con una frase lunga (es. "Non è colpa mia, io ho solo firmato") va a capo dentro il proprio riquadro invece di essere tagliata.

- [ ] **Passo 10: Commit**

```bash
git add src/FrasiSquisite.App/Pages/GamePage.xaml src/FrasiSquisite.App/Resources/Styles/Styles.xaml
git commit -m "fix(reveal): riquadri senza bordo per il testo fisso, le caselle lunghe vanno a capo"
```

---

### Task 4: Chiudere la voce nel backlog

**File:**
- Modificare: `docs/superpowers/backlog.md`

- [ ] **Passo 1: Aggiornare il backlog**

In `docs/superpowers/backlog.md`, rimuovere la voce 1 (righe 11-41, dal titolo `## 1. Il reveal si legge a scatti` fino al separatore `---` che la chiude) e rinumerare le voci restanti: `## 2.` → `## 1.`, `## 3.` → `## 2.`, `## 4.` → `## 3.`, `## 5.` → `## 4.`.

- [ ] **Passo 2: Commit**

```bash
git add docs/superpowers/backlog.md
git commit -m "docs: backlog, voce 1 risolta (reveal col tessuto connettivo del template)"
```
