# Illustrazione della frase — Piano di implementazione

> **Per chi esegue:** SOTTO-SKILL RICHIESTA: usare superpowers:subagent-driven-development (consigliata) o superpowers:executing-plans, un task alla volta. I passi usano caselle (`- [ ]`).

**Obiettivo:** dopo il voto, l'host può chiedere l'illustrazione di una frase qualsiasi della classifica; l'immagine compare a tutti.

**Architettura:** stessa del lotto della rifinitura. Il motore non chiama nessuno: emette l'effetto `RequestIllustration` e riceve l'evento `IllustrationFinished`. `GameHost` esegue in un task staccato, perché `DispatchAsync` tiene il lucchetto della stanza per tutto il giro degli effetti e l'evento di ritorno passa dallo stesso lucchetto. I byte non entrano mai nel motore: vivono in un `ImageStore` in memoria e si servono da un endpoint HTTP con un identificativo casuale.

**Stack:** .NET 10, ASP.NET Core minimal API, SignalR, MAUI (`net10.0-android`), xUnit 2.9.3.

**Riferimento:** [spec AI](../specs/2026-08-03-ai-design.md) §5, §7, §8, §9, §11. Terzo dei tre pezzi di §10.

---

## Vincoli globali

Valgono per ogni task, senza ripeterli.

- **Il motore resta puro.** Niente I/O, niente `async`, niente orologio, niente `Guid.NewGuid()`, niente casualità non iniettata dentro `FrasiSquisite.Domain`. Chi ha bisogno di un identificativo casuale se lo fa generare fuori e lo passa nell'evento, come già fa `BotAdded`.
- **Il degrado è un'implementazione, non un `if`.** L'unico `if (aiOptions.Abilitato)` del progetto sta in `Program.cs` e ci resta. Senza chiave si registra un provider che fallisce subito, e il gioco resta interamente giocabile.
- **La chiave non entra mai** nel repository, nell'immagine Docker, nell'APK o in un log. Arriva solo come `Ai__ApiKey`.
- **Nessuna eccezione deve poter cadere una partita.** I provider tornano `null` invece di lanciare; il task staccato di `GameHost` ha due `try/catch` e manda comunque l'evento di ritorno.
- **Protocollo v7**, uguaglianza stretta: l'APK v6 verrà rifiutato e va reinstallato.
- **Pacchetti**: gestione centrale in `Directory.Packages.props`. Mai `Version=` inline in un `.csproj`.
- **Lingua**: codice, commenti, messaggi di commit e testo a schermo in italiano, come il resto del progetto. I commenti spiegano il *perché*, non il *cosa*.
- **Firma dei commit**: `commit.gpgsign` è attivo. Se 1Password è bloccato, **fermarsi e segnalarlo**; mai `--no-gpg-sign`.
- **Comando dei test**: `dotnet test FrasiSquisite.slnx` (estensione `.slnx`, non `.sln`).
- **Punto di partenza**: 715 test verdi (Shared 76, Domain 494, App 95, Server 50).

---

## Struttura dei file

**Domain (puro)**
- `src/FrasiSquisite.Domain/Model/GameState.cs` — un campo in più: quali frasi hanno già chiesto un'illustrazione
- `src/FrasiSquisite.Domain/Engine/GameEvent.cs` — `IllustrationRequested`, `IllustrationFinished`
- `src/FrasiSquisite.Domain/Engine/Effect.cs` — `RequestIllustration`
- `src/FrasiSquisite.Domain/Engine/GameEngine.Illustration.cs` — **nuovo**, le guardie e le due transizioni
- `src/FrasiSquisite.Domain/Engine/GameEngine.cs` — due righe nel dispatch
- `src/FrasiSquisite.Domain/Engine/GameEngine.Room.cs` — azzeramento a nuova partita e ritorno in lobby

**Shared (contratto)**
- `src/FrasiSquisite.Shared/Protocol/ClientMessages.cs` — `RequestIllustrationRequest`
- `src/FrasiSquisite.Shared/Protocol/ServerMessages.cs` — `IllustrationReadyMessage`, `IllustrationFailedMessage`
- `src/FrasiSquisite.Shared/Protocol/ProtocolVersion.cs` — 6 → 7

