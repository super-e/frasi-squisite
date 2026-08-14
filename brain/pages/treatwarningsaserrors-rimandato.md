---
id: treatwarningsaserrors-rimandato
title: "TreatWarningsAsErrors repo-wide: rimandato, non rifiutato"
category: decision
status: active
tags: [nullable, build, repository]
created: "2026-08-14T13:22:35"
updated: "2026-08-14T13:22:45"
---

<!-- compiled_truth -->
**Cosa:** `ImageStore.Salva` torna `string?` e il nullable non è imposto
come errore di build in nessun progetto del repository (nessun
`TreatWarningsAsErrors`/`WarningsAsErrors` in `Directory.Build.props` o
nei singoli `.csproj`, verificato con una ricerca esaustiva il
2026-08-14).

**Perché non è stato attivato qui:** il chiamante esistente di `Salva`
(`GameHost.AvviaIllustrazione`) gestisce già correttamente il caso
`null` — non è un bug in produzione, è mancanza di una rete di
sicurezza a livello di compilatore. Attivare `TreatWarningsAsErrors`
è per costruzione una modifica repository-wide (vive in
`Directory.Build.props`, non in un singolo progetto): può far
emergere warning nullable preesistenti altrove, mai misurati, e
farebbe fallire la build in un punto imprevedibile per un rilievo
minore isolato.

**Quando riconsiderarlo:** la prossima volta che si tocca
`Directory.Build.props` per un altro motivo, o come iniziativa a sé
stante con la sua build di verifica dedicata — non dentro un lotto di
rilievi minori.


## Timeline

- time: 2026-08-14T13:22:35
  kind: decision
  summary: "Created this page: TreatWarningsAsErrors repo-wide: rimandato, non rifiutato"
  source: "docs/superpowers/backlog.md §4 rilievo 2; piano 2026-08-14-rilievi-minori"
  affects: [treatwarningsaserrors-rimandato]

- time: 2026-08-14T13:22:45
  kind: decision
  summary: "Decisione iniziale: rimandato, non rifiutato"
  source: "docs/superpowers/backlog.md §4 rilievo 2"
  affects: [treatwarningsaserrors-rimandato]
