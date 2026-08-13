---
id: retry-riconnessione-comandi
title: "Retry di riconnessione sui comandi, e perché il bottone Riconnetti non lo riusa"
category: decision
status: active
created: "2026-08-13T17:58:06"
updated: "2026-08-13T17:58:39"
---

<!-- compiled_truth -->
**Cosa:** ogni comando della ViewModel (`GameSessionViewModel.EseguiComandoAsync`)
che fallisce per un guasto di trasporto (non un rifiuto del server, quello
resta `HubException`) tenta **una sola volta** una riconnessione + rientro
in stanza (`ReconnectTransportAndRoomAsync`), poi ripete l'azione originale
una volta sola. Se fallisce ancora, stesso messaggio generico di sempre.

**Perché il bottone "Riconnetti" non riusa `EseguiComandoAsync`:**
`ReconnectAsync` (il comando dietro il bottone) ha un try/catch a sé, non
passa da `EseguiComandoAsync(ReconnectTransportAndRoomAsync)` come una
bozza iniziale del piano prevedeva. Il motivo: `EseguiComandoAsync` ritenta
l'azione al suo interno chiamando di nuovo l'helper di riconnessione — ma
quando l'azione stessa *è* l'helper di riconnessione, il risultato è un
secondo `RejoinRoomAsync` duplicato verso il server a una singola
pressione. Bug reale, trovato in revisione, non un'ipotesi: il piano
stesso violava la propria regola "un solo tentativo, nessun
retry-del-retry" (vedi [[rientro-in-partita]] per il rientro esplicito che
questo helper riusa).

**Invariante di pulizia banner/errore:** qualunque messaggio dal server
diverso da `ErrorMessage` (in `GameSessionViewModel.OnMessage`) è la prova
che il giro di andata e ritorno funziona di nuovo, e svuota sia `ErrorText`
sia `ConnectionBanner`. Prima di questo lavoro `ConnectionBanner` ("un bot
gioca al tuo posto") non veniva mai svuotato — restava visibile per sempre
una volta comparso. Questa pulizia vive in `OnMessage` e non in un hook
`OnScreenChanged` perché durante il Reveal ogni `RevealStepMessage`
riassegna `Screen` allo stesso valore che ha già, e il setter generato da
`[ObservableProperty]` non invoca `On<Prop>Changed` quando il valore nuovo
è uguale al vecchio: un hook non pulirebbe mai un errore o un banner
rimasti stantii durante il Reveal.

**Rischio accettato, non mitigato:** le azioni di gioco hanno quasi tutte
guardie server-side contro il doppio invio (`ALREADY_SUBMITTED`,
`ALREADY_VOTED`, guardie di fase), quindi un retry del comando originale è
sicuro. L'unica eccezione nota è `AddBot` (nessuna guardia): un retry
sfortunato può aggiungere un bot in più, rimediabile con `RemoveBotAsync`
già esistente. Scelta deliberata di non aggiungere deduplica.

**Raggio d'azione:** solo client (`GameSessionViewModel`, `GamePage.xaml`),
nessun cambiamento a `IGameConnection`, `SignalRGameConnection` o al
server.


## Timeline

- time: 2026-08-13T17:58:06
  kind: decision
  summary: "Created this page: Retry di riconnessione sui comandi, e perché il bottone Riconnetti non lo riusa"
  source: "docs/superpowers/specs/2026-08-13-retry-riconnessione-design.md; piano 2026-08-13; commit a10501b..48274a6"
  affects: [retry-riconnessione-comandi]

- time: 2026-08-13T17:58:39
  kind: decision
  summary: decisione iniziale e le due invarianti scoperte in revisione
  source: brain update-truth
  affects: [retry-riconnessione-comandi]
