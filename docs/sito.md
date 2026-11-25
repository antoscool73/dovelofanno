# Sito dovelofanno.it

File statici, senza compilazione, pubblicati con Cloudflare Pages (progetto collegato a questo repository, ramo `main`,
comando di build vuoto, cartella di output `site`).

- `site/index.html`: pagina pubblica.
- `site/beta/`: istruzioni per chi prova la beta. Protetta da Cloudflare Access (elenco delle email, gestito da Antonio),
  insieme a `download.dovelofanno.it`. Dal 22/11/2026: `index.html` (scelta dell'installazione, primi passi, playlist,
  chiave TMDB, Telegram, aggiornare, problemi, sorgenti) e una pagina per area (`synology/`, `qnap/`, `linux/`,
  `windows/`) scritta per chi non è pratico, con download, passi e «Cosa vedrai». La barra fissa in alto è ripetuta in
  ogni pagina (niente script: cambia solo `aria-current`); le icone stanno in `site/beta/icone.svg`.
- I download stanno su Cloudflare R2 (`download.dovelofanno.it/<versione>/`), non qui: Pages accetta file fino a 25 MB.
  Li carica `.github/workflows/release.yml` quando ci sono i segreti `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY`,
  `R2_ENDPOINT` (`https://<account-id>.r2.cloudflarestorage.com`) e `R2_BUCKET`.
- A ogni versione nuova: aggiornare numero, nomi dei file, dimensioni e impronte (da `SHA256SUMS.txt` del rilascio) in
  tutte le pagine di `site/beta/` (`grep -rl <versione vecchia> site/beta`). Il file sta in `docs` e non in `site` perché tutto `site` viene pubblicato.
- `version.json`: l'ultima versione pubblicata, letta una volta al giorno da ogni server (Impostazioni › Aggiornamenti).
  Va aggiornato (versione, data, note) nel commit del tag: `release.yml` si ferma se non coincide con il tag. Pages lo
  pubblica appena il commit arriva su `main`, quindi qualche minuto prima che i file siano su R2.
- Nessuna risorsa esterna (caratteri di sistema, niente script): `_headers` lo impone con la Content-Security-Policy.
- Gli indirizzi email stanno tra `<!--email_off-->` e `<!--/email_off-->`: altrimenti l'«Email Address Obfuscation» di
  Cloudflare li sostituisce con «[email protected]» e uno script che la nostra Content-Security-Policy blocca.

## Piano: il sito con la documentazione (disegno del 25/11/2026, non ancora fatto)

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
│  ├─ data/release.json      versione, file, dimensioni, SHA256: lo scrive release.yml
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

### Pagine (`/guida/…`)
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

Fuori da `/guida`: `/`, `/novita/` (da `CHANGELOG.md`), `/sostieni/`, `/contatti/`, `/privacy/`, `/note-legali/`,
`/stampa/` (logo e schermate). `/beta/` resta dietro Cloudflare Access e riusa le pagine di installazione con un
riquadro «download beta». Solo italiano, struttura pronta per `/en/`. Una sola documentazione (l'ultima versione),
con l'etichetta «dalla 0.x» dove serve.

### Moduli (Pages Functions, funzionano anche senza script nella pagina)
1. Contatti / segnala un problema (pubblico): nome facoltativo, email, argomento (domanda, problema, proposta,
   stampa), messaggio, versione, privacy. Mail a info@; contro lo spam campo nascosto e limite di invii.
2. Richiedi la beta (pubblico): email, dove lo installeresti, come ci hai conosciuto. Va in una tabella da approvare,
   poi l'email si aggiunge all'elenco di Access.
3. «Questa pagina ti è stata utile? 👍/👎» in fondo alla guida: solo contatori, niente cookie.

Turnstile solo se arriva spam (serve un'eccezione alla CSP per challenges.cloudflare.com). Donazioni solo con
collegamenti esterni.

### Immagini
- Schermate generate da `tools/screenshots.ts` (server demo, dati finti senza loghi né locandine con diritti),
  chiaro e scuro, PC e telefono, rigenerate a ogni versione.
- Nomi `screens/guida/scheda-opera-1.webp`, testo alternativo in italiano, WebP con `<picture>` per chiaro/scuro.
- Schemi in SVG con i colori del logo.

### Pubblicazione
Cloudflare Pages con cartella principale `site` e compilazione Node; anteprime per ramo con noindex; download su R2.
CI prima della pubblicazione: collegamenti rotti, CSP, `version.json` uguale al tag.

### Da decidere
1. Starlight o Hugo (dopo la prova sulla CSP).
2. Nome della sezione: «Guida» si confonde con la Guida TV; alternative «Manuale» o «Aiuto».
3. Modulo beta: approvazione a mano o invito automatico.
4. Documentazione di installazione pubblica subito o in `/beta` fino alla 1.0.
