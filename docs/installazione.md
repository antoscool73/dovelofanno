# Installazione

DoveLoFanno è un server: lo si installa una volta in casa (NAS, mini-PC o il PC sempre acceso) e lo si usa dal browser
all'indirizzo `http://<nome-o-indirizzo>:8765`. Nella wave 3 c'è solo l'API (`/api/v1/...`, documentata in
[api/openapi-v1.json](api/openapi-v1.json)); l'interfaccia web arriva con la wave 5.

## NAS o mini-PC con Docker

Immagine per amd64 e arm64: `ghcr.io/antoscool73/dovelofanno:edge` (privata finché il repository è privato:
serve `docker login ghcr.io` con un token GitHub che abbia `read:packages`).

```yaml
# deploy/compose.yaml
services:
  dovelofanno:
    image: ghcr.io/antoscool73/dovelofanno:edge
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
2. Pacchetto: `dotnet publish src/DoveLoFanno.Server -c Release -r win-x64 --self-contained -o DoveLoFanno-win-x64`
   (o l'archivio della CI, quando ci saranno i rilasci).
3. Da PowerShell come amministratore:

   ```powershell
   .\deploy\windows\Install-DoveLoFanno.ps1 -Package .\DoveLoFanno-win-x64 -RecordingsDir "D:\Registrazioni"
   ```

   Programma in `C:\Program Files\DoveLoFanno`, dati in `C:\ProgramData\DoveLoFanno`, servizio "DoveLoFanno" con avvio
   automatico e riavvio in caso di errore, porta 8765 aperta solo sulle reti private. Lo stesso comando aggiorna
   un'installazione esistente senza toccare dati e registrazioni.
4. Disinstallare: `.\deploy\windows\Uninstall-DoveLoFanno.ps1` (i dati restano; `-RemoveData` toglie database, backup e log).

Il servizio tiene sveglio il PC durante le registrazioni (`powercfg /requests` mostra "DoveLoFanno: registrazione in
corso"); non impedisce la sospensione da coperchio o tasto.

**Smart App Control.** Se è attivo (Sicurezza di Windows › Controllo app e browser), Windows blocca il programma perché
non è firmato. Finché non c'è una firma del codice, sul PC serve disattivarlo oppure usare Docker su un'altra macchina.

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
