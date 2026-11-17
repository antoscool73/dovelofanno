# Installazione

DoveLoFanno è un server: lo si installa una volta in casa (NAS, mini-PC o il PC sempre acceso) e lo si usa dal browser
all'indirizzo `http://<nome-o-indirizzo>:8765`. L'API (`/api/v1/...`) è documentata in
[api/openapi-v1.json](api/openapi-v1.json).

## NAS o mini-PC con Docker

Immagine pubblica per amd64 e arm64: `ghcr.io/antoscool73/dovelofanno`, con il tag della versione (per esempio
`0.6.0`), `latest` per l'ultima versione rilasciata o `edge` per l'ultima compilazione del ramo `main`.

```yaml
# deploy/compose.yaml
services:
  dovelofanno:
    image: ghcr.io/antoscool73/dovelofanno:latest
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
- Aggiornare: `docker compose pull && docker compose up -d`. Il database si aggiorna da solo all'avvio e prima ne
  viene fatta una copia (`data/dlf.db.vN.bak`).

## Solo un PC Windows (servizio)

1. ffmpeg: `winget install Gyan.FFmpeg`.
2. Pacchetto: l'archivio `DoveLoFanno-<versione>-win-x64.zip` dalla pagina
   [Releases](https://github.com/antoscool73/dovelofanno/releases), estratto in una cartella qualunque: contiene
   `app\` (il programma), gli script di installazione, la licenza e `SHA256SUMS.txt` con le impronte dei file.
3. Da PowerShell come amministratore, nella cartella estratta:

   ```powershell
   .\Install-DoveLoFanno.ps1 -Package .\app -RecordingsDir "D:\Registrazioni"
   ```

   Programma in `C:\Program Files\DoveLoFanno`, dati in `C:\ProgramData\DoveLoFanno`, servizio "DoveLoFanno" con avvio
   automatico e riavvio in caso di errore, porta 8765 aperta solo sulle reti private. Lo stesso comando, lanciato
   dall'archivio di una versione nuova, aggiorna un'installazione esistente senza toccare dati e registrazioni.
4. Disinstallare: `.\Uninstall-DoveLoFanno.ps1` (i dati restano; `-RemoveData` toglie database, backup e log).

Per compilare da sé il pacchetto: `cd web && npm ci && npm run build`, poi
`dotnet publish src/DoveLoFanno.Server -c Release -r win-x64 --self-contained -o app`; gli script sono in `deploy\windows`.

Il servizio tiene sveglio il PC durante le registrazioni (`powercfg /requests` mostra "DoveLoFanno: registrazione in
corso"); non impedisce la sospensione da coperchio o tasto.

**Smart App Control.** Se è attivo (Sicurezza di Windows › Controllo app e browser), Windows blocca il programma perché
non è firmato. Finché i rilasci non sono firmati (vedi [CODE_SIGNING.md](../CODE_SIGNING.md)), su quel PC si usa
Docker su un'altra macchina. Senza Smart App Control, al primo avvio può comparire l'avviso di SmartScreen.

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
