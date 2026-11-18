// Catalogo delle piattaforme (logo, ordine di rilievo, gruppo) letto una volta dal server e condiviso da tutte le
// schermate. Un nome che il catalogo non conosce ha l'iniziale al posto del logo e va in fondo.
import { api, type Platform } from './api/client';

class Platforms {
  list = $state<Platform[]>([]);
  #byName = $derived(new Map(this.list.map((p) => [p.name.toLowerCase(), p])));
  #loading: Promise<void> | null = null;

  load(): Promise<void> {
    this.#loading ??= api.platforms().then((l) => { this.list = l; }).catch(() => { this.#loading = null; });
    return this.#loading;
  }

  /** Dati della piattaforma; per un nome sconosciuto gruppo e nome breve sono il nome stesso. */
  of(name: string): Platform {
    const n = name.trim();
    const hit = this.#byName.get(n.toLowerCase());
    if (hit) return hit;
    return { name: n, logo: '', priority: 1000, group: n, short: n };
  }

  /** Ordina i nomi dalla piattaforma più diffusa, i canali subito dopo la loro piattaforma. */
  sort(names: string[]): string[] {
    const idx = new Map(this.list.map((p, i) => [p.name.toLowerCase(), i]));
    return [...names].sort((a, b) => (idx.get(a.toLowerCase()) ?? 1e6) - (idx.get(b.toLowerCase()) ?? 1e6) || a.localeCompare(b));
  }
}

export const platforms = new Platforms();
