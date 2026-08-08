# Rientro in partita — Piano di implementazione

> **Per chi esegue:** SOTTO-SKILL RICHIESTA: usare superpowers:subagent-driven-development (consigliata) o superpowers:executing-plans, un task alla volta. I passi usano caselle (`- [ ]`).

**Obiettivo:** un giocatore disconnesso (schermo spento, rete che va e viene, app chiusa del tutto) può rientrare nella stessa partita entro 30 secondi, sulla stessa schermata in cui l'ha lasciata, senza fare nulla di esplicito.

**Architettura:** il server aspetta 30s (periodo di grazia, gestito da `GameHost` con lo stesso pattern già usato per rifinitura/illustrazione — timer sganciato che rientra come evento) prima di dispatchare `PlayerLeft`. Un nuovo evento `PlayerRejoined`, gestito dal motore puro, rimette `IsConnected` a vero e rimanda al giocatore rientrato lo stesso messaggio di fase che avrebbe già ricevuto restando connesso (riuso totale dei tipi di messaggio esistenti, nessun formato nuovo per il contenuto). Il client persiste il `RoomCode` (il `PlayerId` lo è già) e tenta il rientro sia all'avvio sia al ritorno della connessione.

**Tech Stack:** .NET 10, MAUI (`net10.0-android`), ASP.NET Core, SignalR, xUnit 2.9.3.

**Riferimento:** [design](../specs/2026-08-08-rientro-in-partita-design.md).

## Vincoli globali

Valgono per ogni task, senza ripeterli.

- **Il motore resta puro.** Niente I/O, niente `async`, niente orologio, niente casualità non iniettata dentro `FrasiSquisite.Domain`. Il periodo di grazia vive in `GameHost`, mai nel motore — stesso principio già applicato al timeout della rifinitura/illustrazione.
- **Nessun formato nuovo per il contenuto del rientro.** Il messaggio che un giocatore rientrato riceve è ricostruito dalle funzioni che il motore ha già (`SlotRequestFor`, `FrammentiReveal`, `FrasiComposte`, `Classifica`) — mai un DTO "istantanea di tutto".
- **Protocollo: 8 → 9**, uguaglianza stretta (`ProtocolVersion.IsCompatible`).
- **Un rifiuto del rientro non è un errore da mostrare.** `RejoinRejectedMessage` non passa mai dal banner rosso (`EseguiComandoAsync`): dal punto di vista dell'utente non è un errore, è solo "quella partita non c'è più".
- **Lingua**: codice, commenti, messaggi di commit e testo a schermo in italiano, come il resto del progetto. I commenti spiegano il *perché*, non il *cosa*.
- **Firma dei commit**: `commit.gpgsign` è attivo. Se 1Password è bloccato, **fermarsi e segnalarlo**; mai `--no-gpg-sign`.
- **Comando dei test**: `dotnet test FrasiSquisite.slnx` (estensione `.slnx`, non `.sln`). A differenza del lotto del reveal fluido, qui ogni task è additivo o isolato a un solo progetto — la soluzione intera resta verde dopo ciascuno dei quattro task, non solo alla fine.
- **Punto di partenza**: 800 test verdi (Shared 83, Domain 511, App 105, Server 101).
- **Nessun test automatico copre `.xaml`/il ciclo di vita MAUI o il comportamento reale del sistema operativo** in questo repository. Il Task 5 include passi di verifica manuale esplicitamente non automatizzati.

---

## Struttura dei file

**Domain (motore puro)**
- `src/FrasiSquisite.Domain/Engine/GameEngine.Players.cs` — nuovo evento `PlayerRejoined`, nuovo handler `OnPlayerRejoined`, nuovo helper `MessaggioDiRipristino`.
- `src/FrasiSquisite.Domain/Engine/GameEngine.Writing.cs` — `SlotRequests` refattorizzato per riusare un nuovo helper `SlotRequestFor(state, playerIndex)` — stesso output, DRY con `MessaggioDiRipristino`.
- `src/FrasiSquisite.Domain/Engine/GameEngine.cs` — un case in più nello switch di `Handle`.

**Shared (contratto)**
- `src/FrasiSquisite.Shared/Protocol/ClientMessages.cs` — nuovo `RejoinRoomRequest`.
- `src/FrasiSquisite.Shared/Protocol/ServerMessages.cs` — nuovo `RejoinRejectedMessage`.
- `src/FrasiSquisite.Shared/Protocol/ProtocolVersion.cs` — `Current` da 8 a 9.

**Server**
- `src/FrasiSquisite.Server/Realtime/GracePeriodTimer.cs` — nuovo file: `IGracePeriodTimer` e la sua implementazione reale, per rendere il periodo di grazia testabile senza un vero `Task.Delay` di 30s.
- `src/FrasiSquisite.Server/Realtime/GameHost.cs` — nuova dipendenza opzionale `IGracePeriodTimer`, nuova tabella dei periodi di grazia pendenti, `AvviaPeriodoDiGrazia`/`AnnullaPeriodoDiGrazia`.
- `src/FrasiSquisite.Server/Realtime/GameHub.cs` — `OnDisconnectedAsync` avvia il periodo di grazia invece di dispatchare `PlayerLeft` subito; nuovo metodo `RejoinRoom`.
- `src/FrasiSquisite.Server/Program.cs` — registrazione DI di `IGracePeriodTimer`.

**App (client MAUI)**
- `src/FrasiSquisite.App/Services/IRoomSession.cs` — nuova interfaccia, persistenza del `RoomCode`.
- `src/FrasiSquisite.App/Services/PreferencesRoomSession.cs` — implementazione di produzione su `Preferences.Default`.
- `src/FrasiSquisite.App/Services/IGameConnection.cs` — nuovo evento `Reconnected`, nuovo metodo `RejoinRoomAsync`.
- `src/FrasiSquisite.App/Services/SignalRGameConnection.cs` — implementa `Reconnected` (separato da `ConnectionInterrupted`), `RejoinRoomAsync`, nuovo case nel deserializzatore.
- `src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs` — nuova dipendenza `IRoomSession`, `TryRejoinAsync()`, persistenza del `RoomCode` a ogni `RoomStateMessage`, gestione di `RejoinRejectedMessage`.
- `src/FrasiSquisite.App/MauiProgram.cs` — registrazione DI di `IRoomSession`.
- `src/FrasiSquisite.App/Pages/GamePage.xaml.cs` — `OnAppearing` chiama `TryRejoinAsync()`.
- `src/FrasiSquisite.App/App.xaml.cs` — `Window.Resumed` chiama `TryRejoinAsync()`.

**Test**
- `tests/FrasiSquisite.Domain.Tests/Engine/RientroTests.cs` — nuovo file.
- `tests/FrasiSquisite.Shared.Tests/Protocol/ProtocolContractTests.cs` — catena di versione estesa a v9, roundtrip dei due nuovi tipi.
- `tests/FrasiSquisite.Server.Tests/Realtime/GameHostTests.cs` — periodo di grazia: scade e dispatcha, annullato e non dispatcha nulla, tabella per-istanza.
- `tests/FrasiSquisite.Server.Tests/Realtime/GameHubTests.cs` — `IGracePeriodTimer` di test nella factory di default (nessun test esistente aspetta 30 secondi veri), nuovi test su `RejoinRoom`.
- `tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs` — `IRoomSession` finto, tentativo di rientro, `RejoinRejectedMessage`.
- `tests/FrasiSquisite.App.Tests/FakeRoomSession.cs` — nuovo file.
- `tests/FrasiSquisite.App.Tests/FakeGameConnection.cs` — nuovo evento `Reconnected`, nuovo metodo `RejoinRoomAsync`.

---

### Task 1: `PlayerRejoined` nel motore puro

**File:**
- Modificare: `src/FrasiSquisite.Domain/Engine/GameEngine.Players.cs`
- Modificare: `src/FrasiSquisite.Domain/Engine/GameEngine.Writing.cs`
- Modificare: `src/FrasiSquisite.Domain/Engine/GameEngine.cs`
- Test: `tests/FrasiSquisite.Domain.Tests/Engine/RientroTests.cs` (nuovo)

**Interfacce:**
- Produce: `PlayerRejoined(Guid PlayerId) : GameEvent`. Nessun nuovo tipo di messaggio: gli effetti usano `SlotRequestMessage`, `RevealStepMessage`, `VoteRequestMessage`, `GameFinishedMessage` — tutti già esistenti.

Task isolato al progetto Domain: nessun altro progetto referenzia `PlayerRejoined` finché non arriva il Task 3. `dotnet test FrasiSquisite.slnx` resta verde per tutto questo task.

- [ ] **Passo 1: Scrivere i test che falliscono**

Creare `tests/FrasiSquisite.Domain.Tests/Engine/RientroTests.cs`:

