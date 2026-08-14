# Scala max_tokens della rifinitura con la partita — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `OpenAiCompatibleTextProvider` manda `max_tokens = 2000` fisso in ogni chiamata; con una partita numerosa (es. 9 giocatori × 8 caselle = 72 caselle in un'unica risposta di rifinitura) il testo generato supera il limite prima ancora di contare i token di ragionamento del modello, la risposta tronca, il JSON risulta sbilanciato, e la rifinitura torna al testo grezzo. Questo piano fa scalare `max_tokens` con la dimensione della partita, sullo stesso principio già applicato al timeout (design 2026-08-12 "migliora la rifinitura" §3.1), e rende un troncamento distinguibile nei log da una risposta davvero malformata.

**Architettura:** `IAiTextProvider.CompletaAsync` guadagna un parametro esplicito `maxTokens`, così ogni chiamante decide il tetto in base a cosa sta chiedendo — `RefinementRunner` lo calcola da `frasi.Count * ruoli.Count` (caselle totali) con la formula `500 + 120 * caselleTotali`, configurabile via `AiOptions`; `IllustrationRunner`, che chiede solo una breve descrizione visiva, passa una costante fissa invariata. `OpenAiCompatibleTextProvider` legge anche `choices[0].finish_reason` dalla risposta e logga un warning distinto quando vale `"length"`, per non confondere un troncamento con una risposta malformata quando `Leggi` in `RefinementRunner` scarta il risultato.

**Tech Stack:** .NET 10, `System.Text.Json`, xUnit.

## Global Constraints

- `IAiTextProvider` ha oggi **due** implementazioni di produzione (`OpenAiCompatibleTextProvider`, `DisabledAiTextProvider`) più un doppio di test (`FakeAiTextProvider` in `tests/FrasiSquisite.Server.Tests/Ai/FakeAiTextProvider.cs`) e **due** chiamanti di produzione (`RefinementRunner`, `IllustrationRunner`): ogni task che cambia la firma dell'interfaccia deve aggiornare tutti e cinque i punti nello stesso commit, altrimenti il progetto non compila.
- **Nota per chi esegue i piani di backlog fuori ordine:** se il piano `docs/superpowers/plans/2026-08-14-cache-ai-bot-wordpool.md` è già stato eseguito prima di questo, esiste anche un sesto punto da aggiornare — `BotWordPoolRunner.GeneraAsync`, che in quel piano chiama `ai.CompletaAsync(sistema, utente, ct)` con la firma odierna a 3 argomenti. Aggiorna quella chiamata passando una costante fissa (es. `1500`, coerente con la scelta fatta per `IllustrationRunner` in questo piano) come quarto argomento.
- Non introdurre un tetto massimo (`Math.Min`) sulla formula di `max_tokens`: il backlog non lo richiede, a differenza del timeout (`TimeoutMassimoSecondi`) che invece lo aveva esplicitamente. Non aggiungerlo per simmetria non richiesta (YAGNI).

---

### Task 1: `max_tokens` variabile su `IAiTextProvider.CompletaAsync` e i suoi implementatori/chiamanti

**Files:**
- Modify: `src/FrasiSquisite.Server/Ai/IAiTextProvider.cs`
- Modify: `src/FrasiSquisite.Server/Ai/OpenAiCompatibleTextProvider.cs:20`
- Modify: `src/FrasiSquisite.Server/Ai/DisabledAiTextProvider.cs:10-11`
- Modify: `src/FrasiSquisite.Server/Ai/IllustrationRunner.cs:37,52`
- Modify: `tests/FrasiSquisite.Server.Tests/Ai/FakeAiTextProvider.cs:40`
- Test: `tests/FrasiSquisite.Server.Tests/Ai/OpenAiCompatibleTextProviderTests.cs`

**Interfaccia:**
- Produce: `IAiTextProvider.CompletaAsync(string sistema, string utente, CancellationToken ct, int maxTokens) : Task<string?>` — il Task 2 (in `RefinementRunner`) e un eventuale futuro chiamante consumano questa firma a 4 argomenti.

- [ ] **Step 1: Scrivi il test che verifica che `max_tokens` finisca nella richiesta HTTP**

