# Frasi Squisite — Design

**Data:** 2026-07-29
**Stato:** approvato in brainstorming, pronto per la pianificazione

---

## 1. Obiettivo

Un gioco per Android che implementa il "cadavere squisito" nella sua variante
surrealista a schema grammaticale: ogni giocatore riempie una casella (soggetto,
aggettivo, verbo…) senza vedere le altre, e alla fine i pezzi si incastrano in
frasi assurde.

Multiplayer online con stanze, pensato per gente **fisicamente nella stessa
stanza** — a cena, in vacanza — ognuno con il proprio telefono.

**Distribuzione:** APK privato e backend self-hosted sul Proxmox di casa. Le
scelte architetturali devono però tenere aperta la strada a una futura
pubblicazione sul Play Store senza riscritture.

**Vincolo trasversale:** è un progetto vivo. Ogni decisione va valutata anche
per quanto costa cambiarla o estenderla fra sei mesi.

---

## 2. Regole del gioco

### 2.1 Schema

Uno **schema** è una sequenza ordinata di K caselle. Ogni casella ha un ruolo
grammaticale, un prompt mostrato al giocatore e un esempio.

Non esiste alcuna validazione di concordanza grammaticale fra le caselle: il
fatto che i pezzi non si incastrino è il divertimento, non un difetto.

Schema di riferimento (`surrealista-classico`, K = 5):

| # | Ruolo       | Prompt                        | Esempio        |
|---|-------------|-------------------------------|----------------|
| 0 | Soggetto    | Un soggetto, con l'articolo   | Il cadavere    |
| 1 | Aggettivo   | Un aggettivo                  | squisito       |
| 2 | Verbo       | Un verbo coniugato            | berrà          |
| 3 | Complemento | Un complemento oggetto        | il vino        |
| 4 | Aggettivo   | Un altro aggettivo            | nuovo          |

### 2.2 Round

Con N giocatori (bot inclusi) e K caselle si costruiscono **N frasi in
parallelo** in **K round**.

Al round *r*, il giocatore *p* riempie la casella *r* della frase
`(p + r) mod N`.

Proprietà garantite da questa formula, per qualsiasi N ≥ 2 e qualsiasi K:

- ogni frase riceve ogni casella esattamente una volta;
- ogni giocatore scrive esattamente K volte;
- nessun giocatore resta mai in attesa di un altro per poter scrivere.

Il round avanza quando **tutti** hanno inviato la propria casella o è scaduto il
timer.

**N è fissato al momento di `StartGame` e non cambia più.** Se un giocatore
abbandona a metà partita, il numero di frasi resta invariato e le sue caselle
successive vengono riempite dal pool. Non si entra in una partita già iniziata:
si può entrare in una stanza solo mentre è in `Lobby`. Chi arriva dopo attende
la partita successiva.

### 2.3 Segretezza

Quando il server chiede al giocatore *p* di scrivere, invia **soltanto il ruolo
grammaticale e il prompt**. Il contenuto delle altre caselle di quella frase non
attraversa mai la rete verso quel client.

La segretezza è quindi una proprietà del protocollo, non una scelta di
presentazione che un client modificato possa aggirare. Questo è il requisito
centrale del gioco.

### 2.4 Reveal e voto

Al termine dei K round le frasi si scoprono **una casella alla volta**,
sincronizzate su tutti i telefoni e ritmate dal server, una frase dopo l'altra.

L'autore di ciascuna casella viene rivelato solo **dopo** che la frase è
completa, mai durante lo scoprimento: sapere chi ha scritto la casella
successiva ne anticiperebbe il contenuto.