```csharp
using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Shared.Protocol;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Engine;

public class RientroTests
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
    public void IlRientroRimetteIsConnectedAVero()
    {
        var stato = PartitaAvviata(n: 3, k: 5);
        stato = _motore.Handle(stato, new PlayerLeft(Giocatore(1))).State;

        var risultato = _motore.Handle(stato, new PlayerRejoined(Giocatore(1)));

        Assert.True(risultato.State.FindPlayer(Giocatore(1))!.IsConnected);
    }

    [Fact]
    public void IlRientroInScritturaMandaLaCasellaCorrenteSoloAChiRientra()
    {
        var stato = PartitaAvviata(n: 3, k: 5);
        stato = _motore.Handle(stato, new PlayerLeft(Giocatore(1))).State;

        var risultato = _motore.Handle(stato, new PlayerRejoined(Giocatore(1)));

        var richiesta = Assert.Single(risultato.MessagesTo<SlotRequestMessage>(Giocatore(1)));
        Assert.Equal(0, richiesta.Round);
        Assert.Equal("Ruolo0", richiesta.Ruolo);
        Assert.Empty(risultato.MessagesTo<SlotRequestMessage>(Giocatore(0)));
        Assert.Empty(risultato.MessagesTo<SlotRequestMessage>(Giocatore(2)));
    }

    [Fact]
    public void IlRientroDuplicatoNonCambiaNulla()
    {
        var stato = PartitaAvviata(n: 3, k: 5);
        stato = _motore.Handle(stato, new PlayerLeft(Giocatore(1))).State;
        stato = _motore.Handle(stato, new PlayerRejoined(Giocatore(1))).State;

        var risultato = _motore.Handle(stato, new PlayerRejoined(Giocatore(1)));

        Assert.True(risultato.State.FindPlayer(Giocatore(1))!.IsConnected);
        Assert.Empty(risultato.MessagesTo<SlotRequestMessage>(Giocatore(1)));
    }

    [Fact]
    public void IlRientroDiUnGiocatoreInesistenteNonProduceEffetti()
    {
        var stato = PartitaAvviata(n: 3, k: 5);

        var risultato = _motore.Handle(stato, new PlayerRejoined(Giocatore(99)));

        Assert.Empty(risultato.Effects);
        Assert.Same(stato, risultato.State);
    }

    [Fact]
    public void IlRientroInRifinituraMandaSoloLoStatoStanza()
    {
        var stato = PartitaAvviata(n: 3, k: 2);
        for (var round = 0; round < 2; round++)
        {
            for (var g = 0; g < 3; g++)
            {
                stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(g), $"p{round}{g}")).State;
            }
        }
        Assert.Equal(RoomPhase.Refining, stato.Phase);

        stato = _motore.Handle(stato, new PlayerLeft(Giocatore(1))).State;

        var risultato = _motore.Handle(stato, new PlayerRejoined(Giocatore(1)));

        Assert.Single(risultato.Effects);
        Assert.IsType<BroadcastToRoom>(risultato.Effects[0]);
    }

    [Fact]
    public void IlRientroInRevealMandaIFrammentiCorrenti()
    {
        var stato = PartitaAvviata(n: 3, k: 3);
        for (var round = 0; round < 3; round++)
        {
            for (var g = 0; g < 3; g++)
            {
                stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(g), $"p{round}{g}")).State;
            }
        }
        stato = _motore.Handle(stato, new RefinementFinished(null)).State;
        stato = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0))).State;
        Assert.Equal(RoomPhase.Reveal, stato.Phase);

        stato = _motore.Handle(stato, new PlayerLeft(Giocatore(1))).State;

        var risultato = _motore.Handle(stato, new PlayerRejoined(Giocatore(1)));

        var passo = Assert.Single(risultato.MessagesTo<RevealStepMessage>(Giocatore(1)));
        Assert.Equal(0, passo.PhraseIndex);
        Assert.False(passo.PhraseComplete);
    }

    [Fact]
    public void IlRientroInVotoMandaLeFrasiComposte()
    {
        var stato = PartitaAvviata(n: 3, k: 3);
        for (var round = 0; round < 3; round++)
        {
            for (var g = 0; g < 3; g++)
            {
                stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(g), $"p{round}{g}")).State;
            }
        }
        stato = _motore.Handle(stato, new RefinementFinished(null)).State;
        for (var i = 0; i < 3 * 3; i++)
        {
            stato = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0))).State;
        }
        Assert.Equal(RoomPhase.Voting, stato.Phase);

        stato = _motore.Handle(stato, new PlayerLeft(Giocatore(1))).State;

        var risultato = _motore.Handle(stato, new PlayerRejoined(Giocatore(1)));

        var richiesta = Assert.Single(risultato.MessagesTo<VoteRequestMessage>(Giocatore(1)));
        Assert.Equal(3, richiesta.Phrases.Count);
    }

    [Fact]
    public void IlRientroAPartitaConclusaMandaLaClassifica()
    {
        var stato = PartitaAvviata(n: 3, k: 3);
        for (var round = 0; round < 3; round++)
        {
            for (var g = 0; g < 3; g++)
            {
                stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(g), $"p{round}{g}")).State;
            }
        }
        stato = _motore.Handle(stato, new RefinementFinished(null)).State;
        for (var i = 0; i < 3 * 3; i++)
        {
            stato = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0))).State;
        }
        for (var g = 0; g < 3; g++)
        {
            stato = _motore.Handle(stato, new VoteCast(Giocatore(g), 0)).State;
        }
        Assert.Equal(RoomPhase.Finished, stato.Phase);

        stato = _motore.Handle(stato, new PlayerLeft(Giocatore(1))).State;

        var risultato = _motore.Handle(stato, new PlayerRejoined(Giocatore(1)));

        var finale = Assert.Single(risultato.MessagesTo<GameFinishedMessage>(Giocatore(1)));
        Assert.Equal(3, finale.Results.Count);
    }
}
```

- [ ] **Passo 2: Eseguire i test e verificare che falliscano**

Run: `dotnet test tests/FrasiSquisite.Domain.Tests --filter "FullyQualifiedName~RientroTests"`
Expected: FAIL — `PlayerRejoined` non esiste (errore di compilazione).

- [ ] **Passo 3: Aggiungere l'evento**

In `src/FrasiSquisite.Domain/Engine/GameEngine.Players.cs`, dopo la riga `public sealed record PlayerLeft(Guid PlayerId) : GameEvent;` (riga 9), aggiungere:

```csharp

/// <summary>
/// Un giocatore disconnesso torna: rimette IsConnected a vero e fa
/// ripartire verso di lui il messaggio della fase corrente, come se non
/// fosse mai stato via (design rientro §3.2). Dispatchato da
/// GameHub.RejoinRoom dopo aver già verificato che il giocatore esista
/// nella stanza.
/// </summary>
public sealed record PlayerRejoined(Guid PlayerId) : GameEvent;
```

Alla fine dello stesso file, prima dell'ultima `}` di chiusura della classe (dopo `NextBotName`, riga 231), aggiungere:

```csharp

    private EngineResult OnPlayerRejoined(GameState state, PlayerRejoined e)
    {
        var giocatore = state.FindPlayer(e.PlayerId);
        if (giocatore is null)
        {
            // Difesa in profondità: GameHub verifica già che il giocatore
            // esista nella stanza prima di dispatchare questo evento.
            return EngineResult.NoChange(state);
        }

        if (giocatore.IsConnected)
        {
            // Rientro duplicato o in corsa: nessuna modifica, stesso schema
            // idempotente di OnPlayerJoined per un secondo ingresso.
            return new EngineResult(state, [new BroadcastToRoom(RoomState(state))]);
        }

        var giocatori = state.Players
            .Select(p => p.Id == e.PlayerId ? p with { IsConnected = true } : p)
            .ToList();

        var aggiornato = state with { Players = giocatori };

        List<Effect> effetti = [new BroadcastToRoom(RoomState(aggiornato))];

        if (MessaggioDiRipristino(aggiornato, e.PlayerId) is { } messaggio)
        {
            effetti.Add(new SendToPlayer(e.PlayerId, messaggio));
        }

        return new EngineResult(aggiornato, effetti);
    }

    /// <summary>
    /// Il messaggio che il giocatore rientrato avrebbe già ricevuto restando
    /// connesso, per la fase in cui si trova ora la stanza — null se la fase
    /// non ne prevede uno oltre allo stato stanza (lobby, rifinitura: niente
    /// da mostrare oltre a una lista di giocatori o uno spinner). Nessun
    /// formato nuovo: sono gli stessi tipi di messaggio già usati altrove nel
    /// motore, solo ricostruiti per un giocatore invece che mandati in
    /// broadcast o a chi ha appena fatto un'azione.
    /// </summary>
    private object? MessaggioDiRipristino(GameState state, Guid playerId) => state.Phase switch
    {
        RoomPhase.Writing => SlotRequestFor(state, state.IndexOfPlayer(playerId)),
        RoomPhase.Reveal => new RevealStepMessage(
            state.RevealPhraseIndex,
            state.Phrases.Count,
            FrammentiReveal(state.Schema, state.Phrases[state.RevealPhraseIndex], state.RevealSlotCount),
            state.RevealSlotCount >= state.Phrases[state.RevealPhraseIndex].Slots.Count),
        RoomPhase.Voting => new VoteRequestMessage(FrasiComposte(state)),
        RoomPhase.Finished => new GameFinishedMessage(Classifica(state, state.Votes)),
        _ => null,
    };
```

- [ ] **Passo 4: Estrarre `SlotRequestFor` da `SlotRequests`**

In `src/FrasiSquisite.Domain/Engine/GameEngine.Writing.cs`, sostituire (righe 186-203):

```csharp
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

con:

```csharp
    private IEnumerable<Effect> SlotRequests(GameState state)
    {
        for (var i = 0; i < state.Players.Count; i++)
        {
            yield return new SendToPlayer(state.Players[i].Id, SlotRequestFor(state, i));
        }
    }

    /// <summary>
    /// La richiesta di casella per un giocatore preso per posizione, non per
    /// id: è così che SlotRequests la costruiva già inline. Estratta perché
    /// il rientro (design rientro §3.2) deve poter ricostruire la stessa
    /// identica richiesta per un solo giocatore, con IndexOfPlayer al posto
    /// dell'indice di ciclo.
    /// </summary>
    private SlotRequestMessage SlotRequestFor(GameState state, int playerIndex)
    {
        var assegnazione = _mode.AssignSlot(state.Round, playerIndex, state.Players.Count, state.Schema);
        var casella = state.Schema.Caselle[assegnazione.SlotIndex];

        // Nota: PhraseIndex resta deliberatamente fuori dal messaggio.
        return new SlotRequestMessage(
            state.Round,
            state.Schema.SlotCount,
            casella.Ruolo,
            casella.Prompt,
            casella.Esempio);
    }
```

- [ ] **Passo 5: Aggiungere il case allo switch**

In `src/FrasiSquisite.Domain/Engine/GameEngine.cs`, sostituire (riga 36):

```csharp
        PlayerLeft e => OnPlayerLeft(state, e),
```

con:

```csharp
        PlayerLeft e => OnPlayerLeft(state, e),
        PlayerRejoined e => OnPlayerRejoined(state, e),
```

- [ ] **Passo 6: Eseguire i test e verificare che passino**

Run: `dotnet test tests/FrasiSquisite.Domain.Tests --filter "FullyQualifiedName~RientroTests"`
Expected: PASS — 7 test verdi.

Run l'intera suite Domain, per verificare che il refactoring di `SlotRequests` non abbia cambiato nulla per i test esistenti (in particolare quelli sull'avvio di un round, che dipendono dal contenuto esatto della richiesta): `dotnet test tests/FrasiSquisite.Domain.Tests`
Expected: PASS — 518 test verdi (511 + 7).

Run l'intera soluzione: `dotnet test FrasiSquisite.slnx`
Expected: PASS — 807 test verdi in tutto (Shared 83, Domain 518, App 105, Server 101), nessun altro progetto toccato.

- [ ] **Passo 7: Commit**

```bash
git add src/FrasiSquisite.Domain/Engine/GameEngine.Players.cs \
        src/FrasiSquisite.Domain/Engine/GameEngine.Writing.cs \
        src/FrasiSquisite.Domain/Engine/GameEngine.cs \
        tests/FrasiSquisite.Domain.Tests/Engine/RientroTests.cs
