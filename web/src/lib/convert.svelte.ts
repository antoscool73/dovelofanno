// «Converti...» (20/11/2026, come in RegistraTV): una registrazione o un file della libreria in MP4 o MP3. La finestra
// (ConvertDialog, montata in App.svelte) chiede il formato e mette la conversione in coda sul server, che avvisa alla
// fine con una notifica. Le conversioni si seguono in Impostazioni › Registrazioni.
import { api, ApiError } from './api/client';
import { session } from './session.svelte';

export type ConvertTarget = { recordingId?: number; libraryItemId?: number; name: string };

/**
 * Formati in cui ha senso convertire un file (23/11/2026, Antonio: «che senso ha convertire da mp4 a mp4?»): mai nel suo
 * stesso formato, e un MP3 è solo audio, quindi non ha niente da convertire. Un MP4 va solo in MP3; TS, MKV e gli altri
 * in tutti e due.
 */
export function formatsFor(name: string): ('mp4' | 'mp3')[] {
  const ext = /\.([a-z0-9]+)$/i.exec(name)?.[1]?.toLowerCase() ?? '';
  if (ext === 'mp3') return [];
  return (['mp4', 'mp3'] as const).filter((f) => f !== ext);
}
/** Il pulsante «Converti...» compare solo se c'è un formato in cui convertire. */
export const canConvert = (name: string) => formatsFor(name).length > 0;

class ConvertState {
  target = $state<ConvertTarget | null>(null);
  format = $state<'mp4' | 'mp3'>('mp4');
  busy = $state(false);
  error = $state('');
  done = $state('');

  open(t: ConvertTarget): void {
    this.target = t;
    this.format = formatsFor(t.name)[0] ?? 'mp4';
    this.error = '';
    this.done = '';
  }

  close(): void {
    this.target = null;
  }

  async start(): Promise<void> {
    const t = this.target;
    if (!t) return;
    this.busy = true;
    this.error = '';
    try {
      await api.convert({ recordingId: t.recordingId ?? null, libraryItemId: t.libraryItemId ?? null, format: this.format, profileId: session.profile?.id ?? null });
      this.done = `In coda: «${t.name}» in ${this.format.toUpperCase()}. Il file nuovo va accanto all'originale, che resta; alla fine arriva una notifica.`;
    } catch (e) {
      this.error = e instanceof ApiError ? e.message : 'Conversione non avviata';
    } finally {
      this.busy = false;
    }
  }
}

export const convertState = new ConvertState();
export const openConvert = (t: ConvertTarget) => convertState.open(t);
