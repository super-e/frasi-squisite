---
id: keystore-firma-dedicato
title: "Keystore di firma Android dedicato, fuori dal repository"
category: decision
status: active
created: "2026-08-12T10:52:37"
updated: "2026-08-12T10:54:00"
---

<!-- compiled_truth -->
**Cosa:** le build Release Android sono firmate con un keystore
dedicato (`FRASI_KEYSTORE_PATH`/`ALIAS`/`PASSWORD`, variabili
d'ambiente lette dal `.csproj`), non più col keystore di debug
generato al volo da ogni PC di sviluppo.

**Il problema che ha causato questa decisione:** il progetto viene
compilato da più PC (almeno due, di due persone/profili diversi). Senza
keystore fisso, ogni build Release veniva firmata con un certificato
diverso a seconda della macchina, e Android rifiutava di aggiornare
un'app già installata con un certificato diverso — anche dopo una
disinstallazione apparentemente completa, il sintomo era "conflitto con
un pacchetto esistente" al prossimo tentativo da un PC diverso.

**Perché le password non stanno nel repository:** il file `.csproj`
legge `$(FRASI_KEYSTORE_PATH)` ecc. da variabili d'ambiente; il
keystore vero vive fuori dal repository
(`C:\Users\Enrico\keystores\frasi-squisite.keystore` sul PC principale)
e va copiato manualmente (chiavetta, cartella privata — mai
email/chat/git) su ogni macchina che compila una Release, con le
stesse tre variabili d'ambiente impostate lì (`setx`, persistente).

**Nota tecnica:** la condizione MSBuild non usa
`GetTargetPlatformIdentifier` combinato con `And` fra due funzioni con
virgolette annidate — genera MSB4092 (errore di parsing). Usa un
confronto diretto `'$(TargetFramework)' == 'net10.0-android'`, valido
perché il progetto ha un solo `TargetFrameworks`.

**Raggio d'azione:** tocca solo `FrasiSquisite.App.csproj` e il
processo di build locale, nessun impatto su server o protocollo.


## Timeline

- time: 2026-08-12T10:52:37
  kind: decision
  summary: "Created this page: Keystore di firma Android dedicato, fuori dal repository"
  source: "src/FrasiSquisite.App/FrasiSquisite.App.csproj; commit 3adf76b"
  affects: [keystore-firma-dedicato]

- time: 2026-08-12T10:54:00
  kind: decision
  summary: "catturata durante il lotto overlay illustrazione, 2026-08-12"
  source: ".csproj; sessione 2026-08-09..2026-08-12"
  affects: [keystore-firma-dedicato]
