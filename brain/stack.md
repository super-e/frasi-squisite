---
slug: stack
title: Tech stack
role: tech-stack choices
updated: "2026-08-12T10:55:15"
---

# Tech stack

| Ambito | Scelta | Perché |
|---|---|---|
| Runtime | .NET 10, quattro progetti (`Shared`/`Domain`/`Server`/`App`) | dipendenze unidirezionali, vedi la pagina radice architecture |
| Client | .NET MAUI, **solo Android** (`net10.0-android`) | iOS esplicitamente fuori scope; MAUI lo permetterebbe ma nessuna scelta lo agevola |
| MVVM | CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`) | idiomatico, DI condivisa con ASP.NET Core (`Microsoft.Extensions.DependencyInjection` su entrambi i lati) |
| Realtime | ASP.NET Core + SignalR (`GameHub`), `HubConnection` con `.WithAutomaticReconnect()` lato client | riconnessione di trasporto automatica; il rientro applicativo (stanza/turno) è costruito sopra, vedi [[rientro-in-partita]] |
| Persistenza stato vivo | **In memoria** (`IRoomRegistry`/`RoomRegistry`, `ConcurrentDictionary`) | nessun database — vedi [[persistenza-mai-implementata]], scostamento dal piano originale |
| AI testo | `IAiTextProvider`, implementazione `OpenAiCompatibleTextProvider` verso endpoint compatibile OpenAI (default `api.ppq.ai`, modello configurato `glm-5.2`) | endpoint/modello/chiave da configurazione, mai hardcoded — cambiare fornitore è una variabile d'ambiente |
| AI immagine | `IAiImageProvider`, implementazione `OpenAiCompatibleImageProvider` (modello configurato `nano-banana-2`, dimensione 1K) | stesso principio, timeout separato e più largo (90s) perché non blocca una partita in corso |
| Degrado AI | `DisabledAiTextProvider`/`DisabledAiImageProvider`, attivati quando `AiOptions.ApiKey` è vuota | fallback come vera implementazione, non un `if` — vedi [[fallback-come-implementazione]] |
| Test | xUnit su tutti e quattro i progetti; `WebApplicationFactory<Program>` per l'integrazione hub | il grosso della copertura sta su `Domain` (motore puro, nessun mock di rete necessario) |
| Deploy | Docker + `docker compose`, su un LXC Proxmox self-hosted (CT dedicato, reverse-proxy Caddy su un altro CT verso `frasisquisite.carraraenri.co`) | APK privata, backend self-hosted; niente Play Store nella v1 |
| Firma APK | Keystore dedicato fuori dal repository, referenziato via variabili d'ambiente (`FRASI_KEYSTORE_*`) nel `.csproj` | senza, ogni PC di build firma con un keystore di debug diverso e il telefono rifiuta gli aggiornamenti — vedi [[keystore-firma-dedicato]] |
| Segreti | Variabili d'ambiente / 1Password, mai in `appsettings.json` versionato | la chiave AI (`Ai__ApiKey`) arriva dal `.env` del container |

## Cosa NON c'è (nonostante la spec originale lo prevedesse)

Vedi [[persistenza-mai-implementata]] per il dettaglio: niente Postgres,
niente EF Core Migrations, niente `IFieldCipher`/AES-GCM, niente
archivio server-side interrogabile, niente ingresso via QR funzionante
(solo un'etichetta segnaposto in UI), niente suggerimenti AI su
richiesta del giocatore (`RequestSuggestion` non esiste nel
protocollo), niente feature flag indipendenti per le singole funzioni
AI (un solo interruttore: la presenza della chiave API).
