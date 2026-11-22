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