**Server**
- `src/FrasiSquisite.Server/Ai/AiOptions.cs` — modello e dimensione dell'immagine
- `src/FrasiSquisite.Server/Ai/IAiImageProvider.cs` — **nuovo**, separato da `IAiTextProvider` (spec §7: l'endpoint immagini non è `/chat/completions`)
- `src/FrasiSquisite.Server/Ai/OpenAiCompatibleImageProvider.cs` — **nuovo**
- `src/FrasiSquisite.Server/Ai/DisabledAiImageProvider.cs` — **nuovo**
- `src/FrasiSquisite.Server/Ai/IllustrationRunner.cs` — **nuovo**, le due chiamate
- `src/FrasiSquisite.Server/Images/ImageStore.cs` — **nuovo**, i byte in memoria con identificativo opaco
- `src/FrasiSquisite.Server/Realtime/GameHost.cs` — un ramo in più in `EseguiAsync`
- `src/FrasiSquisite.Server/Realtime/GameHub.cs` — `RequestIllustration`
- `src/FrasiSquisite.Server/Program.cs` — registrazioni ed endpoint

**App**
- `src/FrasiSquisite.App/Services/IGameConnection.cs`, `SignalRGameConnection.cs` — un metodo
- `src/FrasiSquisite.App/ViewModels/PhraseResultRowView.cs` — da immutabile a osservabile
- `src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs` — comando e due messaggi
- `src/FrasiSquisite.App/Pages/GamePage.xaml` — pulsante e riquadro

---

### Task 1: Il motore chiede l'illustrazione e riceve l'esito

Tutto in `FrasiSquisite.Domain` e `FrasiSquisite.Shared`: nessuna rete, nessun byte.

**File:**
- Modificare: `src/FrasiSquisite.Domain/Model/GameState.cs`
- Modificare: `src/FrasiSquisite.Domain/Engine/GameEvent.cs`, `Effect.cs`, `GameEngine.cs`
- Creare: `src/FrasiSquisite.Domain/Engine/GameEngine.Illustration.cs`
- Modificare: `src/FrasiSquisite.Shared/Protocol/ClientMessages.cs`, `ServerMessages.cs`, `ProtocolVersion.cs`
- Test: `tests/FrasiSquisite.Domain.Tests/Engine/IllustrazioneTests.cs` (nuovo), `tests/FrasiSquisite.Shared.Tests/Protocol/ProtocolContractTests.cs`

**Interfacce prodotte** (le usano i task 5 e 6):
```csharp
public sealed record IllustrationRequested(Guid RequestedBy, int PhraseIndex) : GameEvent;
public sealed record IllustrationFinished(int PhraseIndex, string? Path) : GameEvent;
public sealed record RequestIllustration(int PhraseIndex, string Frase) : Effect;
public sealed record RequestIllustrationRequest(string RoomCode, int PhraseIndex);
public sealed record IllustrationReadyMessage(int PhraseIndex, string Path);
public sealed record IllustrationFailedMessage(int PhraseIndex, string Message);
```

**Perché lo stato è un insieme di indici e non una mappa a indirizzi.** Al motore serve rispondere a una domanda sola: *questa frase è già stata chiesta?* Gli indirizzi non gli servono mai — li manda via broadcast nel momento in cui arrivano e non li rilegge. Tenerli sarebbe stato morto, e avvicinerebbe il motore ai byte, che la spec §5 vuole fuori.

- [ ] **Passo 1: Scrivere i test che falliscono**

Creare `tests/FrasiSquisite.Domain.Tests/Engine/IllustrazioneTests.cs`:

```csharp
using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Shared.Protocol;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Engine;

public class IllustrazioneTests
{
    private const int N = 3;
    private const int K = 3;

    private readonly IGameEngine _motore =
        new GameEngine(new RoleSchemaMode(), new StaticWordPool(), new SeededRandomSource(1));

    private static Guid Giocatore(int i) => Guid.Parse($"00000000-0000-0000-0000-{i:D12}");

    /// <summary>Partita conclusa: tutti hanno votato, la classifica è arrivata.</summary>
    private GameState AllaClassifica()
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

        stato = _motore.Handle(stato, new RefinementFinished(null)).State;

        for (var i = 0; i < N * K; i++)
        {
            stato = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0))).State;
        }

        for (var i = 0; i < N; i++)
        {
            stato = _motore.Handle(stato, new VoteCast(Giocatore(i), 0)).State;
        }

        return stato;
    }

    [Fact]
    public void LHostChiedeLIllustrazioneEIlMotoreEmetteLEffetto()
    {
        var risultato = _motore.Handle(AllaClassifica(), new IllustrationRequested(Giocatore(0), 1));

        var effetto = Assert.Single(risultato.Effects.OfType<RequestIllustration>());
        Assert.Equal(1, effetto.PhraseIndex);
        Assert.False(string.IsNullOrWhiteSpace(effetto.Frase));
    }

    /// <summary>
    /// L'effetto porta la frase COMPOSTA, non le caselle: chi genera l'immagine
    /// deve leggere una frase italiana, e ricomporla fuori dal motore vorrebbe
    /// dire duplicare Schema.Compose in un posto che non ha lo schema.
    /// </summary>
    [Fact]
    public void LEffettoPortaLaFraseComposta()
    {
        var stato = AllaClassifica();

        var risultato = _motore.Handle(stato, new IllustrationRequested(Giocatore(0), 1));

        var effetto = Assert.Single(risultato.Effects.OfType<RequestIllustration>());
        var attesa = stato.Schema.Compose([.. stato.Phrases[1].Slots.Select(s => s!.Text)]);
        Assert.Equal(attesa, effetto.Frase);
    }

    /// <summary>
    /// Un doppio tocco non paga due volte (spec §5). Ogni immagine costa circa
    /// nove centesimi: questa non è un'ottimizzazione, è la differenza fra un
    /// dito impaziente e il conto che raddoppia.
    /// </summary>
    [Fact]
    public void LaStessaFraseNonVieneChiestaDueVolte()
    {
        var stato = _motore.Handle(AllaClassifica(), new IllustrationRequested(Giocatore(0), 1)).State;

        var risultato = _motore.Handle(stato, new IllustrationRequested(Giocatore(0), 1));

        Assert.Empty(risultato.Effects.OfType<RequestIllustration>());
        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(0)));
        Assert.Equal("ILLUSTRATION_ALREADY_REQUESTED", errore.Code);
    }

    [Fact]
    public void UnAltraFraseSiPuoChiedereLoStesso()
    {
        var stato = _motore.Handle(AllaClassifica(), new IllustrationRequested(Giocatore(0), 1)).State;

        var risultato = _motore.Handle(stato, new IllustrationRequested(Giocatore(0), 0));

        Assert.Single(risultato.Effects.OfType<RequestIllustration>());
    }

    [Fact]
    public void SoloLHostPuoChiederla()
    {
        var risultato = _motore.Handle(AllaClassifica(), new IllustrationRequested(Giocatore(1), 0));

        Assert.Empty(risultato.Effects.OfType<RequestIllustration>());
        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(1)));
        Assert.Equal("NOT_HOST", errore.Code);
    }

    [Fact]
    public void PrimaDellaClassificaNonSiPuoChiedere()
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(K));
        stato = _motore.Handle(stato, new PlayerJoined(Giocatore(0), "G0")).State;

        var risultato = _motore.Handle(stato, new IllustrationRequested(Giocatore(0), 0));

        Assert.Empty(risultato.Effects.OfType<RequestIllustration>());
        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(0)));
        Assert.Equal("NOT_FINISHED", errore.Code);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void UnIndiceFuoriDaiLimitiVieneRifiutato(int indice)
    {
        var risultato = _motore.Handle(AllaClassifica(), new IllustrationRequested(Giocatore(0), indice));

        Assert.Empty(risultato.Effects.OfType<RequestIllustration>());
        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(0)));
        Assert.Equal("NO_SUCH_PHRASE", errore.Code);
    }

    [Fact]
    public void LIllustrazioneProntaVieneMandataATutti()
    {
        var stato = _motore.Handle(AllaClassifica(), new IllustrationRequested(Giocatore(0), 1)).State;

        var risultato = _motore.Handle(stato, new IllustrationFinished(1, "/illustrazioni/abc"));

        var messaggio = Assert.Single(risultato.Broadcasts<IllustrationReadyMessage>());
        Assert.Equal(1, messaggio.PhraseIndex);
        Assert.Equal("/illustrazioni/abc", messaggio.Path);
    }

    /// <summary>
    /// Il fallimento deve TOGLIERE la frase dall'insieme, o il pulsante
    /// resterebbe spento per sempre e l'host non potrebbe riprovare.
    /// </summary>
    [Fact]
    public void DopoUnFallimentoSiPuoRiprovare()
    {
        var stato = _motore.Handle(AllaClassifica(), new IllustrationRequested(Giocatore(0), 1)).State;
        var fallito = _motore.Handle(stato, new IllustrationFinished(1, null));

        Assert.Single(fallito.Broadcasts<IllustrationFailedMessage>());

        var riprova = _motore.Handle(fallito.State, new IllustrationRequested(Giocatore(0), 1));

        Assert.Single(riprova.Effects.OfType<RequestIllustration>());
    }

    /// <summary>
    /// Stessa guardia della rifinitura: un esito che arriva quando la stanza è
    /// già ripartita non deve toccare la partita nuova.
    /// </summary>
    [Fact]
    public void UnEsitoFuoriFaseVieneIgnorato()
    {
        var stato = _motore.Handle(AllaClassifica(), new IllustrationRequested(Giocatore(0), 1)).State;
        stato = _motore.Handle(stato, new NewGameRequested(Giocatore(0))).State;

        var risultato = _motore.Handle(stato, new IllustrationFinished(1, "/illustrazioni/abc"));

        Assert.Empty(risultato.Effects);
    }

    [Fact]
    public void UnaPartitaNuovaAzzeraLeIllustrazioni()
    {
        var stato = _motore.Handle(AllaClassifica(), new IllustrationRequested(Giocatore(0), 1)).State;

        stato = _motore.Handle(stato, new NewGameRequested(Giocatore(0))).State;

        Assert.Empty(stato.IllustrationsRequested);
    }

    [Fact]
    public void IlRitornoInLobbyAzzeraLeIllustrazioni()
    {
        var stato = _motore.Handle(AllaClassifica(), new IllustrationRequested(Giocatore(0), 1)).State;

        stato = _motore.Handle(stato, new BackToLobbyRequested(Giocatore(0))).State;

        Assert.Empty(stato.IllustrationsRequested);
    }
}
```

Aggiungere in `tests/FrasiSquisite.Shared.Tests/Protocol/ProtocolContractTests.cs`, accanto al test esistente sulla versione, sostituendo il numero atteso:

```csharp
    [Fact]
    public void LaVersioneDelProtocolloE7()
    {
        Assert.Equal(7, ProtocolVersion.Current);
    }
```

Il test esistente che asserisce 6 va **modificato**, non affiancato: due test che asseriscono numeri diversi sulla stessa costante non possono essere entrambi verdi.

- [ ] **Passo 2: Eseguire i test e vederli fallire**

```bash
dotnet test tests/FrasiSquisite.Domain.Tests --filter "FullyQualifiedName~IllustrazioneTests"
```

Atteso: errore di compilazione — `IllustrationRequested` non esiste. È il fallimento giusto: i tipi arrivano nel passo dopo.

- [ ] **Passo 3: I tipi del protocollo**

In `src/FrasiSquisite.Shared/Protocol/ClientMessages.cs`, in fondo:

```csharp
/// <summary>
/// <paramref name="PhraseIndex"/> è l'indice della frase, lo stesso che porta
/// ogni riga di <c>PhraseResultView</c>: non l'indice di riga della classifica,
/// che dipende dall'ordinamento per voti.
/// </summary>
public sealed record RequestIllustrationRequest(string RoomCode, int PhraseIndex);
```

In `src/FrasiSquisite.Shared/Protocol/ServerMessages.cs`, in fondo:

```csharp
/// <summary>
/// <paramref name="Path"/> è relativo (es. <c>/illustrazioni/xY3…</c>) e non
/// assoluto: il client conosce già l'indirizzo del server, e un indirizzo
/// assoluto costruito lato server sbaglierebbe ogni volta che c'è un reverse
/// proxy davanti — che è esattamente la configurazione in cui gira (Caddy).
///
/// L'identificativo dentro il percorso è casuale e non l'indice della frase:
/// il codice stanza è corto e indovinabile, e con l'indice chiunque potrebbe
/// pescare le illustrazioni di partite altrui provando codici a caso (spec §5).
/// </summary>
public sealed record IllustrationReadyMessage(int PhraseIndex, string Path);

/// <summary>
/// Il pulsante deve tornare disponibile: senza questo messaggio resterebbe in
/// attesa per sempre, e l'host non saprebbe se riprovare.
/// </summary>
public sealed record IllustrationFailedMessage(int PhraseIndex, string Message);
```

In `src/FrasiSquisite.Shared/Protocol/ProtocolVersion.cs` portare `Current` da 6 a 7.

- [ ] **Passo 4: Evento, effetto e stato**

In `src/FrasiSquisite.Domain/Engine/GameEvent.cs`, in fondo:

```csharp
/// <summary>
/// L'host chiede l'illustrazione di una frase della classifica (spec §5). Non
/// è automatica sulla vincitrice: a pareggio bisognerebbe sceglierne una a
/// caso, a zero voti non ce ne sarebbe nessuna, e ogni partita costerebbe
/// comunque circa nove centesimi anche quando a nessuno interessa.
/// </summary>
public sealed record IllustrationRequested(Guid RequestedBy, int PhraseIndex) : GameEvent;

/// <summary>
/// L'esito. <paramref name="Path"/> nullo significa fallito — rete giù,
/// timeout, chiave assente, modello che rifiuta. Diversamente dalla
/// rifinitura i due casi NON si trattano allo stesso modo: qui l'host ha
/// chiesto qualcosa esplicitamente e deve sapere che non è arrivata.
/// </summary>
public sealed record IllustrationFinished(int PhraseIndex, string? Path) : GameEvent;
```

In `src/FrasiSquisite.Domain/Engine/Effect.cs`, in fondo:

```csharp
/// <summary>
/// Chiede l'illustrazione di una frase. Porta la frase composta e nient'altro:
/// il motore non sa se dietro ci sia un modello, una cache o niente.
/// </summary>
public sealed record RequestIllustration(int PhraseIndex, string Frase) : Effect;
```

In `src/FrasiSquisite.Domain/Model/GameState.cs` aggiungere il campo in fondo alla lista dei parametri del record:

```csharp
    IReadOnlyDictionary<Guid, int> Votes,
    IReadOnlySet<int> IllustrationsRequested)
```

e in `NewRoom`, dopo `Votes:`:

```csharp
            Votes: new Dictionary<Guid, int>(),
            // Solo gli indici: al motore serve sapere se una frase è già stata
            // chiesta, non dove sia finita l'immagine. Gli indirizzi li manda
            // e non li rilegge, e tenerli avvicinerebbe il motore ai byte, che
            // la spec §5 vuole fuori.
            IllustrationsRequested: new HashSet<int>());
```

- [ ] **Passo 5: Le due transizioni**

Creare `src/FrasiSquisite.Domain/Engine/GameEngine.Illustration.cs`:

```csharp
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Shared.Protocol;

namespace FrasiSquisite.Domain.Engine;

/// <summary>
/// L'illustrazione su richiesta dell'host (spec §5). Come per la rifinitura il
/// motore non chiama nessuno: emette un effetto e aspetta l'evento di ritorno.
/// </summary>
public sealed partial class GameEngine
{
    private static EngineResult OnIllustrationRequested(GameState state, IllustrationRequested e)
    {
        if (state.Phase != RoomPhase.Finished)
        {
            return Error(state, e.RequestedBy, "NOT_FINISHED", "La partita non è ancora finita.");
        }

        if (e.RequestedBy != state.HostId)
        {
            return Error(state, e.RequestedBy, "NOT_HOST", "Solo chi ospita può chiedere l'illustrazione.");
        }

        if (e.PhraseIndex < 0 || e.PhraseIndex >= state.Phrases.Count)
        {
            return Error(state, e.RequestedBy, "NO_SUCH_PHRASE", "Quella frase non esiste.");
        }

        if (state.IllustrationsRequested.Contains(e.PhraseIndex))
        {
            return Error(state, e.RequestedBy, "ILLUSTRATION_ALREADY_REQUESTED", "Quella frase ce l'ha già.");
        }

        var chieste = new HashSet<int>(state.IllustrationsRequested) { e.PhraseIndex };
        var chiesto = state with { IllustrationsRequested = chieste };

        var frase = state.Schema.Compose([.. state.Phrases[e.PhraseIndex].Slots.Select(s => s!.Text)]);

        return new EngineResult(chiesto, [new RequestIllustration(e.PhraseIndex, frase)]);
    }

    private static EngineResult OnIllustrationFinished(GameState state, IllustrationFinished e)
    {
        // Stessa guardia della rifinitura: se la stanza è ripartita, questo
        // esito appartiene a una partita che non c'è più. Nessun errore verso
        // il client: non l'ha chiesto nessun giocatore, è un evento interno.
        if (state.Phase != RoomPhase.Finished)
        {
            return EngineResult.NoChange(state);
        }

        if (e.Path is not null)
        {
            return new EngineResult(state, [
                new BroadcastToRoom(new IllustrationReadyMessage(e.PhraseIndex, e.Path)),
            ]);
        }

        // Togliere l'indice è ciò che riaccende il pulsante: senza, l'host
        // resterebbe con un'attesa che non finisce e nessun modo di riprovare.
        var chieste = new HashSet<int>(state.IllustrationsRequested);
        chieste.Remove(e.PhraseIndex);

        return new EngineResult(state with { IllustrationsRequested = chieste }, [
            new BroadcastToRoom(new IllustrationFailedMessage(
                e.PhraseIndex,
                "L'illustrazione non è arrivata. Si può riprovare.")),
        ]);
    }
}
```

In `src/FrasiSquisite.Domain/Engine/GameEngine.cs`, nel `switch` di `Handle`, dopo `RefinementFinished`:

```csharp
        IllustrationRequested e => OnIllustrationRequested(state, e),
        IllustrationFinished e => OnIllustrationFinished(state, e),
```

- [ ] **Passo 6: Azzeramento a nuova partita e ritorno in lobby**

In `src/FrasiSquisite.Domain/Engine/GameEngine.Room.cs`, in `AzzeraPerNuovaPartita` e nella costruzione dello stato di ritorno in lobby, aggiungere alla `with`:

```csharp
            IllustrationsRequested = new HashSet<int>(),
```

Leggere prima entrambi i metodi: se uno dei due costruisce lo stato con `GameState.NewRoom`, lì l'azzeramento c'è già e non va ripetuto.

- [ ] **Passo 7: Eseguire i test**

```bash
dotnet test FrasiSquisite.slnx
```

Atteso: verde. Se `ProtocolContractTests` è rosso sul numero di versione, è il test vecchio da aggiornare (passo 1).

- [ ] **Passo 8: Commit**

```bash
git add src tests && git commit -m "feat(ai): illustrazione come effetto ed evento, protocollo v7"
```

---

### Task 2: Il provider delle immagini, e il degrado

**File:**
- Modificare: `src/FrasiSquisite.Server/Ai/AiOptions.cs`
- Creare: `src/FrasiSquisite.Server/Ai/IAiImageProvider.cs`, `DisabledAiImageProvider.cs`, `OpenAiCompatibleImageProvider.cs`
- Modificare: `src/FrasiSquisite.Server/Program.cs`
- Test: `tests/FrasiSquisite.Server.Tests/Ai/OpenAiCompatibleImageProviderTests.cs` (nuovo), `AiConfigurazioneTests.cs`

**Interfacce prodotte:**
```csharp
public interface IAiImageProvider
{
    Task<byte[]?> GeneraAsync(string promptInglese, CancellationToken ct);
}
```

**Perché torna i byte e non l'indirizzo.** ppq.ai risponde con un URL **firmato e a scadenza** (`…&exp=…`). Passarlo ai client vorrebbe dire mostrargli un riquadro rotto poco dopo (spec §5). Il download avviene qui, dentro il provider, perché è l'unico punto che sa che quel fornitore risponde con un URL invece che con dei byte: un fornitore diverso potrebbe rispondere in base64, e il resto del codice non deve accorgersene.

**Perché un'interfaccia separata da `IAiTextProvider`.** L'endpoint è `/v1/images/generations`, non `/chat/completions`, e la compatibilità OpenAI fra fornitori diversi è solida sul testo e meno garantita sulle immagini (spec §7). Unirle costringerebbe a un metodo che per metà delle implementazioni non ha senso.

- [ ] **Passo 1: Scrivere i test che falliscono**

Creare `tests/FrasiSquisite.Server.Tests/Ai/OpenAiCompatibleImageProviderTests.cs`. Il finto `HttpMessageHandler` deve rispondere a **due** richieste diverse: la generazione e il download.

```csharp
using System.Net;
using System.Text;
using FrasiSquisite.Server.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FrasiSquisite.Server.Tests.Ai;

public class OpenAiCompatibleImageProviderTests
{
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3];

    /// <summary>
    /// Risponde in base al percorso: la generazione torna il JSON passato, il
    /// resto torna i byte. Un handler che rispondesse sempre uguale non
    /// distinguerebbe i due passi, che è proprio ciò che va provato.
    /// </summary>
    private sealed class FintoHandler(string jsonGenerazione, HttpStatusCode codiceGenerazione = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public List<string> PercorsiChiamati { get; } = [];

        public HttpStatusCode CodiceDownload { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            PercorsiChiamati.Add(request.RequestUri!.AbsolutePath);

            if (request.RequestUri!.AbsolutePath.Contains("images/generations", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(codiceGenerazione)
                {
                    Content = new StringContent(jsonGenerazione, Encoding.UTF8, "application/json"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(CodiceDownload)
            {
                Content = new ByteArrayContent(Png),
            });
        }
    }

    private static OpenAiCompatibleImageProvider Provider(FintoHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.esempio/") },
            Options.Create(new AiOptions { ImageModel = "nano-banana-2", ImageSize = "1K", TimeoutSeconds = 10 }),
            NullLogger<OpenAiCompatibleImageProvider>.Instance);

    private const string RispostaBuona =
        """{"data":[{"url":"https://api.esempio/v1/media/xyz?exp=1","content_type":"image/png"}],"cost":0.092}""";

    [Fact]
    public async Task ScaricaIByteDellImmagineGenerata()
    {
        var handler = new FintoHandler(RispostaBuona);

        var byteOttenuti = await Provider(handler).GeneraAsync("a penguin in a suit", CancellationToken.None);

        Assert.Equal(Png, byteOttenuti);
        Assert.Equal(2, handler.PercorsiChiamati.Count);
    }

    [Fact]
    public async Task UnaRispostaDiErroreDelFornitoreTornaNull()
    {
        var handler = new FintoHandler("""{"error":"no credit"}""", HttpStatusCode.PaymentRequired);

        Assert.Null(await Provider(handler).GeneraAsync("x", CancellationToken.None));
    }

    /// <summary>
    /// Le forme che JsonElement può far esplodere con un fornitore terzo:
    /// "data" assente, "data" non array, "url" di tipo sbagliato. Sono le
    /// stesse che avevano fatto passare un difetto Critico nel provider di
    /// testo, dove il catch non prendeva InvalidOperationException.
    /// </summary>
    [Theory]
    [InlineData("""{"cost":0.09}""")]
    [InlineData("""{"data":null}""")]
    [InlineData("""{"data":[]}""")]
    [InlineData("""{"data":[{"url":42}]}""")]
    [InlineData("non è json")]
    public async Task UnaRispostaDiFormaInattesaTornaNull(string corpo)
    {
        Assert.Null(await Provider(new FintoHandler(corpo)).GeneraAsync("x", CancellationToken.None));
    }

    [Fact]
    public async Task SeIlDownloadFallisceTornaNull()
    {
        var handler = new FintoHandler(RispostaBuona) { CodiceDownload = HttpStatusCode.Forbidden };

        Assert.Null(await Provider(handler).GeneraAsync("x", CancellationToken.None));
    }

    [Fact]
    public async Task SenzaChiaveIlProviderSpentoTornaNullSenzaChiamareNessuno()
    {
        Assert.Null(await new DisabledAiImageProvider().GeneraAsync("x", CancellationToken.None));
    }
}
```

- [ ] **Passo 2: Eseguire i test e vederli fallire**

```bash
dotnet test tests/FrasiSquisite.Server.Tests --filter "FullyQualifiedName~ImageProvider"
```

Atteso: errore di compilazione, i tipi non esistono.

- [ ] **Passo 3: Le opzioni**

In `src/FrasiSquisite.Server/Ai/AiOptions.cs`, dopo `TextModel`:

```csharp
    public string ImageModel { get; set; } = "nano-banana-2";

    /// <summary>
    /// 1K basta per un telefono e costa circa nove centesimi; 2K e 4K costano
    /// di più senza che si veda la differenza su uno schermo da sei pollici.
    /// </summary>
    public string ImageSize { get; set; } = "1K";

    /// <summary>
    /// Generare un'immagine richiede molto più tempo che correggere un testo, e
    /// il limite della rifinitura (dieci secondi) la farebbe fallire sempre.
    /// Qui non c'è una partita che aspetta: l'host ha premuto un pulsante e sta
    /// guardando una rotellina, quindi si può essere pazienti.
    /// </summary>
    public int ImageTimeoutSeconds { get; set; } = 90;
```

- [ ] **Passo 4: L'interfaccia e il provider spento**

Creare `src/FrasiSquisite.Server/Ai/IAiImageProvider.cs`:

```csharp
namespace FrasiSquisite.Server.Ai;

/// <summary>
/// Separata da <see cref="IAiTextProvider"/> perché l'endpoint è un altro
/// (/v1/images/generations) e la compatibilità OpenAI fra fornitori è meno
/// garantita sulle immagini che sul testo (spec §7).
///
/// Torna i byte e non un indirizzo: quello che restituisce ppq.ai è firmato e
/// scade, e passarlo ai client significherebbe mostrargli un riquadro rotto
/// poco dopo. Chi implementa decide come procurarseli.
///
/// Non lancia mai: qualunque guasto è <c>null</c>.
/// </summary>
public interface IAiImageProvider
{
    Task<byte[]?> GeneraAsync(string promptInglese, CancellationToken ct);
}
```

Creare `src/FrasiSquisite.Server/Ai/DisabledAiImageProvider.cs`:

```csharp
namespace FrasiSquisite.Server.Ai;

/// <summary>
/// Il degrado come implementazione e non come <c>if</c>: senza chiave si
/// registra questo, e nessun altro file sa che l'AI è spenta.
/// </summary>
public sealed class DisabledAiImageProvider : IAiImageProvider
{
    public Task<byte[]?> GeneraAsync(string promptInglese, CancellationToken ct) =>
        Task.FromResult<byte[]?>(null);
}
```

- [ ] **Passo 5: Il provider vero**

Creare `src/FrasiSquisite.Server/Ai/OpenAiCompatibleImageProvider.cs`:

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FrasiSquisite.Server.Ai;

/// <summary>
/// Due passi in uno: genera, poi scarica. Il fornitore risponde con un URL
/// firmato che scade, quindi i byte vanno presi subito (spec §5).
/// </summary>
public sealed class OpenAiCompatibleImageProvider(
    HttpClient http,
    IOptions<AiOptions> opzioni,
    ILogger<OpenAiCompatibleImageProvider> logger) : IAiImageProvider
{
    private readonly AiOptions _opzioni = opzioni.Value;

    public async Task<byte[]?> GeneraAsync(string promptInglese, CancellationToken ct)
    {
        try
        {
            var richiesta = new
            {
                model = _opzioni.ImageModel,
                prompt = promptInglese,
                n = 1,
                size = _opzioni.ImageSize,
            };

            using var risposta = await http.PostAsJsonAsync("/v1/images/generations", richiesta, ct);

            if (!risposta.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Il fornitore ha risposto {Codice} alla generazione dell'immagine.",
                    (int)risposta.StatusCode);
                return null;
            }

            using var documento = JsonDocument.Parse(await risposta.Content.ReadAsStringAsync(ct));

            var indirizzo = documento.RootElement
                .GetProperty("data")[0]
                .GetProperty("url")
                .GetString();

            if (string.IsNullOrWhiteSpace(indirizzo))
            {
                logger.LogWarning("Generazione senza indirizzo nella risposta.");
                return null;
            }

            using var immagine = await http.GetAsync(indirizzo, ct);

            if (!immagine.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Download dell'immagine fallito con {Codice}: l'indirizzo firmato può essere già scaduto.",
                    (int)immagine.StatusCode);
                return null;
            }

            return await immagine.Content.ReadAsByteArrayAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
            or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException or UriFormatException)
        {
            // Stessa lista del provider di testo, e per la stessa ragione: su
            // un documento sintatticamente valido JsonElement lancia
            // KeyNotFoundException (proprietà assente), IndexOutOfRangeException
            // (indice fuori range) o InvalidOperationException (tipo sbagliato,
            // es. "data": null oppure "url": 42). InvalidOperationException in
            // particolare era il difetto Critico trovato nel provider di testo.
            // UriFormatException si aggiunge qui perché l'indirizzo arriva dal
            // fornitore e non da noi.
            logger.LogWarning(ex, "Generazione dell'immagine fallita.");
            return null;
        }
    }
}
```

- [ ] **Passo 6: Registrazione**

In `src/FrasiSquisite.Server/Program.cs`, dentro il ramo `if (aiOptions.Abilitato)` esistente, dopo la registrazione del provider di testo:

```csharp
    builder.Services.AddHttpClient<IAiImageProvider, OpenAiCompatibleImageProvider>(c =>
    {
        c.BaseAddress = new Uri(aiOptions.BaseUrl);
        c.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", aiOptions.ApiKey);

        // Non TimeoutSeconds: quello è il limite della rifinitura, dieci
        // secondi, e generare un'immagine ne richiede molti di più.
        c.Timeout = TimeSpan.FromSeconds(aiOptions.ImageTimeoutSeconds);
    });
