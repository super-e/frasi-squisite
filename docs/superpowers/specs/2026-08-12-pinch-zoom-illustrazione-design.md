# Pinch-to-zoom sull'illustrazione ingrandita — Design

**Data:** 2026-08-12
**Stato:** approvato in brainstorming, pronto per la pianificazione
**Riferimenti:** [design dell'overlay](2026-08-08-illustrazione-overlay-design.md) — questo documento lo estende dopo un primo giro di prova su dispositivo.

---

## 1. Obiettivo e confini

**Il problema, visto giocando sul telefono:** l'overlay a schermo intero
(già shippato) apre e chiude bene, ma l'immagine resta al suo
ingrandimento fisso. L'auto-rotazione dello schermo aiuta a vedere
meglio in alcuni casi, ma non basta quando servono davvero dettagli più
grandi.

**Obiettivo:** pizzicare per ingrandire/rimpicciolire l'illustrazione
dentro l'overlay, e trascinare per esplorarla quando è ingrandita.

**Fuori scope, di proposito:**

- **Doppio tocco per zoomare/tornare a 1×.** Il pizzico basta; un
  doppio tocco aggiungerebbe un secondo gesto da ricordare senza un
  bisogno chiaro.
- **Persistenza dello zoom fra un'apertura e l'altra.** Ogni apertura
  dell'overlay riparte da 1×: è coerente con l'overlay stesso, che è
  stato esplicito che l'immagine si vede sempre per intero appena
  aperta.
- **Zoom oltre 4×.** L'immagine è generata a 1K (spec AI §7): oltre una
  certa soglia si vedrebbero solo pixel, non dettagli.

---

## 2. Cosa già esiste, e cosa manca

- **L'overlay esiste già** (`GamePage.xaml`): un `Grid` con sfondo
  semitrasparente, `IsVisible` legato a `ExpandedImageUrl`, un
  `TapGestureRecognizer` sul `Grid` che chiama `CollapseImageCommand`
  per chiudere ovunque si tocchi.
- **Nessuno stato di zoom esiste**, né nel `ViewModel` né nella vista:
  l'`Image` dentro l'overlay non ha `Scale`/`TranslationX`/
  `TranslationY` bindati né gesture oltre al tap del genitore.
- **Il tasto Indietro Android chiude già l'overlay incondizionatamente**
  (`GamePage.xaml.cs`, `OnBackButtonPressed`) — resta valido come via di
  chiusura garantita indipendentemente dallo zoom, senza bisogno di
  modifiche.

---

## 3. Architettura

### 3.1 Stato di zoom: solo nella vista, non nel ViewModel

Scala e traslazione sono stato di UI effimero (si azzerano ad ogni
apertura), non stato di gioco: vivono come campi privati in
`GamePage.xaml.cs`, non in `GameSessionViewModel`. Coerente con la
filosofia del progetto ("l'App è deliberatamente stupida, non calcola
nulla sullo stato di gioco") — qui non c'è stato di gioco da calcolare,
solo geometria di un gesto.

```csharp
private double _scaleCorrente = 1;
private double _scalePartenza = 1;
private double _xOffset;
private double _yOffset;
```

### 3.2 Pizzico per zoomare

`PinchGestureRecognizer` sull'`Image` dell'overlay, gestito in
code-behind (`OnPinchUpdated`), pattern standard MAUI: aggiorna
`_scaleCorrente` clampato fra 1 e 4, applica `Image.Scale` e ricalcola
`TranslationX`/`TranslationY` in modo che il punto pizzicato resti
fermo sotto le dita (ancoraggio al centro del pizzico, non all'angolo
dell'immagine).

### 3.3 Trascinamento quando zoomato

`PanGestureRecognizer` sullo stesso `Image`, attivo solo quando
`_scaleCorrente > 1` (altrimenti un trascinamento a 1× non ha nulla da
spostare). Il rientro elastico ai bordi si applica **al termine** del
gesto (`GestureStatus.Completed`): se il trascinamento porta un bordo
dell'immagine scalata oltre il centro dello schermo, la traslazione
viene ricalcolata al valore massimo consentito prima di essere
applicata con un'animazione breve (`Image.TranslateTo`, ~150ms) — non
un vincolo rigido applicato ad ogni frame, che renderebbe il
trascinamento "gommoso" e sgradevole da usare.

### 3.4 Tocco per chiudere, adattato allo zoom

Il `TapGestureRecognizer` esistente sul `Grid` dell'overlay resta, ma
la condizione per chiudere cambia: chiude solo se `_scaleCorrente` è
già tornato a 1 (con una tolleranza, es. `< 1.01`, per gli arrotondamenti
del pizzico). Se il tocco arriva mentre l'immagine è ancora zoomata,
**non chiude**: riporta invece zoom e posizione a 1× (stessa
animazione di rientro di §3.3). Un secondo tocco, a quel punto, chiude
come già succede oggi.

Questo evita che un tocco per errore mentre si esplora l'immagine
ingrandita butti fuori dall'overlay — il tasto Indietro resta la via
di chiusura immediata indipendente dallo zoom (invariato, §2).

---

## 4. Edge case

- **Pizzico oltre i limiti (sotto 1× o sopra 4×):** clampato durante il
  gesto stesso, non solo al rilascio — altrimenti si vedrebbe
  brevemente uno zoom fuori range prima dello scatto indietro.
- **L'overlay si chiude mentre è zoomato** (es. dal tasto Indietro, o
  perché il ViewModel azzera `ExpandedImageUrl` per il rilievo #1 della
  revisione finale del lotto precedente — cambio schermata per un
  giocatore non host): `_scaleCorrente`/offset vanno resettati a 1/0 in
  quel momento, così la **prossima** apertura riparte pulita anche se
  l'ultima chiusura non è passata dal tocco a 1×.
- **Rotazione dello schermo mentre l'overlay è aperto e zoomato:** fuori
  scope — accettato che lo zoom possa apparire leggermente spostato
  rispetto al nuovo centro schermo; non risulta un caso segnalato
  giocando, e MAUI non offre un hook pulito per questo senza
  complicare parecchio l'implementazione per un caso raro.

---

## 5. Testing

**Nessun test automatico:** stato di gesture in code-behind XAML, nella
stessa categoria del resto dell'overlay (spec precedente, §5) — non
testabile in questa codebase. Verifica manuale sul dispositivo:
pizzica per ingrandire, trascina ai bordi (verifica il rientro
elastico), tocca una volta da zoomato (verifica che NON chiuda e che
torni a 1×), tocca una seconda volta (verifica che chiuda), riapri e
verifica che riparta da 1×.

---

## 6. Fuori scope

Vedi §1. Nessun cambiamento al ViewModel, al protocollo o al server:
è una feature interamente contenuta in `GamePage.xaml`/`.xaml.cs`.
