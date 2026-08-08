# Rientro in partita — Design

**Data:** 2026-08-08
**Stato:** approvato in brainstorming, pronto per la pianificazione
**Riferimenti:** [design AI](2026-08-03-ai-design.md) §1 ("Il rientro in partita, ancora fase 2: chi cade non torna.") — questo documento è quella fase 2.

---

## 1. Obiettivo e confini

**Il problema, visto giocando:** basta un niente — schermo spento, app in
background, un blip di rete — perché un giocatore venga espulso dalla
partita e un bot prenda il suo posto, senza modo di tornare. In un party
game a turni, dove si passa buona parte del tempo ad aspettare gli altri col
telefono in tasca, questo è il caso comune, non l'eccezione.

**Obiettivo:** un giocatore che si disconnette per un tempo ragionevole deve
poter rientrare nella stessa partita, sulla stessa schermata in cui l'ha
lasciata, senza dover fare nulla di esplicito — che l'app sia rimasta viva
in background o sia stata chiusa del tutto dal sistema.

**Fuori scope, di proposito:**

- **Recupero delle azioni del bot già avvenute.** Se il periodo di grazia è
  scaduto e il bot ha già scritto una casella o votato, non si torna
  indietro. Il giocatore riprende il controllo dal turno successivo.
- **Notifiche push** quando tocca di nuovo al giocatore rientrato. Il rientro
  risolve "non riesco più a tornare dentro", non "non so quando è il mio
  turno" — un problema diverso.
- **Timeout configurabile per stanza.** Un solo valore fisso (§3), come il
  timeout della rifinitura e dell'illustrazione altrove nel codice.
- **Verifica sulla configurazione reale del reverse proxy (Caddy, CT100)**:
  fuori da questo repository, non verificabile dal codice. Se il problema
  osservato persistesse dopo questo lavoro, è il prossimo posto da guardare.

---

## 2. Cosa già esiste, e cosa manca

Verificato leggendo il codice attuale, non assunto:

- **L'identità del giocatore è già persistente.** `PlayerIdentity` in
  `MauiProgram.cs` salva un GUID stabile in `SecureStorage` al primo avvio.
  Il server usa questo stesso id come chiave in `GameState.Players` —
  un rientro può quindi riconoscere "sei tu" senza inventare nulla di nuovo.
- **`RoomCode` non è persistito.** Vive solo come proprietà osservabile in
  `GameSessionViewModel`, in memoria: chiudere l'app lo perde.
- **Il server non aspetta mai.** `GameHub.OnDisconnectedAsync` dispatcha
  `PlayerLeft` immediatamente, per qualunque motivo di disconnessione. Il
  motore (`GameEngine.Players.cs`, `OnPlayerLeft`) marca subito
  `IsConnected = false` e, se si è in fase di scrittura, un bot riempie la
  casella nello stesso istante.
- **Nessun evento riporta `IsConnected` a `true`.** Nemmeno un secondo
  `PlayerJoined` per lo stesso id lo farebbe: oggi si limita a ri-mandare lo
  stato senza toccare il flag (`OnPlayerJoined`, ramo "giocatore già
  presente").
- **`JoinRoom` rifiuta esplicitamente chi arriva a partita iniziata**
  (`if (stanza.Phase != RoomPhase.Lobby) throw ...`) — quindi oggi non
  esiste **nessuna via**, nemmeno teorica, per rientrare a partita avviata.
- **Il client tenta già una riconnessione di trasporto**
  (`.WithAutomaticReconnect()`, politica di default), ma lo dice il commento
  nel codice stesso: apre una connessione nuova che non recupera
  l'appartenenza ai gruppi SignalR della stanza, già rimossa dal server.
  Oggi l'unico effetto visibile è un banner permanente ("un bot gioca al tuo
  posto"), mai un vero rientro.
- **Le stanze non scadono mai da sole.** `IRoomRegistry` è un dizionario in
  memoria senza TTL: una stanza esiste finché il processo del server non
  riparte. Un rientro, anche a distanza di minuti, trova quindi la stanza
  ancora lì (a meno di un riavvio del server nel frattempo — §5).

---

## 3. Architettura

### 3.1 Periodo di grazia, lato server

`GameHub.OnDisconnectedAsync` non dispatcha più `PlayerLeft` subito. Avvia
invece un timer di **30 secondi** in `GameHost`, con lo stesso pattern già
usato per la rifinitura e l'illustrazione (`Task.Run` sganciato che rientra
con una `DispatchAsync` quando scade — vedi `AvviaRifinitura`,
`AvviaIllustrazione`): il tempo vive nel livello che tiene l'orologio, mai
nel motore.

