<script lang="ts">
  // Registrazione a mano: canale, titolo, giorno e orari, come i campi di RegistraTV. Margini e cartella sono
  // quelli predefiniti del server; il flusso si sceglie al momento di registrare.
  import Button from '../lib/components/Button.svelte';
  import { api, ApiError, type Channel, type Stream } from '../lib/api/client';
  import { at, dateInput, timeInput } from '../lib/format';

  let { onclose, ondone }: { onclose: () => void; ondone: (warnings: string[]) => void } = $props();

  let channels = $state<Channel[]>([]);
  let loadError = $state('');
  const now = new Date();
  const start0 = new Date(now.getTime() + 60_000);
  let channelId = $state<number | null>(null);
  let title = $state('');
  let date = $state(dateInput(start0));
  let from = $state(timeInput(start0));
  let to = $state(timeInput(new Date(start0.getTime() + 60 * 60_000)));
  let format = $state<'mp4' | 'mp3'>('mp4');
  let error = $state('');
  let busy = $state(false);

  let streams = $state<Stream[]>([]);

  // stato del canale dai suoi flussi: i soli DRM non si registrano e non compaiono;
  // prima quelli che funzionano, poi quelli da controllare, poi quelli con problemi
  const rank = (c: Channel) => {
    const l = streams.filter((s) => s.channelId === c.id);
    if (l.some((s) => s.state === 'ok')) return 0;
    if (l.some((s) => s.state === 'unchecked')) return 1;
    if (l.some((s) => s.state !== 'drm')) return 2;
    return 3;
  };
  const suffix = ['', ' (da controllare)', ' (con problemi)'];
  const usable = $derived(
    channels
      .map((c) => ({ c, r: rank(c) }))
      .filter((x) => x.r < 3)
      .sort((a, b) => a.r - b.r || a.c.name.localeCompare(b.c.name, 'it'))
      .map((x) => ({ ...x.c, label: x.c.name + suffix[x.r] })),
  );

  async function loadChannels() {
    try {
      [channels, streams] = await Promise.all([api.channels(), api.allStreams()]);
      channels = channels.filter((c) => !c.excluded);
      const first = usable[0];
      if (channelId === null && first) channelId = first.id;
    } catch (e) {
      loadError = e instanceof ApiError ? e.message : 'Canali non letti';
    }
  }
  loadChannels();

  function dialog(node: HTMLDialogElement) {
    node.showModal();
    node.addEventListener('cancel', (e) => { e.preventDefault(); onclose(); });
  }

  async function submit(e: SubmitEvent) {
    e.preventDefault();
    if (channelId === null) return;
    const start = at(date, from);
    let stop = at(date, to);
    if (stop <= start) stop = new Date(stop.getTime() + 86_400_000); // finisce dopo mezzanotte
    busy = true;
    error = '';
    try {
      const r = await api.scheduleRecording({ channelId, title, start: start.toISOString(), stop: stop.toISOString(), beforeMin: null, afterMin: null, format });
      ondone(r.warnings);
    } catch (err) {
      error = err instanceof ApiError ? err.message : 'Registrazione non programmata';
    } finally {
      busy = false;
    }
  }
</script>

<dialog use:dialog aria-labelledby="nr-title">
  <form onsubmit={submit}>
    <h2 id="nr-title">Nuova registrazione</h2>
    {#if loadError}
      <p class="error" role="alert">{loadError}</p>
    {:else if channels.length === 0}
      <p class="hint">Nessun canale: aggiungi prima una playlist in Impostazioni › Sorgenti.</p>
    {:else}
      <label class="field">Canale
        <select bind:value={channelId} required>
          {#each usable as c (c.id)}
            <option value={c.id}>{c.label}</option>
          {/each}
        </select>
      </label>
      <label class="field">Titolo<input bind:value={title} maxlength="200" required /></label>
      <div class="row">
        <label class="field">Giorno<input type="date" bind:value={date} required /></label>
        <label class="field">Dalle<input type="time" bind:value={from} required /></label>
        <label class="field">Alle<input type="time" bind:value={to} required /></label>
      </div>
      <fieldset>
        <legend>Formato</legend>
        <label class="radio"><input type="radio" bind:group={format} value="mp4" /> Video (MP4)</label>
        <label class="radio"><input type="radio" bind:group={format} value="mp3" /> Solo audio (MP3)</label>
      </fieldset>
      <p class="hint">Con i margini predefiniti del server (Impostazioni › Limiti).</p>
    {/if}
    {#if error}<p class="error" role="alert">{error}</p>{/if}
    <div class="actions">
      <Button variant="ghost" onclick={onclose}>Annulla</Button>
      <Button variant="primary" type="submit" disabled={busy || channelId === null || title.trim() === ''}>Programma</Button>
    </div>
  </form>
</dialog>

<style>
  dialog {
    width: min(560px, calc(100vw - 32px)); padding: 0; border: 1px solid var(--line-strong);
    border-radius: var(--radius-l); background: var(--surface); color: var(--text);
  }
  dialog::backdrop { background: rgba(5, 7, 10, 0.7); }
  form { display: flex; flex-direction: column; gap: 16px; padding: 24px; }
  h2 { font-size: 22px; }
  .row { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; }
  .field { display: flex; flex-direction: column; gap: 6px; font-size: 13px; color: var(--muted); }
  .field input, .field select {
    min-height: 44px; padding: 0 12px; border-radius: var(--radius-s); border: 1px solid var(--line-strong);
    background: var(--card); color: var(--text); font-size: 15px;
  }
  fieldset { border: 0; margin: 0; padding: 0; display: flex; flex-wrap: wrap; gap: 8px 20px; }
  legend { font-size: 13px; color: var(--muted); margin-bottom: 6px; }
  .radio { display: flex; align-items: center; gap: 8px; font-size: 15px; min-height: 36px; }
  .hint { margin: 0; font-size: 13px; color: var(--muted); }
  .error { margin: 0; font-size: 14px; color: var(--rec-text); }
  .actions { display: flex; justify-content: flex-end; gap: 10px; }
  @media (max-width: 520px) { .row { grid-template-columns: 1fr; } }
</style>