`HandlerFittizio` in `tests/FrasiSquisite.Server.Tests/Ai/OpenAiCompatibleTextProviderTests.cs` oggi riceve `HttpRequestMessage request` ma non lo ispeziona mai. Aggiungi in `HandlerFittizio` un modo per catturare l'ultima richiesta, poi un test che legge il corpo e verifica `max_tokens`.

In `tests/FrasiSquisite.Server.Tests/Ai/OpenAiCompatibleTextProviderTests.cs`, modifica `HandlerFittizio.SendAsync` per catturare la richiesta:

```csharp
    private sealed class HandlerFittizio : HttpMessageHandler
    {
        private readonly HttpStatusCode? _codice;
        private readonly string? _corpo;
        private readonly Exception? _daLanciare;

        public string? UltimaRichiestaGrezza { get; private set; }

        private HandlerFittizio(HttpStatusCode? codice, string? corpo, Exception? daLanciare)
        {
            _codice = codice;
            _corpo = corpo;
            _daLanciare = daLanciare;
        }

        public static HandlerFittizio ConRisposta(HttpStatusCode codice, string corpo) =>
            new(codice, corpo, null);

        public static HandlerFittizio ConEccezione(Exception eccezione) =>
            new(null, null, eccezione);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                UltimaRichiestaGrezza = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            if (_daLanciare is not null)
            {
                return await Task.FromException<HttpResponseMessage>(_daLanciare);
            }

            var risposta = new HttpResponseMessage(_codice!.Value)
            {
                Content = new StringContent(_corpo!, Encoding.UTF8, "application/json"),
            };
            return risposta;
        }
    }
```

Poi aggiungi il test, subito dopo `CasoFelice_RispostaBenFormata_RestituisceIlContenuto`:

```csharp
    [Fact]
    public async Task IlMaxTokensPassatoDalChiamanteFiniceNellaRichiesta()
    {
        var handler = HandlerFittizio.ConRisposta(HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"ok"}}]}""");
        var provider = CreaProvider(handler);

        await provider.CompletaAsync("sistema", "utente", CancellationToken.None, maxTokens: 777);

        Assert.NotNull(handler.UltimaRichiestaGrezza);
        using var documento = JsonDocument.Parse(handler.UltimaRichiestaGrezza!);
        Assert.Equal(777, documento.RootElement.GetProperty("max_tokens").GetInt32());
    }
```

- [ ] **Step 2: Esegui il test e verifica che fallisca**

```bash
dotnet test tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~IlMaxTokensPassatoDalChiamanteFiniceNellaRichiesta"
```

Atteso: FAIL — `CompletaAsync` non accetta ancora un quinto parametro `maxTokens`, errore di compilazione.

- [ ] **Step 3: Aggiorna la firma dell'interfaccia**

`src/FrasiSquisite.Server/Ai/IAiTextProvider.cs`, sostituisci:

```csharp
    Task<string?> CompletaAsync(string sistema, string utente, CancellationToken ct);
```

con:

```csharp
    Task<string?> CompletaAsync(string sistema, string utente, CancellationToken ct, int maxTokens);
```

- [ ] **Step 4: Aggiorna `OpenAiCompatibleTextProvider`**

`src/FrasiSquisite.Server/Ai/OpenAiCompatibleTextProvider.cs`, sostituisci:

```csharp
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
```

con:

```csharp
    public async Task<string?> CompletaAsync(string sistema, string utente, CancellationToken ct, int maxTokens)
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
                max_tokens = maxTokens,
            };
```

- [ ] **Step 5: Aggiorna `DisabledAiTextProvider`**

`src/FrasiSquisite.Server/Ai/DisabledAiTextProvider.cs`, sostituisci:

```csharp
    public Task<string?> CompletaAsync(string sistema, string utente, CancellationToken ct) =>
        Task.FromResult<string?>(null);
```

con:

```csharp
    public Task<string?> CompletaAsync(string sistema, string utente, CancellationToken ct, int maxTokens) =>
        Task.FromResult<string?>(null);
```

- [ ] **Step 6: Aggiorna `IllustrationRunner`**

