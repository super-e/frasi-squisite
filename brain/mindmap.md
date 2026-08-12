---
slug: mindmap
title: Feature mindmap
role: feature mindmap
updated: "2026-08-12T10:55:15"
---

# Feature mindmap

```mermaid
mindmap
  root((Frasi Squisite))
    Nucleo di gioco
      Stanze e lobby (codice, host, bot)
      Schema grammaticale come dato JSON
      Round paralleli (formula p+r mod N)
      Segretezza di protocollo
    Reveal e voto
      Reveal cieco, una casella alla volta
      Autori solo a frase completa
      Voto: un umano, un voto
      Classifica finale
      Reveal fluido - tessuto connettivo del template
    AI (feature flag: presenza chiave API)
      Rifinitura - pre-fetch pool per riempimento timeout
      Illustrazione della frase vincente
      Bot più aderenti allo schema - non fatto, backlog #3
    Resilienza
      Passaggio host all'abbandono
      Rientro in partita dopo disconnessione
        Periodo di grazia 30s
        Persistenza stanza lato client
        Tentativo di rientro - avvio, resume, riconnessione
      Overlay illustrazione a schermo intero
    Piattaforma
      Client MAUI Android-only
      Server ASP.NET Core + SignalR
      Deploy Docker su LXC Proxmox
      Firma APK con keystore dedicato
    Fuori scope o mai implementato
      Persistenza Postgres/EF Core
      Cifratura campi AES-GCM
      Archivio server-side interrogabile
      Ingresso via QR - solo etichetta
      Suggerimenti AI su richiesta
      iOS
      Esposizione pubblica del server
```

Le pagine di categoria decisione collegate a ciascun ramo raccontano il
perché, non solo il cosa — vedi in particolare [[rientro-in-partita]] e
[[persistenza-mai-implementata]] per i due rami con più storia.
