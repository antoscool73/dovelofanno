// Avvertenze da accettare al primo avvio (24/11/2026, «Backlog verso il pubblico»). Lo stesso testo è nell'installatore
// Windows: deploy/windows/installer/avvertenze.txt. Cambiando il testo si alza TERMS_VERSION e si aggiorna anche quel file:
// ogni server le richiede di nuovo una volta.
export const TERMS_VERSION = 2;

export const TERMS: { title: string; text: string }[] = [
  {
    title: 'Software libero, senza garanzie',
    text: 'DoveLoFanno è software libero (GPL-3.0 o successive), fornito così com\'è: nessuna garanzia che registri sempre tutto o che funzioni senza errori.',
  },
  {
    title: 'Nessun contenuto incluso',
    text: 'DoveLoFanno non fornisce canali, playlist, guide TV né film: usa solo quello che aggiungi tu. Aggiungi soltanto flussi che hai il diritto di vedere.',
  },
  {
    title: 'Registrazioni per uso privato',
    text: 'Le registrazioni servono a te e alla tua famiglia (copia privata). Non si vendono, non si pubblicano e non si condividono fuori casa.',
  },
  {
    title: 'Dati di altri servizi',
    text: 'Voti, locandine e trame arrivano da TMDB con la tua chiave personale, secondo le condizioni di TMDB; i voti IMDb, se attivi, vengono dal file pubblico di IMDb per uso personale e non commerciale. DoveLoFanno non è affiliato a TMDB, IMDb, JustWatch né alle emittenti.',
  },
  {
    title: 'Il server è tuo',
    text: 'Sei tu a gestirlo: tienilo aggiornato e, se lo raggiungi da fuori casa, proteggilo con una password (Impostazioni › Accesso).',
  },
  {
    title: 'Collegamenti a internet',
    text: 'Nessuna telemetria. Il server si collega solo a quello che scegli tu: playlist e guide TV, TMDB con la tua chiave, il file dei voti IMDb se lo attivi, Telegram se lo colleghi. In più, una volta al giorno, legge da dovelofanno.it qual è l\'ultima versione, senza inviare dati: si spegne in Impostazioni › Aggiornamenti.',
  },
];
