# Contribuire a DoveLoFanno

Grazie per l'interesse. DoveLoFanno è un progetto personale, seguito da una sola persona nel tempo libero: le risposte
possono richiedere qualche giorno.

## Prima di tutto

- **Domande e idee** → [Discussions](https://github.com/antoscool73/dovelofanno/discussions).
- **Errori** → una [segnalazione](https://github.com/antoscool73/dovelofanno/issues/new/choose) con il modello «Errore».
- **Problemi di sicurezza** → mai in pubblico: vedi [SECURITY.md](SECURITY.md).
- **Modifiche grandi** (funzioni nuove, cambi all'API o al database) → prima una discussione o una segnalazione, così ci
  si accorda sulla strada prima che tu scriva il codice.

## Cosa non viene accettato

DoveLoFanno **non include canali né sorgenti** e non aiuta a trovarli. Non vengono accettate segnalazioni, discussioni
o modifiche che contengono o chiedono playlist, indirizzi di flussi, liste di canali, chiavi o modi per aggirare DRM e
abbonamenti. Vengono chiuse senza risposta.

## Compilare e provare

Servono .NET SDK 10 e Node.js 24 (e ffmpeg nel `PATH` per alcuni test).

```bash
cd web && npm ci && npm run check && npm run build && cd ..
dotnet build DoveLoFanno.slnx -c Release
dotnet test DoveLoFanno.slnx -c Release
```

- I test dei voti confrontano i risultati con dati TMDB che non si possono ridistribuire: senza quei dati (repository
  privato) vengono saltati. In CI girano solo per le modifiche dal repository principale.
- Il server si avvia con `dotnet run --project src/DoveLoFanno.Server` e risponde su `http://localhost:8765`; per
  l'interfaccia con ricarica automatica `cd web && npm run dev`.
- L'API è in [docs/api/openapi-v1.json](docs/api/openapi-v1.json): se la cambi, rigenera i tipi con `npm run gen:api`.
  Nella v1 solo aggiunte compatibili.

## Come preparare una modifica

1. Un ramo per modifica, partendo da `main`.
2. Test per il comportamento nuovo o corretto (`tests/DoveLoFanno.Core.Tests`).
3. `dotnet test` e `npm run check` verdi; la CI li rifà su Linux e, nelle richieste di modifica, anche su Windows.
4. Testi dell'interfaccia in italiano, semplici e senza gergo tecnico.
5. Se la modifica si vede dall'utente, una riga nella sezione «Prossima versione» di [CHANGELOG.md](CHANGELOG.md),
   scritta per chi usa il programma (non per chi lo sviluppa).
6. Messaggi di commit brevi che dicono cosa cambia (in italiano o in inglese).

Le modifiche al database passano da una migrazione nuova (mai cambiare quelle già rilasciate): il database degli utenti
si aggiorna da solo all'avvio.

## Licenza dei contributi

DoveLoFanno è distribuito con licenza [GPL-3.0-or-later](LICENSE). Inviando una modifica accetti che sia distribuita
con la stessa licenza e dichiari di averne il diritto (codice tuo o compatibile con la GPL, senza parti copiate da
progetti con licenze incompatibili).

---

## English summary

Questions and ideas go to Discussions, bugs to Issues, security reports privately (see [SECURITY.md](SECURITY.md)).
Contributions that include or ask for playlists, stream URLs, channel lists or ways around DRM are not accepted. Build
with .NET SDK 10 and Node.js 24 (commands above), add tests, keep the UI in Italian, add a user-facing line to
CHANGELOG.md when relevant. Contributions are licensed under GPL-3.0-or-later.
