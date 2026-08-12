---
id: persistenza-mai-implementata
title: "La persistenza Postgres/cifratura pianificata non è mai stata costruita"
category: decision
status: active
created: "2026-08-12T10:52:36"
updated: "2026-08-12T10:53:25"
---

<!-- compiled_truth -->
**Cosa:** la spec originale (§7) pianificava, come fase 3 dedicata:
Postgres in container, EF Core Migrations dal giorno uno, cifratura
AES-256-GCM dei campi di contenuto (`IFieldCipher`), event log
append-only (`game_events`), archivio server-side interrogabile con
condivisione. **Nessuna di queste esiste nel codice attuale.**

**Evidenza:** `src/FrasiSquisite.Server/` non contiene alcun
riferimento a EF Core, Postgres, `IFieldCipher` o `IArchiveRepository`.
Lo stato vivo resta nel `RoomRegistry` in memoria (`ConcurrentDictionary`),
esattamente come da spec §7.1 ("stato vivo... solo in memoria") — quella
parte *è* stata realizzata come da piano. È la fase successiva
(persistenza storica, cifratura, archivio) a non essere mai partita.

**Perché non è (necessariamente) un problema:** il progetto ha
proseguito per fasi guidate da cosa rende la partita *giocata* migliore
(voto, AI, rientro, overlay immagine) piuttosto che seguire l'ordine
delle fasi della spec originale. Coerente con l'ottica "progetto vivo":
le fasi si sono riordinate in base a cosa emergeva giocando davvero,
non al piano scritto a freddo il 29 luglio.

**Raggio d'azione:** se mai ripresa, la persistenza tocca `Server` in
modo isolato (nuovo layer, nessuna modifica a `Domain` — il motore resta
puro). Impatta anche l'architettura di deploy (nuovo container Postgres
nel `docker compose`). Nessun impatto sul protocollo esistente finché
non si aggiungono messaggi di archivio.

**Basso confidenza:** non è chiaro dai documenti se questo sia un
abbandono deliberato o semplicemente non ancora affrontato — da
confermare con l'utente se rilevante.


## Timeline

- time: 2026-08-12T10:52:36
  kind: decision
  summary: "Created this page: La persistenza Postgres/cifratura pianificata non è mai stata costruita"
  source: "confronto fra docs/superpowers/specs/2026-07-29-frasi-squisite-design.md §7 e src/FrasiSquisite.Server/ attuale"
  affects: [persistenza-mai-implementata]

- time: 2026-08-12T10:53:25
  kind: decision
  summary: scostamento rilevato confrontando spec e codice durante brain-bootstrap
  source: "confronto spec §7 / codice, 2026-08-12"
  affects: [persistenza-mai-implementata]
