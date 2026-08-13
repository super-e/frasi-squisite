---
id: rifinitura-guardia-rilassata
title: "La guardia sulla singola parola nella rifinitura è stata rimossa"
category: decision
status: active
created: "2026-08-13T09:02:23"
updated: "2026-08-13T09:02:57"
---

<!-- compiled_truth -->
**Cosa:** `RefinementGuard` (Domain, puro) non verifica più che il testo
rifinito di una casella contenga alla lettera quanto scritto dal
giocatore. Prima quel controllo era assoluto: bastava che il modello
cambiasse una desinenza (plurale, genere, coniugazione) perché la
casella tornasse al testo grezzo, insieme a qualunque vera riscrittura.
Restano invariate le altre tre guardie (non vuota, non oltre 200
caratteri, non ripete il letterale del template).

**Perché è una scelta degna di nota:** questo progetto ha un principio
dichiarato altrove nel brain (vedi [[fallback-come-implementazione]]) —
"un prompt e' una preghiera, la garanzia sta nel codice". Questa
rimozione è uno scostamento **deliberato e informato** da quel
principio, limitato alla fedeltà testuale della singola parola, non
un'eccezione silenziosa.

**Come si è arrivati alla decisione:** ho presentato esplicitamente il
tradeoff all'utente con tre opzioni (nessun cambiamento, una guardia
più permissiva basata su "stessa radice della parola", nessuna guardia
affatto) prima di implementare. L'utente ha scelto "nessuna guardia,
fidati del prompt" — la terza volta che gli è stata posta la stessa
domanda in forme diverse nella stessa sessione (una volta in
brainstorming, una volta quando un revisore indipendente ha riproposto
la stessa guardia "a radice" con dati concreti su un caso degenere:
"il cadavere squisito" → "il defunto elegante" passerebbe senza
controlli). In entrambi i casi la risposta è rimasta la stessa;
l'unica concessione è stata rafforzare il *prompt* perché resti
"aderente alla radice della parola data", non il codice.

**Rischio accettato esplicitamente:** un modello può in teoria
sostituire una parola con un'altra senza che nessun controllo se ne
accorga — mitigato solo dal prompt, non da codice provabile. Da
rivedere se in pratica capitassero derive vistose (nessun meccanismo
di rilevamento automatico esiste oggi).

**Cosa NON è stato toccato:** le tre guardie strutturali (vuoto,
lunghezza, non ripete il template) restano codice puro e testato; il
motore non può ancora fondere o eliminare caselle. Solo la fedeltà del
*contenuto* di una singola casella è passata dal codice al prompt.


## Timeline

- time: 2026-08-13T09:02:23
  kind: decision
  summary: "Created this page: La guardia sulla singola parola nella rifinitura è stata rimossa"
  source: "docs/superpowers/specs/2026-08-12-migliora-rifinitura-design.md; commit bf22d7c..e170fc8"
  affects: [rifinitura-guardia-rilassata]

- time: 2026-08-13T09:02:57
  kind: decision
  summary: catturata dal design e dallo storico di revisione del lotto migliora-rifinitura
  source: "spec 2026-08-12; sessione stessa"
  affects: [rifinitura-guardia-rilassata]
