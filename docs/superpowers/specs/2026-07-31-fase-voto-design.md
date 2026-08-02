# Fase di voto — Design

**Data:** 2026-07-31
**Stato:** approvato in brainstorming, pronto per la pianificazione
**Riferimento:** [design generale](2026-07-29-frasi-squisite-design.md), §2.4 e §13 (fase 2)

---

## 1. Obiettivo e confini

Dopo il reveal la partita si chiude senza che nessuno abbia detto quale frase
fosse la migliore. Questa spec aggiunge una fase di voto fra il reveal e la
fine, e una classifica al posto dell'elenco piatto di frasi.

Non è solo una funzione a sé: il lotto successivo — l'AI — deve illustrare **la
frase vincitrice** (design generale §8.4), e oggi quella frase non esiste. Il
voto è il suo prerequisito.

**Fuori dalla spec, di proposito:**

- Il **timer** di round e di fase (design generale §13, fase 2). La sua assenza
  è il motivo per cui l'host può forzare la chiusura: senza né timer né pulsante,
  un giocatore che posa il telefono blocca la partita a tempo indefinito.
- Il **rientro in partita**, anch'esso fase 2. Va detto esplicitamente perché
  è tentante progettarci sopra: `GameHub.JoinRoom` rifiuta chi arriva a
  partita iniziata, e `OnPlayerJoined` risponde `GAME_IN_PROGRESS` fuori dalla
  lobby senza comunque riportare `IsConnected` a vero. **Oggi chi cade non
  torna**: resta disconnesso, il bot gioca per lui, ed esce dai votanti attesi
  per sempre. Ogni comportamento "quando rientra…" descriverebbe codice
  irraggiungibile, e come tale non va né specificato né testato qui.
- L'**illustrazione** della vincitrice e tutto il resto dell'AI.
- La **persistenza** dell'esito. La classifica vive quanto la stanza.
- Il **container Docker** del server, richiesto nella stessa sessione e
  concordato subito dopo questo lotto.

---

## 2. Regole del voto

**Un voto a testa.** Si tocca una frase, la più votata vince. Scartate le
alternative a punteggio pesato, podio e categorie: aggiungono interazione su
mobile e conteggi, per risolvere un problema — i pareggi — che in un gioco da
tavolo è accettabile mostrare com'è.

**Non esiste "la propria frase".** Con l'assegnazione `(p + r) mod N` del design
generale §2.2, ogni giocatore ha contribuito a ogni frase. La regola "non puoi
votare la tua", che in un gioco così ci si aspetterebbe, non ha nulla da
esprimere: non va implementata né spiegata a chi gioca.

**Votano gli umani connessi.** Cioè i giocatori con `IsBot` falso **e**
`IsConnected` vero. I bot non votano — il design generale §8.3 lo stabilisce già:
"il vincitore lo decidono le persone". Chi è disconnesso è già rimpiazzato da un
bot fino al rientro, quindi non vota nemmeno lui.

**Il voto non si cambia.** Coerente con le caselle: una volta scritta, non si
riscrive. Un secondo voto riceve un errore privato `ALREADY_VOTED`, gemello di
`ALREADY_SUBMITTED`.

**Il voto è cieco.** Si vota sui soli testi. Gli autori compaiono con la
classifica, dopo la chiusura — vedi §3.

**Solo i totali.** La classifica mostra i punteggi, non chi ha votato cosa. Il
dettaglio resta comunque nello stato del server, quindi mostrarlo in futuro
costerà un campo in più nel messaggio finale — un cambio di protocollo, ma
additivo: nessuna regola di gioco da ripensare, nessun dato da ricostruire.

---

## 3. Segretezza degli autori

Oggi `RevealStepMessage` porta gli autori appena la frase è completa, cioè
**prima** del voto. Con il voto cieco quel campo non deve più viaggiare durante
il reveal.

**Il campo esce dal tipo, non viene lasciato vuoto.** È lo stesso principio già
scritto su `SlotRequestMessage`: *"questa assenza è il modo in cui la segretezza
del gioco è garantita dal tipo e non dalla disciplina di chi scrive il codice"*
(design generale §2.3, §4.2). Un campo sempre vuoto è una regressione che
aspetta di succedere; un campo che non esiste no.

Gli autori viaggiano quindi **solo** nel messaggio di fine partita.

Questo sposta dopo il voto il momento più divertente del gioco — "sei stato tu a
scrivere *questo*?". È una scelta consapevole: in cambio si vota sui testi e non
sulle simpatie.