Poi ogni giocatore umano vota la frase preferita (non la propria, se la
riconosce — non c'è modo di impedirlo né serve). I bot non votano. Si proclama
la frase vincitrice e, se l'AI è disponibile, se ne genera un'illustrazione.

---

## 3. Architettura

### 3.1 Progetti

Quattro progetti, con dipendenze rigorosamente unidirezionali:

```
FrasiSquisite.Shared    (net10.0)  contratti, DTO, schemi, validazione
        ▲          ▲
        │          │
FrasiSquisite.Domain   FrasiSquisite.App     (net10.0-android, MAUI)
   (net10.0)
        ▲
        │
FrasiSquisite.Server    (net10.0, ASP.NET Core)
```

**`Shared`** — I DTO dei messaggi hub, le definizioni degli schemi grammaticali
e le regole di validazione dell'input. Nessuna dipendenza da MAUI né da ASP.NET
Core. Referenziato da `App` e da `Domain`: i contratti fra client e server non
possono divergere perché sono lo stesso codice.

**`Domain`** — Il `GameEngine`, le modalità di gioco, lo stato e gli effetti.
Codice puro: nessun I/O, nessun `async`, nessuna conoscenza di SignalR, del
database o di HTTP.

**`Server`** — L'hub SignalR, l'adapter che esegue gli effetti, i provider AI,
la persistenza, la cifratura, i timer.

**`App`** — L'applicazione MAUI. Deliberatamente **stupida**: non calcola nulla
sullo stato di gioco, rende quello che il server le dice.

### 3.2 Il motore restituisce effetti, non li esegue

```csharp
public sealed record EngineResult(GameState State, IReadOnlyList<Effect> Effects);

public interface IGameEngine
{
    EngineResult Handle(GameState state, GameEvent evt);
}
```

`Effect` è un tipo somma di record: `SendToPlayer`, `BroadcastToRoom`,
`ScheduleTimer`, `CancelTimer`, `RequestAiPool`, `AppendEvents`, `PersistGame`.

Un adapter sottile nel progetto `Server` (`GameHost`) prende gli effetti e li
esegue: invia via SignalR, arma i timer, chiama l'AI, scrive sul database.

**Conseguenza sui test:** un test asserisce sugli `Effect` prodotti, cioè sui
messaggi che *sarebbero* stati inviati, senza mockare nulla di rete. Una partita
completa da 6 giocatori e 5 round, con disconnessioni e timeout, si simula in
millisecondi. La suite resta inoltre valida senza modifiche se un giorno SignalR
venisse sostituito da un altro trasporto.

### 3.3 Il tempo e il caso sono dipendenze

Il motore non chiama mai `DateTime.UtcNow` né `Task.Delay`. Usa `TimeProvider`
(standard in .NET 10) e, nei test, `FakeTimeProvider` da
`Microsoft.Extensions.TimeProvider.Testing`: un timeout da 60 secondi scade in
zero millisecondi e in modo deterministico.

In un gioco a turni cronometrati questa è la scelta che più incide sulla qualità
della suite. Senza, i test dei timer sono lenti e intermittenti, e il risultato
concreto è che smettono di essere scritti.

Analogamente la casualità (assegnazione delle frasi, pescaggio dal dizionario,
ordine del reveal) passa da `IRandomSource`. Con un seed fisso una partita è
riproducibile bit per bit, e un bug segnalato si ricrea esattamente.

### 3.4 Modalità di gioco sostituibile

```csharp
public interface IGameMode
{
    string Id { get; }
    int PhraseCount(int playerCount, Schema schema);
    SlotAssignment AssignSlot(int round, int playerIndex, int playerCount, Schema schema);
    bool IsComplete(GameState state);
}
```

L'unica implementazione iniziale è `RoleSchemaMode`. La logica va scritta
comunque: metterla dietro un'interfaccia costa quasi nulla e fa sì che la
variante "frase a catena" diventi in futuro una classe nuova invece di una
riscrittura del motore.

---

## 4. Protocollo

### 4.1 Handshake e versione

All'ingresso il client dichiara `ProtocolVersion`. Il server risponde
accettando, degradando, o rifiutando con un messaggio esplicito
("aggiorna l'app").

Con distribuzione via APK i client saranno **sempre** disallineati fra loro.
Senza questo controllo il primo aggiornamento incompatibile si manifesta come
crash inspiegabili sui telefoni di chi non ha aggiornato.

### 4.2 Messaggi

**Client → Server:** `CreateRoom`, `JoinRoom`, `RejoinRoom`, `LeaveRoom`,
`AddBot`, `RemovePlayer`, `SetSchema`, `StartGame`, `SubmitSlot`,
`RequestSuggestion`, `CastVote`, `GetArchive`, `GetGame`.

**Server → Client:** `RoomState`, `GameStarted`, `SlotRequest`, `RoundProgress`,
`RevealStep`, `VotePhase`, `Results`, `ImageReady`, `ProtocolRejected`,
`GameAborted`, `Error`.

`SlotRequest` contiene esclusivamente: indice di round, ruolo, prompt, esempio,
scadenza del timer. **Mai** il contenuto della frase.

### 4.3 Macchina a stati della stanza

```
Lobby ──StartGame──► Writing(round 0)
                          │
              tutti hanno inviato oppure timer scaduto
                          │
                          ▼
                   Writing(round r+1) ──[r+1 = K]──► Reveal
                                                       │
                                                       ▼
                                                     Voting
                                                       │
                                                       ▼
                                                    Results
                                                       │
                                              NewGame ──┴──► Lobby
```

---

## 5. Testabilità e iniezione delle dipendenze

Ogni dipendenza esterna sta dietro un'interfaccia registrata nel container DI.
MAUI e ASP.NET Core usano entrambi
`Microsoft.Extensions.DependencyInjection`: gli idiomi sono identici sui due
lati.

| Interfaccia         | Produzione                | Degrado                      | Test                       |
|---------------------|---------------------------|------------------------------|----------------------------|
| `IAiProvider`       | `PpqAiProvider`           | `StaticDictionaryAiProvider` | `RecordingAiProvider`      |
| `IFieldCipher`      | `AesGcmFieldCipher`       | —                            | `NoOpFieldCipher`          |
| `IArchiveRepository`| `PostgresArchiveRepository`| —                           | `InMemoryArchiveRepository`|
| `IGameConnection`   | `SignalRGameConnection`   | —                            | `FakeGameConnection`       |
| `TimeProvider`      | `TimeProvider.System`     | —                            | `FakeTimeProvider`         |
| `IRandomSource`     | `SystemRandomSource`      | —                            | `SeededRandomSource`        |

Due punti meritano di essere sottolineati.

**Il fallback è un'implementazione, non un `if`.** Quando l'AI è irraggiungibile
il container risolve `StaticDictionaryAiProvider`, che è una vera
implementazione dell'interfaccia. La garanzia "il gioco è giocabile senza AI"
non dipende quindi da rami condizionali sparsi in dieci punti del codice, ed è
il motivo per cui sopravvive ai refactor invece di marcire in silenzio.

**Il client si testa senza server.** Le ViewModel dipendono da
`IGameConnection`, mai da `HubConnection`. Con `FakeGameConnection` si prova
l'intero flusso di schermate a server spento, e si può sviluppare la UI prima
che il backend esista.

---

## 6. Evolvibilità

**Gli schemi grammaticali sono dati, non codice.** Un file JSON — embedded
resource nella v1, servito dal server in seguito — definisce caselle, ruoli e
prompt:

```json
{
  "id": "surrealista-classico",
  "version": 1,
  "nome": "Surrealista classico",
  "caselle": [
    { "ruolo": "Soggetto", "prompt": "Un soggetto, con l'articolo", "esempio": "Il cadavere" }
  ],
  "template": "{0} {1} {2} {3} {4}"
}
```

Aggiungere uno schema "titolo di giornale" o "proverbio" significa modificare un
file. E quando il server servirà gli schemi, si potranno introdurre modalità
nuove **senza pubblicare una nuova APK** — la scelta che più di ogni altra rende
questo un progetto vivo invece che finito.

Il campo `template` con segnaposto numerati consente in futuro composizioni non
lineari (una casella che compare due volte, o in ordine diverso da quello di
scrittura) senza cambiare il formato.

**Migrazioni DB dal giorno uno.** EF Core Migrations, con la prima migrazione
creata insieme al primo schema. Introdurle a posteriori, con partite reali già
archiviate e cifrate, costa sproporzionatamente di più.

**Feature flag in configurazione.** Ciascuna delle quattro funzioni AI si
attiva e disattiva da `appsettings`, indipendentemente dalle altre. Serve per il
degrado, per il controllo dei costi, e per provare varianti fra amici.

---

## 7. Persistenza

### 7.1 Stato vivo

Lo stato della partita in corso vive **solo in memoria** sul server, in un
`RoomRegistry`. Non viene persistito a ogni transizione: raddoppierebbe la
complessità del motore per coprire un riavvio del server a metà round, evento
raro in uso privato e con conseguenza tollerabile.

Se il server si riavvia, le stanze attive sono perse e i client ricevono
`GameAborted`. **Limite accettato consapevolmente per la v1.**

### 7.2 Event log leggero

Durante la partita il motore emette eventi di dominio (`PlayerJoined`,
`GameStarted`, `SlotFilled`, `RoundCompleted`, `VoteCast`, `GameFinished`) che
l'adapter accoda su una tabella append-only.

**Precisazione deliberata: questo non è event sourcing.** Lo stato vivo resta
quello in memoria e non viene mai ricostruito dagli eventi a runtime. Il log è
un registro storico. È la forma leggera della scelta: costa poco più di quanto
già costa salvare il risultato, e abilita in futuro statistiche, replay del
reveal e correzioni retroattive su partite passate — dati che non si possono
retrofittare, perché non sarebbero stati raccolti.

Il risultato finale della partita viene comunque materializzato in tabelle
proprie: leggerlo non deve richiedere di rigiocare il log.

### 7.3 Database

**Postgres** in un container accanto al server. SQLite basterebbe per giocare
fra amici, ma la strada verso il Play Store resta aperta e migrare
SQLite → Postgres a posteriori è uno dei retrofit più fastidiosi.

Tabelle principali: `games`, `phrases`, `slots`, `votes`, `game_events`,
`images`.

### 7.4 Cifratura dei contenuti

`IFieldCipher` cifra i campi di contenuto (testo delle caselle, frase composta,
nickname, immagine generata) con AES-256-GCM prima della scrittura, salvandoli
come `bytea` nella forma `nonce(12B) ‖ ciphertext ‖ tag(16B)`.

Note implementative:

- **Associated data:** ogni cifratura lega come AAD l'identificativo della riga
  e il nome della colonna, così un ciphertext spostato su un'altra riga fa
  fallire la decifratura invece di essere accettato.
- **Versione di chiave:** ogni riga porta un `key_version` (`smallint`), che
  consente la rotazione senza downtime e senza migrare l'archivio.
- Restano in chiaro id, timestamp, numero di giocatori e schema usato: servono
  per ordinare e filtrare, e non rivelano contenuti.

**Costo accettato:** la ricerca testuale nell'archivio non è possibile lato
database. Cercare significa decifrare e filtrare in memoria — sostenibile per
qualche migliaio di partite, non oltre. Da ripensare in caso di pubblicazione.

Le chiavi (cifratura e ppq.ai) arrivano al container come variabili d'ambiente,
custodite in 1Password Environments. Mai nel database, mai in un file
versionato.

### 7.5 Archivio: solo lato server

Il telefono consulta l'archivio su richiesta e lo tiene in memoria per la sola
durata della sessione, senza scriverlo su disco. Una copia in chiaro
dell'archivio su cinque telefoni annullerebbe la cifratura del database.

Il costo — niente archivio offline — è nullo per un gioco che offline non
funziona comunque. La condivisione su WhatsApp resta possibile: testo o immagine
vengono generati al volo e passati allo share sheet di Android, come azione
esplicita dell'utente.

---

## 8. Intelligenza artificiale

Un'unica interfaccia `IAiProvider`, invocata **esclusivamente dal server**. La
chiave API non entra mai nell'APK: un APK si decompila in trenta secondi.

### 8.1 Suggerimenti su richiesta

Il giocatore in blocco creativo chiede tre proposte per la propria casella. Il
server manda al modello **soltanto il ruolo grammaticale e lo schema, mai il
contenuto della frase in costruzione**.

Non è solo igiene sui dati: suggerimenti contestuali permetterebbero di dedurre
cosa hanno scritto gli altri leggendo proposte troppo calzanti. Sarebbe una
falla nella meccanica del gioco, prima ancora che nella riservatezza.

Limite: 2 richieste per giocatore per round.

### 8.2 Riempimento dei timeout, con pre-fetch

All'inizio di ogni round il server richiede in background un **pool** di parole
per ciascun ruolo dello schema. Allo scadere del timer di un giocatore, pesca
dal pool: istantaneo.

Il pre-fetch non è un'ottimizzazione ma un requisito funzionale. Chiamare il
modello al momento del timeout bloccherebbe la partita per alcuni secondi
davanti a tutti, ed è la differenza fra una funzione che funziona e una che
irrita. Se il pool è vuoto, si pesca dal dizionario statico compilato nel
binario.

### 8.3 Bot

Per il `GameEngine` un bot è un giocatore identico agli altri: occupa uno slot,
riceve `SlotRequest`, risponde. **Il motore non contiene alcun ramo
condizionale che distingua bot da umani** — è ciò che tiene pulita la logica di
gioco.

Attingono allo stesso pool pre-fetchato, con un ritardo simulato di 3–15 secondi
(a 200 ms sarebbero fastidiosi e smaccati). Non votano: il vincitore lo decidono
le persone.

### 8.4 Illustrazione della frase vincitrice

Dopo il voto, Nano Banana genera un'immagine surrealista dalla frase vincente.
La chiamata è **asincrona e mai bloccante**: la schermata dei risultati mostra
subito la classifica, con un segnaposto al posto dell'immagine, e invia
`ImageReady` quando è pronta.

L'immagine viene salvata cifrata su disco del server e decifrata al volo quando
servita.

### 8.5 Requisito di degrado

**Il gioco è interamente giocabile senza AI.** Se ppq.ai è irraggiungibile, in
errore o a credito esaurito: i suggerimenti si disattivano con un messaggio
esplicito, timeout e bot pescano dal dizionario statico, l'illustrazione viene
saltata. Nessuna di queste condizioni interrompe o degrada una partita in corso.

Questo è un requisito verificato da test, non un ripiego opportunistico.

---

## 9. Identità e resilienza

**Identità.** Un `playerId` (GUID) generato al primo avvio e custodito in
`SecureStorage`. Nessun account, nessuna registrazione. Il nickname è
un'etichetta modificabile, non un'identità.

**Riconnessione.** SignalR con riconnessione automatica; al ripristino il client
invia `RejoinRoom(codice, playerId)` e ritorna esattamente allo stato in cui
era.

**Giocatore assente.** Chi non rientra entro il timer si vede riempire la
casella dal pool e la partita prosegue. Non si aspetta mai nessuno.

**Host che esce.** Il ruolo di host passa al giocatore presente da più tempo. La
partita non muore con lui.

**Server che si riavvia.** Le stanze in memoria sono perse; i client ricevono
`GameAborted` con un messaggio chiaro e tornano alla home.

---

## 10. Interfaccia

Schermate: Home → Crea/Unisciti → Lobby → Scrittura → Attesa → Reveal → Voto →
Risultati → Archivio.

**Ingresso via QR.** L'host mostra un QR con il codice stanza, gli altri
inquadrano. Digitare il codice resta possibile come alternativa.

**Reveal teatrale.** Le caselle si scoprono una alla volta, sincronizzate su
tutti i telefoni e ritmate dal server. È il momento in cui sta la risata del
gioco e va costruito apposta, non risolto con una lista.

**Attesa.** La schermata mostra chi ha già inviato e chi manca, con il countdown
del round.

---

## 11. Strategia di test

**Il grosso della copertura sta su `Domain`.** Essendo puro, ci si simulano
partite intere con ogni combinazione di N e K, disconnessioni, timeout,
abbandoni e cambi di host — in millisecondi, senza rete e senza database.

Test specifici richiesti:

- **Proprietà dei round:** per ogni N in 2..12 e K in 3..8, verificare che ogni
  frase riceva ogni casella una volta sola e che ogni giocatore scriva
  esattamente K volte.
- **Segretezza:** nessun `SendToPlayer` deve mai contenere il testo di una
  casella non ancora rivelata. È il test che protegge il requisito centrale del
  gioco e va scritto esplicitamente.
- **Timer:** con `FakeTimeProvider`, scadenze e riempimenti automatici.
- **Degrado AI:** partita completa con `IAiProvider` che lancia eccezioni a ogni
  chiamata; deve concludersi normalmente.
- **Cifratura:** round-trip, rotazione di chiave, e tamper detection (un
  ciphertext spostato su un'altra riga deve far fallire la decifratura).
- **Contratti:** snapshot test sulla serializzazione dei DTO di `Shared`, per
  intercettare le rotture di compatibilità prima che le scopra un client vecchio.
- **Integrazione hub:** `TestServer` ASP.NET Core con client SignalR reale.

L'implementazione del motore procede in TDD.

---

## 12. Deploy

Container Docker su un LXC del Proxmox: il server ASP.NET Core e Postgres,
orchestrati da `docker compose`. Segreti da variabili d'ambiente.

L'APK viene distribuito come file, con versione del protocollo allineata a
quella del server.

Per giocare fuori casa il server va esposto (Cloudflare Tunnel o Tailscale).
Non è nello scope della v1.

---

## 13. Fasi

Ogni fase produce qualcosa di **già giocabile**.

1. **Nucleo** — `Domain` in TDD, hub SignalR, client MAUI. Schema unico, senza
   AI, senza voto, senza persistenza. Una partita completa dall'inizio al
   reveal.
2. **Robustezza** — Timer di round, riconnessione, passaggio di host, fase di
   voto, ingresso via QR.
3. **Persistenza** — Postgres, migrazioni, cifratura dei campi, event log,
   archivio server-side, condivisione.
4. **AI** — Le quattro funzioni, con feature flag e degrado verificato da test.
5. **Rifinitura** — Schemi multipli, reveal teatrale, illustrazione.

---

## 14. Fuori scope e vincoli noti

- **Persistenza dello stato vivo:** un riavvio del server interrompe le partite
  in corso.
- **Ricerca testuale nell'archivio:** impossibile lato database per via della
  cifratura; richiede decifratura in memoria.
- **Moderazione dei contenuti:** non necessaria in uso privato. Una eventuale
  pubblicazione sul Play Store richiederà policy di moderazione e segnalazione
  dei contenuti generati dagli utenti, oltre a privacy policy e data safety
  form.
- **iOS:** fuori scope. MAUI lo renderebbe possibile, ma nessuna scelta viene
  presa per agevolarlo.
- **Esposizione pubblica del server:** fuori scope della v1.
