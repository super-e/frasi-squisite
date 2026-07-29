# Mappatura del design mobile sull'app esistente

**Data:** 2026-07-29
**Stato:** analisi, nessuna decisione presa

**Sorgente:** progetto Claude Design "Frasi Squisite Mobile App"
(`2b75e09e-7bd6-4e8e-abb4-e76510e50333`), file `Frasi Squisite.dc.html`.
`android-frame.jsx` è la cornice del telefono usata solo per l'anteprima e
`support.js` è il runtime del framework: nessuno dei due contiene design da
implementare.

---

## 1. Corrispondenza fra schermate

| Design | App oggi | Note |
|---|---|---|
| Home | Home | Il design **non ha il campo del server**, che a noi serve. Sposta il gear delle impostazioni in alto a destra, mostra il nickname a piè di pagina, e nasconde il codice stanza dietro un pulsante "Ho un codice" invece di tenerlo sempre visibile. |
| Impostazioni | — | Non esiste. Nel design serve solo a scegliere il tema; per noi è anche il posto naturale dove mettere l'indirizzo del server. |
| Lobby | Lobby | Il design aggiunge: QR, descrizione dello schema, avatar con iniziale, badge "host", righe bot con rinomina/rimuovi, "+ Aggiungi bot", limite di 9 giocatori. |
| Scrittura | Writing | Il design aggiunge l'anello di countdown, usa un'area di testo multiriga invece di un campo singolo, disabilita "Invia" a testo vuoto, e chiude con una nota di piè di pagina. |
| Attesa | Waiting | Il design elenca **chi manca, per nome**, con spunte per chi ha inviato. Noi mostriamo solo `2 di 5`. |
| Reveal | Reveal | Il design mostra le caselle non ancora scoperte come `···`, e ha un battito in più: "Chi l'ha scritta?" è un tocco separato dopo l'ultima parola. |
| Voto | — | Non esiste. |
| Risultati | Finished | La nostra `Finished` elenca le frasi composte. Il design mostra vincitrice, barre dei voti, e un segnaposto per l'illustrazione AI. |

---

## 2. Cosa non richiede alcun cambio al server

Queste cose sono pura presentazione: si fanno nel client e la fase 1 resta la
fase 1.

- Il sistema dei due temi e tutta la tipografia.
- Home, Lobby (senza QR e senza bot), Scrittura (senza timer), Attesa (con i
  soli conteggi che già abbiamo), Reveal, Finished — rivestite.
- La schermata Impostazioni, che ospita la scelta del tema e l'indirizzo del
  server.
- `Frase N di M` durante il reveal: `RevealStepMessage` porta già
  `PhraseIndex` e `TotalPhrases`, il client li ignora.
- Il battito separato "Chi l'ha scritta?": il server manda già gli autori
  insieme alla casella che completa la frase, quindi il client può
  trattenerli e mostrarli al tocco successivo. Nessun cambio di protocollo.
- Caselle `···` per il non ancora scoperto: il client sa quante caselle ha lo
  schema (`RoomStateMessage.SlotCount`) e quante ne ha ricevute.

## 3. Cosa richiede un cambio al server

- **Attesa per nome.** `RoundProgressMessage(Round, Submitted, Total)` deve
  portare anche chi ha inviato. Tocca il DTO, il motore e i test. Piccolo, ma
  è un cambio di protocollo e quindi va con un incremento di
  `ProtocolVersion`. Nota: la spec §10 chiedeva già "chi ha già inviato e chi
  manca" e l'implementazione l'aveva silenziosamente ridotto a un conteggio —
  il design ci ridà ragione contro il codice.
- **Bot.** Un evento nel motore che aggiunga un `Player` con `IsBot: true` e
  `IsConnected: false`, `StartGame` che riempia i non connessi al round 0, un
  metodo hub con DTO, e la UI. Il riempimento delle caselle riusa
  `FillDisconnected`, che esiste già. L'id del bot lo genera l'hub, non il
  motore, per non perdere la riproducibilità da seed.
- **Rinomina e rimozione dei bot.** Due eventi in più, solo in lobby.
- **Timer di round.** È la fase 2 della spec: richiede `TimeProvider` nel
  motore, l'effetto `ScheduleTimer`, e il riempimento allo scadere.
- **Voto e Risultati.** Richiede la fase di voto completa: due stati nuovi
  nella macchina (`Voting`, `Results`), i DTO, il conteggio, la gestione dei
  pari merito. È il grosso della fase 2.
- **QR.** Generazione lato client, ma serve una libreria (ZXing.Net.Maui) —
  quindi un pacchetto nuovo, non un cambio di server.
- **Illustrazione AI.** Fase 4 per intero.

---

## 4. I due nodi tecnici veri

### I font sono metà dell'identità

Il design usa **Unbounded** (600/700/800) per i titoli e **Space Grotesk**
(500/600/700) nel tema scuro. Nel browser arrivano da Google Fonts; in un APK
vanno inclusi come `MauiFont`, quindi scaricati e versionati nel repo. Oggi ci
sono solo i due OpenSans del template.

Entrambi sono sotto SIL Open Font License, quindi si possono redistribuire in
un'app. Va aggiunta la nota di licenza.

Senza i font il design perde gran parte del carattere: i titoli sono la cosa
più riconoscibile del mockup.

### Il tema non è chiaro/scuro di sistema

I due temi differiscono per colori **e** per font **e** per raggio degli angoli
(24 contro 14) e ombre. E il design li fa scegliere esplicitamente
dall'utente in Impostazioni, non li fa seguire l'impostazione di sistema.

Questo esclude `AppThemeBinding`, che in MAUI copre solo la coppia
chiaro/scuro di sistema. Serve uno **scambio di `ResourceDictionary` a
runtime**, con la scelta persistita nelle `Preferences`.

La conseguenza che decide se il lavoro funziona o no: **ogni stile deve
riferirsi alle risorse con `DynamicResource`, non `StaticResource`**. Con
`StaticResource` il valore viene risolto una volta sola e il cambio di tema non
si vede finché l'app non riparte. È il tipo di errore che si scopre a lavoro
finito.

---

## 5. Lotti di lavoro, in ordine di valore per costo

**A — Fondamenta del tema e riveste le schermate esistenti.**
Font nel progetto, due `ResourceDictionary`, stili con `DynamicResource`,
scelta persistita, `GamePage.xaml` riscritto, schermata Impostazioni con tema
e indirizzo del server. Include anche i pezzi gratuiti: `Frase N di M`,
caselle `···`, autori come battito separato, "Invia" disabilitato a vuoto,
"Ho un codice" al posto del campo sempre visibile.
Nessun cambio al server, nessun cambio di protocollo.

**B — Bot.**
Aggiungi, rinomina, rimuovi. Sblocca la verifica in solitaria del gate della
fase 1.

**C — Attesa per nome.**
Cambio di protocollo, incremento di `ProtocolVersion`, allineamento della
spec §10.

**D — Voto e Risultati.**
La fase 2 della spec. Da fare come fase a sé, con il suo piano e il suo ciclo
di review.

**E — Timer, QR, illustrazione AI.**
Fasi 2 e 4 come già pianificate.

---

## 6. Cosa il design dice della spec

Due punti in cui il mockup non aggiunge lavoro ma corregge il progetto:

- L'attesa per nome era un requisito (§10) sceso a conteggio senza che
  nessuno lo dichiarasse. Il design lo riporta a galla.
- La spec non prevedeva alcun tema né schermata impostazioni. Se i temi
  entrano, la spec va aggiornata: diventano una scelta di prodotto, non una
  rifinitura.
