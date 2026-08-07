# Backlog

Cose viste giocando o emerse dalle revisioni, che non sono state fatte al
momento. Non è un elenco di desideri: ogni voce dice **cosa** e soprattutto
**perché**, così chi la prende in mano non deve ricostruire il contesto.

Ordine indicativo di priorità, non vincolante.

---

## 1. Ingrandire l'illustrazione toccandola

**Visto giocando, 4 agosto 2026.** L'immagine si genera correttamente, ma nel
riquadro della classifica è piccola.

Serve poterla aprire a schermo intero con un tocco. L'immagine è già servita
da un endpoint HTTP con il suo indirizzo, quindi non serve niente lato server:
è tutto nel client.

---

## 2. Il fallimento intermittente di `GameHubTests`

**Dieci manifestazioni durante il lotto dell'illustrazione, su almeno cinque
test diversi.** Sempre la stessa firma: un'attesa che scade **solo** quando
gira la suite intera, mai in isolamento, e che sparisce al rilancio. Ha già
colpito anche un test d'integrazione scritto durante quel lotto.

**Non è un difetto di prodotto** e precede il lotto AI.

**Diagnosi della revisione finale** — da qui, non da zero:

`WaitFor`/`WaitForCount` **sono già** attese su condizione, con polling a
20 ms; il tempo fisso è solo il tetto massimo. Il sospetto è fame di thread
nel pool, e nel codice ci sono tre acceleratori concreti:

1. `InitializeAsync` costruisce un `WebApplicationFactory<Program>` **per ogni
   metodo di test** — diciassette host ASP.NET completi per esecuzione della
   classe.
2. Non c'è `xunit.runner.json`, quindi le classi girano in parallelo fino al
   numero di core.
3. `DisposeAsync` chiama `_factory.Dispose()` **sincrono** su un host che è
   `IAsyncDisposable`: blocca un thread del pool sullo spegnimento mentre le
   altre classi corrono.

**Il candidato più economico da provare per primo è il terzo:**
`await _factory.DisposeAsync()`. Poi, in ordine di resa attesa, un
`IClassFixture` condiviso — attenzione: condivide i singleton `IRoomRegistry`
e `ImageStore`, e i test che sostituiscono servizi devono comunque tenersi la
propria fabbrica — e infine un `maxParallelThreads` moderato.

**Perché vale la pena chiuderlo presto**, e non è la suite: una suite
d'integrazione ballerina è il modo in cui il prossimo difetto vero viene
archiviato come "il solito flake".

---

## 3. Bot più aderenti allo schema

Il secondo dei tre pezzi del lotto AI, l'unico non ancora fatto. Descritto in
[spec AI §6](specs/2026-08-03-ai-design.md).

È il pezzo più isolato: **il motore non cambia di una riga.** Una seconda
implementazione di `IWordPool` che serve da una cache, con ricaduta su
`StaticWordPool` quando la voce non c'è, e un servizio in sottofondo che
riempie la cache all'avvio — una chiamata per schema, sei in tutto.

**Il punto da non dimenticare:** le parole generate vanno passate per
`SlotTextValidator` prima di entrare in cache. Quelle dei bot finiscono nelle
caselle senza che il motore le rivalidi (`FillDisconnected` le scrive
direttamente), quindi una parola di ottanta caratteri sfonderebbe in silenzio
il limite che vale per tutti gli altri.

---

## 4. Rilievi minori lasciati aperti dalle revisioni

Nessuno blocca niente. Elencati perché una decisione presa e non scritta
diventa una svista.

- **Manca il test del verso opposto della promozione host**: un host
  retrocesso non deve più vedere il pulsante "Illustra". Il codice è
  simmetrico e corretto, ma nessun test bloccherebbe chi "ottimizzasse"
  `OnIsHostChanged` con un `if (value)`.
- **`ImageStore.Salva` torna `string?`** e il nullable non è imposto come
  errore. Il rimedio vero è `WarningsAsErrors` su tutto il repository: è una
  decisione di repository, non di un singolo ramo.
- **Un esito di illustrazione in volo può scavalcare una partita nuova**: la
  finestra è limitata ai 90 s del timeout, e rigiocare una partita intera in
  quel tempo non è realistico. **Da rivedere se il reveal diventasse
  automatico.**
- **`IsWaiting` non viene spento da un `ErrorMessage`**, che non porta
  l'indice della frase. Oggi irraggiungibile. Lasciato com'è di proposito:
  spegnere l'attesa su un errore qualunque riabiliterebbe il pulsante mentre
  una generazione è ancora in corso, che è peggio.
- **Il percorso relativo dell'immagine** ignora un eventuale prefisso di
  percorso del reverse proxy. Corretto per il Caddy a sottodominio in uso,
  sbagliato se il server finisse sotto `https://host/frasi/`.
- **Lo sfratto del deposito immagini è FIFO globale fra stanze**: il traffico
  di una partita può sfrattare l'immagine di un'altra. Servono ~75 MB di
  ricambio per arrivarci.
- **Apostrofi ASCII** (`e'` invece di `è`) nei commenti di alcuni file. Va
  fatta una passata in un commit suo su tutto il repository, non annegata in
  un lotto funzionale.
- **Il costo non ha un tetto**: l'host può illustrare una frase per riga, a
  circa nove centesimi l'una. Se in una serata diventasse un problema, il
  posto dove metterlo è il motore, come limite per stanza — non il client, che
  non è la fonte della verità.
