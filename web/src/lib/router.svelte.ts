// Router minimo con indirizzi veri (/guida, /registrazioni...): il server rimanda a index.html ogni
// indirizzo che non è un file né /api. Nessuna dipendenza esterna.

export type Route = 'stasera' | 'opera' | 'profili' | 'guida' | 'libreria' | 'guarda' | 'registrazioni' | 'preferenze' | 'impostazioni' | 'non-trovata';

const paths: Record<string, Route> = {
  '/': 'stasera',
  '/stasera': 'stasera',
  '/profili': 'profili',
  '/guida': 'guida',
  '/libreria': 'libreria',
  '/registrazioni': 'registrazioni',
  '/preferenze': 'preferenze',
  '/impostazioni': 'impostazioni',
};

export function routeOf(pathname: string): Route {
  const p = pathname.length > 1 ? pathname.replace(/\/+$/, '') : pathname;
  if (/^\/opera\/\d+$/.test(p)) return 'opera';
  if (/^\/guarda\/\d+$/.test(p)) return 'guarda';
  if (/^\/libreria\/[a-z-]+(\/\d+)?$/.test(p)) return 'libreria';
  return paths[p] ?? 'non-trovata';
}

class Router {
  path = $state(window.location.pathname);
  route = $derived(routeOf(this.path));
  /** Id dell'opera per /opera/{id}. */
  workId = $derived(Number(this.path.match(/^\/opera\/(\d+)/)?.[1] ?? 0));
  /** Id del file per /guarda/{id} (player). */
  itemId = $derived(Number(this.path.match(/^\/guarda\/(\d+)/)?.[1] ?? 0));
  /** Sezione della libreria: /libreria/film, /libreria/liste/12 ... */
  librarySection = $derived(this.path.match(/^\/libreria\/([a-z-]+)/)?.[1] ?? 'liste');
  libraryListId = $derived(Number(this.path.match(/^\/libreria\/liste\/(\d+)/)?.[1] ?? 0));

  constructor() {
    window.addEventListener('popstate', () => { this.path = window.location.pathname; });
  }

  go(path: string, replace = false): void {
    if (path === this.path) return;
    if (replace) history.replaceState(null, '', path);
    else history.pushState(null, '', path);
    this.path = path;
    window.scrollTo(0, 0);
  }
}

export const router = new Router();

/** Per i link: <a href="/guida" onclick={navigate}> segue l'indirizzo senza ricaricare la pagina. */
export function navigate(e: MouseEvent): void {
  if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
  const a = e.currentTarget as HTMLAnchorElement;
  if (a.target || a.origin !== window.location.origin) return;
  e.preventDefault();
  router.go(a.pathname);
}