git commit -m "feat(rientro): il motore rimette IsConnected e rimanda il messaggio della fase corrente"
```

---

### Task 2: Protocollo v8 → v9

**File:**
- Modificare: `src/FrasiSquisite.Shared/Protocol/ClientMessages.cs`
- Modificare: `src/FrasiSquisite.Shared/Protocol/ServerMessages.cs`
- Modificare: `src/FrasiSquisite.Shared/Protocol/ProtocolVersion.cs`
- Test: `tests/FrasiSquisite.Shared.Tests/Protocol/ProtocolContractTests.cs`

**Interfacce:**
- Produce: `RejoinRoomRequest(int ProtocolVersion, Guid PlayerId, string RoomCode)` in `FrasiSquisite.Shared.Protocol` (client→server). `RejoinRejectedMessage` in `FrasiSquisite.Shared.Protocol` (server→client, nessun campo).

Task additivo e isolato al progetto Shared: nessun tipo esistente cambia forma, nessun altro progetto si rompe. `dotnet test FrasiSquisite.slnx` resta verde per tutto questo task.

- [ ] **Passo 1: Scrivere i test che falliscono**

In `tests/FrasiSquisite.Shared.Tests/Protocol/ProtocolContractTests.cs`, sostituire il blocco (righe 9-77, dal commento sopra `LaVersioneDelProtocolloE8` fino alla chiusura di `UnClientDiSetteVersioniPrimaNonECompatibile`):

```csharp
    // Il reveal fluido porta il protocollo a v8: RevealStepMessage cambia
    // forma (RevealedSlots diventa Fragments, per intercalare il tessuto
    // connettivo del template alle caselle), un client v7 non saprebbe più
    // decodificarlo. Anche qui il rifiuto esplicito ("aggiorna l'app") è il
    // comportamento giusto.
    [Fact]
    public void LaVersioneDelProtocolloE8()
    {
        Assert.Equal(8, ProtocolVersion.Current);
    }

    // v7 è l'unica versione davvero installata sul campo: l'APK del lotto
    // precedente, uscito prima che il reveal fluido portasse il protocollo a
    // v8. Questo caso era rimasto scoperto quando Current è avanzato: la
    // convenzione del file (allungare la catena a ogni avanzamento, senza
    // perdere i casi vecchi) impone di aggiungerlo qui, in cima, e di
    // rinumerare "quante versioni prima" tutti i casi già coperti.
    [Fact]
    public void UnClientDellaVersionePrecedenteNonECompatibile()
    {
        Assert.False(ProtocolVersion.IsCompatible(7));
    }

    // Un client v6 è incompatibile tanto quanto uno v7: il caso non va perso
    // quando la versione corrente avanza, altrimenti una regressione che
    // accettasse "solo" v6 passerebbe inosservata.
    [Fact]
    public void UnClientDiDueVersioniPrimaNonECompatibile()
    {
        Assert.False(ProtocolVersion.IsCompatible(6));
    }

    // Stessa cautela per v5: la catena di incompatibilità pregresse resta
    // tutta coperta man mano che la versione corrente avanza (spec del
    // progetto: "i test che asseriscono ProtocolVersion vanno aggiornati...
    // tenendo anche i casi vecchi").
    [Fact]
    public void UnClientDiTreVersioniPrimaNonECompatibile()
    {
        Assert.False(ProtocolVersion.IsCompatible(5));
    }

    // E per v4.
    [Fact]
    public void UnClientDiQuattroVersioniPrimaNonECompatibile()
    {
        Assert.False(ProtocolVersion.IsCompatible(4));
    }

    // E per v3.
    [Fact]
    public void UnClientDiCinqueVersioniPrimaNonECompatibile()
    {
        Assert.False(ProtocolVersion.IsCompatible(3));
    }

    // E per v2.
    [Fact]
    public void UnClientDiSeiVersioniPrimaNonECompatibile()
    {
        Assert.False(ProtocolVersion.IsCompatible(2));
    }

    // E per v1, la prima versione mai esistita.
    [Fact]
    public void UnClientDiSetteVersioniPrimaNonECompatibile()
    {
        Assert.False(ProtocolVersion.IsCompatible(1));
    }
```

con:

```csharp
    // Il rientro in partita porta il protocollo a v9: RejoinRoom è un
    // metodo nuovo dell'hub e RejoinRejectedMessage un tipo nuovo, ma un
    // client v8 che provasse comunque a rientrare (impossibile: non ha il
    // pulsante/la chiamata) non avrebbe comunque modo di decodificare la
    // risposta. Stesso rifiuto esplicito ("aggiorna l'app").
    [Fact]
    public void LaVersioneDelProtocolloE9()
    {
        Assert.Equal(9, ProtocolVersion.Current);
    }

    // v8 è l'unica versione davvero installata sul campo: l'APK del lotto
    // precedente, uscito prima che il rientro in partita portasse il
    // protocollo a v9. Questo caso era rimasto scoperto quando Current è
    // avanzato: la convenzione del file (allungare la catena a ogni
    // avanzamento, senza perdere i casi vecchi) impone di aggiungerlo qui,
    // in cima, e di rinumerare "quante versioni prima" tutti i casi già
    // coperti.
    [Fact]
    public void UnClientDellaVersionePrecedenteNonECompatibile()
    {
        Assert.False(ProtocolVersion.IsCompatible(8));
    }

    // Un client v7 è incompatibile tanto quanto uno v8: il caso non va perso
    // quando la versione corrente avanza, altrimenti una regressione che
    // accettasse "solo" v7 passerebbe inosservata.
    [Fact]
    public void UnClientDiDueVersioniPrimaNonECompatibile()
    {
        Assert.False(ProtocolVersion.IsCompatible(7));
    }

    // Stessa cautela per v6: la catena di incompatibilità pregresse resta
    // tutta coperta man mano che la versione corrente avanza (spec del
    // progetto: "i test che asseriscono ProtocolVersion vanno aggiornati...
    // tenendo anche i casi vecchi").
    [Fact]
    public void UnClientDiTreVersioniPrimaNonECompatibile()
    {
        Assert.False(ProtocolVersion.IsCompatible(6));
    }

    // E per v5.
    [Fact]
    public void UnClientDiQuattroVersioniPrimaNonECompatibile()
    {
        Assert.False(ProtocolVersion.IsCompatible(5));
    }

    // E per v4.
    [Fact]
    public void UnClientDiCinqueVersioniPrimaNonECompatibile()
    {
        Assert.False(ProtocolVersion.IsCompatible(4));
    }

    // E per v3.
    [Fact]
    public void UnClientDiSeiVersioniPrimaNonECompatibile()
    {
        Assert.False(ProtocolVersion.IsCompatible(3));
    }

    // E per v2.
    [Fact]
    public void UnClientDiSetteVersioniPrimaNonECompatibile()
    {
        Assert.False(ProtocolVersion.IsCompatible(2));
    }

    // E per v1, la prima versione mai esistita.
    [Fact]
    public void UnClientDiOttoVersioniPrimaNonECompatibile()
    {
        Assert.False(ProtocolVersion.IsCompatible(1));
    }

    [Fact]
    public void RoundtripDiRejoinRoomRequest()
    {
        var originale = new RejoinRoomRequest(
            ProtocolVersion: 9,
            PlayerId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            RoomCode: "ABCD");

        var json = JsonSerializer.Serialize(originale, ProtocolJson.Options);
        var ricostruito = JsonSerializer.Deserialize<RejoinRoomRequest>(json, ProtocolJson.Options);

        Assert.Equal(originale, ricostruito);
    }

    [Fact]
    public void RoundtripDiRejoinRejectedMessage()
    {
        var originale = new RejoinRejectedMessage();

        var json = JsonSerializer.Serialize(originale, ProtocolJson.Options);
        var ricostruito = JsonSerializer.Deserialize<RejoinRejectedMessage>(json, ProtocolJson.Options);

        Assert.Equal(originale, ricostruito);
    }
```

- [ ] **Passo 2: Eseguire i test e verificare che falliscano**

Run: `dotnet test tests/FrasiSquisite.Shared.Tests --filter "FullyQualifiedName~ProtocolContractTests"`
Expected: FAIL — `RejoinRoomRequest`/`RejoinRejectedMessage` non esistono, `ProtocolVersion.Current` vale ancora 8 (errori di compilazione).

- [ ] **Passo 3: Aggiungere i tipi di protocollo**

In `src/FrasiSquisite.Shared/Protocol/ClientMessages.cs`, sostituire (riga 5):

```csharp
public sealed record JoinRoomRequest(int ProtocolVersion, Guid PlayerId, string Nickname, string RoomCode);
```

con:

```csharp
public sealed record JoinRoomRequest(int ProtocolVersion, Guid PlayerId, string Nickname, string RoomCode);

/// <summary>
/// A differenza di JoinRoomRequest non porta Nickname: il giocatore esiste
/// già nella stanza, il nickname non cambia (design rientro §4).
/// </summary>
public sealed record RejoinRoomRequest(int ProtocolVersion, Guid PlayerId, string RoomCode);
```

Alla fine di `src/FrasiSquisite.Shared/Protocol/ServerMessages.cs` (dopo `IllustrationFailedMessage`, ultima riga del file), aggiungere:

```csharp

/// <summary>
/// Stanza sparita, o il giocatore non è fra quelli della stanza: nessun
/// campo, perché al client non serve distinguere i due casi (design rientro
/// §4) — in entrambi il comportamento è lo stesso, cancellare il codice
/// stanza salvato e tornare/restare alla lobby, senza mostrarlo come errore.
/// </summary>
public sealed record RejoinRejectedMessage;
```

In `src/FrasiSquisite.Shared/Protocol/ProtocolVersion.cs`, sostituire:

```csharp
    public const int Current = 8;
```

con:

```csharp
    public const int Current = 9;
```

- [ ] **Passo 4: Eseguire i test e verificare che passino**

Run: `dotnet test tests/FrasiSquisite.Shared.Tests`
Expected: PASS — 86 test verdi (83 + 1 nuovo caso nella catena di versione + 2 roundtrip; il rinominato `LaVersioneDelProtocolloE9` non aggiunge un conteggio).

Run l'intera soluzione: `dotnet test FrasiSquisite.slnx`
Expected: PASS — 810 test verdi (Shared 86, Domain 518, App 105, Server 101).

- [ ] **Passo 5: Commit**

```bash
git add src/FrasiSquisite.Shared/Protocol/ClientMessages.cs \
        src/FrasiSquisite.Shared/Protocol/ServerMessages.cs \
        src/FrasiSquisite.Shared/Protocol/ProtocolVersion.cs \
        tests/FrasiSquisite.Shared.Tests/Protocol/ProtocolContractTests.cs
git commit -m "feat(rientro): RejoinRoomRequest e RejoinRejectedMessage, protocollo v9"
```

---

### Task 3: Periodo di grazia e `GameHub.RejoinRoom`

**File:**
- Create: `src/FrasiSquisite.Server/Realtime/GracePeriodTimer.cs`
- Modificare: `src/FrasiSquisite.Server/Realtime/GameHost.cs`
- Modificare: `src/FrasiSquisite.Server/Realtime/GameHub.cs`
- Modificare: `src/FrasiSquisite.Server/Program.cs`
- Test: `tests/FrasiSquisite.Server.Tests/Realtime/GameHostTests.cs`, `tests/FrasiSquisite.Server.Tests/Realtime/GameHubTests.cs`

**Interfacce:**
- Consuma: `PlayerRejoined` (Task 1), `RejoinRoomRequest`/`RejoinRejectedMessage` (Task 2).
- Produce: `IGracePeriodTimer` con `Task DelayAsync(TimeSpan durata, CancellationToken ct)`. `GameHost.AvviaPeriodoDiGrazia(string roomCode, Guid playerId)` e `GameHost.AnnullaPeriodoDiGrazia(string roomCode, Guid playerId)`, entrambi `public void`. `GameHub.RejoinRoom(RejoinRoomRequest request)`.

Nessun tipo esistente cambia forma in modo da rompere altri progetti: l'App non referenzia `FrasiSquisite.Server`, quindi resta fuori da questo task. `dotnet test FrasiSquisite.slnx` resta verde per tutto questo task.

- [ ] **Passo 1: Scrivere il test che fallisce, sul timer**

Creare `src/FrasiSquisite.Server/Realtime/GracePeriodTimer.cs`:

```csharp
namespace FrasiSquisite.Server.Realtime;

