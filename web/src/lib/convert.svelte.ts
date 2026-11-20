// «Converti...» (20/11/2026, come in RegistraTV): una registrazione o un file della libreria in MP4 o MP3. La finestra
// (ConvertDialog, montata in App.svelte) chiede il formato e mette la conversione in coda sul server, che avvisa alla
// fine con una notifica. Le conversioni si seguono in Impostazioni › Registrazioni.
import { api, ApiError } from './api/client';
import { session } from './session.svelte';

export type ConvertTarget = { recordingId?: number; libraryItemId?: number; name: string };

class ConvertState {
  target = $state<ConvertTarget | null>(null);
  format = $state<'mp4' | 'mp3'>('mp4');
  busy = $state(false);
  error = $state('');
  done = $state('');

  open(t: ConvertTarget): void {
    this.target = t;
    // come RegistraTV: un file già MP4 si converte di solito in MP3
    this.format = /\.mp4$/i.test(t.name) ? 'mp3' : 'mp4';
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
