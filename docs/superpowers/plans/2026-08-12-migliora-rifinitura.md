# Migliora la rifinitura — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** correggere il timeout della rifinitura AI (scade sistematicamente a 10s), permettere all'AI di aggiustare la forma delle parole per la concordanza, e passare il ruolo grammaticale di ogni casella al modello.

**Architecture:** tre correzioni indipendenti sulla fase di rifinitura esistente (`GameEngine.Refining.cs` → `RequestRefinement` → `RefinementRunner` → `RefinementGuard`), nessuna nuova fase, nessun cambiamento al protocollo.

**Tech Stack:** .NET 10, xUnit, nessuna dipendenza nuova.

## Global Constraints

- Timeout della rifinitura proporzionale al numero di frasi: base 15s +
  3s per ogni frase oltre la prima, tetto a 30s (spec §3.1).
- `RefinementGuard` non verifica più che la casella rifinita contenga
  alla lettera il testo grezzo — scelta deliberata dell'utente,
  informata del rischio, in scostamento dal principio "la garanzia sta
  nel codice non nel prompt" che vale per il resto della rifinitura
  (spec §3.2, §1). Le altre tre guardie (non vuoto, non oltre 200
  caratteri, non ripete il letterale del template) restano invariate.
- Il prompt di sistema permette esplicitamente di aggiustare la forma
  delle parole (plurale, genere, coniugazione) ma non di sostituirle o
  aggiungere idee nuove (spec §3.2).
- `RequestRefinement` porta anche `Ruoli`: un elenco di ruoli
  grammaticali, uno per schema, condiviso da tutte le frasi — non
  ripetuto per ognuna (spec §3.3).
- Nessun cambiamento al protocollo client-server, nessuna nuova fase di
  gioco, nessuna nuova chiamata AI (spec §1).
- Lingua italiana in codice, commenti e messaggi di commit; commit
  firmati GPG.