/// <summary>
/// Astrae l'attesa del periodo di grazia (GameHost): nei test unitari si
/// sostituisce con un finto controllabile a comando, mai un vero Task.Delay
/// da attendere per davvero (design rientro §7).
/// </summary>
public interface IGracePeriodTimer
{
    Task DelayAsync(TimeSpan durata, CancellationToken ct);
}

public sealed class RealGracePeriodTimer : IGracePeriodTimer
{
    public Task DelayAsync(TimeSpan durata, CancellationToken ct) => Task.Delay(durata, ct);
}
```

In `tests/FrasiSquisite.Server.Tests/Realtime/GameHostTests.cs`, aggiungere un finto controllabile e tre test, subito dopo `DueHostHannoTabelleDeiLucchettiDistinte` (dopo la sua chiusura `}`, riga 74):

```csharp

    /// <summary>
    /// Non completa mai da solo: il test decide quando "scade" chiamando
    /// FaiScadere, o annullando il CancellationToken passato a DelayAsync
    /// (come farebbe AnnullaPeriodoDiGrazia con un token vero). Zero
    /// Task.Delay reali, come richiesto dal design del rientro (§7).
    /// </summary>
    private sealed class TimerControllabile : IGracePeriodTimer
    {
        private readonly TaskCompletionSource _tcs = new();

        public Task DelayAsync(TimeSpan durata, CancellationToken ct)
        {
            ct.Register(() => _tcs.TrySetCanceled(ct));
            return _tcs.Task;
        }

        public void FaiScadere() => _tcs.TrySetResult();
    }

    /// <summary>
    /// La tabella dei periodi di grazia protegge lo stesso genere di stato
    /// per-stanza di _locks: stesso motivo, campo d'istanza mai statico.
    /// </summary>
    [Fact]
    public void LaTabellaDeiPeriodiDiGraziaEPerIstanzaENonStatica()
    {
        var campi = typeof(GameHost)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(ConcurrentDictionary<(string, Guid), CancellationTokenSource>))
            .ToList();

        var campo = Assert.Single(campi);

        Assert.False(campo.IsStatic, $"'{campo.Name}' è statico: i periodi di grazia verrebbero condivisi fra host diversi.");
    }

    [Fact]
    public async Task IlPeriodoDiGraziaScadutoDispatchaPlayerLeft()
    {
        var engine = new FakeGameEngine(_ => []);
        var rooms = new FakeRoomRegistry();
        rooms.Seed("STANZA", StanzaVuota("STANZA"));
        var timer = new TimerControllabile();
        var giocatore = Guid.NewGuid();

        var host = new GameHost(engine, rooms, null!, null!, null!, null!, NullLogger<GameHost>.Instance, timer);

        host.AvviaPeriodoDiGrazia("STANZA", giocatore);
        timer.FaiScadere();

        await AttendiCondizioneAsync(
            () => engine.EventiRicevuti.Any(e => e is PlayerLeft pl && pl.PlayerId == giocatore),
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task IlPeriodoDiGraziaAnnullatoNonDispatchaNulla()
    {
        var engine = new FakeGameEngine(_ => []);
        var rooms = new FakeRoomRegistry();
        rooms.Seed("STANZA", StanzaVuota("STANZA"));
        var timer = new TimerControllabile();
        var giocatore = Guid.NewGuid();

        var host = new GameHost(engine, rooms, null!, null!, null!, null!, NullLogger<GameHost>.Instance, timer);

        host.AvviaPeriodoDiGrazia("STANZA", giocatore);
        host.AnnullaPeriodoDiGrazia("STANZA", giocatore);

        // Margine reale, breve: dà tempo a un eventuale (erroneo) dispatch
        // di completare prima dell'asserzione negativa.
        await Task.Delay(100);
        Assert.DoesNotContain(engine.EventiRicevuti, e => e is PlayerLeft);
    }
```

- [ ] **Passo 2: Eseguire i test e verificare che falliscano**

Run: `dotnet test tests/FrasiSquisite.Server.Tests --filter "FullyQualifiedName~GameHostTests"`
Expected: FAIL — `GameHost` non ha un costruttore a 8 argomenti, `AvviaPeriodoDiGrazia`/`AnnullaPeriodoDiGrazia` non esistono (errori di compilazione).

- [ ] **Passo 3: Implementare il periodo di grazia in `GameHost`**

In `src/FrasiSquisite.Server/Realtime/GameHost.cs`, sostituire la firma della classe e i campi (righe 16-37):

```csharp
public sealed class GameHost(
    IGameEngine engine,
    IRoomRegistry rooms,
    IHubContext<GameHub> hub,
    RefinementRunner runner,
    IllustrationRunner illustrazioni,
    ImageStore deposito,
    ILogger<GameHost> logger)
{
    /// <summary>
    /// Un lucchetto per codice stanza. <b>Deve restare un campo d'istanza, non
    /// statico:</b> protegge lo stato tenuto da <see cref="IRoomRegistry"/>,
    /// che vive nel container di dipendenze, quindi deve avere lo stesso
    /// ambito. Da <c>static</c> la tabella sopravviveva al container e veniva
    /// condivisa fra host diversi nello stesso processo — in produzione senza
    /// conseguenze, perché <c>GameHost</c> è registrato come singleton e di
    /// host ce n'è uno solo, ma nei test due host indipendenti che pescano lo
    /// stesso codice stanza finivano per serializzarsi a vicenda pur non
    /// avendo nulla in comune.
    /// </summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks =
        new(StringComparer.OrdinalIgnoreCase);
```

con:

```csharp
public sealed class GameHost(
    IGameEngine engine,
    IRoomRegistry rooms,
    IHubContext<GameHub> hub,
    RefinementRunner runner,
    IllustrationRunner illustrazioni,
    ImageStore deposito,
    ILogger<GameHost> logger,
    IGracePeriodTimer? timer = null)
{
    // Parametro opzionale invece che obbligatorio: i circa dieci test
    // esistenti che costruiscono GameHost a mano (GameHostTests.cs) non
    // hanno nulla a che fare col periodo di grazia, e forzarli tutti a
    // passare un ottavo argomento sarebbe puro rumore. In produzione
    // Program.cs registra comunque il timer vero esplicitamente in DI.
    private readonly IGracePeriodTimer _timer = timer ?? new RealGracePeriodTimer();

    /// <summary>
    /// Un lucchetto per codice stanza. <b>Deve restare un campo d'istanza, non
    /// statico:</b> protegge lo stato tenuto da <see cref="IRoomRegistry"/>,
    /// che vive nel container di dipendenze, quindi deve avere lo stesso
    /// ambito. Da <c>static</c> la tabella sopravviveva al container e veniva
    /// condivisa fra host diversi nello stesso processo — in produzione senza
    /// conseguenze, perché <c>GameHost</c> è registrato come singleton e di
    /// host ce n'è uno solo, ma nei test due host indipendenti che pescano lo
    /// stesso codice stanza finivano per serializzarsi a vicenda pur non
    /// avendo nulla in comune.
    /// </summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Un periodo di grazia in corso per (stanza, giocatore): rientrare
    /// prima che scada annulla il dispatch di PlayerLeft sottostante, senza
    /// che nessun bot prenda mai il posto (design rientro §3.1). Campo
    /// d'istanza per lo stesso motivo di <see cref="_locks"/>.
    /// </summary>
    private readonly ConcurrentDictionary<(string RoomCode, Guid PlayerId), CancellationTokenSource> _periodiDiGrazia =
        new();

    private static readonly TimeSpan DurataPeriodoDiGrazia = TimeSpan.FromSeconds(30);
```

Subito prima del metodo `PlayerGroup` (ultimo membro della classe, riga 210), aggiungere:

```csharp
    /// <summary>
    /// Avvia il periodo di grazia per una disconnessione: se non annullato
    /// da un rientro entro 30s, dispatcha PlayerLeft esattamente come faceva
    /// prima GameHub.OnDisconnectedAsync in modo sincrono (design rientro
    /// §3.1). Sganciato e senza attesa, stesso stile di AvviaRifinitura.
    /// </summary>
    public void AvviaPeriodoDiGrazia(string roomCode, Guid playerId)
    {
        var cts = new CancellationTokenSource();
        _periodiDiGrazia[(roomCode, playerId)] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await _timer.DelayAsync(DurataPeriodoDiGrazia, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Annullato da un rientro in tempo (AnnullaPeriodoDiGrazia ha
                // già rimosso e disposto questo stesso token): nessun
                // PlayerLeft da dispatchare.
                return;
            }

            // Scaduto senza essere annullato. Una finestra di corsa residua
            // con una disconnessione successiva dello stesso giocatore resta
            // accettata, stesso principio già in uso in GameHub.JoinRoom: è
            // l'eccezione, non la regola.
            _periodiDiGrazia.TryRemove((roomCode, playerId), out _);

            try
            {
                await DispatchAsync(roomCode, new PlayerLeft(playerId));
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Disconnessione del giocatore {PlayerId} dalla stanza {RoomCode}: dispatch di PlayerLeft fallita dopo il periodo di grazia.",
                    playerId,
                    roomCode);
            }
        });
    }

    /// <summary>
    /// Annulla un periodo di grazia pendente: chiamato da
    /// GameHub.RejoinRoom prima di dispatchare PlayerRejoined. Nessun
    /// effetto se non ce n'era uno (rientro senza una disconnessione
    /// recente, o periodo già scaduto).
    /// </summary>
    public void AnnullaPeriodoDiGrazia(string roomCode, Guid playerId)
    {
        if (_periodiDiGrazia.TryRemove((roomCode, playerId), out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    public static string PlayerGroup(Guid playerId) => $"player:{playerId}";
```

E rimuovere la vecchia riga (ora duplicata, era l'ultima del file):

```csharp
    public static string PlayerGroup(Guid playerId) => $"player:{playerId}";
```

- [ ] **Passo 4: Aggiornare `GameHub`**

In `src/FrasiSquisite.Server/Realtime/GameHub.cs`, sostituire (righe 116-145):

```csharp
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(RoomKey, out var room) && room is string roomCode &&
            Context.Items.TryGetValue(PlayerKey, out var player) && player is Guid playerId)
        {
            try
            {
                await host.DispatchAsync(roomCode, new PlayerLeft(playerId));
            }
            catch (Exception ex)
            {
                // La stanza può essere sparita (es. riavvio del server, che
                // lancia HubException), ma su un socket già a metà morto anche
                // IOException, ObjectDisposedException o OperationCanceledException
                // sono guasti plausibili nell'invio degli effetti. In
                // disconnessione non c'è più nessun client a cui segnalarlo,
                // quindi non deve far esplodere la disconnessione - ma va comunque
                // loggato (a livello Warning, non Error: è un percorso atteso,
                // non un guasto del server) perché è l'unica traccia osservabile
                // rimasta di quel che è successo.
                logger.LogWarning(
                    ex,
                    "Disconnessione del giocatore {PlayerId} dalla stanza {RoomCode}: dispatch di PlayerLeft fallita.",
                    playerId,
                    roomCode);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }
```

con:

```csharp
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(RoomKey, out var room) && room is string roomCode &&
            Context.Items.TryGetValue(PlayerKey, out var player) && player is Guid playerId)
        {
            // Non dispatcha più PlayerLeft subito: avvia un periodo di
            // grazia di 30s (design rientro §3.1). Se il giocatore rientra
            // in tempo, GameHub.RejoinRoom lo annulla prima che scada, e
            // nessun bot prende mai il suo posto. Il log del fallimento del
            // dispatch, quando il periodo scade davvero, vive ora dentro
            // AvviaPeriodoDiGrazia stesso.
            host.AvviaPeriodoDiGrazia(roomCode, playerId);
        }

        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// A differenza di JoinRoom, funziona anche a partita già iniziata: è
    /// pensato apposta per quello. Il controllo (la stanza esiste? il
    /// giocatore è già fra quelli della stanza?) avviene prima di toccare il
    /// motore, stesso pattern già in uso in SetSchema per uno schema
    /// inesistente: un rifiuto atteso non è un'eccezione, è un messaggio
    /// mirato al chiamante (design rientro §3.3).
    /// </summary>
    public async Task RejoinRoom(RejoinRoomRequest request)
    {
        RichiediProtocolloCompatibile(request.ProtocolVersion);

        if (!rooms.TryGet(request.RoomCode, out var stanza) || stanza.FindPlayer(request.PlayerId) is null)
        {
            await Clients.Caller.SendAsync(
                "ReceiveMessage", nameof(RejoinRejectedMessage), new RejoinRejectedMessage());
            return;
        }

        host.AnnullaPeriodoDiGrazia(request.RoomCode, request.PlayerId);
        await EntraAsync(request.RoomCode, request.PlayerId);
        await host.DispatchAsync(request.RoomCode, new PlayerRejoined(request.PlayerId));
    }
```

- [ ] **Passo 5: Registrare `IGracePeriodTimer` in DI**

In `src/FrasiSquisite.Server/Program.cs`, sostituire (riga 27):

```csharp
builder.Services.AddSingleton<GameHost>();
```

con:

```csharp
builder.Services.AddSingleton<IGracePeriodTimer, RealGracePeriodTimer>();
builder.Services.AddSingleton<GameHost>();
```

- [ ] **Passo 6: Eseguire i test e verificare che passino**

Run: `dotnet test tests/FrasiSquisite.Server.Tests --filter "FullyQualifiedName~GameHostTests"`
Expected: PASS — 13 test verdi (10 esistenti + 3 nuovi).

- [ ] **Passo 7: Velocizzare il periodo di grazia nei test d'integrazione di `GameHub`**

Il periodo di grazia di produzione dura 30s reali: userli davvero in ogni test che tocca una disconnessione renderebbe la suite improponibilmente lenta. In `tests/FrasiSquisite.Server.Tests/Realtime/GameHubTests.cs`, sostituire (righe 22-26):

```csharp
    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>();
        return Task.CompletedTask;
    }
```

con:

```csharp
    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton<IGracePeriodTimer>(new VelocizzaGraziaTimer())));
        return Task.CompletedTask;
    }
