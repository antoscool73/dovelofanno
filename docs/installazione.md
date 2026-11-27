# Installazione

DoveLoFanno è un server: lo si installa una volta in casa (NAS, mini-PC o il PC sempre acceso) e lo si usa dal browser
all'indirizzo `http://<nome-o-indirizzo>:8765`. L'API (`/api/v1/...`) è documentata in
[api/openapi-v1.json](api/openapi-v1.json).

## NAS o mini-PC con Docker

L'immagine (amd64 e arm64) si scarica solo da [dovelofanno.it](https://dovelofanno.it/beta/): il file
`dovelofanno-<versione>-docker-<arch>.tar.gz`. Prima di caricarlo si controlla l'impronta con `SHA256SUMS.txt`, allegato
al rilascio su GitHub e scaricabile accanto ai file (`sha256sum -c SHA256SUMS.txt --ignore-missing`). Poi si carica con
`docker load -i dovelofanno-<versione>-docker-<arch>.tar.gz` (o da Container Manager / Container Station: Immagine ›
Importa) e nel compose si usa `image: dovelofanno:<versione>`. Le immagini non sono su un registro (niente
`docker pull`).

```yaml
# deploy/compose.yaml
services:
  dovelofanno:
    image: dovelofanno:0.4.0        # la versione caricata con docker load
    restart: unless-stopped
    ports: ["8765:8765"]
    volumes:
      - ./data:/data                # database, log, backup
      - ./registrazioni:/recordings # registrazioni
    environment:
      TZ: Europe/Rome
```

- Synology: Container Manager › Progetto › Crea, incollando il file.
- Il container gira come utente `app` (uid 1654): le due cartelle devono essere scrivibili da lui, oppure si aggiunge
  `user: "<uid>:<gid>"` con l'utente del NAS che possiede le cartelle.
- Aggiornare: scaricare e caricare con `docker load -i` il file della versione nuova, cambiare il numero nella riga
  `image` e lanciare `docker compose up -d`. Il database si aggiorna da solo all'avvio e prima ne
  viene fatta una copia (`data/dlf.db.vN.bak`).

## Solo un PC Windows (servizio)

1. Scaricare `DoveLoFanno-<versione>-setup.exe` e aprirlo (chiede i permessi di amministratore).
2. Scegliere dove salvare le registrazioni (proposta: `C:\Users\Public\Videos\DoveLoFanno`).
3. L'installatore fa il resto:
   - runtime .NET 10 e runtime ASP.NET Core 10 di Microsoft, se mancano: li scarica da `aka.ms/dotnet`, controlla
     che siano firmati da Microsoft e li installa (il programma non li include: le correzioni di sicurezza arrivano da
     Microsoft). Servono tutti e due: l'installatore di ASP.NET Core non porta il runtime .NET di base;
   - ffmpeg, se manca: `winget install Gyan.FFmpeg` per tutto il PC (lo deve vedere anche il servizio);
   - programma in `C:\Program Files\DoveLoFanno\server`, dati in `C:\ProgramData\DoveLoFanno`, servizio "DoveLoFanno"
     con avvio automatico e riavvio in caso di errore, porta 8765 aperta solo sulle reti private.
   Registro: `C:\ProgramData\DoveLoFanno\logs\installazione.log`.
4. Aggiornare: lanciare l'installatore della versione nuova (dati e registrazioni restano).
5. Disinstallare: Impostazioni › App › DoveLoFanno. Chiede se togliere anche database, backup e log; le registrazioni
   restano sempre.

Senza finestre (per chi installa su più PC): `DoveLoFanno-<versione>-setup.exe /VERYSILENT /SUPPRESSMSGBOXES
/RECORDINGS="D:\Registrazioni"`.

Installazione a mano, senza installatore: `dotnet publish src/DoveLoFanno.Server -c Release -r win-x64
--self-contained false -o app` (dopo `cd web && npm ci && npm run build`), poi da PowerShell come amministratore
`deploy\windows\Install-DoveLoFanno.ps1 -Package .\app -RecordingsDir "D:\Registrazioni"` (servono i runtime .NET e
ASP.NET Core 10 e ffmpeg); `Uninstall-DoveLoFanno.ps1` per togliere. L'installatore si compila con
`tools\ci\build-installer.ps1` (Inno Setup 6).

Il servizio tiene sveglio il PC durante le registrazioni (`powercfg /requests` mostra "DoveLoFanno: registrazione in
corso"); non impedisce la sospensione da coperchio o tasto.

**Smart App Control.** Se è attivo (Sicurezza di Windows › Controllo app e browser), Windows blocca il programma perché
non è firmato. Finché i rilasci non sono firmati (vedi [CODE_SIGNING.md](../CODE_SIGNING.md)), su quel PC si usa
Docker su un'altra macchina. Senza Smart App Control, al primo avvio può comparire l'avviso di SmartScreen.

## Password del server

- Senza password il server risponde solo alla rete di casa (indirizzi privati: 192.168.x.x, 10.x.x.x, 172.16-31.x.x,
  IPv6 locali) e rifiuta le richieste che arrivano da un proxy o da un tunnel.
- Con la password (Impostazioni › Accesso, almeno 8 caratteri) ogni dispositivo la inserisce una volta e resta dentro
  finché non esce, non cambia la password o non lo usa per 180 giorni. Serve per raggiungere il server da fuori casa
  (VPN, proxy inverso, tunnel). Cambiandola, tutti gli altri dispositivi devono rifare l'accesso.
- Password dimenticata: creare un file vuoto `reset-password` nella cartella dati (Docker: `data/reset-password`;
  Windows: `C:\ProgramData\DoveLoFanno\reset-password`) e riavviare il server (Docker: `docker compose restart`;
  Windows: `Restart-Service DoveLoFanno` da PowerShell come amministratore). All'avvio la password viene tolta e il
  file cancellato; poi se ne imposta una nuova dalla rete di casa.

## Backup

- Ogni notte alle 3 il server copia il database in `data/backup/dlf-AAAAMMGG-HHMMSS.db` e tiene le ultime 7 copie;
  `POST /api/v1/backups` ne fa una subito.
- Le registrazioni non sono nel backup: stanno nella loro cartella.
- Ripristino: fermare il server, mettere la copia al posto di `data/dlf.db` (togliendo `dlf.db-wal` e `dlf.db-shm`),
  riavviare.

## Cartella dati

| Percorso | Contenuto |
| --- | --- |
| `dlf.db` | database (sorgenti, canali, flussi, registrazioni, ...) |
| `backup/` | copie notturne del database |
| `logs/` | log delle registrazioni e di ffmpeg |
| `registrazioni/` | registrazioni, se non si indica un'altra cartella |

DoveLoFanno non legge né scrive mai la cartella di RegistraTV (`%LOCALAPPDATA%\RegistraTV`).
