# Migliora la rifinitura — Design

**Data:** 2026-08-12
**Stato:** approvato in brainstorming, pronto per la pianificazione
**Riferimenti:** la fase di rifinitura esiste già (`GameEngine.Refining.cs`, `RefinementRunner`, `RefinementGuard`) — questo documento la corregge e la migliora, non la introduce.

---

## 1. Obiettivo e confini

**Il problema, visto giocando il 12 agosto 2026:** frasi rivelate con
connettivi mancanti ("insieme alla ex moglie Una montagna" invece di
"insieme alla ex moglie, su una montagna") e maiuscole rimaste a metà
frase. La causa **non è di design**: la rifinitura AI esiste già e fa
esattamente questo mestiere — ma nei log del server compare
sistematicamente

```
Chiamata al fornitore AI fallita: si prosegue senza rifinitura.
System.Threading.Tasks.TaskCanceledException: The operation was canceled.
```

Il timeout di 10 secondi (`AiOptions.TimeoutSeconds`) scade prima che il
modello risponda — una chiamata testuale osservata nello stesso log ha
impiegato 9,6s solo per le intestazioni. Quando scade, l'intera
rifinitura di **tutte** le frasi della partita torna al testo grezzo,
non solo la casella in questione.

**Obiettivo:** tre correzioni mirate, indipendenti, sulla rifinitura
esistente:

1. Un timeout realistico, proporzionale al numero di frasi da rifinire.
2. Una guardia che permetta all'AI di aggiustare la forma delle parole
   (concordanza di genere/numero, coniugazione), non solo di
   aggiungere testo intorno.
3. Il ruolo grammaticale di ogni casella passato al modello, oggi
   assente dal prompt.

**Fuori scope, di proposito:**

- **Nessuna nuova chiamata AI o fase di gioco.** Resta la stessa
  chiamata, allo stesso punto (fra scrittura e reveal).
- **Nessun cambiamento al protocollo client-server.** Tutto il lavoro
  sta fra `GameEngine.Refining.cs` e il livello AI del server.
- **Non risolve la lentezza del modello stesso, la nasconde meno.**
  Un timeout più largo non rende il modello più veloce: rende meno
  probabile che una chiamata lenta ma valida venga scartata inutilmente.

---

## 2. Cosa già esiste, e cosa manca

Verificato leggendo il codice attuale, non assunto:

- **La rifinitura è già batch per l'intera partita**: `RequestRefinement`
  porta *tutte* le frasi in una sola chiamata (`RefinementRunner.
  RifinisciAsync`), non una per frase. Un timeout fisso non tiene conto
  di quante frasi ci sono da rifinire nello stesso giro.
- **La guardia (`RefinementGuard.Accettabile`) impone oggi che la
  casella rifinita contenga alla lettera il testo grezzo** (a meno di
  maiuscole/spazi): è quello che blocca ogni aggiustamento della forma
  della parola (plurale, genere, coniugazione), perché la parola
  flessa non è più una sottostringa esatta dell'originale.
