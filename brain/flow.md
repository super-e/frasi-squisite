---
slug: flow
title: Key flows
role: key flows
updated: "2026-08-12T10:51:20"
---

# Key flows

Una partita completa, dalla creazione della stanza alla classifica
finale con illustrazione opzionale. Ogni giocatore riempie una casella
grammaticale (soggetto, aggettivo, verbo…) **senza vedere le altre**
della stessa frase — la segretezza è una proprietà del protocollo
(`SlotRequestMessage` porta solo ruolo/prompt/esempio, mai testo di
altre caselle), non una scelta di presentazione. Vedi
[[segretezza-di-protocollo]].

```mermaid
sequenceDiagram
    participant P as Giocatore (App/MAUI)
    participant H as GameHub (SignalR)
    participant E as GameEngine (Domain, puro)
    participant Host as GameHost (adapter Server)
    participant AI as Provider AI (opzionale)

    P->>H: CreateRoomRequest / JoinRoomRequest
    H->>E: Handle(GameEvent)
    E-->>H: EngineResult(State, Effects)
    Host->>P: RoomStateMessage (broadcast stanza)

    P->>H: StartGameRequest (solo host)
    H->>E: Handle(StartGame)
    E-->>Host: Effects: SlotRequest per ogni giocatore
    Host->>P: SlotRequestMessage (ruolo, prompt, esempio - MAI il testo altrui)

    loop K round
        P->>H: SubmitSlotRequest(testo)
        H->>E: Handle(SlotSubmitted)
        E-->>Host: RoundProgressMessage a tutti
        Note over E: se un giocatore è disconnesso,<br/>il bot riempie la sua casella (nessun timer di round)
    end

    E-->>Host: fase Reveal: RevealStepMessage, una casella alla volta,<br/>ritmato dal server, autori solo a frase completa
    Host->>P: RevealStepMessage (broadcast, sincronizzato)

    P->>H: CastVoteRequest
    H->>E: Handle(VoteCast)
    E-->>Host: GameFinishedMessage (classifica, voto cieco)
    Host->>P: GameFinishedMessage

    opt Host chiede l'illustrazione
        P->>H: RequestIllustrationRequest
        Host->>AI: genera immagine dalla frase vincente (async, non bloccante)
        AI-->>Host: immagine pronta o fallita
        Host->>P: IllustrationReadyMessage / IllustrationFailedMessage
    end
```

## Rientro dopo disconnessione

Percorso separato, aggiunto dopo il nucleo iniziale (vedi
[[rientro-in-partita]]): alla disconnessione SignalR, `GameHub` non
espelle subito — avvia un **periodo di grazia di 30s** in `GameHost`
(salvo in fase `Lobby`, dove l'espulsione è immediata). Se il client
rientra entro la grazia (`RejoinRoomRequest` con lo stesso `playerId`),
il motore rimanda il messaggio della fase corrente esatta — incluso il
caso "la tua casella era già stata riempita dal bot" — senza che il
giocatore perda nulla. Il client tenta il rientro da tre punti: avvio a
freddo (`OnAppearing`), `Window.Resumed`, e riconnessione di trasporto
(`OnReconnected`), leggendo la stanza salvata da `IRoomSession`.
