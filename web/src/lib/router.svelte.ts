// Router minimo con indirizzi veri (/guida, /registrazioni...): il server rimanda a index.html ogni
// indirizzo che non è un file né /api. Nessuna dipendenza esterna.

import { entryKey, newEntryState, restoreHere, saveHere } from './scrollMemory';

export type Route = 'stasera' | 'cerca' | 'opera' | 'profili' | 'guida' | 'libreria' | 'guarda' | 'registrazioni' | 'avvisi' | 'preferenze' | 'impostazioni' | 'non-trovata';

const paths: Record<string, Route> = {
  '/': 'stasera',
  '/stasera': 'stasera',
  '/profili': 'profili',
  '/guida': 'guida',
  '/libreria': 'libreria',
  '/registrazioni': 'registrazioni',
  '/avvisi': 'avvisi',
  '/preferenze': 'preferenze',
  '/impostazioni': 'impostazioni',
  '/cerca': 'cerca',
};

export function routeOf(pathname: string): Route {
  const p = pathname.length > 1 ? pathname.replace(/\/+$/, '') : pathname;
  if (/^\/opera\/\d+$/.test(p)) return 'opera';
  if (/^\/guarda\/\d+$/.test(p)) return 'guarda';
  if (/^\/libreria\/[a-z-]+(\/\d+)?$/.test(p)) return 'libreria';
  return paths[p] ?? 'non-trovata';
}

/** Indirizzi di prima che portano altrove: I miei avvisi era /preferenze#avvisi fino al 24/11/2026. */
function moved(url: URL): URL {
  if (url.pathname.replace(/\/+$/, '') === '/preferenze' && url.hash === '#avvisi') return new URL('/avvisi', url.origin);
  return url;
}

class Router {
  path = $state(window.location.pathname);
  /** Cresce a ogni navigazione, anche verso lo stesso percorso con un'altra ancora (/cerca#list=3): una schermata che
   *  legge l'ancora solo all'apertura si ricrea con {#key router.nav}. */
  nav = $state(0);
  route = $derived(routeOf(this.path));
  /** Id dell'opera per /opera/{id}. */
  workId = $derived(Number(this.path.match(/^\/opera\/(\d+)/)?.[1] ?? 0));
  /** Id del file per /guarda/{id} (player). */
  itemId = $derived(Number(this.path.match(/^\/guarda\/(\d+)/)?.[1] ?? 0));
  /** Sezione della libreria: /libreria/film, /libreria/liste/12 ... */
  librarySection = $derived(this.path.match(/^\/libreria\/([a-z-]+)/)?.[1] ?? 'liste');
  libraryListId = $derived(Number(this.path.match(/^\/libreria\/liste\/(\d+)/)?.[1] ?? 0));
  /** Quante pagine dell'app ci sono prima di questa nella cronologia (history.state.d): con 0 (pagina aperta da un
   *  link o ricaricata all'inizio) «Indietro» non compare (26/11/2026). */
  depth = $state(0);

  constructor() {
    // la posizione la rimette scrollMemory (anche le file di locandine), non il browser
    if ('scrollRestoration' in history) history.scrollRestoration = 'manual';
    const here = moved(new URL(window.location.href));
    if (here.pathname !== window.location.pathname) {
      history.replaceState(history.state, '', here.pathname + here.hash);
      this.path = here.pathname;
    }
    entryKey();
    this.depth = depthOf(history.state);
    window.addEventListener('popstate', () => { this.path = window.location.pathname; this.depth = depthOf(history.state); this.nav++; restoreHere(); });
  }

  /** Va a un indirizzo dell'app; un'ancora (/opera/12#avvisami) resta nell'indirizzo ma non nel percorso. */
  go(path: string, replace = false): void {
    const url = moved(new URL(path, window.location.origin));
    if (url.pathname === this.path && url.hash === window.location.hash) return;
    if (replace) history.replaceState(history.state, '', url.pathname + url.hash);
    else { saveHere(); this.depth++; history.pushState({ ...newEntryState(), d: this.depth }, '', url.pathname + url.hash); }
    this.path = url.pathname;
    this.nav++;
    window.scrollTo(0, 0);
  }

  /** «Indietro» dell'app: la pagina di prima; senza una pagina dell'app prima, La mia lista. */
  back(): void {
    if (this.depth > 0) history.back();
    else this.go('/');
  }
}

function depthOf(state: unknown): number {
  const d = (state as { d?: unknown } | null)?.d;
  return typeof d === 'number' && d > 0 ? d : 0;
}

export const router = new Router();

/** Per i link: <a href="/guida" onclick={navigate}> segue l'indirizzo senza ricaricare la pagina. */
export function navigate(e: MouseEvent): void {
  if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
  const a = e.currentTarget as HTMLAnchorElement;
  if (a.target || a.origin !== window.location.origin) return;
  e.preventDefault();
  router.go(a.pathname + a.hash);
}
