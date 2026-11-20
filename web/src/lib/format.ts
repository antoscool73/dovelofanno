// Date, orari e durate in italiano, come nel prototipo: "Stasera", "Domani", "Mar 17", "21:05".

const dayNames = ['Dom', 'Lun', 'Mar', 'Mer', 'Gio', 'Ven', 'Sab'];

function startOfDay(d: Date): number {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
}

export function time(iso: string | Date): string {
  const d = typeof iso === 'string' ? new Date(iso) : iso;
  return d.toLocaleTimeString('it-IT', { hour: '2-digit', minute: '2-digit' });
}

/** "Oggi"/"Stasera" (dalle 18), "Domani", "Ieri", altrimenti "Mar 17". */
export function day(iso: string | Date, now: Date = new Date()): string {
  const d = typeof iso === 'string' ? new Date(iso) : iso;
  const diff = Math.round((startOfDay(d) - startOfDay(now)) / 86_400_000);
  if (diff === 0) return d.getHours() >= 18 ? 'Stasera' : 'Oggi';
  if (diff === 1) return 'Domani';
  if (diff === -1) return 'Ieri';
  return `${dayNames[d.getDay()]} ${d.getDate()}`;
}

export function range(fromIso: string, toIso: string): string {
  return `${time(fromIso)}–${time(toIso)}`;
}

/** 95 -> "1 h 35", 40 -> "40 min", 120 -> "2 h". */
export function minutes(total: number): string {
  const m = Math.round(total);
  if (m < 60) return `${m} min`;
  const h = Math.floor(m / 60);
  const r = m % 60;
  return r === 0 ? `${h} h` : `${h} h ${String(r).padStart(2, '0')}`;
}

export function between(fromIso: string, toIso: string): number {
  return (new Date(toIso).getTime() - new Date(fromIso).getTime()) / 60_000;
}

/** "2026-11-16" per <input type="date"> nel fuso locale. */
export function dateInput(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

export function timeInput(d: Date): string {
  return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
}

/** Data locale + ora "21:05" -> Date. */
export function at(date: string, hhmm: string): Date {
  const [y, mo, da] = date.split('-').map(Number);
  const [h, mi] = hhmm.split(':').map(Number);
  return new Date(y ?? 1970, (mo ?? 1) - 1, da ?? 1, h ?? 0, mi ?? 0);
}

export function fileName(path: string): string {
  const i = Math.max(path.lastIndexOf('/'), path.lastIndexOf('\\'));
  return i >= 0 ? path.slice(i + 1) : path;
}

/** Etichetta di un giorno per i pulsanti della guida: "Oggi, lun 16", "Domani, mar 17", "Mer 18". */
export function dayChip(d: Date, now: Date = new Date()): string {
  const diff = Math.round((startOfDay(d) - startOfDay(now)) / 86_400_000);
  const base = `${dayNames[d.getDay()]} ${d.getDate()}`;
  if (diff === 0) return `Oggi, ${base.toLowerCase()}`;
  if (diff === 1) return `Domani, ${base.toLowerCase()}`;
  return base;
}

/** Mezzanotte locale del giorno di d. */
export function midnight(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate());
}

/** Voto con la virgola: 6.4 -> "6,4". */
export function vote(v: number | null | undefined): string {
  return v == null ? '' : v.toLocaleString('it-IT', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
}

/** Ordine dei canali: quello della playlist (come RegistraTV), poi per numero del digitale terrestre e nome. */
export function byChannelOrder(
  a: { order?: number | null; number?: number | null; name: string },
  b: { order?: number | null; number?: number | null; name: string },
): number {
  // prima l'ordine della playlist (dal server, come RegistraTV); i canali della sola guida dopo, per numero e nome
  const oa = a.order ?? null;
  const ob = b.order ?? null;
  if (oa !== ob) {
    if (oa == null) return 1;
    if (ob == null) return -1;
    return oa - ob;
  }
  const na = a.number ?? null;
  const nb = b.number ?? null;
  if (na !== nb) {
    if (na == null) return 1;
    if (nb == null) return -1;
    return na - nb;
  }
  return a.name.localeCompare(b.name, 'it', { numeric: true });
}

/** Durata di un video: 6435 -> "1:47:15", 95 -> "1:35". */
export function clock(seconds: number): string {
  const s = Math.max(0, Math.floor(seconds));
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const r = s % 60;
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${String(r).padStart(2, '0')}` : `${m}:${String(r).padStart(2, '0')}`;
}

/**
 * La puntata di un passaggio di una serie o di un programma: «S1 E3 · Titolo della puntata»; vuoto per i film. Per serie
 * e programmi il numero va sempre mostrato, per capire che cosa si guarda o si registra (Antonio, 20/11/2026).
 */
export function episodeOf(p: { episode?: string | null; subtitle?: string | null; group?: string | null }): string {
  if (p.group === 'Film') return '';
  return [p.episode?.trim(), p.subtitle?.trim()].filter(Boolean).join(' · ');
}

/** Titolo di una registrazione: titolo, numero della puntata e titolo della puntata se breve (come SeriesStore.RecordingTitle). */
export function recordingTitle(p: { title: string; episode?: string | null; subtitle?: string | null; group?: string | null }): string {
  if (p.group === 'Film') return p.title;
  const parts = [p.title, p.episode?.trim() ?? ''];
  if (p.subtitle && p.subtitle.trim().length <= 60) parts.push(p.subtitle.trim());
  return parts.filter(Boolean).join(' - ');
}