```

Aggiungere l'`using` mancante in cima al file, insieme agli altri `using FrasiSquisite...` (righe 3-8): il file oggi invoca l'hub solo per nome di stringa (`InvokeAsync("RejoinRoom", ...)`), non referenzia mai il tipo `GameHub` in C#, quindi `FrasiSquisite.Server.Realtime` — dove vivono sia `GameHub` sia il nuovo `IGracePeriodTimer` — non è ancora importato:

```csharp
using FrasiSquisite.Server.Realtime;
```

Aggiungere la classe, in un punto qualunque a livello di classe `GameHubTests` (es. accanto a `ConnettiAsync`):

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

Aggiungere quattro nuovi test, in un punto qualunque a livello di classe (es. dopo `DisconnessioneSuStanzaSparitaNonLasciaErroriNeiLogDelServer`):

```csharp
    /// <summary>
    /// Attende una condizione invece di un numero fisso di messaggi: dopo il
    /// periodo di grazia il numero esatto di RoomStateMessage intermedi
    /// (uno da PlayerLeft, eventualmente altri da FillDisconnected) non è
    /// interessante quanto lo stato finale — stesso principio del pattern
    /// già in uso in GameHostTests.cs.
    /// </summary>
    private static async Task AttendiCondizioneAsync(Func<bool> condizione, TimeSpan timeout)
    {
        var scadenza = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < scadenza)
        {
            if (condizione())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(condizione(), $"Condizione non soddisfatta entro {timeout}.");
    }

    [Fact]
    public async Task IlRientroEntroIlPeriodoDiGraziaNonFaSubentrareUnBot()
    {
        await using var anna = await ConnettiAsync();
        await using var bruno = await ConnettiAsync();

        var codice = await anna.Connection.InvokeAsync<string>(
            "CreateRoom", new CreateRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "Anna"));
        var brunoId = Guid.NewGuid();
        await bruno.Connection.InvokeAsync("JoinRoom", new JoinRoomRequest(ProtocolVersion.Current, brunoId, "Bruno", codice));
        await anna.WaitFor<RoomStateMessage>(TimeSpan.FromSeconds(5));

        // In lobby una disconnessione rimuove per davvero (spec del design,
        // §6): serve essere a partita avviata perché il rientro abbia senso.
        await anna.Connection.InvokeAsync("StartGame", new StartGameRequest(codice));
        await bruno.WaitFor<SlotRequestMessage>(TimeSpan.FromSeconds(5));

        var passiPrima = anna.CountOf<RoomStateMessage>();
        await bruno.Connection.StopAsync();

        // Rientra subito, ben prima dei 200ms del VelocizzaGraziaTimer di
        // questa suite: il periodo di grazia deve annullarsi, non scadere.
        // Una nuova connessione (non si può riavviare quella fermata sopra),
        // stesso ConnettiAsync già usato per anna/bruno.
        await using var brunoRientrato = await ConnettiAsync();
        await brunoRientrato.Connection.InvokeAsync(
            "RejoinRoom", new RejoinRoomRequest(ProtocolVersion.Current, brunoId, codice));

        await anna.WaitForCount<RoomStateMessage>(passiPrima + 1, TimeSpan.FromSeconds(5));

        // Margine oltre l'unico RoomStateMessage atteso (quello del
        // rientro): se il periodo di grazia non fosse stato annullato per
        // davvero, ne arriverebbe un secondo (da PlayerLeft) entro i 200ms
        // del VelocizzaGraziaTimer di questa suite, e qui lo si scoprirebbe.
        await Task.Delay(300);
        Assert.Equal(passiPrima + 1, anna.CountOf<RoomStateMessage>());
        Assert.True(anna.Last<RoomStateMessage>().Players.Single(p => p.Id == brunoId).IsConnected);
    }

    [Fact]
    public async Task IlRientroDopoIlPeriodoDiGraziaFunzionaComunque()
    {
        await using var anna = await ConnettiAsync();
        await using var bruno = await ConnettiAsync();

        var codice = await anna.Connection.InvokeAsync<string>(
            "CreateRoom", new CreateRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "Anna"));
        var brunoId = Guid.NewGuid();
        await bruno.Connection.InvokeAsync("JoinRoom", new JoinRoomRequest(ProtocolVersion.Current, brunoId, "Bruno", codice));
        await anna.WaitFor<RoomStateMessage>(TimeSpan.FromSeconds(5));

        await anna.Connection.InvokeAsync("StartGame", new StartGameRequest(codice));
        await bruno.WaitFor<SlotRequestMessage>(TimeSpan.FromSeconds(5));

        await bruno.Connection.StopAsync();

        // Aspetta più a lungo dei 200ms del VelocizzaGraziaTimer: il periodo
        // di grazia scade per davvero (un bot prende il posto di Bruno per
        // questo round) prima del tentativo di rientro sotto.
        await AttendiCondizioneAsync(
            () => anna.Last<RoomStateMessage>().Players.Single(p => p.Id == brunoId).IsConnected == false,
            TimeSpan.FromSeconds(2));

        await using var brunoRientrato = await ConnettiAsync();
        await brunoRientrato.Connection.InvokeAsync(
            "RejoinRoom", new RejoinRoomRequest(ProtocolVersion.Current, brunoId, codice));

        await AttendiCondizioneAsync(
            () => anna.Last<RoomStateMessage>().Players.Single(p => p.Id == brunoId).IsConnected,
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task IlRientroConCodiceStanzaInesistenteVieneRifiutato()
    {
        await using var anna = await ConnettiAsync();

        await anna.Connection.InvokeAsync(
            "RejoinRoom", new RejoinRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "NONESISTE"));

        await anna.WaitFor<RejoinRejectedMessage>(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task IlRientroConGiocatoreSconosciutoNellaStanzaVieneRifiutato()
    {
        await using var anna = await ConnettiAsync();

        var codice = await anna.Connection.InvokeAsync<string>(
            "CreateRoom", new CreateRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "Anna"));
        await anna.WaitFor<RoomStateMessage>(TimeSpan.FromSeconds(5));

        await using var estraneo = await ConnettiAsync();
        await estraneo.Connection.InvokeAsync(
            "RejoinRoom", new RejoinRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), codice));

        await estraneo.WaitFor<RejoinRejectedMessage>(TimeSpan.FromSeconds(5));
    }
