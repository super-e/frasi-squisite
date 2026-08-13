# Retry di riconnessione sulle azioni + bottone manuale — Design

**Data:** 2026-08-13
**Stato:** approvato in brainstorming, pronto per la pianificazione
**Riferimenti:** [design rientro in partita](2026-08-08-rientro-in-partita-design.md) (periodo di grazia + rientro esplicito, di cui questo lavoro riusa `RejoinRoomAsync`)

---

## 1. Obiettivo e confini

**Il problema, visto giocando:** quando il trasporto è giù nel momento in
cui si preme un'azione (invia casella, vota, ecc.), oggi il comando fallisce
subito con un errore generico. L'unico modo per tornare a giocare è che il
trasporto si riconnetta da solo (`WithAutomaticReconnect`, che smette di
riprovare dopo lo schedule di default e poi non fa più nulla) o che l'utente
metta l'app in background/foreground, riattivando `TryRejoinAsync` tramite
`Window.Resumed`. Non c'è modo esplicito, immediato, di dire "riprova ora".

**Obiettivo:**
1. Ogni comando verso il server, se fallisce per un guasto di trasporto,
   tenta una riconnessione + rientro in stanza e ripete il comando una
   volta sola, prima di mostrare l'errore.
2. Un bottone "Riconnetti", visibile quando il banner di connessione è
   attivo, per un tentativo esplicito immediato senza dover agire nel gioco.

**Bug collegato, trovato in fase di analisi (dentro lo scope, non
un'aggiunta):** `ConnectionBanner` ("un bot sta giocando al tuo posto") viene
impostato quando il trasporto cade ma non viene **mai svuotato**, nemmeno
dopo un rientro riuscito — resta visibile per sempre una volta comparso una
volta. Verrà svuotato con la stessa regola che oggi svuota già `ErrorText`
su ogni messaggio dal server diverso da `ErrorMessage`.

**Fuori scope, di proposito:**

- **Retry multipli con backoff.** Un solo tentativo di riconnessione per
  comando (o per pressione del bottone): se fallisce, l'utente vede
  l'errore e può riprovare lui stesso. Niente attese nascoste.
- **Deduplica esplicita delle azioni non idempotenti.** Le azioni di gioco
  con stato hanno già guardie server-side contro il doppio invio
  (`ALREADY_SUBMITTED`, `ALREADY_VOTED`, guardie di fase) — verificato
  leggendo `GameEngine.Writing.cs` e affini. L'unica eccezione è `AddBot`
  (nessuna guardia, un retry sfortunato può aggiungere un bot in più): non
  mitigato, perché rimediabile con un tap su `RemoveBotAsync`, già
  esistente.
- **Toccare la retry policy del trasporto SignalR** (`WithAutomaticReconnect`
  di default). Complementare a questo lavoro ma non necessario per
  soddisfare l'obiettivo: il caso che ha motivato questa richiesta (una VPN
  che blocca la rete) non si risolve con nessuna policy di retry del
  trasporto, serve un'azione esplicita — che è esattamente il bottone.
- **Cambi al protocollo o al server.** Tutto il lavoro è nel client
  (`GameSessionViewModel`, `GamePage.xaml`).

---

## 2. Cosa già esiste, e cosa manca

Verificato leggendo il codice attuale:

- **`EnsureConnectedAsync()`** (privato, `GameSessionViewModel`) ricrea il
  trasporto se `IsConnected` è falso. Oggi chiamato solo da `CreateRoomAsync`,
  `JoinRoomAsync` e `TryRejoinAsync` — non dagli altri comandi di gioco
  (`SubmitSlotAsync`, `CastVoteAsync`, `AdvanceRevealAsync`, `AddBotAsync`,
  ecc.), che vanno dritti a `_connection.XxxAsync(...)`.
- **`EseguiComandoAsync`** è già il punto unico per cui passa ogni
  `[RelayCommand]`: cattura `HubException` (rifiuto del server, mostrato
  com'è) e ogni altra `Exception` (guasto di trasporto, oggi mostrato come
  "Non riesco a raggiungere il server.", nessun retry).
- **`TryRejoinAsync`** fa già "riconnetti trasporto + `RejoinRoomAsync`",
  ma inghiotte silenziosamente ogni eccezione (pensato per i trigger in
  background, dove un fallimento non deve mostrare errore) — non riusabile
  as-is per un comando esplicito, che deve invece mostrare l'errore se fallisce.
- **`ConnectionBanner`** impostato in un solo punto
  (`OnConnectionInterrupted`), mai svuotato in nessun altro punto del file.

---

## 3. Architettura

Tutto in `GameSessionViewModel` (client), nessun cambiamento a
`IGameConnection`, `SignalRGameConnection` o al server.

### 3.1 Nuovo helper: `ReconnectTransportAndRoomAsync()`

