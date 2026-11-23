// Intro all'ingresso (23/11/2026, richiesta di Antonio): il logo animato con la tarantella («Jingle v1.mp4», ridotto a
// 720p in public/intro.mp4). Una volta per scheda del browser, dopo la scelta del profilo; si salta con un clic o Esc e
// si spegne in Preferenze › Notifiche, su questo dispositivo. Niente intro a chi chiede meno animazioni al sistema.
const KEY = 'dlf.intro';
const SEEN = 'dlf.intro.vista';

export function introOn(): boolean {
  try { return localStorage.getItem(KEY) !== 'off'; } catch { return true; }
}
export function setIntroOn(v: boolean): void {
  try { if (v) localStorage.removeItem(KEY); else localStorage.setItem(KEY, 'off'); } catch { /* solo per questa visita */ }
}

/** Da mostrare adesso? (attiva, non ancora vista in questa scheda, animazioni permesse) */
export function introDue(): boolean {
  if (!introOn()) return false;
  if (window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) return false;
  try { return sessionStorage.getItem(SEEN) !== '1'; } catch { return true; }
}
export function introSeen(): void {
  try { sessionStorage.setItem(SEEN, '1'); } catch { /* al massimo si rivede */ }
}
