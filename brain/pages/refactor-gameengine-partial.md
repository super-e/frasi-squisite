---
id: refactor-gameengine-partial
title: "GameEngine spezzato in partial class, una per fase"
category: decision
status: active
created: "2026-08-12T10:52:37"
updated: "2026-08-12T10:54:41"
---

<!-- compiled_truth -->
**Cosa:** `GameEngine` è diventato una `partial class` divisa per fase
di gioco (`GameEngine.Writing.cs`, `.Reveal.cs`, `.Refining.cs`,
`.Players.cs`, ecc.) invece di un unico file.

**Quando e perché:** durante il lotto voto (`refactor(motore): GameEngine
spezzato in partial per fase`, subito prima di aggiungere la fase di
voto stessa) — segno che il file unico era già scomodo da tenere in
testa prima ancora di aggiungere una fase nuova.

**Pattern confermato dalle fasi successive:** ogni fase aggiunta dopo
(AI/rifinitura, illustrazione, rientro) ha seguito la stessa
convenzione — un nuovo `GameEngine.<Fase>.cs` invece di far crescere un
file esistente. Coerente con la preferenza generale del progetto per
file piccoli e a responsabilità singola (vedi la pagina radice
architecture).

**Alternative implicite scartate:** un `GameEngine` monolitico con
region/commenti a separare le fasi — mai adottato; la separazione è
sempre stata a livello di file, non di organizzazione interna a un file
unico.


## Timeline

- time: 2026-08-12T10:52:37
  kind: decision
  summary: "Created this page: GameEngine spezzato in partial class, una per fase"
  source: commit 5172d95
  affects: [refactor-gameengine-partial]

- time: 2026-08-12T10:54:00
  kind: decision
  summary: catturata dal commit di refactor
  source: commit 5172d95
  affects: [refactor-gameengine-partial]

- time: 2026-08-12T10:54:41
  kind: decision
  summary: corretto riferimento alla pagina radice architecture
  source: self-review lint-links
  affects: [refactor-gameengine-partial]
