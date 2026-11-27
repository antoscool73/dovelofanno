# Sito dovelofanno.it

Progetto Astro con Starlight in `site/` (dal 27/11/2026; prima erano file HTML scritti a mano), pubblicato con
Cloudflare Pages: progetto collegato a questo repository, ramo `main`, **cartella principale `site`, comando di build
`npm ci && npm run build`, cartella di output `dist`**, Node 24 (variabile `NODE_VERSION=24` nelle impostazioni di
Pages). In locale: `cd site`, `npm ci`, `npm run build` (o `npm run dev` per le modifiche al volo).

- `src/pages/index.astro`: la home. `src/pages/404.astro`: pagina non trovata per tutto il sito.
- `src/pages/beta/`: istruzioni per chi prova la beta. Protetta da Cloudflare Access (elenco delle email, gestito da
  Antonio), insieme a `download.dovelofanno.it`. `index.astro` (scelta dell'installazione, primi passi, aggiornare,
  problemi, sorgenti), una pagina per area (`synology`, `qnap`, `linux`, `windows`) scritta per chi non è pratico, con
  download, passi e «Cosa vedrai», e `novita.astro`, che legge `CHANGELOG.md` mentre il sito si compila. Barra in alto
  e piede una volta sola in `src/layouts/Beta.astro`; le icone stanno in `public/beta/icone.svg`.
- `src/content/docs/manuale/`: il manuale pubblico (`/manuale/`), pagine MDX con il menu laterale di Starlight
  (gruppi in `astro.config.mjs`), ricerca Pagefind e `<CosaVedrai>` (`src/components/`). Playlist, chiave TMDB,
  Telegram e telefono sono passati qui dalla pagina beta, che ora rimanda al manuale (le ancore restano).
- `public/`: copiato così com'è (`_headers`, `version.json`, `robots.txt`, `llms.txt`, `style.css` di home e beta,
  immagini, intro). La sitemap la genera Astro (`sitemap-index.xml`), senza le pagine della beta.
- I download stanno su Cloudflare R2 (`download.dovelofanno.it/<versione>/`), non qui: Pages accetta file fino a 25 MB.
  Li carica `.github/workflows/release.yml` quando ci sono i segreti `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY`,
  `R2_ENDPOINT` (`https://<account-id>.r2.cloudflarestorage.com`) e `R2_BUCKET`.
- A ogni versione nuova, finito `release.yml`: `python tools/site-release.py vX.Y.Z`. Scarica il `release.json` che
  `release.yml` allega al rilascio (nomi, byte, SHA-256) in `site/src/data/release.json`, da cui le pagine della beta
  prendono versione, collegamenti, dimensioni (MB decimali) e impronte. Eccezione: la pagina QNAP resta sui file
  «classici» della 0.2.2 (`prova-qnap/`) finché non è provato che Container Station importa i `.tar.gz` del rilascio.
- `public/version.json`: l'ultima versione pubblicata, letta una volta al giorno da ogni server (Impostazioni ›
  Aggiornamenti). Lo scrive `python tools/changelog.py` da `CHANGELOG.md` nel commit del tag: `release.yml` si ferma se
  non coincide con il tag. Pages lo pubblica appena il commit arriva su `main`, quindi qualche minuto prima che i file
  siano su R2.
- Nessuna risorsa esterna (caratteri di sistema). Content-Security-Policy in `public/_headers`: rigida per home, beta e
  404 (niente in linea). Il manuale ne ha una sua, che `tools/csp-headers.mjs` calcola dopo la compilazione dalle
  pagine vere e scrive nell'intestazione di `/manuale/*`: gli script e i blocchi `<style>` in linea di Starlight (tema,
  menu…) passano solo con la loro impronta SHA-256; gli attributi `style=` (Starlight, blocchi di codice) hanno
  `'unsafe-inline'` solo in `style-src-attr`; `'wasm-unsafe-eval'` serve alla ricerca. Anche `/pagefind/*` ha
  `'wasm-unsafe-eval'`, perché la ricerca gira in un Worker che prende la CSP dalla sua risposta. Niente `security.csp`
  di Astro: improntava solo gli script elaborati da lui e bloccava quelli `is:inline` di Starlight (prova nel browser
  del 27/11/2026). `csp-headers.mjs` fa fallire la compilazione se in home, beta o 404 compare qualcosa in linea.
