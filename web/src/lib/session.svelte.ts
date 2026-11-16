// Profilo attivo su questo dispositivo. Resta in alto a destra in ogni schermata e un clic lo cambia.
// Si ricorda nel browser (comodità del dispositivo, non sicurezza: il confine è la rete di casa).
import type { Profile } from './api/client';

const KEY = 'dlf.profilo';

function readStored(): number | null {
  try {
    const v = localStorage.getItem(KEY);
    return v ? Number(v) : null;
  } catch {
    return null;
  }
}

class Session {
  profile = $state<Profile | null>(null);
  storedId = readStored();

  enter(p: Profile): void {
    this.profile = p;
    this.storedId = p.id;
    try { localStorage.setItem(KEY, String(p.id)); } catch { /* navigazione privata: si richiede la scelta */ }
  }

  leave(): void {
    this.profile = null;
    this.storedId = null;
    try { localStorage.removeItem(KEY); } catch { /* idem */ }
  }
}

export const session = new Session();
