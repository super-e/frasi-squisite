# Ingrandire l'illustrazione toccandola — Design

**Data:** 2026-08-08
**Stato:** approvato in brainstorming, pronto per la pianificazione
**Riferimenti:** [backlog](../backlog.md) #1 ("Ingrandire l'illustrazione toccandola")

---

## 1. Obiettivo e confini

**Il problema, visto giocando il 4 agosto 2026:** l'immagine generata si vede
correttamente nel riquadro della classifica, ma è piccola (`HeightRequest="240"`)
e non c'è modo di vederla più grande.

**Obiettivo:** toccando l'immagine, questa si apre a schermo intero; toccando
di nuovo (in un punto qualunque), si richiude. Nessuna richiesta al server:
l'indirizzo dell'immagine è già noto al client.

**Fuori scope, di proposito:**

- **Zoom/pan sull'immagine ingrandita.** Ingrandire "a schermo intero" basta;
  zoomare oltre non serve per un'illustrazione generata in un formato fisso.
- **Precaricamento o cache dedicata.** L'immagine è già stata scaricata e
  mostrata in piccolo: il controllo `Image` di MAUI la tiene già in cache,
  mostrarla più grande non richiede un nuovo giro di rete in pratica.
- **Condivisione/salvataggio dell'immagine** dall'overlay. Non richiesto,
  altra funzionalità.

---

## 2. Cosa già esiste, e cosa manca

Verificato leggendo il codice attuale, non assunto:

- **`PhraseResultRowView`** (riga della classifica finale) ha già `ImageUrl`
  (`string?`, osservabile) e `PhraseIndex`. Nasce una sola volta all'arrivo di
  `GameFinishedMessage` e non cambia identità per il resto della partita.
- **`GamePage.xaml`** mostra oggi l'immagine piccola dentro il
  `DataTemplate` della `CollectionView` della classifica:
  `<Image Source="{Binding ImageUrl}" Aspect="AspectFit" HeightRequest="240" .../>`.
  Nessun gesto è collegato: l'immagine non reagisce al tocco.
- **La root della pagina è `ScrollView > VerticalStackLayout`**, non un
  `Grid` — non esiste oggi un livello su cui sovrapporre un overlay a
  schermo intero.
- **I comandi di riga esistenti** (es. `RequestIllustrationCommand`) sono
  definiti sul `GameSessionViewModel` (BindingContext della pagina, non della
  riga) e ricevono la riga o un valore come `CommandParameter`, con binding
  `Source={x:Reference RootPage}` — lo stesso pattern da riusare qui.
- **`PulisciStatoDiPartitaConclusa()`** è il punto in cui `GameSessionViewModel`
  già azzera lo stato legato alla classifica finale (es. `FinalResults`)
  quando si torna alla lobby o si avvia una nuova partita.

---

## 3. Architettura

### 3.1 Stato, lato ViewModel

Una sola proprietà osservabile su `GameSessionViewModel`, non per riga: si
può ingrandire una sola immagine alla volta, quindi non serve stato
per-riga.

```csharp
[ObservableProperty]
private string? _expandedImageUrl;

[RelayCommand]
private void ExpandImage(string url) => ExpandedImageUrl = url;

[RelayCommand]
private void CollapseImage() => ExpandedImageUrl = null;
```

### 3.2 Overlay, lato XAML

La root di `GamePage.xaml` diventa un `Grid` con due livelli sovrapposti
nella stessa cella:

1. Lo `ScrollView` esistente, invariato.
2. Un nuovo overlay: sfondo semitrasparente a piena pagina + `Image
   AspectFit` centrata, bound a `ExpandedImageUrl`. Visibile solo quando la
   proprietà non è vuota (riuso del converter `NotEmpty` già in uso nel
   file per `ErrorText`/`ConnectionBanner`). Un `TapGestureRecognizer` su
   tutto l'overlay chiama `CollapseImageCommand` — tocco ovunque per
   chiudere, come già deciso in una risposta precedente per un'altra
   conferma di questa sessione.

Il piccolo `Image` di riga (oggi senza gesti) guadagna un
`TapGestureRecognizer` che chiama `ExpandImageCommand` passando
`{Binding ImageUrl}` come parametro.

### 3.3 Pulizia dello stato

Se l'overlay è aperto e nel frattempo parte una nuova partita
(`NewGameAsync`/`BackToLobbyAsync`, che passano entrambi da
`PulisciStatoDiPartitaConclusa()`), `ExpandedImageUrl` va azzerato insieme
al resto — altrimenti resterebbe un'immagine "fantasma" sopra la schermata
successiva (lobby o nuova partita).

---

## 4. Edge case

- **L'immagine sparisce mentre l'overlay è aperto**: non può succedere in
  pratica — `ImageUrl` di una riga non torna mai `null` una volta
  valorizzato (nessun evento lo azzera), quindi non serve un binding
  difensivo sull'overlay stesso. L'unico modo per cui l'overlay si svuota è
  l'azzeramento esplicito in §3.3.
- **Tocco sull'immagine mentre `IsWaiting` è vero** (illustrazione in
  volo): impossibile — l'`Image` piccola è visibile solo quando `ImageUrl`
  non è vuoto, e `CanRequest` (quindi `IsWaiting`) diventa irrilevante a
  quel punto: l'immagine c'è già.

---

## 5. Testing

Solo a livello di `GameSessionViewModel`, coerente con come sono testati gli
altri stati di schermata in questo progetto (il rendering XAML dell'overlay
non è testabile in questa codebase):

- `ExpandImage("url")` imposta `ExpandedImageUrl`.
- `CollapseImage()` lo azzera.
- `PulisciStatoDiPartitaConclusa()` (via `NewGameAsync`/`BackToLobbyAsync`)
  azzera anche `ExpandedImageUrl`, non solo `FinalResults`.

---

## 6. Fuori scope

Vedi §1. Nessun cambiamento lato server, protocollo o dominio: la voce di
backlog stessa lo anticipava ("è tutto nel client").