```

e nel ramo `else`:

```csharp
    builder.Services.AddSingleton<IAiImageProvider, DisabledAiImageProvider>();
```

- [ ] **Passo 7: Estendere il test di configurazione**

In `tests/FrasiSquisite.Server.Tests/Ai/AiConfigurazioneTests.cs` aggiungere, sullo stesso modello dei test esistenti per `IAiTextProvider`, due test: senza chiave il servizio risolto è `DisabledAiImageProvider`; con chiave è `OpenAiCompatibleImageProvider`.

- [ ] **Passo 8: Eseguire i test e committare**

```bash
dotnet test FrasiSquisite.slnx
```

```bash
git add src tests && git commit -m "feat(ai): provider delle immagini, degrado come implementazione"
```

---

### Task 3: `IllustrationRunner` — tradurre, poi disegnare

**File:**
- Creare: `src/FrasiSquisite.Server/Ai/IllustrationRunner.cs`
- Modificare: `src/FrasiSquisite.Server/Program.cs`
- Test: `tests/FrasiSquisite.Server.Tests/Ai/IllustrationRunnerTests.cs` (nuovo)

**Interfacce consumate:** `IAiTextProvider.CompletaAsync`, `IAiImageProvider.GeneraAsync`, `AiOptions`.
**Interfacce prodotte:** `Task<byte[]?> IllustraAsync(string fraseItaliana, CancellationToken ct)`.

**Perché due chiamate e non una** (spec §5). La frase è italiana, assurda, e contiene parti che un disegno non può usare: *"cosa dice la gente"* non aiuta un'immagine, la confonde. La prima chiamata traduce e **seleziona ciò che è disegnabile**; la seconda genera. Passare la frase intera a un generatore di immagini produrrebbe un collage illeggibile.

- [ ] **Passo 1: Scrivere i test che falliscono**

Creare `tests/FrasiSquisite.Server.Tests/Ai/IllustrationRunnerTests.cs`, riusando `FakeAiTextProvider` già presente nella cartella (leggerlo prima per usarne l'API):

```csharp
using FrasiSquisite.Server.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FrasiSquisite.Server.Tests.Ai;

