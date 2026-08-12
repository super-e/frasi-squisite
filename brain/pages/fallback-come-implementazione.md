---
id: fallback-come-implementazione
title: "Il degrado (no-AI, no-rete) è un'implementazione vera, non un if"
category: decision
status: active
created: "2026-08-12T10:52:36"
updated: "2026-08-12T10:52:59"
---

<!-- compiled_truth -->
**Cosa:** quando una dipendenza esterna non è disponibile (AI
irraggiungibile o senza chiave), il container DI risolve
un'**implementazione vera e diversa** dell'interfaccia — non un ramo
`if (aiDisponibile)` sparso nel codice chiamante.

**Verificato nel codice attuale:** `IAiTextProvider` e
`IAiImageProvider` hanno rispettivamente `DisabledAiTextProvider` e
`DisabledAiImageProvider` come implementazioni di degrado. L'unico
interruttore è `AiOptions.Abilitato => !string.IsNullOrWhiteSpace(ApiKey)`.

**Alternative scartate:** feature flag booleani controllati a chiamata
("if AI enabled then... else...") — scartati perché il ramo di degrado
finirebbe duplicato in ogni punto che invoca l'AI, con alto rischio che
uno dei punti venga dimenticato durante un refactor.

**Perché:** la garanzia "il gioco è giocabile senza AI" non dipende da
disciplina sparsa in dieci punti del codice, e sopravvive ai refactor
invece di marcire in silenzio. È un requisito verificato da test
(partita completa con provider che lancia eccezioni ad ogni chiamata),
non un ripiego opportunistico.

**Scostamento dalla spec originale:** la spec (§8.5) immaginava
**quattro** funzioni AI con feature flag *indipendenti* in
`appsettings`. Nella pratica sono state costruite solo due funzioni
(rifinitura, illustrazione — non i "suggerimenti su richiesta"), con
un **unico** interruttore globale (presenza della chiave), non quattro
flag separati. Vedi [[persistenza-mai-implementata]] per altri
scostamenti simili tra piano e realizzazione.


## Timeline

- time: 2026-08-12T10:52:36
  kind: decision
  summary: "Created this page: Il degrado (no-AI, no-rete) è un'implementazione vera, non un if"
  source: "docs/superpowers/specs/2026-07-29-frasi-squisite-design.md §5, §8.5; src/FrasiSquisite.Server/Ai/AiOptions.cs"
  affects: [fallback-come-implementazione]

- time: 2026-08-12T10:52:59
  kind: decision
  summary: catturata dalla spec e verificata nel codice AI attuale
  source: "spec §5, §8.5; AiOptions.cs"
  affects: [fallback-come-implementazione]