```
disconnessione
   ↓  GameHost avvia un timer di 30s per (stanza, giocatore)
rientro entro 30s ────────────► timer annullato, nessun bot, PlayerLeft mai dispatchato
rientro dopo 30s ─────────────► il bot ha già giocato quel turno, si riprende da quello dopo
nessun rientro ───────────────► il timer scade, PlayerLeft dispatchato come oggi
```

`GameHost` tiene una mappa `(RoomCode, PlayerId) → CancellationTokenSource`
per i timer pendenti. Un rientro che arriva in tempo la trova e annulla il
timer *prima* di dispatchare l'evento di rientro (§3.2) — nessun lucchetto
nuovo da inventare: `DispatchAsync` serializza già tutti gli eventi per
stanza, quindi l'ordine fra "annulla il timer" e "il timer scade" è già ben
definito da quella stessa serializzazione.

**Il periodo di grazia è unico e vale in ogni fase**, lobby inclusa: non fa
differenza per il motore *perché* un giocatore è sparito, solo che è
sparito. Nessuna casistica speciale per fase in `GameHost`.

### 3.2 Nuovo evento di dominio: `PlayerRejoined`

```csharp
public sealed record PlayerRejoined(Guid PlayerId) : GameEvent;
```

Gestito in `GameEngine.Players.cs`, accanto a `OnPlayerJoined`/`OnPlayerLeft`:

- Se il giocatore non esiste nella stanza: nessun effetto (difesa in
  profondità — `GameHub` valida già prima di dispatchare, §3.3).
- Se è già connesso (rientro duplicato o in corsa): nessuna modifica,
  stesso schema idempotente di `OnPlayerJoined`.
- Altrimenti: `IsConnected` torna `true`, e la risposta è
  `BroadcastToRoom(RoomState(...))` come sempre, più — solo nelle fasi dove
  serve un dato in più oltre allo stato stanza — un `SendToPlayer` mirato al
  solo giocatore rientrato: `SlotRequestMessage` in scrittura,
  `RevealStepMessage` in reveal (via `FrammentiReveal`, già esistente),
  `VoteRequestMessage` in voto (via `FrasiComposte`, già esistente),
  `GameFinishedMessage` a partita conclusa (via `Classifica`, già
  esistente). In lobby e in rifinitura basta `RoomState`: la schermata non
  ha altro da mostrare (una lista di giocatori, uno spinner) e il client
  sceglie già lo schermo giusto solo dalla fase. **Nessun formato nuovo per
  il contenuto**: è lo stesso messaggio che il giocatore avrebbe ricevuto
  restando connesso, ricostruito da zero dallo stato attuale, non salvato in
  cache da nessuna parte.

### 3.3 `GameHub.RejoinRoom`

```csharp
public async Task RejoinRoom(RejoinRoomRequest request)
{
    RichiediProtocolloCompatibile(request.ProtocolVersion);

    if (!rooms.TryGet(request.RoomCode, out var stanza) ||
        stanza.FindPlayer(request.PlayerId) is null)
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

A differenza di `JoinRoom`, **non** controlla la fase — è pensato apposta
per funzionare a partita già iniziata. La validazione (stanza esiste?
giocatore riconosciuto?) avviene *prima* di toccare il motore, stesso
pattern già usato in `SetSchema` per uno schema inesistente: un rifiuto
"normale" non genera un'eccezione, genera un messaggio mirato al chiamante.

---

## 4. Protocollo (v8 → v9)

Due tipi nuovi, nessuna modifica a quelli esistenti:

```csharp
public sealed record RejoinRoomRequest(int ProtocolVersion, Guid PlayerId, string RoomCode);

