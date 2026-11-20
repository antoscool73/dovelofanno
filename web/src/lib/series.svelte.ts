// «Registra la serie» (20/11/2026, richiesta di Antonio): premendo Registra su una puntata di una serie o di un
// programma (mai su un film) si chiede se registrare solo quella o tutte le puntate che andranno in onda. Con «tutte»
// il server crea la regola e programma le puntate in guida; la finestra (SeriesDialog, montata in App.svelte) mostra
// l'elenco e le puntate che mancano. Uso: `const how = await recordChoice(p); if (how === null) return;
// if (how === 'series') { ricarica; return; }` e con 'one' si programma la sola puntata come prima.
import { api, ApiError, type GuideProgramme, type SeriesCheck } from './api/client';
import { session } from './session.svelte';

class SeriesState {
  asking = $state<{ p: GuideProgramme; resolve: (v: 'one' | 'all' | null) => void } | null>(null);
  result = $state<SeriesCheck | null>(null);
  busy = $state(false);
  error = $state('');

  answer(v: 'one' | 'all' | null): void {
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

/** 'one' = registra solo questa (lo fa chi chiama), 'series' = regola creata, null = annullato. */
export async function recordChoice(p: GuideProgramme): Promise<'one' | 'series' | null> {
  if (!canBeSeries(p)) return 'one';
  seriesState.asking?.resolve(null);
  const v = await new Promise<'one' | 'all' | null>((resolve) => { seriesState.asking = { p, resolve }; });
  if (v !== 'all') return v === 'one' ? 'one' : null;
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