public class IllustrationRunnerTests
{
    private sealed class FintoImageProvider(byte[]? risposta) : IAiImageProvider
    {
        public string? PromptRicevuto { get; private set; }

        public int Chiamate { get; private set; }

        public Task<byte[]?> GeneraAsync(string promptInglese, CancellationToken ct)
        {
            Chiamate++;
            PromptRicevuto = promptInglese;
            return Task.FromResult(risposta);
        }
    }

    private static readonly byte[] Png = [1, 2, 3];

    private static IllustrationRunner Runner(IAiTextProvider testo, IAiImageProvider immagine) =>
        new(testo, immagine, Options.Create(new AiOptions()), NullLogger<IllustrationRunner>.Instance);

    [Fact]
    public async Task TraduceEPoiGenera()
    {
        var testo = new FakeAiTextProvider("a penguin in a pinstripe suit assembling a bookshelf");
        var immagine = new FintoImageProvider(Png);

        var esito = await Runner(testo, immagine).IllustraAsync("Un pinguino in doppiopetto…", CancellationToken.None);

        Assert.Equal(Png, esito);
        Assert.Equal("a penguin in a pinstripe suit assembling a bookshelf", immagine.PromptRicevuto);
    }

    /// <summary>
    /// Se la traduzione non arriva non si genera niente: mandare la frase
    /// italiana grezza al generatore costerebbe comunque nove centesimi per
    /// produrre un collage. Meglio fallire e lasciare che l'host riprovi.
    /// </summary>
    [Fact]
    public async Task SenzaTraduzioneNonSiGeneraEQuindiNonSiSpende()
    {
        var immagine = new FintoImageProvider(Png);

        var esito = await Runner(new FakeAiTextProvider(null), immagine).IllustraAsync("qualcosa", CancellationToken.None);

        Assert.Null(esito);
        Assert.Equal(0, immagine.Chiamate);
    }