`src/FrasiSquisite.Server/Ai/IllustrationRunner.cs`: la traduzione italiano→descrizione visiva è un output breve (il prompt stesso impone "Massimo quaranta parole", riga 31) e non scala con la dimensione della partita — passa una costante fissa invariata rispetto al comportamento odierno. Aggiungi la costante subito sopra `IllustraAsync` (riga 37):

```csharp
    /// <summary>
    /// La traduzione italiano-inglese e' vincolata a "massimo quaranta
    /// parole" dal prompt di sistema qui sopra: non scala con la partita
    /// come la rifinitura, quindi resta un tetto fisso.
    /// </summary>
    private const int MaxTokensDescrizione = 2000;

    public async Task<byte[]?> IllustraAsync(string fraseItaliana, CancellationToken ct)
```

E aggiorna la chiamata a riga 52:

```csharp
            var grezza = await testo.CompletaAsync(Sistema, fraseItaliana, scadenza.Token);
```

in:

```csharp
            var grezza = await testo.CompletaAsync(Sistema, fraseItaliana, scadenza.Token, MaxTokensDescrizione);
```

- [ ] **Step 7: Aggiorna `FakeAiTextProvider`**

`tests/FrasiSquisite.Server.Tests/Ai/FakeAiTextProvider.cs`, aggiungi una proprietà per catturare l'ultimo `maxTokens` ricevuto (utile per test futuri su `RefinementRunner`, Task 2) e aggiorna la firma:

```csharp
    public int UltimoMaxTokens { get; private set; }

    public async Task<string?> CompletaAsync(string sistema, string utente, CancellationToken ct, int maxTokens)
    {
        Chiamate++;
        UltimoSistema = sistema;
        UltimoUtente = utente;
        UltimoMaxTokens = maxTokens;
```

(il resto del corpo del metodo resta invariato).

- [ ] **Step 8: Compila e esegui il test dello Step 1**

```bash
dotnet build tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore
dotnet test tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~IlMaxTokensPassatoDalChiamanteFiniceNellaRichiesta"
```

Atteso: PASS. Se `RefinementRunnerTests.cs` o `IllustrationRunnerTests.cs` non compilano più a questo punto è atteso — li aggiornano i Task 2 e 3.

- [ ] **Step 9: Aggiorna le chiamate esistenti in `RefinementRunner` e nei suoi test per farli tornare a compilare (valore temporaneo, il Task 2 lo sostituisce)**

`src/FrasiSquisite.Server/Ai/RefinementRunner.cs:85`, sostituisci:

```csharp
            var risposta = await ai.CompletaAsync(Sistema, utente, scadenza.Token);
```

con (valore letterale temporaneo — il Task 2 lo sostituisce con la formula):

```csharp
            var risposta = await ai.CompletaAsync(Sistema, utente, scadenza.Token, 2000);
```

- [ ] **Step 10: Esegui l'intera suite del progetto server e verifica che compili e passi**

```bash
dotnet test tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore
```

