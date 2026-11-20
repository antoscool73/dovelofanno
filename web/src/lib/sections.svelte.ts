// «Apri tutte» e «Chiudi tutte» le sezioni di La mia lista (20/11/2026, richiesta di Antonio). Ogni Section guarda
// questo comando: quando cambia `n` si apre o si chiude e ricorda lo stato come se l'avessi toccata.
class SectionsCommand {
  n = $state(0);
  closed = $state(false);

  all(closed: boolean): void {
    this.closed = closed;
    this.n++;
  }
}

export const sectionsCommand = new SectionsCommand();

/** Sezioni predefinite di La mia lista che si possono nascondere (Preferenze › La mia lista); le chiavi del server. */
export const HOME_OPTIONAL: { key: string; label: string }[] = [
  { key: 'continua', label: 'Continua a guardare' },
  { key: 'registrazioni', label: 'In registrazione e in arrivo' },
  { key: 'stasera-miei', label: 'Stasera in TV · Nei miei abbonamenti' },
  { key: 'stasera-altri', label: 'Stasera in TV · Non nei miei abbonamenti' },
  { key: 'settimana', label: 'Questa settimana' },
];
