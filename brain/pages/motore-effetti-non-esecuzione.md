---
id: motore-effetti-non-esecuzione
title: "Il GameEngine restituisce effetti, non li esegue"
category: decision
status: active
created: "2026-08-12T10:52:36"
updated: "2026-08-12T10:54:41"
---

<!-- compiled_truth -->
**Cosa:** `IGameEngine.Handle(GameState, GameEvent)` ritorna
`EngineResult(GameState, IReadOnlyList<Effect>)`. Il motore non chiama
mai SignalR, non scrive su disco, non arma timer reali: produce una
lista di `Effect` (tipo somma: `SendToPlayer`, `BroadcastToRoom`,
timer, chiamate AI...) che un adapter sottile in `Server`
(`GameHost`) esegue davvero.

**Alternative scartate:** un motore che chiama direttamente le
dipendenze (SignalR, timer) dietro interfacce mockate nei test —
scartata perché anche mockando, una partita completa con
disconnessioni e timeout richiederebbe orchestrare mock complessi e
tempo reale o quasi-reale.

**Perché:** un test asserisce sugli `Effect` prodotti — cioè sui
messaggi che *sarebbero* stati inviati — senza mockare nulla di rete.
Una partita completa da 6 giocatori e 5 round, con disconnessioni e
timeout, si simula in millisecondi. La suite resta valida anche se
SignalR fosse sostituito da un altro trasporto.

**Raggio d'azione:** è la decisione architetturale fondante di
`Domain` (vedi la pagina radice architecture). Ogni fase di gioco
aggiunta dopo (voto, AI, rientro) ha seguito lo stesso pattern: nuovo
`GameEvent`, nuovo/i `Effect`, mai una chiamata diretta a I/O dentro
`GameEngine.*.cs`. Collegata a [[fallback-come-implementazione]] (il
fallback funziona proprio perché l'esecuzione degli effetti è
centralizzata in un solo adapter sostituibile).


## Timeline

- time: 2026-08-12T10:52:36
  kind: decision
  summary: "Created this page: Il GameEngine restituisce effetti, non li esegue"
  source: "docs/superpowers/specs/2026-07-29-frasi-squisite-design.md §3.2; commit 707050d"
  affects: [motore-effetti-non-esecuzione]

- time: 2026-08-12T10:52:59
  kind: decision
  summary: catturata dalla spec originale e dal codice attuale
  source: "spec §3.2"
  affects: [motore-effetti-non-esecuzione]

- time: 2026-08-12T10:54:41
  kind: decision
  summary: "corretto riferimento alla pagina radice architecture (non va in [[ ]])"
  source: self-review lint-links
  affects: [motore-effetti-non-esecuzione]
