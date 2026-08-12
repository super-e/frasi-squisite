---
id: segretezza-di-protocollo
title: "Segretezza come proprietà del protocollo, non della UI"
category: concept
status: active
created: "2026-08-12T10:52:36"
updated: "2026-08-12T10:53:25"
---

<!-- compiled_truth -->
**Definizione:** quando il server chiede a un giocatore di scrivere una
casella, il messaggio (`SlotRequestMessage`) porta **esclusivamente**
ruolo grammaticale, prompt, esempio e il flag `GiaInviato` — mai il
testo delle altre caselle della stessa frase, nemmeno cifrato o
nascosto lato client.

**Perché è "di protocollo" e non "di presentazione":** un client
modificato (decompilato, o un client alternativo scritto da zero) non
potrebbe comunque leggere il contenuto altrui, perché quel contenuto
non attraversa mai la rete verso di lui. Se la segretezza dipendesse
solo dal client che sceglie di non mostrare un campo che *ha* ricevuto,
basterebbe un client modificato per rompere il requisito centrale del
gioco.

**Estensione al reveal:** durante il reveal (`RevealStepMessage`), gli
autori delle caselle vengono rivelati solo **dopo** che la frase è
completa — mai durante lo scoprimento — per lo stesso motivo: sapere chi
ha scritto la casella successiva ne anticiperebbe il contenuto.

**Verifica richiesta esplicitamente dalla spec (§11):** "nessun
`SendToPlayer` deve mai contenere il testo di una casella non ancora
rivelata" è un test esplicito da scrivere, non solo un'intenzione di
design — la spec lo chiama "il test che protegge il requisito centrale
del gioco".

**Controesempio che NON rispetta questo concetto:** un ipotetico
messaggio di debug che includesse "l'anteprima di come sarà la frase"
prima del reveal violerebbe la segretezza anche se mai mostrato in UI,
perché la violazione è nel payload di rete, non nello schermo.


## Timeline

- time: 2026-08-12T10:52:36
  kind: decision
  summary: "Created this page: Segretezza come proprietà del protocollo, non della UI"
  source: "docs/superpowers/specs/2026-07-29-frasi-squisite-design.md §2.3, §11; src/FrasiSquisite.Shared/Protocol/ServerMessages.cs"
  affects: [segretezza-di-protocollo]

- time: 2026-08-12T10:53:25
  kind: decision
  summary: catturata dalla spec e verificata nel protocollo attuale
  source: "spec §2.3, §11; ServerMessages.cs"
  affects: [segretezza-di-protocollo]