    [Fact]
    public async Task SeLaGenerazioneFallisceLEsitoENullo()
    {
        var testo = new FakeAiTextProvider("a penguin");

        var esito = await Runner(testo, new FintoImageProvider(null)).IllustraAsync("x", CancellationToken.None);

        Assert.Null(esito);
    }

    /// <summary>
    /// Un modello che risponde con un blocco markdown o con una frase davanti
    /// è la norma, non l'eccezione: il prompt vale come preghiera, la pulizia
    /// come garanzia.
    /// </summary>
    [Theory]
    [InlineData("```\na penguin\n```", "a penguin")]
    [InlineData("  a penguin  ", "a penguin")]
    [InlineData("\"a penguin\"", "a penguin")]
    public async Task LaTraduzioneVienePulitaPrimaDiEssereUsata(string grezza, string attesa)
    {
        var immagine = new FintoImageProvider(Png);

        await Runner(new FakeAiTextProvider(grezza), immagine).IllustraAsync("x", CancellationToken.None);

        Assert.Equal(attesa, immagine.PromptRicevuto);
    }

    [Fact]
    public async Task UnaTraduzioneVuotaNonFaGenerareNiente()
    {
        var immagine = new FintoImageProvider(Png);

        var esito = await Runner(new FakeAiTextProvider("   "), immagine).IllustraAsync("x", CancellationToken.None);

        Assert.Null(esito);
        Assert.Equal(0, immagine.Chiamate);
    }
}
```

**Nota per chi implementa:** se `FakeAiTextProvider` non accetta una risposta `null` o un valore fisso nel costruttore, estenderlo invece di crearne un secondo.

- [ ] **Passo 2: Eseguire i test e vederli fallire**

```bash
dotnet test tests/FrasiSquisite.Server.Tests --filter "FullyQualifiedName~IllustrationRunner"
```

- [ ] **Passo 3: Implementare**

Creare `src/FrasiSquisite.Server/Ai/IllustrationRunner.cs`:

```csharp
using Microsoft.Extensions.Options;

namespace FrasiSquisite.Server.Ai;

