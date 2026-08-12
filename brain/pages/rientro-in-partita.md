---
id: rientro-in-partita
title: "Rientro in partita dopo disconnessione: periodo di grazia 30s + persistenza client"
category: decision
status: active
created: "2026-08-12T10:52:36"
updated: "2026-08-12T10:54:41"
---

<!-- compiled_truth -->
**Cosa:** un giocatore disconnesso ha 30 secondi di grazia
(`GameHost`, `IGracePeriodTimer`) prima che un bot subentri; se rientra
entro la grazia (`RejoinRoomRequest` con lo stesso `playerId`, protocollo
v9), il motore rimanda il messaggio esatto della fase corrente —
incluso il caso "la tua casella era già stata riempita da un bot", che
il client mostra come attesa invece di far riscrivere la casella. In
fase `Lobby` l'espulsione è **immediata**, nessuna grazia (non c'è
partita in corso da salvare).

**Perché ora e non nel piano originale:** motivato dall'osservazione
diretta giocando ("basta un niente per uscire e non poter rientrare").
Priorità esplicita dell'utente: "mi importa che l'esperienza utente sia
smooth e frictionless" — ha spostato questo lavoro davanti ad altre
voci di backlog quando l'ha notato.

**Bug reali trovati SOLO dalla revisione, non dall'implementatore:**
1. Timer di grazia orfano che espelleva un giocatore già rientrato
   (race fra `Cancel()` e lettura del token).
2. Guardia sull'identità di connessione mancante sia sul percorso di
   grazia sia sull'evizione immediata in lobby — un fix parziale al
   primo giro aveva "riaperto" la stessa vulnerabilità sul secondo
   percorso, scoperta solo alla ri-revisione.

**Perché rilevante oltre il codice:** dimostra il valore della
revisione di ramo intero **dopo** che le revisioni per singolo task
sono già passate pulite — nessuna delle due revisioni per task aveva
visto il problema, perché ciascuna guardava solo il proprio task.

**Tre punti di tentativo di rientro nel client:** avvio a freddo
(`GamePage.OnAppearing`), resume dell'app (`Window.Resumed`),
riconnessione di trasporto (`IGameConnection.Reconnected`, evento
separato da `ConnectionInterrupted`). Tutti e tre chiamano
`GameSessionViewModel.TryRejoinAsync()`, che non fa nulla se non c'è
una stanza salvata in `IRoomSession`.

**Collegata alle pagine radice flow** (sequenza di rientro) **e
architecture** (stato vivo solo in memoria — è il motivo per cui serve
un periodo di grazia anziché una vera persistenza).


## Timeline

- time: 2026-08-12T10:52:36
  kind: decision
  summary: "Created this page: Rientro in partita dopo disconnessione: periodo di grazia 30s + persistenza client"
  source: "docs/superpowers/specs/2026-08-08-rientro-in-partita-design.md; commit 95bd50f..012723d"
  affects: [rientro-in-partita]

- time: 2026-08-12T10:53:59
  kind: decision
  summary: "catturata da spec, piano e storico commit del lotto"
  source: "spec 2026-08-08-rientro-in-partita; commit 95bd50f..012723d"
  affects: [rientro-in-partita]

- time: 2026-08-12T10:54:41
  kind: decision
  summary: corretti riferimenti alle pagine radice flow e architecture
  source: self-review lint-links
  affects: [rientro-in-partita]
