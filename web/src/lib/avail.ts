// Dove si vede un titolo, in quattro stati che le locandine mostrano con piccole icone (AvailBadges):
// in TV (canale in chiaro o di un mio abbonamento), in casa (file o registrazione), in streaming nei miei abbonamenti,
// a pagamento (streaming fuori dai miei abbonamenti, noleggio o acquisto, canale a pagamento che non ho).
import type { GuideProgramme } from './api/client';
import { inMySubscriptions, payPlatform, streamingFor } from './pay';

export interface Avail {
  /** Testo del passaggio in TV visibile ("stasera 21:16, Twenty Seven"), vuoto se non passa o non si vede. */
  tv: string;
  home: boolean;
  /** Piattaforme dei miei abbonamenti dove è in streaming. */
  mine: string[];
  /** Dove si vede pagando: streaming fuori dai miei abbonamenti, noleggio, acquisto, canale a pagamento. */
  pay: string[];
}

export interface AvailSource {
  streaming?: string[] | null;
  paid?: string[] | null;
  inHouse?: boolean | null;
}

/**
 * Stato di un titolo per un profilo con gli abbonamenti <paramref>mySubs</paramref>. <paramref>airing</paramref> è il
 * passaggio in TV da considerare (null se non passa) e <paramref>tvText</paramref> come descriverlo.
 */
export function availOf(x: AvailSource, mySubs: string[], airing?: GuideProgramme | null, tvText = ''): Avail {
  const mine = streamingFor(x, mySubs);
  const pay: string[] = [];
  let tv = '';
  if (airing) {
    if (inMySubscriptions(airing, mySubs)) tv = tvText || 'in TV';
    // un canale a pagamento non vende il singolo film: serve il suo abbonamento
    else pay.push(`${payPlatform(airing) ?? 'canale a pagamento'} (abbonamento che non hai)`);
  }
  // streaming in abbonamento fuori dai miei (le varianti "with Ads" di uno che ho non contano)
  for (const s of x.streaming ?? []) {
    const n = s.toLowerCase();
    if (!mine.some((m) => n === m.toLowerCase() || n.startsWith(m.toLowerCase() + ' ')) && !pay.includes(s)) pay.push(s);
  }
  for (const s of x.paid ?? []) if (!pay.includes(s)) pay.push(s);
  // «a pagamento» solo se non c'è un modo di vederlo senza spendere altro: in TV, in casa o nei miei abbonamenti
  const home = !!x.inHouse;
  return { tv, home, mine, pay: tv || home || mine.length > 0 ? [] : pay };
}

export const hasAny = (a: Avail) => !!a.tv || a.home || a.mine.length > 0 || a.pay.length > 0;
