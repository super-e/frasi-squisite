---
slug: background
title: Project background
role: project background
updated: "2026-08-12T10:55:15"
---

# Project background

**Cos'è.** Un'implementazione multiplayer del "cadavere squisito"
surrealista a schema grammaticale: ogni giocatore riempie una casella
(soggetto, aggettivo, verbo…) senza vedere le altre, e alla fine i pezzi
si incastrano in frasi assurde che nessuno ha scritto per intero e
tutti hanno scritto in parte.

**Per chi.** Gente **fisicamente nella stessa stanza** — a cena, in
vacanza — ognuno col proprio telefono. Non è pensato per giocare a
distanza: è un gioco da tavolo digitale, non un sostituto della
presenza.

**Distribuzione.** APK privata (non Play Store) + backend self-hosted
su un LXC del Proxmox di casa dell'autore, dietro un reverse-proxy
Caddy. Nessun account, nessuna registrazione: un `playerId` generato al
primo avvio e custodito in `SecureStorage`.

**Il vincolo dichiarato che guida ogni scelta: "è un progetto vivo".**
Ogni decisione architetturale nella spec originale viene esplicitamente
valutata anche per quanto costerebbe cambiarla o estenderla fra sei
mesi — non solo per la correttezza immediata. Coerente con la memoria
utente già presente prima di questo brain: priorità a testabilità e
evolvibilità sopra la profondità sulla sicurezza.

**Perché le scelte architetturali tengono aperta la via al Play Store**
anche se non è l'obiettivo attuale: Postgres invece di SQLite (mai
comunque implementato, vedi [[persistenza-mai-implementata]]), cifratura
dei contenuti pianificata dal giorno uno. Nella pratica il progetto ha
proceduto per fasi via via più mirate al gioco effettivamente giocato
(reveal, voto, AI, rientro, overlay immagine) piuttosto che completare
il piano di persistenza/pubblicazione originale — vedi la pagina radice
roadmap.

**Non obiettivi, dichiarati nella spec originale:**
- iOS (MAUI lo permetterebbe, nessuna scelta lo agevola)
- Esposizione pubblica del server nella v1
- Moderazione dei contenuti (non necessaria in uso privato)
- Ricerca testuale nell'archivio (impossibile lato database con
  cifratura — comunque non rilevante, l'archivio non è mai stato
  costruito)

## Bassa confidenza / da confermare con l'utente

Quanto sopra è tratto dalla spec di design (`docs/superpowers/specs/
2026-07-29-frasi-squisite-design.md`) e dal README, non da
un'intervista diretta. Non risulta dai documenti **chi** gioca
tipicamente (famiglia? amici? un gruppo fisso?), né se esista un
orizzonte concreto per una pubblicazione pubblica, né quanto la
metafora del "progetto vivo" sia un vincolo permanente o una fase.
