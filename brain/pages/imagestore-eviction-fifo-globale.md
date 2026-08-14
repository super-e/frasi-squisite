---
id: imagestore-eviction-fifo-globale
title: "ImageStore: eviction FIFO globale fra stanze, accettata"
category: decision
status: active
tags: [immagini, imagestore, eviction]
created: "2026-08-14T13:22:49"
updated: "2026-08-14T13:22:58"
---

<!-- compiled_truth -->
**Cosa:** `ImageStore` (`src/FrasiSquisite.Server/Images/ImageStore.cs`) è un
unico singleton condiviso da tutte le stanze, con una coda FIFO e un
budget in byte **globali** (default 75MB), non per-stanza. Quando il
budget sfora, `Salva` sfratta le immagini più vecchie in assoluto,
indipendentemente da quale stanza le ha prodotte: il traffico di una
partita attiva può far sfrattare l'immagine ancora visibile di
un'altra partita conclusa in un'altra stanza.

**Perché è accettato così:** renderlo per-stanza richiederebbe
riservare una fetta di budget a ogni stanza attiva (o un limite per
stanza sopra a quello globale), il che a sua volta richiede più
memoria di ricambio per non sprecare budget con stanze inattive — il
backlog stima ~75MB in più per eliminare del tutto la contesa. Non è
stato implementato: il traffico reale (poche stanze contemporanee, in
un contesto amicale) rende la contesa fra stanze concorrenti rara
nella pratica.

**Quando riconsiderarlo:** se il numero di stanze concorrenti crescesse
davvero (uso non più solo fra amici), o se uno sfratto cross-stanza
venisse osservato giocando, non solo dedotto dal codice.


## Timeline

- time: 2026-08-14T13:22:49
  kind: decision
  summary: "Created this page: ImageStore: eviction FIFO globale fra stanze, accettata"
  source: "docs/superpowers/backlog.md §4 rilievo 6; piano 2026-08-14-rilievi-minori"
  affects: [imagestore-eviction-fifo-globale]

- time: 2026-08-14T13:22:58
  kind: decision
  summary: "Decisione iniziale: FIFO globale accettata, non per-stanza"
  source: "docs/superpowers/backlog.md §4 rilievo 6"
  affects: [imagestore-eviction-fifo-globale]
