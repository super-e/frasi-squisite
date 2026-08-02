# Frasi Squisite

Il "cadavere squisito" surrealista come gioco multiplayer per Android.

Ogni giocatore riempie una casella grammaticale — soggetto, aggettivo, verbo —
senza vedere quelle degli altri. Alla fine i pezzi si incastrano e vengono fuori
frasi che nessuno ha scritto e tutti hanno scritto.

Pensato per gente **nella stessa stanza**, ognuno con il proprio telefono.

## Struttura

```
src/
  FrasiSquisite.Shared    contratti, DTO, schemi grammaticali, validazione
  FrasiSquisite.Domain    motore di gioco puro: nessun I/O, nessun async
  FrasiSquisite.Server    hub SignalR, persistenza, AI, cifratura
  FrasiSquisite.App       client MAUI (solo Android)
tests/
  FrasiSquisite.Domain.Tests    il grosso della copertura
  FrasiSquisite.Shared.Tests    contratti e serializzazione
  FrasiSquisite.Server.Tests    integrazione hub e persistenza
```

Le dipendenze sono unidirezionali: `App` e `Domain` vedono solo `Shared`;
`Server` vede `Domain` e `Shared`. `Domain` non conosce SignalR, HTTP o il
database.

## Comandi

Compilare tutto:

```bash
dotnet build FrasiSquisite.slnx
```

Eseguire i test:

```bash
dotnet test tests/FrasiSquisite.Domain.Tests
```

## Documentazione

Il design completo — regole, protocollo, scelte architetturali e loro motivo —
sta in [docs/superpowers/specs/2026-07-29-frasi-squisite-design.md](docs/superpowers/specs/2026-07-29-frasi-squisite-design.md).
La fase di voto ha una sua spec a parte:
[docs/superpowers/specs/2026-07-31-fase-voto-design.md](docs/superpowers/specs/2026-07-31-fase-voto-design.md).

## Stato

Partita giocabile dalla lobby alla classifica, con più dispositivi Android
collegati a un server locale. Ci sono i bot, sei schemi grammaticali fra cui
scegliere, due temi, e un voto finale.

Il voto è **cieco**: durante il reveal non si vede chi ha scritto cosa, e i nomi
compaiono solo con la classifica. Un voto a testa, e votano soltanto gli umani
connessi — i bot non votano, il vincitore lo decidono le persone.

Senza timer, riconnessione a partita iniziata, persistenza né AI: arrivano nelle
fasi successive (vedi spec §13).

## Licenza

[Apache License 2.0](LICENSE).

La licenza copre il codice. I font inclusi nell'app restano sotto le
rispettive SIL Open Font License, elencate in [NOTICE.md](NOTICE.md) insieme
all'attribuzione richiesta dalla clausola 4(d).
