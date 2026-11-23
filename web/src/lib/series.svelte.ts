// La finestra di Registra (RecordDialog, montata in App.svelte). Dal 23/11/2026 (richiesta di Antonio) ogni tocco su
// Registra chiede conferma: si vede cosa si registra (puntata, giorno, orario, canale) e con quali impostazioni (minuti
// prima e dopo, formato: le predefinite, compresse in una riga, si cambiano solo per questa registrazione). OK è il
// pulsante predefinito (Invio), Esc annulla. Niente «non chiedere più»: registrare merita una riflessione (Antonio).
// Per serie e programmi (mai per i film) la stessa finestra chiede «solo questa puntata» o «tutta la serie» («Registra
// la serie», 20/11/2026) e, dall'anteprima del server, quante puntate prima di questa sono registrate, recuperabili o
// perse. Con «tutta la serie» il server crea la regola e la finestra mostra l'esito.
// Uso: `const how = await recordChoice(p); if (how === null) return; if (how === 'series') { ricarica; return; }`
// e altrimenti `api.scheduleRecording({ ..., ...how })` (how ha beforeMin, afterMin e format).
import { api, ApiError, type GuideProgramme, type SeriesCheck } from './api/client';
import { session } from './session.svelte';

/** Impostazioni di una registrazione: null = la predefinita. */
export type RecordOptions = { beforeMin: number | null; afterMin: number | null; format: string | null };
type Answer = { how: 'one'; opts: RecordOptions } | { how: 'all' } | null;

class SeriesState {
  asking = $state<{ p: GuideProgramme; resolve: (v: Answer) => void } | null>(null);
  result = $state<SeriesCheck | null>(null);
  busy = $state(false);
  error = $state('');

  answer(v: Answer): void {
    const a = this.asking;
    this.asking = null;
    a?.resolve(v);
  }

  close(): void {
    this.result = null;
    this.error = '';
  }
}

export const seriesState = new SeriesState();

/** Si chiede «tutte le puntate?» per tutto tranne i film. */
export const canBeSeries = (p: GuideProgramme) => p.group !== 'Film' && p.workId != null;

/** Le impostazioni da usare, 'series' = regola creata, null = annullato. */
export async function recordChoice(p: GuideProgramme): Promise<RecordOptions | 'series' | null> {
  seriesState.asking?.resolve(null);
  const v = await new Promise<Answer>((resolve) => { seriesState.asking = { p, resolve }; });
  if (v === null) return null;
  if (v.how === 'one') return v.opts;
  seriesState.busy = true;
  seriesState.error = '';
  seriesState.result = null;
  try {
    seriesState.result = await api.recordSeries({ programmeId: p.id, profileId: session.profile?.id ?? null });
  } catch (e) {
    seriesState.error = e instanceof ApiError ? e.message : 'Serie non programmata';
  } finally {
    seriesState.busy = false;
  }
  return 'series';
}