- `tools/check-links.mjs` (dopo la compilazione): collegamenti interni, ancore, icone degli sprite e email fuori da
  `email_off`. Se fallisce, Pages non pubblica.
- Gli indirizzi email stanno tra `<!--email_off-->` e `<!--/email_off-->` (componente `Email.astro`): altrimenti
  l'«Email Address Obfuscation» di Cloudflare li sostituisce con «[email protected]» e uno script che la nostra
  Content-Security-Policy blocca.
- `compressHTML: false`: la compressione di Astro toglie gli a capo accanto ai tag in riga e attacca le parole
  («scrivi a <code>…» diventava «scrivi a…» senza spazio).

## Piano: il sito con la documentazione (disegno del 25/11/2026)

**Stato al 27/11/2026.** Fatto: impianto Starlight, home e beta portate in Astro, `release.json`, controllo dei
collegamenti e della CSP, `llms.txt`, prime pagine del manuale (introduzione e primi passi). Decisi con Antonio:
Starlight (la prova sulla CSP è sopra), sezione **«Manuale»** (`/manuale/`, non «Guida», che si confonde con la Guida
TV), installazione in `/beta` fino alla 1.0, modulo beta con **approvazione a mano**. Da fare: le altre pagine del
manuale a gruppi, i moduli (Pages Functions), le schermate con Playwright e dati finti (oggi la home mostra locandine
vere), `_redirects` quando un indirizzo cambia, `/novita/` pubblica e le pagine fuori dal manuale.

Modello: la documentazione di Tvheadend (docs.tvheadend.org). Da lì prendiamo l'impianto: installazione per
piattaforma, una pagina per ogni schermata, concetti, problemi, sviluppatori, `llms.txt`. Non prendiamo: servizio
ospitato (loro usano GitBook), schermate vecchie su un altro sito, sezione «WIP» pubblica, tono per tecnici. Le pagine
seguono lo stile di `/beta/synology/`: per chi non è pratico, con «Cosa vedrai».

### Motore
Con 40–60 pagine l'HTML scritto a mano non regge (barra ripetuta, numero di versione da sostituire ovunque).
Proposta: **Astro Starlight** dentro `site/` (pagine in Markdown, menu laterale generato, ricerca Pagefind senza
servizi esterni, Node già nel repository per `web/`; Cloudflare Pages compila con `npm ci && npm run build`).
Da verificare prima: la nostra CSP (`script-src 'self'`) contro lo script interno di Starlight per il tema
chiaro/scuro (impronte nella CSP generate da Astro?). Se non si risolve: **Hugo** con modelli tutti nostri.

### Repository
```
site/
├─ astro.config.mjs          menu laterale, Pagefind, collegamenti in alto
├─ package.json
├─ public/                   copiato così com'è
│  ├─ _headers               CSP, noindex per /beta e *.pages.dev
│  ├─ _redirects             vecchi indirizzi → nuovi
│  ├─ version.json           resta qui: lo leggono i server
│  ├─ robots.txt  llms.txt   (sitemap generata)
│  ├─ favicon.svg  og.png
│  └─ media/intro.mp4 intro.jpg
├─ src/
│  ├─ pages/index.astro      la home attuale
│  ├─ content/docs/          le pagine
│  ├─ components/            Passi, CosaVedrai, Download (dalla versione), Novità
│  ├─ data/release.json      versione, file, dimensioni, SHA256: da tools/site-release.py
│  ├─ assets/
│  │  ├─ screens/<sezione>/<pagina>-<n>.webp
│  │  ├─ diagrams/*.svg
│  │  └─ brand/
│  └─ styles/dlf.css         colori del logo; il rosso solo per «registra»
├─ functions/api/            Pages Functions dei moduli (contatto.ts, beta.ts)
└─ tools/
   ├─ screenshots.ts         Playwright sul server demo con dati finti
   └─ check-links.ts
```
`release.json` sostituisce il «cerca e sostituisci la versione» in `site/beta/`. `docs/` e `deploy/` restano dove sono.

