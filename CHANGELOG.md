# Novità di DoveLoFanno

Le novità di ogni versione della beta, dalla più recente. Con la 1.0 pubblica si riparte da zero.

Da questo file `tools/changelog.py` genera la pagina <https://dovelofanno.it/beta/novita/> e le note di
`site/version.json` (quelle che l'app mostra in Impostazioni › Aggiornamenti e nella campanella).

<!-- La sezione «Prossima versione» non ha numero né data: tools/changelog.py la salta, così version.json non annuncia
     un aggiornamento che non c'è ancora. Al rilascio diventa «## X.Y.Z · gg/mm/aaaa» e si lancia lo script. -->
## Prossima versione

- Registrazioni da LA7 e Mediaset (DASH) di nuovo complete: prima potevano salvarne solo pezzi da mezzo minuto

## 0.4.0 · 25/11/2026

- Password del server, oppure accesso solo dalla rete di casa; ogni dispositivo entra una volta
- Avvertenze da accettare al primo avvio
- «Il top per la serata» in cima a La mia lista: i tre film migliori in TV e in ognuno dei tuoi abbonamenti; sotto, il film di stasera
- Banner e scheda con l'immagine di scena del film; la locandina solo se manca
- Dove Lo Fanno, nella scheda: prima dove lo vedi senza pagare, poi a pagamento
- «I miei avvisi» come pagina nella barra in alto, con locandina, matita e bidone
- «Avvisami» in una finestra a comparsa, uguale in Cerca, nella scheda e in I miei avvisi
- Libreria a sezioni con locandine; «Continua a guardare» con locandina e barra di quanto hai visto
- Avviso «Pronto da guardare» quando la copia per il browser è finita; audio sempre stereo (alcuni 5.1 restavano muti)
- Telegram: negli orari di silenzio i messaggi aspettano e arrivano tutti insieme alla fine; orari in Preferenze › Notifiche
- Telegram: i messaggi su film e programmi arrivano con la locandina e il pulsante «Apri la scheda» (con il telefono sul Wi-Fi di casa)
- Avvisi: un messaggio solo per film, con tutti i posti dove vederlo, invece di uno per piattaforma
- Icona della casa per La mia lista; un clic sul logo apre «Informazioni»
- Intro con un jingle nuovo

## 0.3.0 · 23/11/2026

- Guida TV che scorre di continuo su tutti i giorni, con lo zoom (anche Ctrl+rotella o due dita) e il trascinamento col mouse
- In alto, ferme, le opzioni e l'anteprima del canale; sotto scorre solo la griglia
- Loghi dei canali e filtro «Canali»: nazionali, della tua regione, di altre regioni, altri, radio
- Canali senza guida in fondo alla griglia, con «Registra a orario libero»
- Preferenze › Canali nella guida: per ogni canale Sempre, Mai o Automatico
- Col mouse fermo su un programma della guida se ne legge il titolo completo
- Registra chiede conferma con orari, margini e formato; per le serie, solo questa puntata o tutta la serie
- Registrazioni in programma modificabili con la matita finché non partono
- «Visto» su ogni titolo e «Nascondi visti» in La mia lista
- Un clic su una locandina apre sempre la scheda e «Indietro» torna al punto di prima
- Intro col jingle all'ingresso (si spegne in Preferenze › Notifiche)
- Converti non propone più da MP4 a MP4; in Cerca la ricerca parte anche con Invio
- Immagini Docker che si importano anche su QNAP

## 0.2.2 · 22/11/2026

- Installatore per Windows: se sul PC c'era già un runtime .NET 10 più vecchio, il servizio non partiva; ora lo aggiorna
- Se l'installazione non riesce, la causa è scritta nel registro dell'installazione

## 0.2.1 · 22/11/2026

- Installatore per Windows: su un PC senza .NET il servizio non partiva; ora installa da Microsoft anche il runtime .NET di base

## 0.2.0 · 20/11/2026

- Registra la serie: tutte le puntate, saltando le repliche
- Converti: registrazioni e file di casa in MP4 o MP3
- La mia lista con «Da vedere stasera» e «I miei titoli»
- Canali a pagamento riconosciuti: «Su Netflix» al posto di un canale che non vedi
- Filtro Disponibile più semplice: «In TV gratis» e «In TV a pagamento»
- Stagione ed episodio sempre in vista
- Barra in alto con la lente e l'ingranaggio delle Impostazioni

## 0.1.1 · 20/11/2026

- Guida di nuovo visibile con due playlist: i canali uguali diventano flussi di riserva
- Bidone per togliere una playlist
- Conferma prima di cambiare il tipo di una cartella della libreria

## 0.1.0 · 19/11/2026

- Prima beta: guida TV, La mia serata, Cerca, liste, avvisi, registrazioni e libreria di casa
