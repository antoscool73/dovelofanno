// «Visto» ovunque compaia un'opera (23/11/2026, richiesta di Antonio): le opere viste dal profilo, lette una volta
// dal server e tenute qui, così ogni locandina, riga di ricerca, scheda o programma della guida mostra lo stesso stato
// e lo cambia con un tocco (SeenButton). «Nascondi visti» (interruttore della pagina principale, accanto ad
// Apri/Chiudi tutte) toglie i visti dagli elenchi; si ricorda per profilo su questo dispositivo.
import { api } from './api/client';
import { session } from './session.svelte';

const HIDE_KEY = 'dlf.nascondiVisti.';

class SeenState {
  ids = $state(new Set<number>());
  hide = $state(false);
  busy = $state(new Set<number>());
  private loadedFor: number | null = null;

  /** Legge i visti del profilo attivo (una volta per profilo). */
  sync(): void {
    const id = session.profile?.id ?? null;
    if (id === this.loadedFor) return;
    this.loadedFor = id;
    this.ids = new Set();
    try { this.hide = id != null && localStorage.getItem(HIDE_KEY + id) === '1'; } catch { this.hide = false; }
    if (id == null) return;
    api.watchedIds(id).then((l) => { if (this.loadedFor === id) this.ids = new Set(l); }).catch(() => { /* senza stato: tutti «non visti» */ });
  }

  has(workId: number | null | undefined): boolean {
    return workId != null && this.ids.has(workId);
  }

  /** Segna (o toglie) il visto; true se è andato a buon fine. */
  async toggle(workId: number): Promise<boolean> {
    const prof = session.profile;
    if (!prof || this.busy.has(workId)) return false;
    const was = this.ids.has(workId);
    this.busy = new Set(this.busy).add(workId);
    try {
      if (was) await api.unwatched(prof.id, workId);
      else await api.watched(prof.id, workId);
      this.set(workId, !was);
      return true;
    } catch {
      return false;
    } finally {
      const b = new Set(this.busy);
      b.delete(workId);
      this.busy = b;
    }
  }

  /** Aggiorna lo stato dopo un cambio fatto altrove (la Scheda, il player). */
  set(workId: number, watched: boolean): void {
    const n = new Set(this.ids);
    if (watched) n.add(workId);
    else n.delete(workId);
    this.ids = n;
  }

  setHide(v: boolean): void {
    this.hide = v;
    const id = session.profile?.id;
    if (id == null) return;
    try { if (v) localStorage.setItem(HIDE_KEY + id, '1'); else localStorage.removeItem(HIDE_KEY + id); } catch { /* solo per questa visita */ }
  }

  /** Va tolto dagli elenchi: è visto e «Nascondi visti» è acceso. */
  hidden(workId: number | null | undefined): boolean {
    return this.hide && this.has(workId);
  }
}

export const seenState = new SeenState();