Atteso: tutti i test passano (incluso `RefinementRunnerTests`, invariati nel comportamento — solo l'argomento in più a monte).

- [ ] **Step 11: Commit**

```bash
git add src/FrasiSquisite.Server/Ai/IAiTextProvider.cs src/FrasiSquisite.Server/Ai/OpenAiCompatibleTextProvider.cs src/FrasiSquisite.Server/Ai/DisabledAiTextProvider.cs src/FrasiSquisite.Server/Ai/IllustrationRunner.cs src/FrasiSquisite.Server/Ai/RefinementRunner.cs tests/FrasiSquisite.Server.Tests/Ai/FakeAiTextProvider.cs tests/FrasiSquisite.Server.Tests/Ai/OpenAiCompatibleTextProviderTests.cs
git commit -m "feat(ai): max_tokens diventa un parametro esplicito di CompletaAsync

Prerequisito per far scalare il tetto della rifinitura con la
dimensione della partita (prossimo commit): IllustrationRunner passa
una costante invariata, RefinementRunner per ora un valore
temporaneo identico a quello di prima (2000)."
```

---

### Task 2: `RefinementRunner` calcola `max_tokens` dalle caselle totali

**Files:**
- Modify: `src/FrasiSquisite.Server/Ai/RefinementRunner.cs:69-85`
- Modify: `src/FrasiSquisite.Server/Ai/AiOptions.cs`
- Test: `tests/FrasiSquisite.Server.Tests/Ai/RefinementRunnerTests.cs`

**Interfaccia:**
- Consuma: `IAiTextProvider.CompletaAsync(..., int maxTokens)` dal Task 1; `FakeAiTextProvider.UltimoMaxTokens` dal Task 1 Step 7.
- Produce: nessuna nuova interfaccia pubblica — `RifinisciAsync` mantiene la sua firma odierna.

- [ ] **Step 1: Scrivi il test sulla formula**

In `tests/FrasiSquisite.Server.Tests/Ai/RefinementRunnerTests.cs`, aggiungi (dopo `UnaChiamataSolaPerTutteLeFrasi`, che già copre lo stesso stile di verifica su `ai.Chiamate`):

```csharp
    /// <summary>
    /// Due frasi da due caselle ciascuna fanno 4 caselle totali: con i
    /// valori di default (base 500, 120 per casella) il tetto atteso è
    /// 500 + 120 * 4 = 980.
    /// </summary>
    [Fact]
    public async Task IlMaxTokensCresceConLeCaselleTotali()
    {
        var ai = new FakeAiTextProvider
        {
            Risposta = """{"frasi": [{"caselle": ["a", "b"]}, {"caselle": ["c", "d"]}]}""",
        };

        await Crea(ai).RifinisciAsync([["a", "b"], ["c", "d"]], Template, Ruoli, CancellationToken.None);

        Assert.Equal(980, ai.UltimoMaxTokens);
    }

    /// <summary>
    /// Con una sola frase da due caselle il tetto è quello base più il
    /// contributo delle sole due caselle di quella frase: 500 + 120 * 2 = 740.
    /// </summary>
    [Fact]
    public async Task ConUnaSolaFraseIlMaxTokensUsaSoloLeSueCaselle()
    {
        var ai = new FakeAiTextProvider
        {
            Risposta = """{"frasi": [{"caselle": ["a", "b"]}]}""",
        };

        await Crea(ai).RifinisciAsync([["a", "b"]], Template, Ruoli, CancellationToken.None);

        Assert.Equal(740, ai.UltimoMaxTokens);
    }
```

- [ ] **Step 2: Esegui i test e verifica che falliscano**

```bash
dotnet test tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~IlMaxTokensCresceConLeCaselleTotali|FullyQualifiedName~ConUnaSolaFraseIlMaxTokensUsaSoloLeSueCaselle"
```

Atteso: FAIL — `ai.UltimoMaxTokens` vale ancora 2000 (valore temporaneo del Task 1), non 980/740.

- [ ] **Step 3: Aggiungi le opzioni configurabili**

`src/FrasiSquisite.Server/Ai/AiOptions.cs`, aggiungi dopo `TimeoutMassimoSecondi` (riga 46):

```csharp

    /// <summary>
    /// Base del tetto di token concessi alla risposta della rifinitura,
    /// prima di aggiungere il contributo delle caselle totali (vedi
    /// <see cref="RifinituraMaxTokensPerCasella"/>). Un tetto fisso a 2000
    /// (il valore precedente, prima che questo campo esistesse) troncava la
    /// risposta con partite numerose (backlog.md §5): 9 giocatori x 8
    /// caselle di uno schema = 72 caselle in un'unica risposta batch.
    /// </summary>
    public int RifinituraMaxTokensBase { get; set; } = 500;

    /// <summary>
    /// Quanti token in più concedere per ogni casella totale (frasi x
    /// caselle per frase) da rifinire nella stessa chiamata batch.
    /// </summary>
    public int RifinituraMaxTokensPerCasella { get; set; } = 120;
```

- [ ] **Step 4: Calcola `maxTokens` in `RifinisciAsync` e passalo alla chiamata**

`src/FrasiSquisite.Server/Ai/RefinementRunner.cs`, subito dopo il calcolo di `secondi` (righe 69-74), aggiungi:

```csharp
        var secondi = Math.Min(
            _opzioni.TimeoutMassimoSecondi,
            _opzioni.TimeoutSeconds + _opzioni.TimeoutSecondiPerFraseAggiuntiva * Math.Max(0, frasi.Count - 1));

        // Stesso principio del timeout qui sopra: una rifinitura batch per
        // tutta la partita produce più testo con più caselle nella stessa
        // risposta, e un tetto fisso a 2000 troncava la risposta con
        // partite numerose (backlog.md §5) — il JSON risultava sbilanciato,
        // Leggi lo scartava, e l'intera rifinitura tornava al testo grezzo.
        var caselleTotali = frasi.Count * ruoli.Count;
        var maxTokens = _opzioni.RifinituraMaxTokensBase + _opzioni.RifinituraMaxTokensPerCasella * caselleTotali;

        using var scadenza = CancellationTokenSource.CreateLinkedTokenSource(ct);
        scadenza.CancelAfter(TimeSpan.FromSeconds(secondi));
```

(la riga `using var scadenza = ...` già esistente resta dov'è — l'inserimento va tra il calcolo di `secondi` e quella riga).

Poi sostituisci la chiamata (riga con `ai.CompletaAsync` modificata provvisoriamente nel Task 1 Step 9):

```csharp
            var risposta = await ai.CompletaAsync(Sistema, utente, scadenza.Token, 2000);
```

con:

```csharp
            var risposta = await ai.CompletaAsync(Sistema, utente, scadenza.Token, maxTokens);
```

- [ ] **Step 5: Esegui i test e verifica che passino**

```bash
dotnet test tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~RefinementRunnerTests"
```

Atteso: PASS, incluse le due nuove.

- [ ] **Step 6: Esegui l'intera suite del progetto server**

```bash
dotnet test tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore
```

Atteso: tutti i test passano.

- [ ] **Step 7: Commit**

```bash
git add src/FrasiSquisite.Server/Ai/RefinementRunner.cs src/FrasiSquisite.Server/Ai/AiOptions.cs tests/FrasiSquisite.Server.Tests/Ai/RefinementRunnerTests.cs
git commit -m "fix(ai): max_tokens della rifinitura cresce con le caselle totali

Una partita numerosa (9 giocatori x 8 caselle = 72 caselle in
un'unica risposta batch) poteva superare il tetto fisso di 2000
token prima ancora di contare i token di ragionamento del modello:
la risposta troncava, il JSON risultava sbilanciato, Leggi lo
scartava, e la rifinitura tornava al testo grezzo (backlog.md §5).
Stesso principio già applicato al timeout in RefinementRunner."
```

---

### Task 3: Log distinto quando la risposta tronca per `finish_reason == "length"`

**Files:**
- Modify: `src/FrasiSquisite.Server/Ai/OpenAiCompatibleTextProvider.cs:49-55`
- Test: `tests/FrasiSquisite.Server.Tests/Ai/OpenAiCompatibleTextProviderTests.cs`

**Interfaccia:** nessuna — solo logging aggiuntivo, il valore di ritorno di `CompletaAsync` non cambia (un troncamento continua a restituire il `content` parziale così com'è, coerente con l'oggi: distinguerlo è solo questione di osservabilità nei log, non di comportamento).

Oggi un JSON troncato arriva a `RefinementRunner.Leggi`, che lo logga come "Risposta del modello illeggibile" — indistinguibile da una risposta davvero malformata. Questo task aggiunge il segnale a monte, nel provider, dove il campo `finish_reason` è disponibile.

- [ ] **Step 1: Scrivi il test**

In `tests/FrasiSquisite.Server.Tests/Ai/OpenAiCompatibleTextProviderTests.cs`, per verificare il log serve un logger fittizio invece di `NullLogger` — aggiungi un overload di `CreaProvider` che lo accetta:

```csharp
    private static OpenAiCompatibleTextProvider CreaProvider(HttpMessageHandler handler, ILogger<OpenAiCompatibleTextProvider>? logger = null)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://fornitore-fittizio.test/") };
        var opzioni = Options.Create(new AiOptions
        {
            BaseUrl = "http://fornitore-fittizio.test/",
            ApiKey = ChiaveDiProva,
            TextModel = "modello-di-prova",
            TimeoutSeconds = 5,
        });

        return new OpenAiCompatibleTextProvider(http, opzioni, logger ?? NullLogger<OpenAiCompatibleTextProvider>.Instance);
    }
```

(sostituisce la `CreaProvider` esistente a riga 23-38 — stesso corpo, solo il parametro `logger` opzionale in più).

Serve `using Microsoft.Extensions.Logging;` in cima al file (per `ILogger<T>` e `LogLevel`), da aggiungere se non già presente.

Poi il test, con un logger fittizio minimale che cattura i messaggi:

```csharp
    [Fact]
    public async Task FinishReasonLengthVieneLoggatoComeTroncamento()
    {
        var logger = new LoggerFittizio<OpenAiCompatibleTextProvider>();
        var handler = HandlerFittizio.ConRisposta(HttpStatusCode.OK,
            """{"choices":[{"finish_reason":"length","message":{"content":"testo troncato a met"}}]}""");
        var provider = CreaProvider(handler, logger);

        await provider.CompletaAsync("sistema", "utente", CancellationToken.None, maxTokens: 100);

        Assert.Contains(logger.Messaggi, m => m.Contains("troncat", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class LoggerFittizio<T> : ILogger<T>
    {
        public List<string> Messaggi { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messaggi.Add(formatter(state, exception));
    }
```

- [ ] **Step 2: Esegui il test e verifica che fallisca**

```bash
dotnet test tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~FinishReasonLengthVieneLoggatoComeTroncamento"
```

Atteso: FAIL — nessun log contiene "troncat", il provider non legge ancora `finish_reason`.

- [ ] **Step 3: Leggi `finish_reason` e logga se vale `"length"`**

`src/FrasiSquisite.Server/Ai/OpenAiCompatibleTextProvider.cs`, sostituisci:

```csharp
            using var documento = JsonDocument.Parse(await risposta.Content.ReadAsStringAsync(ct));

            return documento.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
```

con:

```csharp
            using var documento = JsonDocument.Parse(await risposta.Content.ReadAsStringAsync(ct));

            var scelta = documento.RootElement.GetProperty("choices")[0];

            if (scelta.TryGetProperty("finish_reason", out var motivo) &&
                motivo.GetString() == "length")
            {
                // Non un errore: la risposta e' comunque usabile fino a dove
                // arriva. E' pero' il segnale che distingue un troncamento
                // per max_tokens (qui) da una risposta davvero malformata
                // (che RefinementRunner.Leggi logga a sua volta, ma senza
                // poter fare questa distinzione: da li' il JSON tagliato a
                // meta' sembra solo "illeggibile").
                logger.LogWarning("Risposta troncata per max_tokens (finish_reason=length).");
            }

            return scelta
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
```

- [ ] **Step 4: Esegui il test e verifica che passi**

```bash
dotnet test tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~FinishReasonLengthVieneLoggatoComeTroncamento"
```

Atteso: PASS.

- [ ] **Step 5: Esegui l'intera suite del progetto server**

```bash
dotnet test tests/FrasiSquisite.Server.Tests/FrasiSquisite.Server.Tests.csproj --no-restore
```

Atteso: tutti i test passano — in particolare i test esistenti con risposte prive di `finish_reason` (es. `CasoFelice_RispostaBenFormata_RestituisceIlContenuto`) continuano a funzionare perché `TryGetProperty` non lancia quando la proprietà manca.

- [ ] **Step 6: Commit**

```bash
git add src/FrasiSquisite.Server/Ai/OpenAiCompatibleTextProvider.cs tests/FrasiSquisite.Server.Tests/Ai/OpenAiCompatibleTextProviderTests.cs
git commit -m "feat(ai): logga finish_reason=length come troncamento distinto

Prima un JSON troncato per max_tokens arrivava a
RefinementRunner.Leggi indistinguibile da una risposta davvero
malformata (stesso log 'illeggibile'). Il segnale ora nasce a monte,
dove il campo è disponibile."
```