**E costa meno di quanto sembri.** La frase *i* prende la casella *r* dal
giocatore `(i − r) mod N`: quando le caselle sono almeno quanti i giocatori — lo
schema di default ne ha otto — **ogni frase contiene il contributo di tutti**.
Sotto ogni frase comparirebbe lo stesso insieme di nomi, cambiando solo
l'abbinamento fra nome e casella. Come informazione per scegliere vale zero, e
occupa la schermata più affollata dell'app.

---

## 4. Flusso

```
Reveal (ultima frase completa)
   ↓  broadcast: elenco frasi da votare
Voting
   ↓  ogni voto: broadcast avanzamento "2 di 3"
   ↓  chiusura: tutti i votanti attesi hanno votato, oppure l'host forza
Finished
   ↓  broadcast: classifica con autori, punteggi, vincitrici
```

I pulsanti "Nuova partita" e "Torna alla lobby" restano dove sono oggi, sulla
schermata finale, che diventa la classifica.

**La condizione di chiusura è un predicato solo:** ogni votante atteso compare
nella mappa dei voti. Con l'insieme dei votanti vuoto è vera per vacuità, ed è
la risposta giusta — chiude senza vincitrice.

**Va valutata anche all'ingresso nella fase**, non solo dopo ogni voto.
L'host che fa avanzare l'ultimo passo del reveal e poi chiude l'app lascerebbe
altrimenti la stanza appesa a zero votanti, senza nessun evento successivo che
rivaluti la condizione.

---

## 5. Casi limite

L'insieme dei votanti cambia durante la fase. È lì che stanno i casi veri.

| Caso | Comportamento |
|---|---|
| Un giocatore si disconnette mentre si vota | I votanti attesi calano. Se i rimasti avevano già votato, **il voto chiude nell'istante della disconnessione**, non al voto successivo. Va gestito come effetto della disconnessione. |
| Un giocatore che aveva già votato si disconnette | **Il suo voto resta valido e conta.** La mappa è indicizzata per giocatore, non per connessione: chi ha detto la sua l'ha detta. Il suo nome esce solo dall'insieme di chi si sta ancora aspettando. |
| Un giocatore rientra durante il voto | **Non può succedere** — vedi §1. Il rientro in partita è fase 2. Nessun comportamento da specificare, nessun test da scrivere. |
| L'host se ne va durante il voto | L'host passa al più anziano fra i connessi, meccanismo già esistente. Il nuovo host eredita il pulsante di chiusura. |
| Nessuno vota | Nessuna vincitrice: classifica a punteggio zero. **Stato legale, non eccezione.** Sarà il lotto AI a decidere cosa illustrare quando non c'è vincitrice. |
| Pareggio | Tutte le frasi a punteggio massimo sono vincitrici ex aequo e compaiono in cima. |
| Voto per un indice inesistente | Errore privato, stato invariato. |
| Voto fuori dalla fase di voto | Errore privato, stato invariato. |
| Chiusura forzata da chi non è host | Errore privato `NOT_HOST`, già esistente. |

---

## 6. Dominio

### 6.1 Stato

`RoomPhase` guadagna `Voting` fra `Reveal` e `Finished`.

`GameState` guadagna una mappa immutabile `giocatore → indice della frase`,
vuota fuori dalla fase di voto e azzerata da "nuova partita" e "torna alla
lobby" insieme al resto.

### 6.2 Eventi

Due nuovi: **voto espresso** e **chiusura forzata**. In più, i due eventi
esistenti di popolazione — giocatore uscito e giocatore rientrato — devono ora
rivalutare la chiusura quando la stanza è in `Voting`.

Il motore resta quello che è: `Handle(state, evt) → EngineResult(State,
Effect[])`, puro, senza I/O, senza orologio, senza `async`.

### 6.3 Il conteggio esce dal motore

Un tipo puro riceve la mappa dei voti e il numero di frasi, e produce classifica
e indici vincitori. Vive da solo perché è una funzione di una riga di stato: si
prova senza montare una partita intera, ed è dove stanno le sottigliezze.

**Ordinamento deterministico:** punteggio decrescente, a parità **indice
crescente**. Senza il secondo criterio la classifica potrebbe cambiare fra due
build a parità di voti — è esattamente l'errore già commesso e corretto sul
catalogo degli schemi.

**Le vincitrici sono tutti gli indici a punteggio massimo, ma l'insieme è vuoto
quando i voti totali sono zero.** Altrimenti "nessuno ha votato" si
presenterebbe come "hanno vinto tutte a pari merito", che è falso e a valle
manderebbe in confusione l'illustrazione.

### 6.4 Split di `GameEngine`

