// Filtro «Canali» della guida (23/11/2026, richiesta di Antonio: con una playlist di canali locali la guida aveva
// centinaia di righe). Ogni canale cade in un gruppo: nazionale (ha il numero del digitale terrestre), della regione
// della famiglia (Preferenze › Canali nella guida, o dal filtro stesso), di un'altra regione, altro (locale senza regione, FAST, estero) o radio. Il
// filtro sceglie i gruppi da vedere e due condizioni (solo flussi funzionanti, niente televendite e religiosi). La
// scelta a mano di Preferenze › Canali nella guida («sempre» / «mai») vince sempre. Vale solo nella guida.
// Senza regione (23/11/2026, Antonio: «se è nessuna fammi vedere tutto») i gruppi non contano: passano tutti i canali,
// restano solo le due condizioni.
import type { Channel } from './api/client';

export type Scope = 'nazionali' | 'regione' | 'altreRegioni' | 'altri' | 'radio';
export type ChannelFilter = { scopes: Scope[]; onlyWorking: boolean; noShop: boolean };

export const SCOPES: { id: Scope; label: string; hint: string }[] = [
  { id: 'nazionali', label: 'Nazionali', hint: 'con il numero del digitale terrestre' },
  { id: 'regione', label: 'Della mia regione', hint: 'riconosciute dal nome (città, provincia, regione)' },
  { id: 'altreRegioni', label: 'Di altre regioni', hint: 'locali di cui si capisce la regione' },
  { id: 'altri', label: 'Altri canali', hint: 'locali senza regione nel nome, canali online, esteri' },
  { id: 'radio', label: 'Radio', hint: 'radio con il video' },
];

export const DEFAULT_FILTER: ChannelFilter = { scopes: ['nazionali', 'regione'], onlyWorking: true, noShop: true };

export const KIND_LABEL: Record<string, string> = {
  radio: 'Radio', news: 'Notizie', sports: 'Sport', kids: 'Bambini', music: 'Musica', religious: 'Religioso',
  shop: 'Televendite', movies: 'Film e serie', culture: 'Cultura', entertainment: 'Intrattenimento', general: 'Generalista',
};

export function scopeOf(c: Channel, region: string): Scope {
  if (c.kind === 'radio') return 'radio';
  if (c.number != null) return 'nazionali';
  if (c.region) return region && c.region === region ? 'regione' : 'altreRegioni';
  return 'altri';
}

/** Il canale passa il filtro (senza la scelta a mano). */
export function passes(c: Channel, f: ChannelFilter, region: string): boolean {
  if (f.onlyWorking && c.streamsOk === 0) return false;
  if (f.noShop && (c.kind === 'shop' || c.kind === 'religious' || c.closed)) return false;
  if (!region) return true;
  return f.scopes.includes(scopeOf(c, region));
}

/** Nella guida: la scelta a mano, altrimenti il filtro. */
export function shownInGuide(c: Channel, f: ChannelFilter, region: string): boolean {
  return c.guideShow ?? passes(c, f, region);
}

const KEY = 'dlf.guida.canali';
export function loadFilter(): ChannelFilter {
  try {
    const v = JSON.parse(localStorage.getItem(KEY) ?? 'null') as Partial<ChannelFilter> | null;
    if (v && Array.isArray(v.scopes)) return { scopes: v.scopes.filter((s) => SCOPES.some((x) => x.id === s)), onlyWorking: !!v.onlyWorking, noShop: !!v.noShop };
  } catch { /* predefinito */ }
  return { ...DEFAULT_FILTER, scopes: [...DEFAULT_FILTER.scopes] };
}
export function saveFilter(f: ChannelFilter): void {
  try { localStorage.setItem(KEY, JSON.stringify(f)); } catch { /* solo per questa visita */ }
}