```

- [ ] **Passo 8: Eseguire i test e verificare che passino**

Run: `dotnet test tests/FrasiSquisite.Server.Tests --filter "FullyQualifiedName~GameHubTests"`
Expected: PASS — 21 test verdi (17 esistenti + 4 nuovi).

Run l'intera suite Server (verifica in particolare che `DisconnessioneSuStanzaSparitaNonLasciaErroriNeiLogDelServer`, che dipende dal comportamento di `OnDisconnectedAsync` su una stanza sparita, sia ancora verde col periodo di grazia di 200ms inserito in mezzo — la sua finestra di attesa è di 2s, ampiamente sufficiente): `dotnet test tests/FrasiSquisite.Server.Tests`
Expected: PASS — 108 test verdi (101 + 3 + 4), "Non superati: 0". Se qualche test preesistente legato a una disconnessione fallisse per un'assunzione di immediatezza non ancora individuata in questo piano, adattarlo ad attendere la condizione (pattern `WaitFor`/`AttendiCondizioneAsync` già in uso nel file) invece di aspettarsi un effetto sincrono.

Run l'intera soluzione: `dotnet test FrasiSquisite.slnx`
Expected: PASS — 817 test verdi (Shared 86, Domain 518, App 105, Server 108).

- [ ] **Passo 9: Commit**

```bash
git add src/FrasiSquisite.Server/Realtime/GracePeriodTimer.cs \
        src/FrasiSquisite.Server/Realtime/GameHost.cs \
        src/FrasiSquisite.Server/Realtime/GameHub.cs \
        src/FrasiSquisite.Server/Program.cs \
        tests/FrasiSquisite.Server.Tests/Realtime/GameHostTests.cs \
        tests/FrasiSquisite.Server.Tests/Realtime/GameHubTests.cs
git commit -m "feat(rientro): periodo di grazia di 30s e GameHub.RejoinRoom"
```

---

### Task 4: Client — persistenza, evento `Reconnected`, tentativo di rientro

**File:**
- Create: `src/FrasiSquisite.App/Services/IRoomSession.cs`
- Create: `src/FrasiSquisite.App/Services/PreferencesRoomSession.cs`
- Modificare: `src/FrasiSquisite.App/Services/IGameConnection.cs`
- Modificare: `src/FrasiSquisite.App/Services/SignalRGameConnection.cs`
- Modificare: `src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs`
- Modificare: `src/FrasiSquisite.App/MauiProgram.cs`
- Modificare: `src/FrasiSquisite.App/Pages/GamePage.xaml.cs`
- Modificare: `src/FrasiSquisite.App/App.xaml.cs`
- Create: `tests/FrasiSquisite.App.Tests/FakeRoomSession.cs`
- Modificare: `tests/FrasiSquisite.App.Tests/FakeGameConnection.cs`
- Modificare: `tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs`

**Interfacce:**
- Consuma: `RejoinRoomRequest`/`RejoinRejectedMessage` (Task 2). Non consuma nulla del Server — il client parla solo sul filo.
- Produce: `IRoomSession` (`string RoomCode { get; }`, `void Save(string roomCode)`, `void Clear()`). `IGameConnection.Reconnected` (evento), `IGameConnection.RejoinRoomAsync(Guid playerId, string roomCode)`. `GameSessionViewModel.TryRejoinAsync()` (`public async Task`).

**Nota rispetto alla spec (design rientro §5.1, §6):** la spec descrive `IRoomSession` come cancellato anche "quando si torna alla lobby" o "una partita finisce". Verificato nel codice: `PulisciStatoDiPartitaConclusa` (chiamata sia da "torna alla lobby" sia da "nuova partita") non fa mai lasciare la stanza — è solo pulizia di collezioni UI, la partita resta la stessa stanza, sempre valida per un rientro. In questa app **non esiste alcun comando che porti un giocatore fuori da una stanza attiva** se non chiudere l'app — l'unico modo in cui il `RoomCode` salvato diventa davvero invalido è un rifiuto esplicito dal server. `IRoomSession` va quindi cancellato **solo** su `RejoinRejectedMessage`, mai da `PulisciStatoDiPartitaConclusa`.

Task isolato al progetto App: nessun altro progetto lo referenzia. `dotnet test FrasiSquisite.slnx` resta verde per tutto questo task.

- [ ] **Passo 1: Scrivere i test che falliscono**

Creare `tests/FrasiSquisite.App.Tests/FakeRoomSession.cs`:

```csharp
using FrasiSquisite.App.Services;

namespace FrasiSquisite.App.Tests;

public sealed class FakeRoomSession : IRoomSession
{
    public string RoomCode { get; private set; } = string.Empty;

    public List<string> Salvati { get; } = [];

    public bool Cancellato { get; private set; }

    public void Save(string roomCode)
    {
        RoomCode = roomCode;
        Salvati.Add(roomCode);
    }

    public void Clear()
    {
        RoomCode = string.Empty;
        Cancellato = true;
    }
}
```

In `tests/FrasiSquisite.App.Tests/FakeGameConnection.cs`, sostituire (riga 15):

```csharp
    public event Action? ConnectionInterrupted;
```

con:

```csharp
    public event Action? ConnectionInterrupted;

    public event Action? Reconnected;
```

Sostituire (riga 60):

```csharp
    public void EmitConnectionInterrupted() => ConnectionInterrupted?.Invoke();
```

con:

```csharp
    public void EmitConnectionInterrupted() => ConnectionInterrupted?.Invoke();

    public void EmitReconnected() => Reconnected?.Invoke();
```

Aggiungere, dopo `JoinRoomAsync` (dopo la sua chiusura `}`, riga 82):

```csharp

    public Task RejoinRoomAsync(Guid playerId, string roomCode)
    {
        LanciaSeImpostato();
        _calls.Add($"RejoinRoom({playerId},{roomCode})");
        return Task.CompletedTask;
    }
```

In `tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs`, sostituire l'helper `Crea()` (righe 13-18):

```csharp
    private static (GameSessionViewModel Vm, FakeGameConnection Conn) Crea()
    {
        var connessione = new FakeGameConnection();
        var vm = new GameSessionViewModel(connessione, Anna, new FakeThemeService(), new FakePlayerProfile()) { ServerUrl = "http://test" };
        return (vm, connessione);
    }
```

con:

```csharp
    private static (GameSessionViewModel Vm, FakeGameConnection Conn, FakeRoomSession Sessione) Crea()
    {
        var connessione = new FakeGameConnection();
        var sessione = new FakeRoomSession();
        var vm = new GameSessionViewModel(connessione, Anna, new FakeThemeService(), new FakePlayerProfile(), sessione) { ServerUrl = "http://test" };
        return (vm, connessione, sessione);
    }
```

Questo rompe la compilazione di **ogni** test esistente che destruttura `Crea()` come `var (vm, conn) = Crea();` (due elementi, ora sono tre). Sostituire ogni occorrenza di:

```csharp
        var (vm, conn) = Crea();
```

con:

```csharp
        var (vm, conn, _) = Crea();
```

in tutto il file (decine di occorrenze — è una sostituzione meccanica di trovare-e-sostituisci, non richiede lettura una per una: il pattern testuale è identico ovunque compaia). Fare lo stesso per l'eventuale variante `CreaConTema()`, se distrutturata a due elementi con lo stesso pattern (verificarlo leggendo la sua firma prima di toccarla — se `CreaConTema()` non chiama `Crea()` con lo stesso numero di elementi non va cambiata).

Aggiungere quattro nuovi test, in un punto qualunque a livello di classe (es. subito dopo l'helper `Crea()`):

```csharp

    [Fact]
    public async Task AllAvvioSenzaStanzaSalvataNonTentaAlcunRientro()
    {
        var (vm, conn, _) = Crea();

        await vm.TryRejoinAsync();

        Assert.DoesNotContain(conn.Calls, c => c.StartsWith("RejoinRoom"));
    }

    [Fact]
    public async Task ConUnaStanzaSalvataAllAvvioTentaIlRientro()
    {
        var (vm, conn, sessione) = Crea();
        sessione.Save("ABCD");

        await vm.TryRejoinAsync();

        Assert.Contains($"RejoinRoom({Anna},ABCD)", conn.Calls);
    }

    [Fact]
    public async Task UnRifiutoDelRientroCancellaLaSessioneSalvataSenzaMostrareUnErrore()
    {
        var (vm, conn, sessione) = Crea();
        sessione.Save("ABCD");

        await vm.TryRejoinAsync();
        conn.Emit(new RejoinRejectedMessage());

        Assert.True(sessione.Cancellato);
        Assert.Equal(string.Empty, vm.ErrorText);
    }

    [Fact]
    public void UnaStanzaRicevutaVieneSalvataInSessione()
    {
        var (vm, conn, sessione) = Crea();

        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby", [new PlayerView(Anna, "Anna", true, true, false)], "storia", 8));

        Assert.Equal("ABCD", sessione.RoomCode);
    }

    [Fact]
    public async Task IlRipristinoDellaConnessioneTentaDiNuovoIlRientro()
    {
        var (vm, conn, _) = Crea();
        vm.RoomCode = "ABCD";

        conn.EmitReconnected();

        // OnReconnected scatena TryRejoinAsync in modo asincrono (fire-and-
        // forget, come vuole un handler di evento void): il FakeGameConnection
        // risponde in modo sincrono, ma un breve margine reale evita un test
        // fragile legato ai dettagli di scheduling di un Task già completato.
        var scadenza = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        while (DateTime.UtcNow < scadenza && !conn.Calls.Any(c => c.StartsWith("RejoinRoom")))
        {
            await Task.Delay(5);
        }

        Assert.Contains($"RejoinRoom({Anna},ABCD)", conn.Calls);
    }
```

- [ ] **Passo 2: Eseguire i test e verificare che falliscano**

Run: `dotnet test tests/FrasiSquisite.App.Tests --filter "FullyQualifiedName~GameSessionViewModelTests"`
Expected: FAIL — `IRoomSession`, `RejoinRejectedMessage`, `TryRejoinAsync`, `RejoinRoomAsync` non esistono (errori di compilazione).

- [ ] **Passo 3: `IRoomSession` e la sua implementazione**

Creare `src/FrasiSquisite.App/Services/IRoomSession.cs`:

```csharp
namespace FrasiSquisite.App.Services;

/// <summary>
/// Astrae la persistenza del codice della stanza in cui si è in partita,
/// stesso schema di <see cref="IPlayerProfile"/>: la ViewModel dipende da
/// questa interfaccia e mai da <c>Preferences</c> direttamente, perché
/// <c>GameSessionViewModel</c> è compilata anche nel progetto di test.
/// </summary>
public interface IRoomSession
{
    /// <summary>Il codice stanza salvato, o <see cref="string.Empty"/> se non c'è una partita in sospeso.</summary>
    string RoomCode { get; }

    /// <summary>Salva il codice stanza: aggiornato a ogni RoomStateMessage ricevuto.</summary>
    void Save(string roomCode);

    /// <summary>
    /// Cancella il codice salvato: da chiamare solo quando il server rifiuta
    /// esplicitamente un tentativo di rientro (design rientro §5.1) — non
    /// quando si torna alla lobby o una partita finisce, perché la stanza
    /// resta comunque valida in entrambi i casi.
    /// </summary>
    void Clear();
}
```

Creare `src/FrasiSquisite.App/Services/PreferencesRoomSession.cs`:

```csharp
namespace FrasiSquisite.App.Services;

/// <summary>
/// Persistenza del codice stanza in <c>Preferences</c>: non è un segreto (a
/// differenza dell'id giocatore in <c>SecureStorage</c>, vedi
/// <c>PlayerIdentity</c> in <c>MauiProgram</c>), stesso schema di
/// <see cref="PreferencesPlayerProfile"/>.
/// </summary>
public sealed class PreferencesRoomSession : IRoomSession
{
    private const string Key = "stanza-in-sospeso";