`GameEngine.cs` è a 652 righe. Il ledger annotava lo split in `partial` come
rimandato quando ne aveva 502: è cresciuto del 30% da allora, e il voto lo fa
crescere ancora.

Si divide per fase — dispatch e aiutanti comuni nel file principale, poi lobby,
scrittura, reveal, voto. **Nessun cambio di comportamento**, e i test esistenti
restano il paracadute che lo dimostra.

---

## 7. Protocollo — v5

`ProtocolVersion.Current` passa da 4 a 5. `IsCompatible` richiede uguaglianza
stretta: l'APK installato oggi verrà rifiutato e va reinstallato.

**Dal client:** voto espresso (codice stanza, indice della frase) e richiesta di
chiusura (codice stanza).

**Dal server:**

- **Elenco da votare** — i soli testi delle frasi composte. Nessun autore.
- **Avanzamento del voto** — votanti / attesi, gemello di `RoundProgressMessage`.
- **`RevealStepMessage`** — perde `Authors` (§3).
- **Fine partita** — da lista di stringhe a lista di risultati: per ogni frase
  testo, autori, voti ricevuti e se ha vinto, **già ordinati**. Il client
  disegna, non calcola: stessa scelta già fatta per `SchemaView` e `PlayerView`.

**Errori nuovi:** `ALREADY_VOTED`, indice fuori intervallo, voto fuori fase.

---

## 8. Client

Una schermata di voto nuova, la schermata finale che diventa classifica, il
reveal che smette di disegnare gli autori.

**Il reveal perde un battito, non solo un'etichetta.** Oggi il pulsante ha tre
stati: "Rivela la prossima parola", poi "Chi l'ha scritta?" — che non chiama il
server, mostra soltanto gli autori già arrivati col passo che ha completato la
frase — e infine "Prossima frase". Senza autori nel messaggio, lo stato di mezzo
non ha più niente da mostrare: **il pulsante torna a due stati** e sparisce la
nota "Scritta da: A · B · C" sotto la frase. Va tolto anche il campo che teneva
gli autori in disparte fra un tocco e l'altro, altrimenti resta stato morto che
il prossimo lettore scambierà per una funzione.

**Vincolo noto, da rispettare fin dalla prima riga.** `GameHost` invia **tutti**
gli effetti prima che il metodo dell'hub ritorni: quando l'ultimo votante vota,
il messaggio di chiusura arriva *durante* la sua `await`. Scrivere lo stato
della schermata dopo quella `await` sovrascrive quello appena arrivato, e la
schermata resta ferma sul voto a partita conclusa.

È la forma esatta del difetto che ha bloccato una partita reale su "in attesa 1
di 2": stessa dinamica, altro punto. La difesa è la stessa già in uso in
`SubmitSlotAsync` — catturare la fase prima della chiamata e scrivere solo se
nel frattempo non è cambiata.

---

## 9. Test

**Conteggio (puro):** pareggi, zero voti, ordinamento deterministico a parità di
punteggio, insieme vincitori vuoto quando i voti sono zero.

**Motore:** ingresso in fase, voto valido, voto doppio, indice invalido, voto
fuori fase, chiusura forzata dall'host, rifiuto a chi host non è. E i casi di
popolazione mobile: chi esce chiude, il voto di chi esce che resta valido, zero
votanti che chiude all'ingresso.

**Protocollo:** contratto e serializzazione dei messaggi nuovi; assenza di
`Authors` in `RevealStepMessage`.

**Hub:** due client che votano davvero fino alla classifica, e isolamento
dell'errore — l'`ALREADY_VOTED` di uno non deve raggiungere l'altro.

**Client — e qui va esteso uno strumento che esiste già a metà.**
`FakeGameConnection` ha `MessaggioDuranteInvio`, aggiunto proprio per riprodurre
il bug del blocco su "in attesa": consegna un messaggio *durante* la chiamata,
prima che il `Task` ritorni. Ma è agganciato **solo a `SubmitSlotAsync`**.

Il voto ha la stessa identica forma — l'ultimo votante riceve la chiusura mentre
la sua `await` è ancora in volo — quindi l'aggancio va reso disponibile anche
sulla chiamata di voto. Senza, il test di quel caso non è esprimibile e
scriveremmo verde sopra un difetto presente.

---

## 10. Cosa resta aperto

- Il **flaky** intermittente di `GameHubTests`: una run fallita e due passate
  nella stessa sessione, più un episodio precedente su un altro test dello
  stesso file. Ipotesi corrente: attese a timeout fisso in una suite che gira in
  un minuto e mezzo. Nessuna diagnosi, e il voto ci aggiunge test d'integrazione.
- Il **timer** di fase resta il completamento naturale della chiusura forzata.