public sealed record RejoinRejectedMessage;
```

`RejoinRoomRequest` rispecchia `JoinRoomRequest` senza `Nickname` (il
giocatore esiste già in stanza, il nickname non cambia). `RejoinRejectedMessage`
non porta un motivo: al client non serve distinguere "stanza sparita" da
"non ti riconosco" — in entrambi i casi il comportamento è lo stesso
(§5.2). Se in futuro servisse per i log del server, si aggiunge lì, non nel
messaggio.

`ProtocolVersion.Current` passa da 8 a 9: un client v8 non saprebbe
interpretare `RejoinRejectedMessage`, e un rientro silenzioso che fallisce
in modo rumoroso (eccezione non gestita) sarebbe peggio di niente. Stesso
rifiuto esplicito ("aggiorna l'app") già in uso.

---

## 5. Client

### 5.1 Persistenza del `RoomCode`

Nuova interfaccia `IRoomSession`, stesso schema di `IPlayerProfile` (che il
ViewModel già usa per il nickname — la ViewModel dipende dall'interfaccia,
mai da `Preferences` direttamente, perché è compilata anche nel progetto di
test):

```csharp
public interface IRoomSession
{
    string RoomCode { get; }
    void Save(string roomCode);
    void Clear();
}
```

Implementazione di produzione su `Preferences.Default` (non è un segreto,
come il nickname — niente `SecureStorage`). `GameSessionViewModel` salva a
ogni `RoomStateMessage` ricevuto (già passa da lì oggi) e cancella quando si
torna alla lobby, una partita finisce, o un rientro viene rifiutato.

### 5.2 Tentativo di rientro: due punti d'ingresso, una sola funzione

`GameSessionViewModel` guadagna `TryRejoinAsync()`: se `IRoomSession.RoomCode`
non è vuoto, chiama `RejoinRoomAsync(playerId, roomCode)` e non fa altro —
il successo o il fallimento arrivano come messaggi (§4), gestiti dal
consueto smistamento in `OnMessage`. Su `RejoinRejectedMessage`: cancella il
salvataggio, resta (o torna) alla lobby, **nessun banner d'errore** — dal
punto di vista dell'utente non è un errore, è solo "quella partita non c'è
più". Il tentativo **non** passa da `EseguiComandoAsync` (che mostrerebbe
qualunque `HubException` come banner rosso): un rientro silenzioso deve
poter fallire in silenzio, anche per un guasto di trasporto puro (app
offline all'avvio).

Due punti la richiamano:

1. **All'avvio**, prima di mostrare la lobby — copre la chiusura completa
   dell'app.
2. **Sull'evento `Reconnected`** della connessione — copre il blip di rete
   o il ritorno in foreground con l'app ancora viva.

Per il punto 2 serve distinguere "il trasporto è tornato" da "il trasporto
è caduto/sta tentando": oggi `ConnectionInterrupted` scatta indistintamente
su `Reconnecting`, `Reconnected` e `Closed`, perché finora comunque non si
poteva fare nulla di diverso nei tre casi. `IGameConnection` guadagna un
evento `Reconnected` separato, che scatta solo sul vero ripristino del
trasporto; `ConnectionInterrupted` resta per gli altri due (il banner
"connessione instabile" durante il tentativo).

### 5.3 Ciclo di vita MAUI

`.WithAutomaticReconnect()` senza parametri si arrende dopo un giro di
tentativi che dura in tutto circa 42s (ritardi di default 0/2/10/30s). Un
telefono rimasto in sospensione più a lungo di così torna in foreground con
la connessione già `Disconnected`, non `Reconnecting` — nessun evento di
trasporto arriverà mai a far scattare il punto 2 sopra.

In `App.xaml.cs`, sull'evento `Resumed` della `Window` (l'API moderna, non
l'`OnResume` legacy di `Application`): se la connessione non risulta
connessa, la si ristabilisce esplicitamente e si richiama `TryRejoinAsync()`.

---

## 6. Casi limite

- **Rientro dopo che il bot ha già giocato il turno** (grazia scaduta): non
  si annulla quel che il bot ha già fatto. Comportamento già presente
  nell'attuale gestione di `IsConnected`, non cambia.
- **Rientro duplicato / in corsa**: idempotente, nessuna modifica di stato
  oltre al primo (§3.2).
- **Corsa fra scadenza della grazia e rientro**: risolta dalla stessa
  serializzazione per-stanza che `DispatchAsync` già garantisce — nessun
  lucchetto aggiuntivo.
- **Riavvio del server durante l'attesa**: la stanza sparisce da
  `IRoomRegistry` (in memoria, nessuna persistenza — invariato da questo
  lavoro). Un rientro successivo trova semplicemente `rooms.TryGet` fallito
  → `RejoinRejectedMessage`, stesso percorso di "stanza sconosciuta".
- **Rientro in lobby**: nessuna differenza di trattamento — se il periodo di
  grazia scade mentre si è ancora in lobby, `OnPlayerLeft` rimuove il
  giocatore dalla lista **per davvero** (comportamento esistente, invariato:
  "in lobby si esce davvero"). Un rientro tardivo trova quindi il proprio
  `PlayerId` non più in `state.Players` → stesso `RejoinRejectedMessage`
  di un giocatore mai esistito. Non è un caso speciale da gestire: è già
  coperto dal controllo di `GameHub.RejoinRoom` così com'è.

---

## 7. Testing

- **Motore (puro)**: nuovi test su `PlayerRejoined` — ripristina
  `IsConnected`, produce il messaggio di risincronizzazione corretto per
  ciascuna fase, idempotente se già connesso, nessun effetto se il
  giocatore non esiste.
- **`GameHost`**: il periodo di grazia dispatcha `PlayerLeft` se non
  annullato entro la finestra, non dispatcha nulla se annullato prima —
  con un orologio iniettabile/finto nei test, mai un vero `Task.Delay` da
  attendere.
- **`GameHub` / integrazione**: `RejoinRoom` con `PlayerId` sconosciuto o
  stanza inesistente produce `RejoinRejectedMessage`, mai un'eccezione non
  gestita.
- **Client**: `IRoomSession` con un finto in memoria (stesso schema del
  finto già esistente per `IPlayerProfile`) — salvataggio a ogni
  `RoomStateMessage`, cancellazione a fine partita e su rifiuto, tentativo
  di rientro all'avvio se c'è un codice salvato, nessun banner d'errore sul
  rifiuto.
- **Non testabile in automatico** (come già oggi per `.xaml`/MAUI):
  l'aggancio `Window.Resumed` in `App.xaml.cs`, e il comportamento reale di
  sospensione di rete del sistema operativo. Verifica manuale su device,
  esplicitamente segnalata nel piano — non spacciata per copertura
  automatica che non esiste.

---

## 8. Fuori scope per questo lotto, da rivedere in futuro

- Se il problema di disconnessione osservato persistesse anche dopo questo
  lavoro, il prossimo sospetto è la configurazione del reverse proxy Caddy
  su CT100 (timeout di inattività sulla connessione WebSocket) — non
  verificabile da questo repository, richiede accesso diretto a CT100.
- Un valore di grazia diverso per fase (es. più permissivo in scrittura, più
  stretto in voto) non è stato scartato per motivi tecnici, solo per
  restare al minimo che risolve il problema riportato. Facile da introdurre
  in seguito se servisse: il timer vive già fuori dal motore.
