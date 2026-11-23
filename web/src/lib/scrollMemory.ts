// Ritorno al punto di prima (23/11/2026, richiesta di Antonio): con «Indietro» (della scheda o del browser) la pagina
// torna dov'era, con le file di locandine scorse allo stesso punto e la locandina cliccata evidenziata.
// Ogni voce della cronologia ha un numero (history.state.k); prima di cambiare pagina si salva com'era quella che si
// lascia; tornandoci si ripristina, riprovando per qualche secondo perché la pagina ricarica i dati e si allunga piano.

type Ident = { by: 'k' | 'href' | 'label'; v: string };
type Snap = { y: number; rows: [number, number][]; focus: Ident | null; focusRow: number };

const STORE = 'dlf.posizioni';
const ROWS = '.cards, .row-scroll, [data-row]';
let lastTarget: Element | null = null;

function load(): Record<string, Snap> {
  try { return JSON.parse(sessionStorage.getItem(STORE) ?? '{}') as Record<string, Snap>; } catch { return {}; }
}
function store(all: Record<string, Snap>): void {
  const keys = Object.keys(all).map(Number).sort((a, b) => b - a).slice(0, 40);
  const kept: Record<string, Snap> = {};
  for (const k of keys) { const v = all[k]; if (v) kept[k] = v; }
  try { sessionStorage.setItem(STORE, JSON.stringify(kept)); } catch { /* solo senza ritorno al punto */ }
}

/** Numero della voce corrente della cronologia (lo assegna se manca). */
export function entryKey(): number {
  const s = history.state as { k?: number } | null;
  if (s && typeof s.k === 'number') return s.k;
  const k = Date.now();
  history.replaceState({ ...(s ?? {}), k }, '');
  return k;
}
export function newEntryState(): { k: number } { return { k: Date.now() + 1 }; }

function identOf(el: Element | null): Ident | null {
  if (!el) return null;
  const k = el.closest<HTMLElement>('[data-k]');
  if (k?.dataset.k) return { by: 'k', v: k.dataset.k };
  const a = el.closest<HTMLAnchorElement>('a[href]');
  if (a) return { by: 'href', v: a.getAttribute('href') ?? '' };
  const b = el.closest<HTMLElement>('button[aria-label]');
  if (b) return { by: 'label', v: b.getAttribute('aria-label') ?? '' };
  return null;
}
function clickable(el: Element): HTMLElement {
  return (el.closest<HTMLElement>('a[href], button') ?? el) as HTMLElement;
}
function find(id: Ident): HTMLElement[] {
  const all = id.by === 'k' ? document.querySelectorAll<HTMLElement>('[data-k]')
    : id.by === 'href' ? document.querySelectorAll<HTMLElement>('a[href]')
    : document.querySelectorAll<HTMLElement>('button[aria-label]');
  return [...all].filter((e) => (id.by === 'k' ? e.dataset.k : e.getAttribute(id.by === 'href' ? 'href' : 'aria-label')) === id.v);
}

// la locandina cliccata: si ricorda il bersaglio dell'ultimo clic o tasto
document.addEventListener('pointerdown', (e) => { lastTarget = e.target as Element; document.querySelectorAll('.was-here').forEach((x) => x.classList.remove('was-here')); }, true);
document.addEventListener('keydown', (e) => { if (e.key === 'Enter' || e.key === ' ') lastTarget = document.activeElement; }, true);

/** Prima di lasciare la pagina: come la si lascia. */
export function saveHere(): void {
  const rows = [...document.querySelectorAll<HTMLElement>(ROWS)];
  const target = lastTarget && document.contains(lastTarget) ? lastTarget : document.activeElement;
  const focus = target && target !== document.body ? identOf(target) : null;
  const row = target?.closest(ROWS) ?? null;
  const all = load();
  all[entryKey()] = { y: window.scrollY, rows: rows.map((r) => [r.scrollLeft, r.scrollTop] as [number, number]), focus, focusRow: row ? rows.indexOf(row as HTMLElement) : -1 };
  store(all);
}

let restoring = 0;
/** Tornando a una pagina con «Indietro»: com'era. */
export function restoreHere(): void {
  const snap = load()[entryKey()];
  if (!snap) return;
  const run = ++restoring;
  const until = Date.now() + 4000;
  // prima la pagina e le file tornano dove erano (quando la pagina è di nuovo lunga abbastanza), poi si cerca la
  // locandina: nella griglia della guida esiste solo dopo che il riquadro è tornato al suo punto
  const place = () => {
    if (run !== restoring) return;
    const rows = [...document.querySelectorAll<HTMLElement>(ROWS)];
    const tall = document.documentElement.scrollHeight >= snap.y + window.innerHeight - 4;
    if ((!tall || rows.length < snap.rows.length) && Date.now() < until) { setTimeout(place, 100); return; }
    rows.forEach((r, i) => { const v = snap.rows[i]; if (v) { r.scrollLeft = v[0]; r.scrollTop = v[1]; } });
    window.scrollTo(0, snap.y);
    if (snap.focus) mark(rows, Date.now() + 1500);
  };
  const mark = (rows: HTMLElement[], end: number) => {
    if (run !== restoring || !snap.focus) return;
    const els = find(snap.focus);
    if (els.length === 0) { if (Date.now() < end) setTimeout(() => mark(rows, end), 100); return; }
    const el = els.find((e) => snap.focusRow < 0 || e.closest(ROWS) === rows[snap.focusRow]) ?? els[0];
    if (!el) return;
    const c = clickable(el);
    // se la fila è cambiata (dati nuovi) la locandina si riporta in vista senza spostare la pagina in verticale
    const row = c.closest<HTMLElement>(ROWS);
    if (row) {
      const rr = row.getBoundingClientRect(), cr = c.getBoundingClientRect();
      if (cr.left < rr.left || cr.right > rr.right) row.scrollLeft += cr.left - rr.left - 16;
    }
    c.focus({ preventScroll: true });
    c.classList.add('was-here');
  };
  setTimeout(place, 0);
}