    public string RoomCode => Preferences.Default.Get(Key, string.Empty);

    public void Save(string roomCode) => Preferences.Default.Set(Key, roomCode);

    public void Clear() => Preferences.Default.Remove(Key);
}
```

- [ ] **Passo 4: `IGameConnection.Reconnected` e `RejoinRoomAsync`**

In `src/FrasiSquisite.App/Services/IGameConnection.cs`, sostituire (righe 11-20):

```csharp
    /// <summary>
    /// Il trasporto è stato interrotto (o si è riconnesso con una nuova
    /// connessione che non recupera l'appartenenza alla stanza SignalR): da
    /// qui in poi un bot gioca al posto del giocatore, finché non esiste un
    /// rejoin di partita (Fase 2, fuori scope). Un solo evento basta: al
    /// chiamante non serve distinguere "riconnessione in corso" da
    /// "riconnesso" da "chiuso", perché la conseguenza per il giocatore è
    /// identica in tutti e tre i casi.
    /// </summary>
    event Action? ConnectionInterrupted;
```

con:

```csharp
    /// <summary>
    /// Il trasporto sta tentando di riconnettersi, o si è chiuso del tutto:
    /// in entrambi i casi mostra il banner "connessione instabile". Non
    /// scatta più su un vero ripristino del trasporto — quello è
    /// <see cref="Reconnected"/>, separato apposta da quando esiste un
    /// tentativo di rientro (design rientro §5.2).
    /// </summary>
    event Action? ConnectionInterrupted;

    /// <summary>
    /// Il trasporto si è ripristinato (.WithAutomaticReconnect), ma con un
    /// nuovo ConnectionId che non recupera da solo l'appartenenza alla
    /// stanza SignalR: chi ascolta deve tentare un rientro esplicito
    /// (design rientro §5.2), non limitarsi a mostrare un banner.
    /// </summary>
    event Action? Reconnected;
```

Aggiungere, dopo `Task JoinRoomAsync(Guid playerId, string nickname, string roomCode);` (riga 28):

```csharp

    /// <summary>A differenza di JoinRoomAsync funziona anche a partita già iniziata (design rientro §3.3).</summary>
    Task RejoinRoomAsync(Guid playerId, string roomCode);
```

- [ ] **Passo 5: `SignalRGameConnection`**

In `src/FrasiSquisite.App/Services/SignalRGameConnection.cs`, sostituire (righe 38-60):

```csharp
        // Il riconnettersi a livello di trasporto (.WithAutomaticReconnect) apre
        // una connessione nuova, con un ConnectionId diverso: non recupera da
        // solo l'appartenenza ai gruppi SignalR della stanza, che il server ha
        // già rimosso marcando il giocatore disconnesso (e facendoci giocare un
        // bot al suo posto). I tre eventi hanno quindi la stessa conseguenza
        // visibile per il giocatore: senza questo avviso il client resterebbe
        // "connesso" (IsConnected true) ma sordo a ogni messaggio successivo,
        // senza che nulla in schermata lo segnali (spec I1).
        _connection.Reconnecting += _ =>
        {
            SollevaConnectionInterrupted();
            return Task.CompletedTask;
        };
        _connection.Reconnected += _ =>
        {
            SollevaConnectionInterrupted();
            return Task.CompletedTask;
        };
        _connection.Closed += _ =>
        {
            SollevaConnectionInterrupted();
            return Task.CompletedTask;
        };
```

con:

```csharp
        // Reconnecting/Closed: il trasporto è giù o ci sta provando, stesso
        // banner di prima (spec I1). Reconnected è diverso da quando esiste
        // il rientro (design rientro §5.2): il trasporto è tornato, ma con
        // un ConnectionId nuovo che non recupera da solo l'appartenenza al
        // gruppo SignalR della stanza — chi ascolta deve tentare un rientro
        // esplicito, non limitarsi a mostrare un banner.
        _connection.Reconnecting += _ =>
        {
            SollevaConnectionInterrupted();
            return Task.CompletedTask;
        };
        _connection.Reconnected += _ =>
        {
            SollevaReconnected();
            return Task.CompletedTask;
        };
        _connection.Closed += _ =>
        {
            SollevaConnectionInterrupted();
            return Task.CompletedTask;
        };
```

Sostituire (riga 14, la dichiarazione dell'evento sull'implementazione — se `IGameConnection` è già un'interfaccia con `event Action?` non serve ridichiararla qui: verificare che il file usi `public event Action? ConnectionInterrupted;` esplicito e aggiungere la stessa riga per `Reconnected`):

```csharp
    public event Action? ConnectionInterrupted;
```

con:

```csharp
    public event Action? ConnectionInterrupted;

    public event Action? Reconnected;
```

Sostituire (righe 76-77):

```csharp
    private void SollevaConnectionInterrupted() =>
        EseguiSulThreadUI(() => ConnectionInterrupted?.Invoke());
```

con:

```csharp
    private void SollevaConnectionInterrupted() =>
        EseguiSulThreadUI(() => ConnectionInterrupted?.Invoke());

    private void SollevaReconnected() =>
        EseguiSulThreadUI(() => Reconnected?.Invoke());
```

Aggiungere, dopo `JoinRoomAsync` (dopo la riga 102):

```csharp

    public Task RejoinRoomAsync(Guid playerId, string roomCode) =>
        Hub.InvokeAsync("RejoinRoom", new RejoinRoomRequest(ProtocolVersion.Current, playerId, roomCode));
```

Aggiungere il case nel deserializzatore (`Deserializza`, dopo `nameof(ErrorMessage) => ...`, riga 171):

```csharp
        nameof(RejoinRejectedMessage) => payload.Deserialize<RejoinRejectedMessage>(ProtocolJson.Options),
```

- [ ] **Passo 6: `GameSessionViewModel`**

In `src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs`, sostituire il costruttore (righe 41-72):

```csharp
    private readonly IGameConnection _connection;
    private readonly Guid _playerId;
    private readonly IThemeService _themeService;
    private readonly IPlayerProfile _playerProfile;

    private bool _fraseCompleta;

    public GameSessionViewModel(IGameConnection connection, Guid playerId, IThemeService themeService, IPlayerProfile playerProfile)
    {
        _connection = connection;
        _playerId = playerId;
        _themeService = themeService;
        _playerProfile = playerProfile;

        _selectedTheme = themeService.Current;
        // Il tema può cambiare solo da Impostazioni, che passa sempre da
        // SelectThemeCommand qui sotto; questa sottoscrizione tiene comunque
        // SelectedTheme sincronizzato con la fonte di verità (IThemeService)
        // invece di duplicarne lo stato.
        _themeService.ThemeChanged += tema => SelectedTheme = tema;

        // Stesso schema del tema: il nickname salvato (lotto-e-brief.md) va
        // letto subito, non solo quando l'utente tocca qualcosa. Se non c'è
        // nulla di salvato, IPlayerProfile.Nickname è string.Empty - lo
        // stesso valore di default del campo, quindi resta vuoto come oggi.
        _nickname = playerProfile.Nickname;

        // Sottoscrizione nel costruttore: la ViewModel deve reagire ai messaggi
        // fin dal primo istante, anche prima che l'utente tocchi qualcosa.
        _connection.MessageReceived += OnMessage;
        _connection.ConnectionInterrupted += OnConnectionInterrupted;
    }
```

con:

```csharp
    private readonly IGameConnection _connection;
    private readonly Guid _playerId;
    private readonly IThemeService _themeService;
    private readonly IPlayerProfile _playerProfile;
    private readonly IRoomSession _roomSession;

    private bool _fraseCompleta;

    public GameSessionViewModel(
        IGameConnection connection,
        Guid playerId,
        IThemeService themeService,
        IPlayerProfile playerProfile,
        IRoomSession roomSession)
    {
        _connection = connection;
        _playerId = playerId;
        _themeService = themeService;
        _playerProfile = playerProfile;
        _roomSession = roomSession;

        _selectedTheme = themeService.Current;
        // Il tema può cambiare solo da Impostazioni, che passa sempre da
        // SelectThemeCommand qui sotto; questa sottoscrizione tiene comunque
        // SelectedTheme sincronizzato con la fonte di verità (IThemeService)
        // invece di duplicarne lo stato.
        _themeService.ThemeChanged += tema => SelectedTheme = tema;

        // Stesso schema del tema: il nickname salvato (lotto-e-brief.md) va
        // letto subito, non solo quando l'utente tocca qualcosa. Se non c'è
        // nulla di salvato, IPlayerProfile.Nickname è string.Empty - lo
        // stesso valore di default del campo, quindi resta vuoto come oggi.
        _nickname = playerProfile.Nickname;

        // Sottoscrizione nel costruttore: la ViewModel deve reagire ai messaggi
        // fin dal primo istante, anche prima che l'utente tocchi qualcosa.
        _connection.MessageReceived += OnMessage;
        _connection.ConnectionInterrupted += OnConnectionInterrupted;
        _connection.Reconnected += OnReconnected;
    }

    /// <summary>
    /// Tenta un rientro silenzioso nella stanza salvata (design rientro
    /// §5.2): usa RoomCode se già popolato (si crede già in una stanza — il
    /// caso del trasporto appena tornato), altrimenti quello persistito da
    /// IRoomSession (il caso dell'avvio a freddo, dove RoomCode è ancora
    /// vuoto). Nessun banner d'errore su un fallimento: dal punto di vista
    /// dell'utente non è un errore, è solo "quella partita non c'è più" - il
    /// fallimento silenzioso copre anche un guasto di puro trasporto (app
    /// offline all'avvio), per restare quanto più possibile senza attrito.
    /// </summary>
    public async Task TryRejoinAsync()
    {
        var codice = RoomCode.Length > 0 ? RoomCode : _roomSession.RoomCode;
        if (codice.Length == 0)
        {
            return;
        }

        try
        {
            await EnsureConnectedAsync();
            await _connection.RejoinRoomAsync(_playerId, codice);
        }
        catch
        {
            _roomSession.Clear();
            RoomCode = string.Empty;
        }
    }

    private void OnReconnected() => _ = TryRejoinAsync();
```

Sostituire (riga 619, dentro il case `RoomStateMessage`):

```csharp
            case RoomStateMessage stato:
                RoomCode = stato.RoomCode;
```

con:

```csharp
            case RoomStateMessage stato:
                RoomCode = stato.RoomCode;
                _roomSession.Save(stato.RoomCode);
```

Aggiungere un case nello switch di `OnMessage`, subito dopo il case `ErrorMessage` (righe 820-822):

```csharp
            case ErrorMessage errore:
                ErrorText = errore.Message;
                break;