- Baseline attuale (verificata con `dotnet test` prima di questo
  piano): **844 test, 0 falliti** (Shared 86, App 126, Domain 520,
  Server 112). Il flake noto e documentato di `GameHubTests` (backlog
  #2) può comparire su run dell'intera suite: verificare sempre in
  isolamento prima di considerarlo una regressione.

---

### Task 1: Timeout scalabile, guardia sulla parola rimossa, ruolo grammaticale nel prompt

**Files:**
- Modify: `src/FrasiSquisite.Server/Ai/AiOptions.cs` (nuovi campi)
- Modify: `src/FrasiSquisite.Domain/Refinement/RefinementGuard.cs` (rimuove il controllo di contenimento)
- Modify: `src/FrasiSquisite.Domain/Engine/Effect.cs` (`RequestRefinement` guadagna `Ruoli`)
- Modify: `src/FrasiSquisite.Domain/Engine/GameEngine.Refining.cs:26` (popola `Ruoli`)
- Modify: `src/FrasiSquisite.Server/Ai/RefinementRunner.cs` (nuovo parametro, prompt aggiornato, timeout dinamico)
- Modify: `src/FrasiSquisite.Server/Realtime/GameHost.cs:156` (passa `richiesta.Ruoli`)
- Modify: `src/FrasiSquisite.Server/Program.cs` (commenti che citavano "10 secondi" fissi — nessun cambiamento di logica)
- Test: `tests/FrasiSquisite.Domain.Tests/Refinement/RefinementGuardTests.cs`
- Test: `tests/FrasiSquisite.Domain.Tests/Engine/RifinituraTests.cs`
- Test: `tests/FrasiSquisite.Server.Tests/Ai/RefinementRunnerTests.cs`
- Test: `tests/FrasiSquisite.Server.Tests/Realtime/GameHostTests.cs`

**Interfaces:**
- Produce: `AiOptions.TimeoutSecondiPerFraseAggiuntiva` (`int`, default 3), `AiOptions.TimeoutMassimoSecondi` (`int`, default 30); `Effect.RequestRefinement(IReadOnlyList<IReadOnlyList<string>> Frasi, string Template, IReadOnlyList<string> Ruoli)`; `RefinementRunner.RifinisciAsync(IReadOnlyList<IReadOnlyList<string>> frasi, string template, IReadOnlyList<string> ruoli, CancellationToken ct)`.
- Consuma: `Schema.Caselle` e `Casella.Ruolo` (già esistenti, `FrasiSquisite.Shared.Schemas`).

- [ ] **Step 1: Scrivi i test che falliscono per la guardia senza controllo di contenimento**

In `tests/FrasiSquisite.Domain.Tests/Refinement/RefinementGuardTests.cs`, trova questi due test:

```csharp
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
```

Sostituiscili con:

```csharp
    /// <summary>
    /// La guardia non verifica più che le parole del giocatore ricompaiano
    /// alla lettera nella casella rifinita (design 2026-08-12 "migliora la
    /// rifinitura", §3.2): per permettere concordanza di genere/numero e
    /// coniugazione, non c'è più un modo puramente sintattico di distinguere
    /// un aggiustamento di forma da una riscrittura completa. La fedeltà
    /// del contenuto resta affidata al prompt, non più al codice - scelta
    /// dell'utente, consapevole del rischio.
    /// </summary>
    [Fact]
    public void UnaCasellaCompletamenteRiscrittaVieneOraAccettata()
    {
        var esito = RefinementGuard.Applica(
            ["il cadavere squisito", "la mamma"],
            ["il defunto elegante", "con la mamma"],
            Semplice);

        Assert.Equal(["il defunto elegante", "con la mamma"], esito);
    }

    /// <summary>
    /// Prova diretta del punto centrale di questo cambiamento (design
    /// 2026-08-12 §3.2): un aggiustamento della forma della parola per
    /// farla concordare (qui, plurale) passa la guardia, cosa impossibile
    /// prima con il controllo di contenimento letterale.
    /// </summary>
    [Fact]
    public void UnaParolaConFormaDiversaPerConcordanzaVieneAccettata()
    {
        var esito = RefinementGuard.Applica(["montagna"], ["su alcune montagne"], "{0}");

        Assert.Equal(["su alcune montagne"], esito);
    }
```

- [ ] **Step 2: Esegui i test e verifica che il primo fallisca**

Run: `dotnet test tests/FrasiSquisite.Domain.Tests --filter "FullyQualifiedName~UnaCasellaCompletamenteRiscrittaVieneOraAccettata"`
Expected: FAIL — la guardia attuale scarta ancora "il defunto elegante" perché non contiene "il cadavere squisito".

- [ ] **Step 3: Rimuovi il controllo di contenimento dalla guardia**

Sostituisci l'intero contenuto di `src/FrasiSquisite.Domain/Refinement/RefinementGuard.cs` con:

```csharp
using System.Text.RegularExpressions;

namespace FrasiSquisite.Domain.Refinement;

/// <summary>
/// Decide, casella per casella, se fidarsi di quello che il modello ha
/// restituito. Protegge da risposte rotte - vuote, troppo lunghe, che
/// ripetono il template - non dalla fedeltà delle singole parole: quella
/// e' affidata al prompt (design 2026-08-12 "migliora la rifinitura",
/// §3.2 - scelta deliberata, non un compromesso implicito).
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
            esito[i] = Accettabile(rifinite[i], precedenti[i]) ? rifinite[i] : grezze[i];
        }

        return esito;
    }

    private static bool Accettabile(string rifinita, string precedente)
    {
        if (string.IsNullOrWhiteSpace(rifinita) || rifinita.Length > MaxCaratteri)
        {
            return false;
        }

        var r = Normalizza(rifinita);

        // Non puo' ripetere cio' che il template gli mette gia' davanti.
        return string.IsNullOrEmpty(precedente)
            || !r.StartsWith(Normalizza(precedente), StringComparison.Ordinal);
    }

    private static string Normalizza(string testo) =>
        SpaziMultipli().Replace(testo.Trim(), " ").ToLowerInvariant();

    /// <summary>
    /// Per ogni segnaposto, il testo fisso che lo precede nel template. Per
    /// "{6}», ed è andata a finire che {7}." il testo grezzo prima di 7 e'
    /// "», ed è andata a finire che", ma quella punteggiatura di contorno
    /// (virgolette, virgole) appartiene alla chiusura del segnaposto
    /// precedente: il modello non la ripeterebbe comunque, quindi il
    /// confronto parte dalla prima vera parola, "ed è andata a finire che".
    /// Cosi' ci si accorge che il modello lo sta ripetendo, perche' il
    /// confronto e' su come INIZIA la casella rifinita.
    ///
    /// La stessa logica vale in coda: per "dicendo: «{5}»" il testo grezzo
    /// prima di 5 e' "dicendo: «", ma quella virgoletta di apertura non la
    /// scrive mai nessun modello (a nessuno si chiede di restituire le
    /// virgolette). Senza toglierla il confronto "come inizia" non
    /// scatterebbe mai per questa casella: si taglia anche la punteggiatura
    /// finale, fermandosi all'ultima vera parola, "dicendo".
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

            var grezzo = precedente >= 0 && precedente < posizione
                ? prima[(precedente + 1)..]
                : prima;

            var senzaTesta = PunteggiaturaIniziale().Replace(grezzo.Trim(), string.Empty);
            esito[i] = PunteggiaturaFinale().Replace(senzaTesta, string.Empty);
        }

        return esito;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaziMultipli();

    /// <summary>
    /// Punteggiatura e spazi in testa a una stringa: quella che introduce un
    /// segnaposto (virgolette, virgole, spazi) e che il modello non ripete
    /// mai insieme al resto del testo letterale.
    /// </summary>
    [GeneratedRegex(@"^[^\p{L}\p{N}]+")]
    private static partial Regex PunteggiaturaIniziale();

    /// <summary>
    /// Punteggiatura e spazi in coda a una stringa: quella che introduce il
    /// segnaposto successivo (i due punti, la virgoletta di apertura) e che
    /// il modello non ripete mai insieme al resto del testo letterale. Si
    /// ferma alla prima parola vera incontrata andando a ritroso, quindi non
    /// puo' mai erodere una parola del template.
    /// </summary>
    [GeneratedRegex(@"[^\p{L}\p{N}]+$")]
    private static partial Regex PunteggiaturaFinale();
}
```

- [ ] **Step 4: Esegui tutta la suite di `RefinementGuardTests` e verifica che passi**

Run: `dotnet test tests/FrasiSquisite.Domain.Tests --filter "FullyQualifiedName~RefinementGuardTests"`
Expected: PASS — 11/11 (9 test invariati + 2 nuovi che sostituiscono i due rimossi).

- [ ] **Step 5: Aggiungi i nuovi campi ad `AiOptions`**

In `src/FrasiSquisite.Server/Ai/AiOptions.cs`, trova:

```csharp
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
```

Sostituiscilo con:

```csharp
    public string BaseUrl { get; set; } = "https://api.ppq.ai";

    /// <summary>
    /// Mai in appsettings.json, che finisce in git: arriva come variabile
    /// d'ambiente (Ai__ApiKey) dal file .env del container.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    public string TextModel { get; set; } = "glm-5.2";

    /// <summary>
    /// Base del tempo concesso alla rifinitura, prima di procedere con le
    /// caselle grezze. Non e' un'ottimizzazione: e' cio' che impedisce a una
    /// partita di restare appesa (spec §4.4). Cresce con
    /// <see cref="TimeoutSecondiPerFraseAggiuntiva"/> fino al tetto di
    /// <see cref="TimeoutMassimoSecondi"/> (design 2026-08-12 "migliora la
    /// rifinitura", §3.1): un valore fisso a 10s scadeva sistematicamente,
    /// anche con poche frasi.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// La rifinitura e' un'unica chiamata per tutte le frasi della partita:
    /// piu' giocatori, piu' caselle da rifinire nello stesso giro. Ogni
    /// frase oltre la prima allunga il tempo concesso di questi secondi.
    /// </summary>
    public int TimeoutSecondiPerFraseAggiuntiva { get; set; } = 3;

    /// <summary>
    /// Tetto oltre il quale il tempo concesso non cresce piu', anche con
    /// molte frasi: una partita numerosa non deve far aspettare tutti quasi
    /// mezzo minuto in piu' di quanto gia' previsto.
    /// </summary>
    public int TimeoutMassimoSecondi { get; set; } = 30;
```

- [ ] **Step 6: Aggiungi `Ruoli` a `RequestRefinement`**

In `src/FrasiSquisite.Domain/Engine/Effect.cs`, trova:

```csharp
public sealed record RequestRefinement(
    IReadOnlyList<IReadOnlyList<string>> Frasi,
    string Template) : Effect;
```

Sostituiscilo con:

```csharp
public sealed record RequestRefinement(
    IReadOnlyList<IReadOnlyList<string>> Frasi,
    string Template,
    IReadOnlyList<string> Ruoli) : Effect;
```

- [ ] **Step 7: Popola `Ruoli` in `GameEngine.Refining.cs`**

In `src/FrasiSquisite.Domain/Engine/GameEngine.Refining.cs`, trova:

```csharp
        effetti.Add(new BroadcastToRoom(RoomState(rifinendo)));
        effetti.Add(new RequestRefinement(frasi, rifinendo.Schema.Template));
```

Sostituiscilo con:

```csharp
        effetti.Add(new BroadcastToRoom(RoomState(rifinendo)));
        effetti.Add(new RequestRefinement(
            frasi,
            rifinendo.Schema.Template,
            [.. rifinendo.Schema.Caselle.Select(c => c.Ruolo)]));
```

- [ ] **Step 8: Aggiorna `RifinituraTests.cs` — la richiesta porta anche i ruoli**

In `tests/FrasiSquisite.Domain.Tests/Engine/RifinituraTests.cs`, trova:

```csharp
    [Fact]
    public void EntrandoInRefiningVieneChiestaLaRifinitura()
    {
        var (_, ultimo) = ScritturaConclusa();

        var richiesta = Assert.Single(ultimo.Effects.OfType<RequestRefinement>());

        Assert.Equal(N, richiesta.Frasi.Count);
        Assert.All(richiesta.Frasi, f => Assert.Equal(K, f.Count));
        Assert.False(string.IsNullOrWhiteSpace(richiesta.Template));
    }
```

Sostituiscilo con:

```csharp
    [Fact]
    public void EntrandoInRefiningVieneChiestaLaRifinitura()
    {
        var (_, ultimo) = ScritturaConclusa();

        var richiesta = Assert.Single(ultimo.Effects.OfType<RequestRefinement>());

        Assert.Equal(N, richiesta.Frasi.Count);
        Assert.All(richiesta.Frasi, f => Assert.Equal(K, f.Count));
        Assert.False(string.IsNullOrWhiteSpace(richiesta.Template));
        Assert.Equal(["Ruolo0", "Ruolo1"], richiesta.Ruoli);
    }
```

(`TestSchemas.WithSlots(K)`, usato da `ScritturaConclusa`, genera caselle
con ruolo `"Ruolo{i}"` — vedi `tests/FrasiSquisite.Domain.Tests/TestSchemas.cs`.)

Poi, nello stesso file, trova:

```csharp
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
        Assert.Equal("con p11", risultato.State.Phrases[0].Slots[1]!.Text);
    }
```

Sostituiscilo con:

```csharp
    /// <summary>
    /// La guardia non confronta piu' il testo rifinito con quello grezzo
    /// (design 2026-08-12 "migliora la rifinitura", §3.2): una riscrittura
    /// completa passa quanto un aggiustamento delicato, perche' il codice
    /// non ha piu' modo di distinguerli. La fedeltà del contenuto resta
    /// affidata al prompt, non piu' a RefinementGuard.
    /// </summary>
    [Fact]
    public void UnaCasellaRiscrittaDalModelloVieneOraAccettata()
    {
        var (stato, _) = ScritturaConclusa();

        var rifinite = stato.Phrases
            .Select((f, i) => (IReadOnlyList<string>)[.. f.Slots.Select((s, j) =>
                i == 0 && j == 0 ? "tutt'altro" : "con " + s!.Text)])
            .ToList();

        var risultato = _motore.Handle(stato, new RefinementFinished(rifinite));

        Assert.Equal("tutt'altro", risultato.State.Phrases[0].Slots[0]!.Text);
        Assert.Equal("con p11", risultato.State.Phrases[0].Slots[1]!.Text);
    }
```

- [ ] **Step 9: Esegui tutta la suite di `Domain.Tests` e verifica che passi**

Run: `dotnet test tests/FrasiSquisite.Domain.Tests`
Expected: PASS — 520/520 (nessun test nuovo qui: un test sostituito 1:1 in
`RifinituraTests`, due sostituiti 1:1 in `RefinementGuardTests`, contati
già al passo 4).

- [ ] **Step 10: Aggiorna `RefinementRunner` — parametro `ruoli`, timeout dinamico, prompt**

Sostituisci l'intero contenuto di `src/FrasiSquisite.Server/Ai/RefinementRunner.cs` con:

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
        - Le parole scelte dai giocatori restano le stesse: puoi aggiustarne
          delicatamente la forma — plurale, genere, coniugazione — per farle
          concordare con il resto della frase. Non sostituirle con parole
          diverse, non cambiarne il significato, non aggiungere idee nuove.
        - Non riordinare le caselle e non spostarne il contenuto.
        - Se una casella si legge già bene, restituiscila identica.
        - Il template della frase contiene già del testo fisso: non ripeterlo,
          e non ripeterne nemmeno il senso.
        - Il campo "ruoli" dice la funzione grammaticale di ogni casella nella
          frase (es. "Con chi?", "Dove?"), nello stesso ordine delle caselle:
          usalo per scegliere la preposizione o l'accordo giusto, non per
          cambiare cosa la casella dice.

        L'assurdo è voluto. Non renderlo sensato: rendilo leggibile.

        Rispondi solo con JSON, senza commenti e senza blocchi di codice:
        {"frasi": [{"caselle": ["...", "..."]}, ...]}
        Tante frasi quante ne ricevi, tante caselle quante ne ha ciascuna,
        nello stesso ordine.
        """;

    public async Task<IReadOnlyList<IReadOnlyList<string>>?> RifinisciAsync(
        IReadOnlyList<IReadOnlyList<string>> frasi,
        string template,
        IReadOnlyList<string> ruoli,
        CancellationToken ct)
    {
        // Non e' un doppione del c.Timeout impostato in Program.cs sull'
        // HttpClient di OpenAiCompatibleTextProvider: quello limita solo la
        // richiesta HTTP di QUELLA implementazione. Questo qui e' il limite a
        // livello di contratto sull'intera operazione "IAiTextProvider.
        // CompletaAsync", valido per qualunque implementazione dietro
        // l'interfaccia, presente o futura - ed e' cio' che permette di
        // provare il timeout (vedi OltreIlTimeoutSiRestituisceNull) con un
        // doppio finto, senza rete. Il valore qui cresce con il numero di
        // frasi (design 2026-08-12 "migliora la rifinitura", §3.1): una
        // rifinitura batch per tutta la partita costa di piu' con piu'
        // giocatori, e un tetto fisso a 10s scadeva sistematicamente anche
        // con poche frasi. TimeoutMassimoSecondi resta cio' che impedisce a
        // una partita numerosa di aspettare troppo.
        var secondi = Math.Min(
            _opzioni.TimeoutMassimoSecondi,
            _opzioni.TimeoutSeconds + _opzioni.TimeoutSecondiPerFraseAggiuntiva * Math.Max(0, frasi.Count - 1));

        using var scadenza = CancellationTokenSource.CreateLinkedTokenSource(ct);
        scadenza.CancelAfter(TimeSpan.FromSeconds(secondi));

        try
        {
            var utente = JsonSerializer.Serialize(new
            {
                template,
                ruoli,
                frasi = frasi.Select(f => new { caselle = f }),
            });

            var risposta = await ai.CompletaAsync(Sistema, utente, scadenza.Token);

            return risposta is null ? null : Leggi(risposta, frasi.Count);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Rifinitura scaduta dopo {Secondi}s: si prosegue con le caselle grezze.", secondi);
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

- [ ] **Step 11: Aggiorna `RefinementRunnerTests.cs` — nuovo parametro e nuovi test sul timeout**

Sostituisci l'intero contenuto di `tests/FrasiSquisite.Server.Tests/Ai/RefinementRunnerTests.cs` con:

```csharp
using FrasiSquisite.Server.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FrasiSquisite.Server.Tests.Ai;

public class RefinementRunnerTests
{
    private const string Template = "{0} {1}";

    private static readonly string[] Ruoli = ["Soggetto", "Predicato"];

    private static RefinementRunner Crea(
        FakeAiTextProvider ai,
        int timeoutSecondi = 15,
        int timeoutSecondiPerFraseAggiuntiva = 3,
        int timeoutMassimoSecondi = 30) =>
        new(ai, Options.Create(new AiOptions
        {
            TimeoutSeconds = timeoutSecondi,
            TimeoutSecondiPerFraseAggiuntiva = timeoutSecondiPerFraseAggiuntiva,
            TimeoutMassimoSecondi = timeoutMassimoSecondi,
        }), NullLogger<RefinementRunner>.Instance);

    [Fact]
    public async Task UnaRispostaBenFormataDiventaCaselleRifinite()
    {
        var ai = new FakeAiTextProvider
        {
            Risposta = """{"frasi": [{"caselle": ["la nonna", "con la mamma"]}]}""",
        };

        var esito = await Crea(ai).RifinisciAsync([["la nonna", "la mamma"]], Template, Ruoli, CancellationToken.None);

        Assert.NotNull(esito);
        Assert.Equal(["la nonna", "con la mamma"], Assert.Single(esito));
    }

    [Fact]
    public async Task IlTemplateFinisceNelMessaggioMandatoAlModello()
    {
        var ai = new FakeAiTextProvider { Risposta = """{"frasi": [{"caselle": ["a", "b"]}]}""" };

        await Crea(ai).RifinisciAsync([["a", "b"]], Template, Ruoli, CancellationToken.None);

        Assert.Contains(Template, ai.UltimoUtente!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IlContenutoDelleCaselleFinisceNelMessaggio()
    {
        var ai = new FakeAiTextProvider { Risposta = """{"frasi": [{"caselle": ["a", "b"]}]}""" };

        await Crea(ai).RifinisciAsync([["primo", "secondo"]], Template, Ruoli, CancellationToken.None);

        Assert.Contains("primo", ai.UltimoUtente!, StringComparison.Ordinal);
        Assert.Contains("secondo", ai.UltimoUtente!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IRuoliFinisconoNelMessaggioMandatoAlModello()
    {
        var ai = new FakeAiTextProvider { Risposta = """{"frasi": [{"caselle": ["a", "b"]}]}""" };

        await Crea(ai).RifinisciAsync([["a", "b"]], Template, Ruoli, CancellationToken.None);

        Assert.Contains("Soggetto", ai.UltimoUtente!, StringComparison.Ordinal);
        Assert.Contains("Predicato", ai.UltimoUtente!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SenzaRispostaDalModelloSiRestituisceNull()
    {
        var ai = new FakeAiTextProvider { Risposta = null };

        Assert.Null(await Crea(ai).RifinisciAsync([["a", "b"]], Template, Ruoli, CancellationToken.None));
    }

    [Fact]
    public async Task UnaRispostaIllegibileNonFaEsplodereNiente()
    {
        var ai = new FakeAiTextProvider { Risposta = "non sono JSON" };

        Assert.Null(await Crea(ai).RifinisciAsync([["a", "b"]], Template, Ruoli, CancellationToken.None));
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

        var esito = await Crea(ai).RifinisciAsync([["a", "b"]], Template, Ruoli, CancellationToken.None);

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
            .RifinisciAsync([["a", "b"]], Template, Ruoli, CancellationToken.None);

        Assert.Null(esito);
    }

    [Fact]
    public async Task UnaChiamataSolaPerTutteLeFrasi()
    {
        var ai = new FakeAiTextProvider
        {
            Risposta = """{"frasi": [{"caselle": ["a", "b"]}, {"caselle": ["c", "d"]}]}""",
        };

        await Crea(ai).RifinisciAsync([["a", "b"], ["c", "d"]], Template, Ruoli, CancellationToken.None);

        Assert.Equal(1, ai.Chiamate);
    }

    /// <summary>
    /// Con una sola frase il tempo concesso e' quello base: nessun
    /// incremento per frasi aggiuntive da applicare.
    /// </summary>
    [Fact]
    public async Task ConUnaSolaFraseIlTimeoutEQuelloBase()
    {
        var ai = new FakeAiTextProvider
        {
            Risposta = """{"frasi": [{"caselle": ["a", "b"]}]}""",
            Ritardo = TimeSpan.FromMilliseconds(1500),
        };

        var esito = await Crea(ai, timeoutSecondi: 1, timeoutSecondiPerFraseAggiuntiva: 1)
            .RifinisciAsync([["a", "b"]], Template, Ruoli, CancellationToken.None);

        Assert.Null(esito);
    }

    /// <summary>
    /// Le stesse impostazioni del test precedente, ma con piu' frasi: il
    /// tempo concesso cresce a sufficienza da reggere lo stesso ritardo che
    /// con una sola frase avrebbe fatto scadere la chiamata.
    /// </summary>
    [Fact]
    public async Task ConPiuFrasiIlTimeoutSiAllunga()
    {
        var ai = new FakeAiTextProvider
        {
            Risposta = """{"frasi": [{"caselle": ["a", "b"]}, {"caselle": ["c", "d"]}, {"caselle": ["e", "f"]}, {"caselle": ["g", "h"]}]}""",
            Ritardo = TimeSpan.FromMilliseconds(1500),
        };

        var esito = await Crea(ai, timeoutSecondi: 1, timeoutSecondiPerFraseAggiuntiva: 1)
            .RifinisciAsync([["a", "b"], ["c", "d"], ["e", "f"], ["g", "h"]], Template, Ruoli, CancellationToken.None);

        Assert.NotNull(esito);
    }

    /// <summary>
    /// Il tetto ferma la crescita: con molte frasi il tempo concesso non
    /// supera mai TimeoutMassimoSecondi, anche se la formula senza tetto
    /// darebbe un numero piu' grande.
    /// </summary>
    [Fact]
    public async Task IlTimeoutNonSuperaIlTetto()
    {
        var ai = new FakeAiTextProvider
        {
            Risposta = """{"frasi": [{"caselle": ["a", "b"]}]}""",
            Ritardo = TimeSpan.FromSeconds(3),
        };

        var esito = await Crea(ai, timeoutSecondi: 1, timeoutSecondiPerFraseAggiuntiva: 1, timeoutMassimoSecondi: 2)
            .RifinisciAsync(
                [["a", "b"], ["c", "d"], ["e", "f"], ["g", "h"], ["i", "l"]],
                Template,
                Ruoli,
                CancellationToken.None);

        Assert.Null(esito);
    }
}
```

- [ ] **Step 12: Esegui la suite di `RefinementRunnerTests` e verifica che passi**

Run: `dotnet test tests/FrasiSquisite.Server.Tests --filter "FullyQualifiedName~RefinementRunnerTests"`
Expected: PASS — 12/12 (8 test preesistenti, invariati nel comportamento
a parte il nuovo parametro `Ruoli` passato + 4 nuovi: `IRuoliFinisconoNel
MessaggioMandatoAlModello` e i tre sul timeout).

- [ ] **Step 13: Passa `richiesta.Ruoli` in `GameHost.cs`**

In `src/FrasiSquisite.Server/Realtime/GameHost.cs`, trova:

```csharp
                rifinite = await runner.RifinisciAsync(richiesta.Frasi, richiesta.Template, CancellationToken.None);
```

Sostituiscilo con:

```csharp
                rifinite = await runner.RifinisciAsync(richiesta.Frasi, richiesta.Template, richiesta.Ruoli, CancellationToken.None);
```

- [ ] **Step 14: Aggiorna le tre costruzioni dirette di `RequestRefinement` in `GameHostTests.cs`**

In `tests/FrasiSquisite.Server.Tests/Realtime/GameHostTests.cs`, ci sono tre occorrenze identiche:

```csharp
            GameStartRequested => [new RequestRefinement([["a", "b"]], "{0} {1}")],
```

Sostituisci **ciascuna** delle tre con:

```csharp
            GameStartRequested => [new RequestRefinement([["a", "b"]], "{0} {1}", ["Soggetto", "Predicato"])],
```

- [ ] **Step 15: Esegui la suite di `GameHostTests` e verifica che passi**

Run: `dotnet test tests/FrasiSquisite.Server.Tests --filter "FullyQualifiedName~GameHostTests"`
Expected: PASS — tutti i test della classe verdi, nessuna riga rossa
per un `RequestRefinement` costruito con l'arità sbagliata.

- [ ] **Step 16: Aggiorna i commenti in `Program.cs` che citavano "10 secondi" fissi**

In `src/FrasiSquisite.Server/Program.cs`, trova:

```csharp
        // Il valore qui pero' deve essere il PIU' GRANDE dei due
        // (ImageTimeoutSeconds, che oggi e' 90 contro i 10 di TimeoutSeconds):
```

Sostituiscilo con:

```csharp
        // Il valore qui pero' deve essere il PIU' GRANDE dei due
        // (ImageTimeoutSeconds, che oggi e' 90 contro il tetto di
        // TimeoutMassimoSecondi della rifinitura, 30):
```

Poi, nello stesso file, trova:

```csharp
        // Non TimeoutSeconds: quello è il limite della rifinitura, dieci
        // secondi, e generare un'immagine ne richiede molti di più.
```

Sostituiscilo con:

```csharp
        // Non TimeoutSeconds: quello è la base del limite della rifinitura
        // (fino a TimeoutMassimoSecondi con molte frasi), e generare
        // un'immagine ne richiede molti di più.
```

(Solo commenti: `c.Timeout = TimeSpan.FromSeconds(Math.Max(aiOptions.
TimeoutSeconds, aiOptions.ImageTimeoutSeconds));` resta 90s dato che
`ImageTimeoutSeconds` è comunque il più grande dei due, anche con
`TimeoutMassimoSecondi` a 30 — nessun cambiamento di comportamento.)

- [ ] **Step 17: Esegui l'intera suite per verificare che nulla si sia rotto**

Run: `dotnet test`
Expected: PASS — 848/848 (Shared 86, App 126, Domain 520, Server 116: +4
in `RefinementRunnerTests`, gli altri progetti invariati). Se compare un
fallimento isolato in `GameHubTests`, verificare in isolamento
(`dotnet test tests/FrasiSquisite.Server.Tests --filter
"FullyQualifiedName~<NomeDelTest>"`) prima di considerarlo una
regressione: è il flake noto del backlog #2.

- [ ] **Step 18: Commit**

```bash
git add src/FrasiSquisite.Server/Ai/AiOptions.cs src/FrasiSquisite.Domain/Refinement/RefinementGuard.cs src/FrasiSquisite.Domain/Engine/Effect.cs src/FrasiSquisite.Domain/Engine/GameEngine.Refining.cs src/FrasiSquisite.Server/Ai/RefinementRunner.cs src/FrasiSquisite.Server/Realtime/GameHost.cs src/FrasiSquisite.Server/Program.cs tests/FrasiSquisite.Domain.Tests/Refinement/RefinementGuardTests.cs tests/FrasiSquisite.Domain.Tests/Engine/RifinituraTests.cs tests/FrasiSquisite.Server.Tests/Ai/RefinementRunnerTests.cs tests/FrasiSquisite.Server.Tests/Realtime/GameHostTests.cs
git commit -m "feat(rifinitura): timeout proporzionale alle frasi, guardia sulla parola rimossa, ruolo grammaticale nel prompt"
```

---

## Verifica manuale (fuori dal piano, per chi gioca dopo)

Non automatizzabile da qui: una partita reale con più giocatori, guardando
i log del server (`docker compose logs -f`) durante la fase di rifinitura —
verificare che non compaia più `TaskCanceledException` nei casi comuni, e
leggere a reveal se le frasi suonano più fluide (connettivi presenti,
concordanza di genere/numero) senza aver perso l'assurdità degli
accostamenti.