/// <summary>
/// Due chiamate, non una (spec §5): prima si traduce la frase italiana in una
/// descrizione visiva inglese tenendo solo ciò che si può disegnare, poi si
/// genera. La frase intera data al generatore produrrebbe un collage.
/// </summary>
public sealed class IllustrationRunner(
    IAiTextProvider testo,
    IAiImageProvider immagini,
    IOptions<AiOptions> opzioni,
    ILogger<IllustrationRunner> logger)
{
    private readonly AiOptions _opzioni = opzioni.Value;

    private const string Sistema = """
        Ricevi una frase surreale in italiano, scritta a più mani in un gioco.
        Trasformala in una descrizione visiva IN INGLESE per un generatore di
        immagini.

        REGOLE
        - Tieni solo ciò che si può disegnare: soggetti, luoghi, azioni,
          oggetti. Scarta ciò che non ha forma — commenti della gente,
          motivazioni, ciò che qualcuno dice, come è andata a finire.
        - Non rendere la scena sensata: l'assurdo è il punto. Se un pinguino
          indossa un doppiopetto, disegnalo col doppiopetto.
        - Niente testo, niente scritte, niente fumetti dentro l'immagine.
        - Una sola scena, non un collage.
        - Massimo quaranta parole.

        Rispondi con la sola descrizione, senza virgolette, senza blocchi di
        codice, senza spiegazioni.
        """;

    public async Task<byte[]?> IllustraAsync(string fraseItaliana, CancellationToken ct)
    {
        using var scadenza = CancellationTokenSource.CreateLinkedTokenSource(ct);
        scadenza.CancelAfter(TimeSpan.FromSeconds(_opzioni.ImageTimeoutSeconds));

        try
        {
            var grezza = await testo.CompletaAsync(Sistema, fraseItaliana, scadenza.Token);
            var prompt = Pulisci(grezza);

            if (string.IsNullOrWhiteSpace(prompt))
            {
                // Senza traduzione non si genera: mandare l'italiano grezzo
                // costerebbe comunque nove centesimi per un risultato che non
                // somiglierebbe alla frase.
                logger.LogWarning("Traduzione per l'illustrazione non arrivata: niente da generare.");
                return null;
            }

            return await immagini.GeneraAsync(prompt, scadenza.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Illustrazione scaduta dopo {Secondi}s.", _opzioni.ImageTimeoutSeconds);
            return null;
        }
    }

    /// <summary>
    /// I modelli incorniciano volentieri la risposta in un blocco markdown o
    /// fra virgolette. Scartarla per questo sarebbe uno spreco.
    /// </summary>
    private static string? Pulisci(string? risposta)
    {
        if (risposta is null)
        {
            return null;
        }

        var pulita = risposta.Trim().Trim('`').Trim().Trim('"').Trim();

        return pulita.Length == 0 ? null : pulita;
    }
}
```

- [ ] **Passo 4: Registrare**

In `Program.cs`, accanto a `AddSingleton<RefinementRunner>()`:

```csharp
builder.Services.AddSingleton<IllustrationRunner>();
```

- [ ] **Passo 5: Test e commit**

```bash
dotnet test FrasiSquisite.slnx
```

```bash
git add src tests && git commit -m "feat(ai): traduzione del disegnabile e generazione, in due chiamate"
```

---

### Task 4: I byte in memoria, e l'indirizzo per prenderli

**File:**
- Creare: `src/FrasiSquisite.Server/Images/ImageStore.cs`
- Modificare: `src/FrasiSquisite.Server/Program.cs`
- Test: `tests/FrasiSquisite.Server.Tests/Images/ImageStoreTests.cs` (nuovo), `tests/FrasiSquisite.Server.Tests/Realtime/IllustrazioneEndpointTests.cs` (nuovo)

**Interfacce prodotte:**
```csharp
public sealed class ImageStore
{
    public string Salva(byte[] byteImmagine);          // torna il percorso, es. "/illustrazioni/xY3…"
    public bool TryGet(string id, out byte[] byteImmagine);
}
```

**Tre decisioni, con il perché.**

*In memoria e non su disco.* Il container è deliberatamente senza stato (nessun volume): niente da montare, nessuna chiave di cifratura da custodire, nessuna pulizia da programmare. Un riavvio interrompe comunque la partita. Si discosta dalla §8.4 del design generale, che le voleva cifrate su disco, e il motivo va scritto nel codice.

*L'identificativo è casuale, non l'indice della frase.* Il codice stanza è di quattro caratteri e indovinabile: con l'indice, chiunque potrebbe pescare le illustrazioni di partite altrui provando codici a caso. L'identificativo **è** la credenziale, quindi va generato con un generatore crittografico e non con `Random`.

*C'è un tetto.* Senza, un server acceso da settimane accumula immagini finché non finisce la memoria — e il container ne ha poca. Cinquanta immagini da ~1,5 MB sono ~75 MB, che è il massimo che questo componente può occupare.

- [ ] **Passo 1: Scrivere i test che falliscono**

Creare `tests/FrasiSquisite.Server.Tests/Images/ImageStoreTests.cs`:

```csharp
using FrasiSquisite.Server.Images;
using Xunit;

namespace FrasiSquisite.Server.Tests.Images;

public class ImageStoreTests
{
    private static byte[] Immagine(byte seme) => [seme, 0, 1, 2];

    [Fact]
    public void QuelCheSiSalvaSiRilegge()
    {
        var deposito = new ImageStore();

        var percorso = deposito.Salva(Immagine(7));

        Assert.True(deposito.TryGet(Id(percorso), out var letti));
        Assert.Equal(Immagine(7), letti);
    }

    [Fact]
    public void UnIdentificativoInventatoNonTrovaNiente()
    {
        Assert.False(new ImageStore().TryGet("non-esiste", out _));
    }

    /// <summary>
    /// L'identificativo È la credenziale: chi ce l'ha vede l'immagine. Due
    /// salvataggi non devono mai produrre lo stesso, e la lunghezza deve
    /// rendere inutile provare a indovinare.
    /// </summary>
    [Fact]
    public void GliIdentificativiSonoTuttiDiversiEAbbastanzaLunghi()
    {
        var deposito = new ImageStore();

        var identificativi = Enumerable.Range(0, 200)
            .Select(i => Id(deposito.Salva(Immagine((byte)i))))
            .ToList();

        Assert.Equal(identificativi.Count, identificativi.Distinct().Count());
        Assert.All(identificativi, id => Assert.True(id.Length >= 20, $"troppo corto: {id}"));
    }

    /// <summary>
    /// Senza tetto un server acceso da settimane riempie la memoria del
    /// container. La più vecchia esce: la partita a cui apparteneva è finita
    /// da un pezzo.
    /// </summary>
    [Fact]
    public void OltreIlTettoLaPiuVecchiaEsce()
    {
        var deposito = new ImageStore(tetto: 3);

        var primo = Id(deposito.Salva(Immagine(1)));
        var secondo = Id(deposito.Salva(Immagine(2)));
        deposito.Salva(Immagine(3));
        deposito.Salva(Immagine(4));

        Assert.False(deposito.TryGet(primo, out _));
        Assert.True(deposito.TryGet(secondo, out _));
    }

    [Fact]
    public void IlPercorsoEQuelloCheIlClientPuoChiamare()
    {
        Assert.StartsWith("/illustrazioni/", new ImageStore().Salva(Immagine(1)), StringComparison.Ordinal);
    }

    private static string Id(string percorso) => percorso["/illustrazioni/".Length..];
}
```

Creare `tests/FrasiSquisite.Server.Tests/Realtime/IllustrazioneEndpointTests.cs`, sullo stesso modello degli altri test che usano `WebApplicationFactory` (leggere `GameHubTests.cs` per la forma esatta della factory usata nel progetto):

```csharp
using System.Net;
using FrasiSquisite.Server.Images;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FrasiSquisite.Server.Tests.Realtime;

public class IllustrazioneEndpointTests(WebApplicationFactory<Program> fabbrica)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task UnIdentificativoValidoServeIByte()
    {
        var deposito = fabbrica.Services.GetRequiredService<ImageStore>();
        var percorso = deposito.Salva([9, 8, 7]);

        var risposta = await fabbrica.CreateClient().GetAsync(percorso);

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        Assert.Equal("image/png", risposta.Content.Headers.ContentType?.MediaType);
        Assert.Equal<byte[]>([9, 8, 7], await risposta.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task UnIdentificativoInventatoDa404()
    {
        var risposta = await fabbrica.CreateClient().GetAsync("/illustrazioni/inventato");

        Assert.Equal(HttpStatusCode.NotFound, risposta.StatusCode);
    }
}
```

- [ ] **Passo 2: Eseguire i test e vederli fallire**

```bash
dotnet test tests/FrasiSquisite.Server.Tests --filter "FullyQualifiedName~Illustrazione|FullyQualifiedName~ImageStore"
```

- [ ] **Passo 3: Implementare il deposito**

Creare `src/FrasiSquisite.Server/Images/ImageStore.cs`:

```csharp
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace FrasiSquisite.Server.Images;

/// <summary>
/// Le immagini vivono in memoria accanto alle stanze, non su disco (spec §5).
/// Il container è deliberatamente senza stato: nessun volume da montare,
/// nessuna chiave di cifratura da custodire, nessuna pulizia da programmare.
/// Un riavvio interrompe comunque la partita in corso, quindi perdere le
/// immagini insieme a essa non toglie niente a nessuno. Si discosta dalla
/// §8.4 del design generale, che le voleva cifrate su disco, per questo.
/// </summary>
public sealed class ImageStore(int tetto = ImageStore.TettoPredefinito)
{
    public const string Prefisso = "/illustrazioni/";

    /// <summary>
    /// Cinquanta immagini a 1K sono circa 75 MB: il massimo che questo
    /// componente può occupare. Senza un tetto un server acceso da settimane
    /// riempirebbe la memoria del container, che ne ha poca.
    /// </summary>
    private const int TettoPredefinito = 50;

    private readonly ConcurrentDictionary<string, byte[]> _immagini = new(StringComparer.Ordinal);

    /// <summary>Ordine d'inserimento, per sapere chi esce quando si sfora.</summary>
    private readonly ConcurrentQueue<string> _ordine = new();

    public string Salva(byte[] byteImmagine)
    {
        // L'identificativo È la credenziale: chi ce l'ha vede l'immagine.
        // Quindi RandomNumberGenerator e non Random, e sedici byte, che
        // rendono inutile provare a indovinare. Con l'indice della frase
        // sarebbe bastato provare codici stanza a caso per pescare le
        // illustrazioni di partite altrui (spec §5).
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        _immagini[id] = byteImmagine;
        _ordine.Enqueue(id);

        while (_ordine.Count > tetto && _ordine.TryDequeue(out var vecchio))
        {
            _immagini.TryRemove(vecchio, out _);
        }

        return Prefisso + id;
    }

    public bool TryGet(string id, out byte[] byteImmagine) => _immagini.TryGetValue(id, out byteImmagine!);
}
```

- [ ] **Passo 4: L'endpoint**

In `Program.cs`, con le altre registrazioni:

```csharp
builder.Services.AddSingleton<ImageStore>();
```

e accanto a `MapGet("/health", …)`:

```csharp
// Non passa da SignalR: un'immagine è un file, e il trasporto del gioco è per
// messaggi piccoli. L'identificativo nel percorso è l'unica credenziale, il
// che rende l'indirizzo condivisibile di proposito — chi ce l'ha, vede.
app.MapGet("/illustrazioni/{id}", (string id, ImageStore deposito) =>
    deposito.TryGet(id, out var byteImmagine)
        ? Results.File(byteImmagine, "image/png")
        : Results.NotFound());
```

- [ ] **Passo 5: Test e commit**

```bash
dotnet test FrasiSquisite.slnx
```

```bash
git add src tests && git commit -m "feat(ai): le illustrazioni in memoria, servite con identificativo casuale"
```

---

### Task 5: `GameHost` esegue, `GameHub` riceve

**File:**
- Modificare: `src/FrasiSquisite.Server/Realtime/GameHost.cs`, `GameHub.cs`
- Test: `tests/FrasiSquisite.Server.Tests/Realtime/GameHostTests.cs`, `GameHubTests.cs`

**Interfacce consumate:** `RequestIllustration` (Task 1), `IllustrationRunner.IllustraAsync` (Task 3), `ImageStore.Salva` (Task 4).

**Le tre garanzie da provare**, le stesse della rifinitura, perché lo stallo che evitano è lo stesso:

1. `EseguiAsync` **non attende** la generazione — `DispatchAsync` tiene il lucchetto della stanza per tutto il giro degli effetti, e l'evento di ritorno passa dallo stesso lucchetto. Attendere qui è stallo garantito.
2. `IllustrationFinished` viene comunque mandato, anche se il runner esplode.
3. Se la stanza è sparita nel frattempo si logga e basta.

- [ ] **Passo 1: Scrivere i test che falliscono**

In `tests/FrasiSquisite.Server.Tests/Realtime/GameHostTests.cs` aggiungere, ricalcando i test già presenti per `AvviaRifinitura` (leggerli prima: usano `FakeGameEngine` e `FakeRoomRegistry`, entrambi già in cartella):

```csharp
    /// <summary>
    /// Se l'host attendesse la generazione, terrebbe il lucchetto della stanza
    /// per tutta la durata della chiamata — e l'evento di ritorno, che passa
    /// da quello stesso lucchetto, non entrerebbe mai. Stallo. Il test lo
    /// dimostra con un runner lento: la dispatch deve tornare subito.
    /// </summary>
    [Fact]
    public async Task LaDispatchNonAspettaLaGenerazione()

    [Fact]
    public async Task AncheSeLaGenerazioneEsplodeLEventoDiRitornoArriva()

    [Fact]
    public async Task SeLaStanzaESparitaSiLoggaESiTiraAvanti()

    /// <summary>
    /// I byte non viaggiano nell'evento: vanno nel deposito, e l'evento porta
    /// il percorso. Il motore non deve mai vedere un PNG (spec §5).
    /// </summary>
    [Fact]
    public async Task IByteFinisconoNelDepositoELEventoPortaIlPercorso()
```

**Da scrivere per esteso ricalcando i test esistenti di `AvviaRifinitura`** — hanno la stessa forma, cambiano il tipo di effetto, il runner e l'evento atteso. Il quarto è nuovo: dopo la dispatch, verificare che `ImageStore.TryGet` sull'identificativo estratto dal percorso nell'evento restituisca gli stessi byte del finto provider.

In `GameHubTests.cs` aggiungere un test d'integrazione: partita fino alla classifica, l'host chiama `RequestIllustration`, e arriva `IllustrationReadyMessage` (con un provider finto registrato nella factory).

**Attenzione, corsa già vista in questo progetto.** I test d'integrazione che pilotano il reveal devono usare `AvanzaRevealFinoAlVotoAsync` invece di chiamare `AdvanceReveal` a occhio: con una fase che si esce da un `Task.Run` staccato, una chiamata troppo presto prende un errore e quel passo è perso per sempre. È il difetto Critico trovato nella revisione del lotto precedente — non reintrodurlo.

- [ ] **Passo 2: Eseguirli e vederli fallire**

- [ ] **Passo 3: Il ramo in `GameHost`**

Aggiungere due parametri al costruttore primario di `GameHost`, dopo `RefinementRunner runner`:

```csharp
    IllustrationRunner illustrazioni,
    ImageStore deposito,
```

e in `EseguiAsync`, accanto a `RequestRefinement`:

```csharp
        // Stessa ragione di RequestRefinement: non si attende, o il ritorno
        // andrebbe in stallo sul lucchetto della stanza.
        RequestIllustration i => AvviaIllustrazione(roomCode, i),
```

e il metodo:

```csharp
    /// <summary>
    /// Genera in sottofondo e torna subito. I byte finiscono nel deposito e
    /// l'evento di ritorno porta solo il percorso: il motore non vede mai un
    /// PNG (spec §5).
    /// </summary>
    private Task AvviaIllustrazione(string roomCode, RequestIllustration richiesta)
    {
        _ = Task.Run(async () =>
        {
            string? percorso = null;

            try
            {
                var byteImmagine = await illustrazioni.IllustraAsync(richiesta.Frase, CancellationToken.None);

                if (byteImmagine is not null)
                {
                    percorso = deposito.Salva(byteImmagine);
                }
            }
            catch (Exception ex)
            {
                // Task slegato: un'eccezione non osservata lascerebbe il
                // pulsante spento per sempre, e nessuno lo saprebbe.
                logger.LogError(ex, "Illustrazione fallita per la stanza {RoomCode}.", roomCode);
            }

            try
            {
                await DispatchAsync(roomCode, new IllustrationFinished(richiesta.PhraseIndex, percorso));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Esito dell'illustrazione non consegnabile alla stanza {RoomCode}.", roomCode);
            }
        });

        return Task.CompletedTask;
    }
```

- [ ] **Passo 4: Il metodo dell'hub**

In `GameHub.cs`, accanto a `CloseVoting`:

```csharp
    public Task RequestIllustration(RequestIllustrationRequest request) =>
        host.DispatchAsync(request.RoomCode, new IllustrationRequested(GiocatoreCorrente(), request.PhraseIndex));
```

Le guardie — fase, host, indice, doppio tocco — stanno tutte nel motore: l'hub inoltra e basta, come per il voto.

- [ ] **Passo 5: Test e commit**

```bash
dotnet test FrasiSquisite.slnx
```

```bash
git add src tests && git commit -m "feat(ai): GameHost genera l'illustrazione senza tenere il lucchetto"
```

---

### Task 6: Il pulsante, l'attesa, l'immagine

**File:**
- Modificare: `src/FrasiSquisite.App/Services/IGameConnection.cs`, `SignalRGameConnection.cs`
- Modificare: `src/FrasiSquisite.App/ViewModels/PhraseResultRowView.cs`, `GameSessionViewModel.cs`
- Modificare: `src/FrasiSquisite.App/Pages/GamePage.xaml`
- Test: `tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs`

**Il cambio di forma da capire prima di scrivere.** `PhraseResultRowView` oggi è immutabile: proprietà `get`-only riempite nel costruttore. Ora deve cambiare dopo essere stata creata — attesa, immagine pronta, fallimento — quindi diventa un `ObservableObject` con tre proprietà osservabili. Il resto (testo, voti, autori) resta immutabile: cambia solo ciò che deve.

**Interfacce prodotte:**
```csharp
Task RequestIllustrationAsync(string roomCode, int phraseIndex);   // IGameConnection
```

Su `PhraseResultRowView`: `int PhraseIndex`, `bool IsHost`, `bool IsWaiting`, `string? ImageUrl`, `bool CanRequest`.

- [ ] **Passo 1: Scrivere i test che falliscono**

In `tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs`, sullo stile dei test già presenti (leggere quelli del voto per la forma del finto `IGameConnection`):

```csharp
    [Fact]
    public void SoloLHostVedeIlPulsanteDellIllustrazione()

    [Fact]
    public async Task ChiedereLIllustrazioneMettelaRigaInAttesa()

    /// <summary>
    /// L'indirizzo che arriva è relativo: il server non sa sotto quale nome è
    /// raggiunto (c'è Caddy davanti). Il client lo combina col proprio
    /// ServerUrl, che è l'unico posto dove quell'informazione esiste davvero.
    /// </summary>
    [Fact]
    public void LIndirizzoRelativoDiventaAssolutoConIlServerUrl()

    [Fact]
    public void LImmagineProntaTogliLAttesaEMostraIlRiquadro()

    /// <summary>
    /// Senza questo il pulsante resterebbe spento per sempre dopo un guasto e
    /// l'host non potrebbe riprovare.
    /// </summary>
    [Fact]
    public void UnFallimentoRiaccendeIlPulsanteEDiceCosaEAndatoStorto()

    /// <summary>
    /// Un messaggio per una frase che non è in classifica non deve far
    /// esplodere niente: arriva dal server, e il client non lo controlla.
    /// </summary>
    [Fact]
    public void UnMessaggioPerUnaFraseSconosciutaVieneIgnorato()

    [Fact]
    public void RicominciareUnaPartitaPulisceLeImmagini()
```

Da scrivere per esteso: ognuno costruisce la ViewModel, le fa ricevere una `GameFinishedMessage` con due righe, e poi il messaggio in prova.

- [ ] **Passo 2: Eseguirli e vederli fallire**

- [ ] **Passo 3: La connessione**

In `IGameConnection.cs`:

```csharp
    /// <summary>Solo l'host: il server rifiuta gli altri con NOT_HOST.</summary>
    Task RequestIllustrationAsync(string roomCode, int phraseIndex);
```

In `SignalRGameConnection.cs`, ricalcando `CastVoteAsync`:

```csharp
    public Task RequestIllustrationAsync(string roomCode, int phraseIndex) =>
        InvocaAsync("RequestIllustration", new RequestIllustrationRequest(roomCode, phraseIndex));
```

(Usare il nome esatto del metodo di invocazione già presente nel file.)

- [ ] **Passo 4: La riga osservabile**

Riscrivere `src/FrasiSquisite.App/ViewModels/PhraseResultRowView.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using FrasiSquisite.Shared.Protocol;

namespace FrasiSquisite.App.ViewModels;

/// <summary>
/// Una riga della classifica finale. Testo, voti e autori arrivano già decisi
/// dal server e non cambiano; l'illustrazione sì, quindi la riga è diventata
/// osservabile — ma solo per le tre proprietà che cambiano davvero.
/// </summary>
public sealed partial class PhraseResultRowView(PhraseResultView risultato, bool isHost) : ObservableObject
{
    public int PhraseIndex { get; } = risultato.PhraseIndex;

    public string Text { get; } = risultato.Text;

    public bool IsWinner { get; } = risultato.IsWinner;

    public string VotesLabel { get; } = risultato.Votes == 1 ? "1 voto" : $"{risultato.Votes} voti";

    public string AuthorsLabel { get; } = risultato.Authors.Count == 0
        ? string.Empty
        : $"Scritta da: {string.Join(" · ", risultato.Authors)}";

    /// <summary>
    /// Il pulsante esiste solo per chi ospita: il server rifiuterebbe comunque
    /// gli altri, ma mostrare un pulsante che dà errore è una bugia.
    /// </summary>
    public bool IsHost { get; } = isHost;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRequest))]
    private bool _isWaiting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRequest))]
    private string? _imageUrl;

    public bool CanRequest => IsHost && !IsWaiting && ImageUrl is null;
}
```

- [ ] **Passo 5: La ViewModel**

Nella costruzione delle righe, passare `IsHost`:

```csharp
            case GameFinishedMessage finale:
                FinalResults.Clear();
                foreach (var risultato in finale.Results)
                {
                    FinalResults.Add(new PhraseResultRowView(risultato, IsHost));
                }

                Screen = ScreenState.Finished;
                break;