```

diventa:

```csharp
            case ErrorMessage errore:
                ErrorText = errore.Message;
                break;

            // Nessun banner: dal punto di vista dell'utente non è un errore,
            // è solo "quella partita non c'è più" (design rientro §5.2).
            case RejoinRejectedMessage:
                _roomSession.Clear();
                RoomCode = string.Empty;
                break;
```

Sostituire il commento di `OnConnectionInterrupted` (righe 586-594):

```csharp
    private void OnConnectionInterrupted()
    {
        // Il trasporto SignalR può riconnettersi da solo (.WithAutomaticReconnect),
        // ma con un nuovo ConnectionId che non recupera l'appartenenza ai gruppi
        // della stanza: il server ha già marcato il giocatore disconnesso e ci
        // gioca un bot al suo posto. Il rejoin di partita è Fase 2 e resta fuori
        // scope, quindi l'avviso non si azzera da solo nemmeno se il trasporto
        // torna su (Reconnected): per questa sessione non cambia nulla.
        ConnectionBanner = "Connessione persa: un bot sta giocando al tuo posto.";
    }
```

con:

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

- [ ] **Passo 7: Eseguire i test e verificare che passino**

Run: `dotnet test tests/FrasiSquisite.App.Tests --filter "FullyQualifiedName~GameSessionViewModelTests"`
Expected: PASS — 99 test verdi (94 esistenti + 5 nuovi).

Run l'intera suite App: `dotnet test tests/FrasiSquisite.App.Tests`
Expected: PASS — 110 test verdi (105 + 5).

- [ ] **Passo 8: DI e agganci di piattaforma (nessun test automatico — vedi Vincoli globali)**

In `src/FrasiSquisite.App/MauiProgram.cs`, sostituire (righe 39-44):

```csharp
        builder.Services.AddSingleton<IPlayerProfile, PreferencesPlayerProfile>();
        builder.Services.AddSingleton(sp => new GameSessionViewModel(
            sp.GetRequiredService<IGameConnection>(),
            PlayerIdentity.Current(),
            sp.GetRequiredService<IThemeService>(),
            sp.GetRequiredService<IPlayerProfile>()));
```

con:

```csharp
        builder.Services.AddSingleton<IPlayerProfile, PreferencesPlayerProfile>();
        builder.Services.AddSingleton<IRoomSession, PreferencesRoomSession>();
        builder.Services.AddSingleton(sp => new GameSessionViewModel(
            sp.GetRequiredService<IGameConnection>(),
            PlayerIdentity.Current(),
            sp.GetRequiredService<IThemeService>(),
            sp.GetRequiredService<IPlayerProfile>(),
            sp.GetRequiredService<IRoomSession>()));
```

In `src/FrasiSquisite.App/Pages/GamePage.xaml.cs`, sostituire il file intero:

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

con:

```csharp
using FrasiSquisite.App.ViewModels;

namespace FrasiSquisite.App.Pages;

public partial class GamePage : ContentPage
{
    private readonly GameSessionViewModel _viewModel;

    public GamePage(GameSessionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _viewModel = viewModel;
    }

    // Copre l'avvio a freddo (design rientro §5.2): GamePage è l'unica
    // ShellContent dell'app (AppShell.xaml), quindi OnAppearing scatta una
    // volta all'avvio. TryRejoinAsync è già no-op silenzioso se non c'è
    // nulla da rientrare, quindi non serve guardia in più qui.
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.TryRejoinAsync();
    }
}
```

In `src/FrasiSquisite.App/App.xaml.cs`, sostituire (righe 6-46, l'intera classe):

```csharp
public partial class App : Application
{
	private readonly IThemeService _themeService;

	public App(IThemeService themeService)
	{
		InitializeComponent();

		_themeService = themeService;

		// Unico punto che tocca Application.Current.Resources per il tema:
		// vincolo tecnico del lotto (vedi lotto-a-brief.md), non un dettaglio.
		// ThemeService non conosce MAUI e non può farlo da sé; qui reagiamo
		// allo stesso evento sia al primo avvio (ApplyInitial, sotto) sia a
		// ogni cambio da Impostazioni, cosicché il percorso sia uno solo. Ogni
		// riferimento a un token di tema in XAML deve essere {DynamicResource},
		// mai {StaticResource}: è lo scambio di dizionario qui sotto che rende
		// visibile un cambio di tema senza riavviare l'app, e solo
		// {DynamicResource} si accorge che il dizionario è cambiato.
		_themeService.ThemeChanged += ApplicaTema;
		_themeService.ApplyInitial();
	}

	private void ApplicaTema(ThemeChoice tema)
	{
		var dizionari = Resources.MergedDictionaries;
		var precedente = dizionari.FirstOrDefault(d => d is ThemeA or ThemeB);
		if (precedente is not null)
		{
			dizionari.Remove(precedente);
		}

		dizionari.Add(tema == ThemeChoice.SurrealistaPop
			? new ThemeA()
			: new ThemeB());
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}
```

con:

```csharp
public partial class App : Application
{
	private readonly IThemeService _themeService;
	private readonly GameSessionViewModel _gameSession;

	public App(IThemeService themeService, GameSessionViewModel gameSession)
	{
		InitializeComponent();

		_themeService = themeService;
		_gameSession = gameSession;

		// Unico punto che tocca Application.Current.Resources per il tema:
		// vincolo tecnico del lotto (vedi lotto-a-brief.md), non un dettaglio.
		// ThemeService non conosce MAUI e non può farlo da sé; qui reagiamo
		// allo stesso evento sia al primo avvio (ApplyInitial, sotto) sia a
		// ogni cambio da Impostazioni, cosicché il percorso sia uno solo. Ogni
		// riferimento a un token di tema in XAML deve essere {DynamicResource},
		// mai {StaticResource}: è lo scambio di dizionario qui sotto che rende
		// visibile un cambio di tema senza riavviare l'app, e solo
		// {DynamicResource} si accorge che il dizionario è cambiato.
		_themeService.ThemeChanged += ApplicaTema;
		_themeService.ApplyInitial();
	}

	private void ApplicaTema(ThemeChoice tema)
	{
		var dizionari = Resources.MergedDictionaries;
		var precedente = dizionari.FirstOrDefault(d => d is ThemeA or ThemeB);
		if (precedente is not null)
		{
			dizionari.Remove(precedente);
		}

		dizionari.Add(tema == ThemeChoice.SurrealistaPop
			? new ThemeA()
			: new ThemeB());
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell());

		// .WithAutomaticReconnect() si arrende dopo circa 42s di tentativi
		// (ritardi di default 0/2/10/30s): un telefono rimasto in sospensione
		// più a lungo torna in foreground con la connessione già Disconnected,
		// non Reconnecting, quindi nessun evento di trasporto farebbe mai
		// scattare OnReconnected da solo (design rientro §5.3). TryRejoinAsync
		// è già un no-op silenzioso se non c'è nulla da rientrare o se la
		// connessione va comunque ristabilita da sé.
		window.Resumed += (_, _) => _ = _gameSession.TryRejoinAsync();

		return window;
	}
}
```

Nessun test automatico copre questi due file (`.xaml`/ciclo di vita MAUI, vedi Vincoli globali). Verificare solo con la build:

Run: `dotnet build src/FrasiSquisite.App -f net10.0-android`
Expected: completato, 0 errori.

- [ ] **Passo 9: Eseguire l'intera soluzione**

Run: `dotnet test FrasiSquisite.slnx`
Expected: PASS — 822 test verdi in tutto (Shared 86, Domain 518, App 110, Server 108), "Non superati: 0" ovunque.

- [ ] **Passo 10: Commit**

```bash
git add src/FrasiSquisite.App/Services/IRoomSession.cs \
        src/FrasiSquisite.App/Services/PreferencesRoomSession.cs \
        src/FrasiSquisite.App/Services/IGameConnection.cs \
        src/FrasiSquisite.App/Services/SignalRGameConnection.cs \
        src/FrasiSquisite.App/ViewModels/GameSessionViewModel.cs \
        src/FrasiSquisite.App/MauiProgram.cs \
        src/FrasiSquisite.App/Pages/GamePage.xaml.cs \
        src/FrasiSquisite.App/App.xaml.cs \
        tests/FrasiSquisite.App.Tests/FakeRoomSession.cs \
        tests/FrasiSquisite.App.Tests/FakeGameConnection.cs \
        tests/FrasiSquisite.App.Tests/ViewModels/GameSessionViewModelTests.cs
git commit -m "feat(rientro): il client persiste la stanza e tenta il rientro all'avvio e alla riconnessione"
```

---

### Task 5: Verifica manuale end-to-end

Nessun passo di questo task ha un test automatico: il comportamento in background/sospensione del sistema operativo non è simulabile in questo repository (vedi Vincoli globali).

- [ ] **Passo 1: Build e avvio**

Run: `dotnet build FrasiSquisite.slnx`
Expected: completato, 0 errori, 0 avvisi nuovi.

Avviare il server (`dotnet run --project src/FrasiSquisite.Server`) e l'app su almeno due dispositivi/emulatori (o un dispositivo + un client da riga di comando, se disponibile).

- [ ] **Passo 2: Rientro con app in background**

Creare una stanza con almeno due giocatori, avviare la partita. Durante la fase di scrittura, mettere in background (non chiudere) l'app di un giocatore per 10-15 secondi (ben sotto i 30s di grazia), poi riportarla in foreground.

Verificare a occhio:
1. Il giocatore torna sulla schermata di scrittura con la propria casella corrente, non su una schermata vuota o bloccata.
2. Gli altri giocatori non hanno mai visto comparire un bot al posto suo.

- [ ] **Passo 3: Bot che subentra dopo il periodo di grazia**

Ripetere, ma questa volta lasciare l'app in background per più di 30 secondi prima di riportarla in foreground.

Verificare a occhio:
1. Un bot ha scritto la casella del giocatore assente (visibile agli altri come submission avvenuta).
2. Il giocatore, tornato in foreground, riprende comunque il controllo dal round successivo — non resta bloccato né duplica un invio.

- [ ] **Passo 4: Chiusura completa dell'app**

Durante una partita in corso (qualunque fase), terminare del tutto l'app di un giocatore (rimuoverla dal task switcher, non solo metterla in background) e riaprirla entro 30 secondi.

Verificare a occhio:
1. L'app riparte e rientra da sola nella stanza, senza mostrare la Home/lobby vuota.
2. Il giocatore si ritrova sulla schermata corretta per la fase in corso.

- [ ] **Passo 5: Partita non più rientrabile**

Terminare del tutto l'app, attendere che la partita finisca (o forzare un riavvio del server, se comodo farlo in locale), poi riaprire l'app.

Verificare a occhio:
1. L'app mostra la Home/lobby normale, senza sbattere in faccia un errore rosso.

- [ ] **Passo 6: Registrare l'esito**

Annotare qui, prima di considerare il lotto chiuso, quali dei quattro scenari sopra sono stati effettivamente verificati su un device reale (non solo letti) e con quale esito — questo passo non ha un comando da eseguire, è la conferma che la verifica manuale è stata fatta davvero, non solo prevista dal piano.