### Pagine (`/manuale/…`)
- Introduzione: cos-e, come-funziona (server + client, schema), requisiti, sorgenti (TV, streaming, casa; niente DRM).
- Installazione: panoramica («Dove lo installi?»), synology, qnap, docker (Linux e altri NAS, amd64/arm64), windows,
  aggiornare, backup-e-ripristino, disinstallare.
- Primi passi: primo-avvio (procedura guidata), playlist, guida-tv, chiave-tmdb, profili, telefono-e-tablet,
  app-windows.
- Usare DoveLoFanno (una pagina per schermata): stasera, guida, cerca, scheda-opera, liste, registrazioni,
  avvisi-e-telegram, impostazioni/* (una per scheda).
- Come funziona: registrazione (margini, spezzoni, unione), voti-dei-film, proposte, dove-lo-vedo.
- Problemi: domande-frequenti, registrazione-incompleta, sospensione-e-standby, playlist-non-si-carica,
  accesso-da-fuori-casa (proxy, HTTPS), log-e-diagnostica.
- Sviluppatori: api (vista dell'OpenAPI), compilare, contribuire, sicurezza, licenze-terze-parti.

Fuori da `/manuale`: `/`, `/novita/` (da `CHANGELOG.md`), `/sostieni/`, `/contatti/`, `/privacy/`, `/note-legali/`,
`/stampa/` (logo e schermate). `/beta/` resta dietro Cloudflare Access e riusa le pagine di installazione con un
riquadro «download beta». Solo italiano, struttura pronta per `/en/`. Una sola documentazione (l'ultima versione),
con l'etichetta «dalla 0.x» dove serve.

### Moduli (Pages Functions, funzionano anche senza script nella pagina)
1. Contatti / segnala un problema (pubblico): nome facoltativo, email, argomento (domanda, problema, proposta,
   stampa), messaggio, versione, privacy. Mail a info@; contro lo spam campo nascosto e limite di invii.
2. Richiedi la beta (pubblico): email, dove lo installeresti, come ci hai conosciuto. Va in una tabella da approvare,
   poi l'email si aggiunge all'elenco di Access.
3. «Questa pagina ti è stata utile? 👍/👎» in fondo al manuale: solo contatori, niente cookie.

Turnstile solo se arriva spam (serve un'eccezione alla CSP per challenges.cloudflare.com). Donazioni solo con
collegamenti esterni.

### Immagini
- Schermate generate da `tools/screenshots.ts` (server demo, dati finti senza loghi né locandine con diritti),
  chiaro e scuro, PC e telefono, rigenerate a ogni versione.
- Nomi `screens/manuale/scheda-opera-1.webp`, testo alternativo in italiano, WebP con `<picture>` per chiaro/scuro.
- Schemi in SVG con i colori del logo.

### Pubblicazione
Cloudflare Pages con cartella principale `site` e compilazione Node; anteprime per ramo con noindex; download su R2.
CI prima della pubblicazione: collegamenti rotti, CSP, `version.json` uguale al tag.

### Decisioni (27/11/2026)
1. Starlight: gli script passano per impronta; per gli attributi `style=` serve `'unsafe-inline'` solo in
   `style-src-attr` e solo nel manuale (provato nel browser: tema, menu e ricerca funzionano senza errori di CSP). Hugo avrebbe tenuto la CSP rigida ma con menu, ricerca e modelli da scrivere.
2. Nome della sezione: «Manuale».
3. Modulo beta: approvazione a mano.
4. Installazione: in `/beta` fino alla 1.0.
