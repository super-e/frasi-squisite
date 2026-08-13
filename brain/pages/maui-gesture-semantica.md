---
id: maui-gesture-semantica
title: "Semantica reale di Pinch/Pan in .NET MAUI (non quella che sembra dal nome)"
category: concept
status: active
created: "2026-08-13T09:02:23"
updated: "2026-08-13T09:02:57"
---

<!-- compiled_truth -->
**Il problema che ha reso necessaria questa pagina:** un pattern
scritto seguendo un ricordo plausibile ma sbagliato del sample
ufficiale Microsoft per il pinch-to-zoom è passato **due revisioni di
task** prima che una revisione di ramo intero, con un modello più
capace, lo scoprisse leggendo la documentazione XML installata
localmente. Due bug critici erano invisibili a un `dotnet build` pulito
e a una lettura superficiale del codice, perché il codice "sembra"
corretto.

**`PinchGestureUpdatedEventArgs.Scale` è relativo all'ULTIMO evento
ricevuto, non cumulativo dall'inizio del gesto.** Un pattern comune
(anche in tutorial pubblici) usa `scala = scalaAllInizioGesto * e.Scale`
ricalcolato a ogni frame — sbagliato: perde l'accumulo di ogni frame
intermedio, e lo zoom raggiungibile si riduce a circa il delta di un
solo frame (~1.05x). Il modo corretto è accumulare ad ogni frame:
`scala = Math.Clamp(scala * e.Scale, min, max)`, senza bisogno di uno
stato "di inizio gesto" per la scala.

**`PanUpdatedEventArgs.TotalX`/`TotalY` sono ZERO a `GestureStatus.
Completed`/`Canceled`.** Sono cumulativi dall'inizio del gesto solo
durante `Running`. Leggerli a fine gesto per calcolare la posizione
finale (pattern altrettanto plausibile) fa scattare indietro
l'elemento alla posizione di partenza ad ogni rilascio. Il valore
corretto a fine gesto è quello già applicato alla view
(`View.TranslationX`/`Y`), non gli argomenti dell'evento.

**Il pivot di scala (`AnchorX`/`AnchorY`) va tenuto fisso** (il default
MAUI, 0.5/0.5, centrato) se si vuole un calcolo dei limiti elastici
prevedibile: farlo seguire dinamicamente il punto pizzicato
(`e.ScaleOrigin`) rompe qualunque formula simmetrica sui limiti di
trascinamento, perché quella formula assume implicitamente un pivot
centrato. Per dare comunque la sensazione di "zoomare sotto le dita"
con un pivot fisso, serve compensare a mano nella traslazione: la
derivazione vera è `T₁ = T₀ + (S₀ − S₁) · offsetLocalePivot`, dove
`offsetLocalePivot = (e.ScaleOrigin.X - 0.5) * Width` va catturato UNA
VOLTA all'inizio del gesto (`GestureStatus.Started`), non ricalcolato
ogni frame.

**Effetto pratico:** vedi il codice reale già corretto in
`src/FrasiSquisite.App/Pages/GamePage.xaml.cs` (`OnPinchUpdated`,
`OnPanUpdated`) e la parte pura/testabile estratta in
`src/FrasiSquisite.App/ViewModels/ZoomPanState.cs` — quest'ultima
esiste apposta perché l'aritmetica di accumulo/limiti sia presa da un
test, non da una revisione umana, la prossima volta che qualcuno la
tocca.


## Timeline

- time: 2026-08-13T09:02:23
  kind: decision
  summary: "Created this page: Semantica reale di Pinch/Pan in .NET MAUI (non quella che sembra dal nome)"
  source: "revisione del lotto pinch-to-zoom, 2026-08-12; verificato contro Microsoft.Maui.Controls.xml e il comportamento Android/iOS"
  affects: [maui-gesture-semantica]

- time: 2026-08-13T09:02:57
  kind: decision
  summary: trovata a costo di tre giri di revisione sul lotto pinch-to-zoom
  source: "revisione finale, 2026-08-12"
  affects: [maui-gesture-semantica]
