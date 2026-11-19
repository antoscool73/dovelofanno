// Conferma delle azioni distruttive (19/11/2026): un popup unico per tutta l'app, montato una volta in App.svelte
// (ConfirmDialog). Uso: `if (!(await ask({ title, text, ok: 'Ferma' }))) return;`. Sostituisce confirm() del browser.

export type ConfirmRequest = {
  title: string;
  text?: string;
  ok: string;              // testo del pulsante che conferma: il verbo dell'azione («Ferma», «Elimina»)
  cancel?: string;         // predefinito «Annulla»
  danger?: boolean;        // pulsante rosso (predefinito sì): l'azione cancella o ferma qualcosa
};

class ConfirmState {
  current = $state<(ConfirmRequest & { resolve: (ok: boolean) => void }) | null>(null);

  ask(r: ConfirmRequest): Promise<boolean> {
    // una sola domanda alla volta: quella vecchia vale come «Annulla»
    this.current?.resolve(false);
    return new Promise((resolve) => { this.current = { ...r, resolve }; });
  }

  answer(ok: boolean): void {
    const c = this.current;
    this.current = null;
    c?.resolve(ok);
  }
}

export const confirmState = new ConfirmState();
export const ask = (r: ConfirmRequest): Promise<boolean> => confirmState.ask(r);
