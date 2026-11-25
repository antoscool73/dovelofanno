# DoveLoFanno

**La risposta a _stasera che si guarda?_** Guida TV, registratore e libreria di casa in un solo server domestico
(Docker su NAS o mini-PC, oppure servizio Windows), da usare dal browser di qualunque dispositivo della casa.

> **Stato: pre-release 0.x.** Funziona ed è usato ogni giorno, ma interfaccia e API possono ancora cambiare.
> Le versioni per Windows non sono ancora firmate (vedi [Code signing policy](#code-signing-policy)).

*English summary below.*

## Cosa fa

- **Guida TV** a griglia e a elenco dalle guide XMLTV indicate nelle playlist; ricerca anche per regista e attori,
  generi, voto dei film (TMDB e IMDb, con la propria chiave TMDB).
- **Stasera**: i titoli in onda stasera che piacciono a ogni profilo, e «la mia serata».
- **Registrazione** dei canali delle playlist M3U dell'utente con ffmpeg, senza ricodifica: spezzoni MPEG-TS che
  sopravvivono alle cadute di rete, unione finale in MP4 (o MP3 per la radio), file `.nfo` per Jellyfin e Kodi.
- **Libreria** delle cartelle di casa (anche sul NAS): riconoscimento di film e serie dal nome del file, player nel
  browser con ripresa dal punto, sottotitoli, liste personali e di famiglia, voto da 1 a 5 stelle.
- **Proposte per te**: ogni profilo dice cosa gli piace (generi, voto minimo, attori, registi) e Stasera propone
  i programmi giusti, senza quelli già visti, già in casa o già in programma.
- **Avvisami**: per ogni titolo tre avvisi indipendenti (quando passa in chiaro in TV, anche con registrazione
  automatica; quando arriva nei tuoi abbonamenti; quando si può noleggiare o comprare).
- **Notifiche** nella campanella dell'app, nel browser e, se lo colleghi, su Telegram; orari di silenzio.
- **Anteprima live** muta dei canali delle playlist nella guida e nelle registrazioni in corso.
- **Profili** per la famiglia, con PIN facoltativo, profilo bambino e abbonamenti ereditati dalla famiglia.

DoveLoFanno **non include canali né sorgenti**: usa solo le playlist e le guide che l'utente aggiunge, ed è utile anche
senza IPTV (guida, libreria, liste). Nessun account, nessun servizio in cloud, nessuna raccolta di dati.

## Installazione

Istruzioni complete in [docs/installazione.md](docs/installazione.md).

> Durante la beta l'installatore Windows e le immagini Docker si scaricano solo dalla pagina riservata a chi prova
> la beta. Nella pagina [Releases](https://github.com/antoscool73/dovelofanno/releases) ci sono i sorgenti di ogni
> versione e le impronte SHA-256 di tutti i file.

- **Docker** (NAS Synology, mini-PC, Linux, amd64 e arm64): immagine `dovelofanno`, esempio in
  [deploy/compose.yaml](deploy/compose.yaml).
- **Windows**: installatore `DoveLoFanno-<versione>-setup.exe`: installa il servizio e, se mancano, il runtime .NET
  di Microsoft e ffmpeg.

Poi si apre `http://<indirizzo-del-server>:8765` dal browser.

## Compilare

Servono .NET SDK 10 e Node.js 24.

```bash
cd web && npm ci && npm run build && cd ..
dotnet build DoveLoFanno.slnx -c Release
dotnet test DoveLoFanno.slnx -c Release
```

L'API è descritta in [docs/api/openapi-v1.json](docs/api/openapi-v1.json); la grafica dell'interfaccia in
[docs/prototipo.md](docs/prototipo.md).

## Contribuire

Domande e idee nelle [Discussions](https://github.com/antoscool73/dovelofanno/discussions), errori nelle
[segnalazioni](https://github.com/antoscool73/dovelofanno/issues). Come proporre modifiche in
[CONTRIBUTING.md](CONTRIBUTING.md); i problemi di sicurezza si segnalano in privato, vedi [SECURITY.md](SECURITY.md).

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org) *(dopo l'approvazione della domanda: fino ad allora i rilasci non sono
firmati)*. Regole, ruoli e privacy in [CODE_SIGNING.md](CODE_SIGNING.md).

## Licenza

Copyright (C) 2026 Antonio Scaglione.

DoveLoFanno è software libero: puoi ridistribuirlo e modificarlo secondo i termini della GNU General Public License
pubblicata dalla Free Software Foundation, versione 3 o (a tua scelta) qualunque versione successiva
(GPL-3.0-or-later). È distribuito senza alcuna garanzia. Testo completo in [LICENSE](LICENSE); componenti di terzi
in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

---

## English summary

DoveLoFanno ("where is it on?") is a self-hosted home server for Italian households: a TV guide (XMLTV), a recorder
for the user's own M3U playlists (ffmpeg, stream copy to MPEG-TS segments merged into MP4), a media library for home
folders with a resumable browser player, family profiles, personal recommendations from the guide, "notify me" alerts
(free-to-air, subscriptions, rent/buy, with optional automatic recording) and notifications (in-app, browser, Telegram). It ships no channels or sources and has no cloud
service, accounts or telemetry. It runs as a Docker container (amd64/arm64) or as a Windows service, and is used from
any browser on the home network. The user interface is in Italian.

- Install: [docs/installazione.md](docs/installazione.md) (Docker image for amd64/arm64, Windows installer). During
  the beta, installers and images are only available to beta testers;
  [Releases](https://github.com/antoscool73/dovelofanno/releases) carry the source code and SHA-256 checksums.
- License: GPL-3.0-or-later, see [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
- Code signing policy: [CODE_SIGNING.md](CODE_SIGNING.md).