- **Il ruolo grammaticale di ogni casella (`Casella.Ruolo`, es. "Con
  chi?", "Dove?") esiste già nello schema** ed è lo stesso testo
  mostrato al giocatore mentre scrive (`SlotRequestMessage.Ruolo`) —
  ma **non viene passato alla rifinitura**: `RequestRefinement` porta
  solo il template e il testo grezzo delle caselle, mai il ruolo.
- **`RefinementGuard`** è codice puro e testato via
  `RefinementGuardTests`, deliberatamente non fidato del prompt da
  solo ("Un prompt e' una preghiera: la garanzia sta qui"). Il punto 3
  di questo design **si scosta consapevolmente** da quel principio,
  limitatamente alla fedeltà della singola parola — decisione presa
  con l'utente, non un compromesso implicito.

---

## 3. Architettura

### 3.1 Timeout proporzionale al numero di frasi

`AiOptions` guadagna due nuovi campi (sostituendo l'uso di
`TimeoutSeconds` per la rifinitura, unico consumatore attuale di
`IAiTextProvider`):

```csharp
public int TimeoutSeconds { get; set; } = 15; // era 10: già stretto per una sola frase
public int TimeoutSecondiPerFraseAggiuntiva { get; set; } = 3;
public int TimeoutMassimoSecondi { get; set; } = 30;
```

`RefinementRunner.RifinisciAsync` calcola il tempo massimo così:

```csharp
var secondi = Math.Min(
    _opzioni.TimeoutMassimoSecondi,
    _opzioni.TimeoutSeconds + _opzioni.TimeoutSecondiPerFraseAggiuntiva * Math.Max(0, frasi.Count - 1));
```

Una partita a 4 giocatori (4 frasi) arriva a 15 + 3×3 = 24s. Il tetto a
30s evita che una partita numerosa faccia aspettare tutti troppo a
lungo — la schermata di attesa della rifinitura esiste già lato client
e regge un'attesa più lunga, ma non un'attesa senza fine.

### 3.2 Guardia sulla parola rimossa, guardie strutturali invariate

`RefinementGuard.Accettabile` non controlla più che la casella rifinita
contenga alla lettera il testo grezzo. Restano invariate le altre tre
guardie, che proteggono da risposte rotte e non dalla fedeltà delle
parole:

- casella rifinita non vuota;
- non oltre `MaxCaratteri` (200);
- non ripete il letterale del template che la precede già.

Resta invariato anche il controllo strutturale in `RefinementGuard.
Applica`: un numero di caselle diverso da quello atteso scarta l'intera
frase, tornando al testo grezzo — l'AI non può fondere o eliminare
caselle, solo aggiustare il contenuto di ciascuna.

Il prompt di sistema di `RefinementRunner` passa da:

> Non sostituire le parole scelte dai giocatori. Devono comparire
> tutte, invariate, dentro la casella corrispondente.

a:

> Le parole scelte dai giocatori restano le stesse: puoi aggiustarne
> delicatamente la forma — plurale, genere, coniugazione — per farle
> concordare con il resto della frase. Non sostituirle con parole
> diverse, non cambiarne il significato, non aggiungere idee nuove.

### 3.3 Ruolo grammaticale nel prompt

`Effect.RequestRefinement` guadagna un campo:

```csharp
public sealed record RequestRefinement(
    IReadOnlyList<IReadOnlyList<string>> Frasi,
    string Template,
    IReadOnlyList<string> Ruoli) : Effect;
```

`GameEngine.Refining.cs` (`EntraInRifinitura`) lo popola da
`rifinendo.Schema.Caselle.Select(c => c.Ruolo)` — un elenco per
schema, condiviso da tutte le frasi (ogni frase segue lo stesso
schema, quindi lo stesso elenco di ruoli posizionali; non va ripetuto
per frase).

`RefinementRunner.RifinisciAsync` guadagna il parametro `ruoli` e lo
include nel payload JSON mandato al modello:

```csharp
var utente = JsonSerializer.Serialize(new
{
    template,
    ruoli,
    frasi = frasi.Select(f => new { caselle = f }),
});
```

Il prompt di sistema guadagna una riga che spiega il nuovo campo:

> Il campo "ruoli" dice la funzione grammaticale di ogni casella nella
> frase (es. "Con chi?", "Dove?"), nello stesso ordine delle caselle:
> usalo per scegliere la preposizione o l'accordo giusto, non per
> cambiare cosa la casella dice.

`GameHost.AvviaRifinitura` passa `richiesta.Ruoli` a `RifinisciAsync`
insieme agli argomenti già esistenti.

---

## 4. Edge case

- **Uno schema con meno ruoli delle caselle attese, o viceversa**: non
  può succedere — `Schema.Caselle` e `Schema.Template` sono generati
  dallo stesso file dati e validati insieme al caricamento (fuori
  scope di questo lotto verificarlo di nuovo qui).
- **Il timeout scade comunque, anche con il nuovo calcolo**: il
  comportamento di fallback resta identico a oggi — si procede con le
  caselle grezze, nessun errore mostrato ai giocatori, la partita non
  si blocca (requisito invariato, spec originale §8.5).
- **La guardia ora accetta una parola "aggiustata" che in realtà il
  modello ha frainteso** (es. cambia il senso invece della sola forma):
  rischio accettato esplicitamente dall'utente in questa sessione,
  mitigato solo dal prompt — non da codice provabile. Da rivedere se
  in pratica capitassero derive vistose.

---

## 5. Testing

- **`AiOptionsTests`/`RefinementRunnerTests`** (o dove già vivono i
  test del runner): il calcolo del timeout con 1, 4, e un numero di
  frasi che supera il tetto di 30s.
- **`RefinementGuardTests`**: aggiornare i test che oggi verificano il
  rifiuto di una casella rifinita che non contiene il testo grezzo
  (quel comportamento cambia); aggiungere un test che una parola con
  forma diversa (es. "montagna" rifinita in "montagne") **viene
  accettata**, mantenendo i test già presenti sulle altre tre guardie
  (vuoto, 200 caratteri, non ripete il template).
- **Test sul motore** (`GameEngine.Refining.cs`): `RequestRefinement`
  porta il campo `Ruoli` popolato correttamente dallo schema attivo.

---

## 6. Fuori scope

Vedi §1. Nessun impatto su protocollo, client, o altre fasi di gioco.
