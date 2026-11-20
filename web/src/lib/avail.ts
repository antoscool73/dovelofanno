// Dove si vede un titolo, in quattro stati che le locandine mostrano con piccole icone (AvailBadges):
// in TV (canale in chiaro o di un mio abbonamento), in casa (file o registrazione), in streaming nei miei abbonamenti,
// a pagamento (canale a pagamento che non ho, streaming fuori dai miei abbonamenti, noleggio o acquisto).
// Regola della € (20/11/2026, scelta di Antonio): un passaggio su un canale a pagamento che non ho la dà anche insieme
// alla TV (due passaggi: uno che vedo, uno su Sky); non c'è mai se il titolo lo vedo on demand nei miei abbonamenti o in
// casa; noleggio, acquisto e streaming fuori abbonamento la danno solo se non lo vedo in nessun altro modo.
import type { GuideProgramme } from './api/client';
import { inMySubscriptions, payPlatform, streamingFor } from './pay';

export interface Avail {
  /** Testo del passaggio in TV visibile ("stasera 21:16, Twenty Seven"), vuoto se non passa o non si vede. */
  tv: string;
  home: boolean;
  /** Piattaforme dei miei abbonamenti dove è in streaming. */
  mine: string[];
  /** Dove si vede pagando: canale a pagamento che non ho, streaming fuori dai miei abbonamenti, noleggio, acquisto. */
  pay: string[];
}

export interface AvailSource {
  streaming?: string[] | null;
  paid?: string[] | null;
  inHouse?: boolean | null;
}

const notMine = (platform: string | null | undefined) => `${platform || 'canale a pagamento'} (abbonamento che non hai)`;

function combine(x: AvailSource, mySubs: string[], tv: string, payTv: string[]): Avail {
  const mine = streamingFor(x, mySubs);
  const home = !!x.inHouse;
  // streaming in abbonamento fuori dai miei (le varianti "with Ads" di uno che ho non contano), noleggio, acquisto
  const other: string[] = [];
  for (const s of x.streaming ?? []) {
    const n = s.toLowerCase();
    if (!mine.some((m) => n === m.toLowerCase() || n.startsWith(m.toLowerCase() + ' ')) && !other.includes(s)) other.push(s);
  }
  for (const s of x.paid ?? []) if (!other.includes(s)) other.push(s);
  const pay = mine.length > 0 || home ? [] : tv ? payTv : [...payTv, ...other.filter((s) => !payTv.includes(s))];
  return { tv, home, mine, pay };
}

/**
 * Stato di un titolo per un profilo con gli abbonamenti <paramref>mySubs</paramref>. <paramref>airing</paramref> è il
 * passaggio in TV da considerare (null se non passa) e <paramref>tvText</paramref> come descriverlo; <paramref>paidAiring</paramref>
 * un altro passaggio, su un canale a pagamento che non ho.
 */
export function availOf(x: AvailSource, mySubs: string[], airing?: GuideProgramme | null, tvText = '', paidAiring?: GuideProgramme | null): Avail {
  let tv = '';
  const payTv: string[] = [];
  for (const a of [airing, paidAiring]) {
    if (!a) continue;
    if (inMySubscriptions(a, mySubs)) {
      if (a === airing) tv = tvText || 'in TV';
    } else {
      const s = notMine(payPlatform(a));
      if (!payTv.includes(s)) payTv.push(s);
    }
  }
  return combine(x, mySubs, tv, payTv);
}

/** Un risultato della ricerca: il server dà già il prossimo passaggio che vedo (nextAiring) e quello a pagamento. */
export interface HitAirings extends AvailSource {
  nextAiring?: string | null;
  paidAiring?: string | null;
  paidChannel?: string;
  paidPlatform?: string;
}

/** Come availOf per un risultato della ricerca; <paramref>tvText</paramref> e <paramref>paidText</paramref> descrivono i due passaggi. */
export function availOfHit(h: HitAirings, mySubs: string[], tvText: string, paidText = ''): Avail {
  const payTv = h.paidAiring ? [`${[h.paidChannel, paidText].filter(Boolean).join(' ')} (${h.paidPlatform || 'canale a pagamento'}, abbonamento che non hai)`] : [];
  return combine(h, mySubs, h.nextAiring ? tvText || 'in TV' : '', payTv);
}

export const hasAny = (a: Avail) => !!a.tv || a.home || a.mine.length > 0 || a.pay.length > 0;