```
private async Task ReconnectTransportAndRoomAsync()
{
    await EnsureConnectedAsync();
    if (RoomCode.Length > 0)
    {
        await _connection.RejoinRoomAsync(_playerId, RoomCode);
    }
}
```

A differenza di `TryRejoinAsync`, non cattura nulla: le eccezioni
propagano, così chi lo chiama (§3.2, §3.3) decide come mostrarle.
`TryRejoinAsync` resta invariato e continua a usare la propria logica (usa
`RoomCode` o, se vuoto, `_roomSession.RoomCode` — il caso dell'avvio a
freddo, che questo helper non deve coprire).

### 3.2 `EseguiComandoAsync` esteso

```
private async Task EseguiComandoAsync(Func<Task> azione)
{
    try
    {
        await azione();
    }
    catch (HubException ex)
    {
        ErrorText = ex.Message;
    }
    catch (Exception)
    {
        try
        {
            await ReconnectTransportAndRoomAsync();
            await azione();
        }
        catch (HubException ex)
        {
            ErrorText = ex.Message;
        }
        catch (Exception)
        {
            ErrorText = "Non riesco a raggiungere il server.";
        }
    }
}
```

Un solo blocco, un solo posto: si applica a ogni comando esistente e
futuro senza codice ripetuto per ciascuno.

**Pulizia collegata:** le chiamate esplicite a `EnsureConnectedAsync()`
dentro `CreateRoomAsync` e `JoinRoomAsync` diventano ridondanti (un
fallimento lì è comunque coperto dal retry generale) e vengono rimosse.

### 3.3 Bottone "Riconnetti"

Nuovo comando:

```
[RelayCommand]
private Task ReconnectAsync() => EseguiComandoAsync(ReconnectTransportAndRoomAsync);
```

`GamePage.xaml`, subito sotto il banner esistente:

```xml
<Label Text="{Binding ConnectionBanner}" TextColor="OrangeRed"
       IsVisible="{Binding ConnectionBanner, Converter={StaticResource NotEmpty}}" />
<Button Text="Riconnetti" Style="{StaticResource SecondaryButton}"
        Command="{Binding ReconnectCommand}"
        IsVisible="{Binding ConnectionBanner, Converter={StaticResource NotEmpty}}" />
```

Un tentativo per pressione, nessun retry-del-retry interno. Il comando
generato da `[RelayCommand]` disabilita già il bottone mentre è in corso
(comportamento di default di CommunityToolkit.Mvvm, nessuna esecuzione
concorrente) — niente doppie pressioni accidentali.

### 3.4 Fix `ConnectionBanner` mai svuotato

In `OnMessage`, dove oggi si svuota `ErrorText` su ogni messaggio diverso
da `ErrorMessage`:

```
if (message is not ErrorMessage)
{
    ErrorText = string.Empty;
    ConnectionBanner = string.Empty;
}
```

Qualunque messaggio dal server (incluso lo `RoomStateMessage` che arriva
dopo un `RejoinRoomAsync` riuscito) è prova che il giro di andata e ritorno
funziona di nuovo.

---

## 4. Gestione errori

Flusso di un comando che fallisce per trasporto (es. tocco "Invia" a
connessione giù):

1. `azione()` lancia (non `HubException`).
2. `EseguiComandoAsync` cattura, chiama `ReconnectTransportAndRoomAsync()`.
3. Se riconnessione+rientro riescono → ripete `azione()` una volta. Se va a
   buon fine, il prossimo messaggio dal server svuota banner ed errore
   (§3.4).
4. Se il secondo tentativo fallisce (di nuovo trasporto, o stavolta
   `HubException`) → stesso trattamento di oggi.
5. Se la riconnessione stessa fallisce (passo 2) → propaga, stesso esito
   del punto 4.

Bottone "Riconnetti": stesso helper, un solo tentativo per pressione — se
fallisce, l'utente vede l'errore e può premere di nuovo.

---

## 5. Testing

`FakeGameConnection` (`FrasiSquisite.App.Tests`) è già pronta per questo
scenario: `NextFailure` fa fallire una sola chiamata e si azzera da sola —
esattamente il pattern "primo tentativo fallisce, il retry riesce". Nessuna
modifica al fake necessaria. Nuovi test in `GameSessionViewModelTests.cs`:

- Un comando (es. `SubmitSlotCommand`) con `NextFailure` impostato →
  `Calls` mostra `Connect` e `SubmitSlot` due volte (riconnessione + retry),
  nessun `ErrorText` popolato.
- Riconnessione anch'essa fallita (secondo guasto) → `ErrorText` popolato,
  comportamento identico a oggi.
- `ReconnectCommand` invocato a connessione giù → `Connect` chiamato, più
  `RejoinRoom` se `RoomCode` è popolato.
- `Emit(new RoomStateMessage(...))` dopo che `ConnectionBanner` è stato
  impostato → si svuota (copre il fix del bug §3.4).