```

(Verificare il nome esatto della proprietà che dice se il giocatore corrente ospita: nel file esiste già per il pulsante di avvio partita.)

Aggiungere i due casi:

```csharp
            case IllustrationReadyMessage pronta:
                if (RigaDiFrase(pronta.PhraseIndex) is { } riga)
                {
                    riga.IsWaiting = false;
                    // L'indirizzo arriva relativo perché il server non sa sotto
                    // quale nome è raggiunto: davanti c'è un reverse proxy.
                    // ServerUrl è l'unico posto dove quell'informazione c'è.
                    riga.ImageUrl = new Uri(new Uri(ServerUrl), pronta.Path).ToString();
                }

                break;

            case IllustrationFailedMessage fallita:
                if (RigaDiFrase(fallita.PhraseIndex) is { } rigaFallita)
                {
                    rigaFallita.IsWaiting = false;
                }

                ErrorText = fallita.Message;
                break;
```

e il metodo privato:

```csharp
    /// <summary>
    /// Torna null se l'indice non è in classifica. Non è paranoia: il messaggio
    /// arriva dal server e il client non ha modo di verificarlo, e una riga
    /// cercata per posizione in una lista ordinata per voti sarebbe comunque
    /// la riga sbagliata.
    /// </summary>
    private PhraseResultRowView? RigaDiFrase(int phraseIndex) =>
        FinalResults.FirstOrDefault(r => r.PhraseIndex == phraseIndex);
