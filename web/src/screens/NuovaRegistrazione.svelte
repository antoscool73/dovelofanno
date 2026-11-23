<script lang="ts">
  // Registrazione a mano («libera»: canale, titolo, giorno e orari, come i campi di RegistraTV) e, dal 23/11/2026, modifica
  // di una registrazione non ancora partita (la matita in Registrazioni): stessi campi, già compilati. Minuti prima e
  // dopo partono dai predefiniti (Impostazioni › Registrazioni); il flusso si sceglie al momento di registrare.
  import { untrack } from 'svelte';
  import Button from '../lib/components/Button.svelte';
  import { api, ApiError, type Channel, type Recording, type Stream } from '../lib/api/client';
  import { at, byChannelOrder, dateInput, timeInput } from '../lib/format';

  let { onclose, ondone, edit = null }: { onclose: () => void; ondone: (warnings: string[]) => void; edit?: Recording | null } = $props();

  let channels = $state<Channel[]>([]);
  let loadError = $state('');
  const now = new Date();
  // da modificare: i suoi orari del programma (senza margini); nuova: da tra un minuto per un'ora
  // letta una volta: il modulo si ricrea per ogni registrazione da modificare ({#key} in Registrazioni)
  const e0 = untrack(() => edit);
  const start0 = e0 ? new Date(e0.start) : new Date(now.getTime() + 60_000);
  const stop0 = e0 ? new Date(e0.stop) : new Date(start0.getTime() + 60 * 60_000);
  let channelId = $state<number | null>(e0?.channelId ?? null);
  let title = $state(e0?.title ?? '');
  let date = $state(dateInput(start0));
  let from = $state(timeInput(start0));
  let to = $state(timeInput(stop0));
  let format = $state<'mp4' | 'mp3'>(e0?.format === 'mp3' ? 'mp3' : 'mp4');
  let before = $state(e0 ? String(e0.beforeMin) : '');
  let after = $state(e0 ? String(e0.afterMin) : '');
  if (!e0) {
    api.generalSettings().then((g) => {
      if (before === '') before = String(g.beforeMin);
      if (after === '') after = String(g.afterMin);
      format = g.format === 'mp3' ? 'mp3' : 'mp4';
    }).catch(() => { before ||= '2'; after ||= '10'; });
  }
  const minutesOk = (s: string) => /^\d{1,3}$/.test(s.trim()) && Number(s) <= 120;
  let error = $state('');
  let busy = $state(false);

  let streams = $state<Stream[]>([]);

  // stato del canale dai suoi flussi: i soli DRM non si registrano e non compaiono; gli altri nell'ordine della
  // playlist (come RegistraTV), con il numero davanti e una nota se non sono ancora controllati o hanno problemi
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
      .sort((a, b) => byChannelOrder(a.c, b.c))
      .map((x) => ({ ...x.c, label: (x.c.number != null ? x.c.number + ' · ' : '') + x.c.name + suffix[x.r] })),
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
    if (!minutesOk(before) || !minutesOk(after)) { error = 'Minuti prima e dopo: da 0 a 120.'; return; }
    const start = at(date, from);
    let stop = at(date, to);
    if (stop <= start) stop = new Date(stop.getTime() + 86_400_000); // finisce dopo mezzanotte
    busy = true;
    error = '';
    try {
      const body = { channelId, title, start: start.toISOString(), stop: stop.toISOString(), beforeMin: Number(before), afterMin: Number(after), format };
      const r = e0 ? await api.updateRecording(e0.id, body) : await api.scheduleRecording(body);
      ondone(r.warnings);
    } catch (err) {
      error = err instanceof ApiError ? err.message : e0 ? 'Registrazione non modificata' : 'Registrazione non programmata';
    } finally {
      busy = false;
    }
  }
</script>

<dialog use:dialog aria-labelledby="nr-title">
  <form onsubmit={submit}>
    <h2 id="nr-title">{e0 ? 'Modifica la registrazione' : 'Nuova registrazione'}</h2>
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
      <div class="row">
        <label class="field">Minuti prima<input inputmode="numeric" maxlength="3" bind:value={before} required /></label>
        <label class="field">Minuti dopo<input inputmode="numeric" maxlength="3" bind:value={after} required /></label>
      </div>
      <fieldset>
        <legend>Formato</legend>
        <label class="radio"><input type="radio" bind:group={format} value="mp4" /> Video (MP4)</label>
        <label class="radio"><input type="radio" bind:group={format} value="mp3" /> Solo audio (MP3)</label>
      </fieldset>
      <p class="hint">{e0 ? 'Si può cambiare finché la registrazione non parte (2 minuti prima dell’inizio, margini compresi).' : 'Minuti prima e dopo partono dalla registrazione predefinita (Impostazioni › Registrazioni).'}</p>
    {/if}
    {#if error}<p class="error" role="alert">{error}</p>{/if}
    <div class="actions">
      <Button variant="ghost" onclick={onclose}>Annulla</Button>
      <Button variant="primary" type="submit" disabled={busy || channelId === null || title.trim() === ''}>{e0 ? 'Salva' : 'Programma'}</Button>
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
