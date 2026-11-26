// Opere con almeno un avviso acceso per il profilo attivo (26/11/2026, Antonio: «l'icona Avvisami non cambia quando
// l'avviso è attivo», per esempio in Cerca). Come i visti (seen.svelte.ts): si leggono una volta per profilo e si
// aggiornano quando il popup «Avvisami» salva, così ogni pulsante «Avvisami» dell'app mostra lo stesso stato.
import { api } from './api/client';
import { session } from './session.svelte';

class AlertedState {
  ids = $state(new Set<number>());
  private loadedFor: number | null = null;

  /** Legge gli avvisi del profilo attivo (una volta per profilo; force = di nuovo, dopo I miei avvisi). */
  sync(force = false): void {
    const id = session.profile?.id ?? null;
    if (id === this.loadedFor && !force) return;
    this.loadedFor = id;
    if (!force) this.ids = new Set();
    if (id == null) return;
    api.alerts(id)
      .then((l) => { if (this.loadedFor === id) this.ids = new Set(l.filter((a) => a.active).map((a) => a.workId)); })
      .catch(() => { /* senza stato: tutti spenti */ });
  }

  has(workId: number | null | undefined): boolean {
    return workId != null && this.ids.has(workId);
  }

  /** Dopo un cambio nel popup: quanti avvisi accesi ha ora l'opera. */
  set(workId: number, active: number): void {
    if ((active > 0) === this.ids.has(workId)) return;
    const n = new Set(this.ids);
    if (active > 0) n.add(workId);
    else n.delete(workId);
    this.ids = n;
  }
}

export const alertedState = new AlertedState();