```

Il comando:

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

In `PulisciStatoDiPartitaConclusa` non serve altro: `FinalResults.Clear()` porta via anche le immagini.

- [ ] **Passo 6: La schermata**

In `src/FrasiSquisite.App/Pages/GamePage.xaml`, dentro il `DataTemplate` della classifica (accanto a `AuthorsLabel`, riga ~443):

```xml
                                    <Button Text="Illustra"
                                            Command="{Binding Source={RelativeSource AncestorType={x:Type vm:GameSessionViewModel}}, Path=RequestIllustrationCommand}"
                                            CommandParameter="{Binding .}"
                                            IsVisible="{Binding CanRequest}" />

                                    <ActivityIndicator IsRunning="{Binding IsWaiting}"
                                                       IsVisible="{Binding IsWaiting}" />

                                    <Image Source="{Binding ImageUrl}"
                                           Aspect="AspectFit"
                                           HeightRequest="240"
                                           IsVisible="{Binding ImageUrl, Converter={StaticResource NotEmpty}}" />
```

Verificare il prefisso di namespace già usato nel file per la ViewModel (`vm:` è un'ipotesi) e che il convertitore `NotEmpty` sia già fra le risorse — lo è, lo usa `AuthorsLabel`.

- [ ] **Passo 7: Test, build MAUI e commit**

```bash
dotnet test FrasiSquisite.slnx
```

```bash
dotnet build src/FrasiSquisite.App/FrasiSquisite.App.csproj -f net10.0-android
```

```bash
git add src tests && git commit -m "feat(ai): pulsante e riquadro dell'illustrazione nella classifica"
```

---

### Task 7: In produzione, e la prova che conta

Nessun codice nuovo. Va fatto **dopo** il merge su `main`.

- [ ] **Passo 1: Aggiornare il container**

```bash
ssh enrico@192.168.86.115 'cd ~/apps/frasi-squisite && git pull --ff-only && docker compose up -d --build'
```

Non serve toccare il `.env`: la chiave è la stessa, e `ImageModel` ha un valore predefinito.

- [ ] **Passo 2: Costruire e installare l'APK**

**Obbligatorio:** il protocollo passa a v7 e l'APK v6 viene rifiutato.

```bash
dotnet build src/FrasiSquisite.App/FrasiSquisite.App.csproj -c Release -f net10.0-android -p:EmbedAssembliesIntoApk=true
```

```bash
adb install -r src/FrasiSquisite.App/bin/Release/net10.0-android/com.supere.frasisquisite-Signed.apk
```

- [ ] **Passo 3: La partita di prova, con la chiave**

Da verificare, e sono tutte cose che i test non possono dimostrare:

1. Il pulsante "Illustra" compare **solo** sul telefono dell'host.
2. L'immagine somiglia alla frase, e non è un collage di tutte le caselle.
3. L'immagine compare **su tutti i telefoni**, non solo su quello dell'host.
4. Un secondo tocco sulla stessa frase non genera una seconda immagine — e quindi non spende altri nove centesimi.
5. Due frasi diverse si possono illustrare entrambe.
6. Dopo "Nuova partita" le immagini spariscono.

- [ ] **Passo 4: La prova del degrado**

```bash
ssh enrico@192.168.86.115 'cd ~/apps/frasi-squisite && mv .env .env.spento && docker compose up -d'
```

Senza chiave: la partita arriva in fondo, il pulsante compare, e il tocco produce **un messaggio di errore** — non un'attesa infinita e non un pulsante che resta spento. Si deve poter riprovare.

Per riaccendere: `mv .env.spento .env && docker compose up -d`.

- [ ] **Passo 5: Guardare quanto è costato**

```bash
ssh enrico@192.168.86.115 'cd ~/apps/frasi-squisite && set -a && . ./.env && set +a && curl -s -X POST https://api.ppq.ai/credits/balance -H "Authorization: Bearer $AI_API_KEY"'
```

Il costo non ha un tetto (spec §11): l'host può illustrare una frase per riga. Se in una serata diventasse un problema, il posto dove metterlo è il motore, come limite per stanza — non il client, che non è la fonte della verità.

---

## Cosa resta fuori, di proposito

- **La persistenza delle immagini.** Vivono quanto il server.
- **Un tetto di spesa.** Vedi sopra: si aggiunge se serve, e si vede solo giocando.
- **La rigenerazione** di un'immagine che non piace: costerebbe due volte e non è stata chiesta.
- **Il flaky intermittente di `GameHubTests`**, ancora senza diagnosi. Questo lotto aggiunge altri test d'integrazione: se si ripresenta, va inseguito allora.
