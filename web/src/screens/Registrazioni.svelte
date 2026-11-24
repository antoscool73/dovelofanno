<script lang="ts">
  import { canConvert, openConvert } from '../lib/convert.svelte';
  import { ask } from '../lib/confirm.svelte';
  import Badge from '../lib/components/Badge.svelte';
  import Button from '../lib/components/Button.svelte';
  import NuovaRegistrazione from './NuovaRegistrazione.svelte';
  import LivePreview from '../lib/components/LivePreview.svelte';
  import { api, ApiError, type Recording } from '../lib/api/client';
  import { between, day, fileName, minutes, range, time } from '../lib/format';
  import { navigate } from '../lib/router.svelte';

  type Filter = 'tutte' | 'active' | 'scheduled' | 'completed' | 'incomplete' | 'failed';
  const filters: { id: Filter; label: string }[] = [
    { id: 'tutte', label: 'Tutte' },
    // le pastiglie seguono le sezioni della pagina: la somma fa sempre «Tutte» (19/11/2026)
    { id: 'active', label: 'In corso' },
    { id: 'scheduled', label: 'In programma' },
    { id: 'completed', label: 'Completate' },
    { id: 'incomplete', label: 'Incomplete' },
    { id: 'failed', label: 'Non riuscite' },
  ];
  const states: Record<string, { label: string; tone: 'blue' | 'green' | 'orange' | 'red' | 'neutral' }> = {
    scheduled: { label: 'In programma', tone: 'blue' },
    recording: { label: 'In registrazione', tone: 'red' },
    merging: { label: 'Unione in corso', tone: 'neutral' },
    completed: { label: 'Completata', tone: 'green' },
    incomplete: { label: 'Incompleta', tone: 'orange' },
    stopped: { label: 'Fermata', tone: 'neutral' },
    failed: { label: 'Non riuscita', tone: 'red' },
    cancelled: { label: 'Annullata', tone: 'neutral' },
  };

  let all = $state<Recording[]>([]);
  let loaded = $state(false);
  let error = $state('');
  let filter = $state<Filter>('tutte');
  let adding = $state(false);
  // la registrazione in modifica (matita): stesso modulo di «Nuova registrazione», già compilato
  let editing = $state<Recording | null>(null);
  let notice = $state<{ text: string; warnings: string[] } | null>(null);
  let now = $state(new Date());
  let busyId = $state<number | null>(null);

  async function load() {
    try {
      all = await api.recordings();
      error = '';
    } catch (e) {
      error = e instanceof ApiError ? e.message : 'Registrazioni non lette';
    } finally {
      loaded = true;
    }
  }
  load();

  // aggiornamento: ogni 5 s se qualcosa è in corso o sta per partire, altrimenti ogni minuto
  const active = $derived(all.filter((r) => r.state === 'recording' || r.state === 'merging'));
  $effect(() => {
    const soon = all.some((r) => r.state === 'scheduled' && new Date(r.recordFrom).getTime() - Date.now() < 5 * 60_000);
    const ms = active.length > 0 || soon ? 5000 : 60_000;
    const t = setInterval(() => { now = new Date(); load(); }, ms);
    return () => clearInterval(t);
  });

  const count = (f: Filter) =>
    f === 'tutte' ? all.filter((r) => r.state !== 'cancelled').length
    : f === 'active' ? active.length
    : f === 'failed' ? all.filter((r) => r.state === 'failed').length
    : f === 'completed' ? all.filter((r) => r.state === 'completed' || r.state === 'stopped').length
    : all.filter((r) => r.state === f).length;

  const scheduled = $derived(all.filter((r) => r.state === 'scheduled').sort((a, b) => a.recordFrom.localeCompare(b.recordFrom)));
  // le più recenti prima, qualunque ordine arrivi dal server
  const finished = $derived(
    all.filter((r) => ['completed', 'incomplete', 'stopped', 'failed'].includes(r.state)).sort((a, b) => b.start.localeCompare(a.start)),
  );
  const shownFinished = $derived(
    filter === 'tutte' ? finished
    : filter === 'completed' ? finished.filter((r) => r.state === 'completed' || r.state === 'stopped')
    : finished.filter((r) => r.state === filter),
  );

  function progress(r: Recording): number {
    const total = between(r.recordFrom, r.recordTo);
    const done = (now.getTime() - new Date(r.recordFrom).getTime()) / 60_000;
    return Math.max(0, Math.min(100, Math.round((done / total) * 100)));
  }

  function meta(r: Recording): string {
    const parts = [r.channelName, r.format === 'mp3' ? 'solo audio MP3' : 'MP4'];
    if (r.beforeMin || r.afterMin) parts.push(`con margini ${range(r.recordFrom, r.recordTo)}`);
    if (r.file) parts.push(fileName(r.file));
    return parts.join(' · ');
  }

  function note(r: Recording): string {
    if (r.state === 'incomplete' && r.lostMinutes > 0) {
      const ranges = r.lostRanges.map((x) => `${time(x.from)}–${time(x.to)}`).join(', ');
      return `Mancano ${minutes(r.lostMinutes)}${ranges ? `: PC in sospensione o partito in ritardo (${ranges})` : ''}.`;
    }
    // per le non riuscite il server scrive il motivo; altrimenti è il percorso del log
    if (r.state === 'failed' && r.log && !/\.log$/i.test(r.log)) return r.log.charAt(0).toUpperCase() + r.log.slice(1) + '.';
    return '';
  }

  async function stop(r: Recording) {
    if (!(await ask({ title: `Fermare «${r.title}»?`, text: 'La registrazione si ferma adesso: quello registrato finora resta e diventa il file.', ok: 'Ferma la registrazione' }))) return;
    busyId = r.id;
    try { await api.stopRecording(r.id); await load(); }
    catch (e) { error = e instanceof ApiError ? e.message : 'Non fermata'; }
    finally { busyId = null; }
  }

  // «Cambia la fine» di una registrazione in corso: un orario (il giorno dopo se è già passato oggi) o +15/+30/+60 min
  let endFor = $state<number | null>(null);
  let endTime = $state('');
  function openEnd(r: Recording) {
    endFor = r.id;
    endTime = time(r.recordTo);
    error = '';
  }
  function endFromTime(hhmm: string): Date | null {
    const m = /^(\d{1,2}):(\d{2})$/.exec(hhmm);
    if (!m) return null;
    const d = new Date();
    d.setHours(Number(m[1]), Number(m[2]), 0, 0);
    if (d.getTime() <= Date.now()) d.setDate(d.getDate() + 1);
    return d;
  }
  async function saveEnd(r: Recording, to: Date | null) {
    if (!to) { error = 'Orario non valido'; return; }
    busyId = r.id;
    try {
      const res = await api.changeRecordingEnd(r.id, to.toISOString());
      notice = { text: `«${r.title}» ora finisce alle ${time(to)}.`, warnings: res.warnings };
      endFor = null;
      await load();
    } catch (e) { error = e instanceof ApiError ? e.message : 'Fine non cambiata'; }
    finally { busyId = null; }
  }
  const later = (r: Recording, min: number) => new Date(new Date(r.recordTo).getTime() + min * 60_000);

  async function cancel(r: Recording) {
    if (!(await ask({ title: `Annullare «${r.title}»?`, text: `Non verrà registrata ${day(r.start, now).toLowerCase()} alle ${time(r.start)}.`, ok: 'Annulla la registrazione', cancel: 'Tienila' }))) return;
    busyId = r.id;
    try { await api.cancelRecording(r.id); await load(); }
    catch (e) { error = e instanceof ApiError ? e.message : 'Non annullata'; }
    finally { busyId = null; }
  }

  function done(warnings: string[]) {
    notice = { text: editing ? 'Registrazione modificata.' : 'Registrazione programmata.', warnings };
    adding = false;
    editing = null;
    load();
  }
</script>

<!-- locandina dell'opera (se c'è) che apre la scheda; senza opera un riquadro vuoto tiene allineate le righe -->
{#snippet poster(r: Recording)}
  {#if r.workId != null}
    <a class="poster" href="/opera/{r.workId}" onclick={navigate} aria-label="Apri la scheda di {r.title}" title="Apri la scheda">
      {#if r.poster}<img src={r.poster} alt="" loading="lazy" />{:else}<span class="ph" aria-hidden="true">{r.title.charAt(0)}</span>{/if}
    </a>
  {:else}
    <span class="poster none" aria-hidden="true"></span>
  {/if}
{/snippet}

<section class="page">
  <div class="top">
    <h1>Registrazioni</h1>
    <Button variant="primary" onclick={() => { adding = true; notice = null; }}>Nuova registrazione</Button>
  </div>

  {#if notice}
    <div class="notice" role="status">
      <strong>{notice.text}</strong>
      {#each notice.warnings as w (w)}<span class="warn">Attenzione: {w}.</span>{/each}
    </div>
  {/if}
  {#if error}<p class="error" role="alert">{error}</p>{/if}

  <div class="filters" role="group" aria-label="Mostra">
    {#each filters as f (f.id)}
      <button type="button" class="chip" class:on={filter === f.id} aria-pressed={filter === f.id} onclick={() => (filter = f.id)}>
        {f.label}<span class="n">{count(f.id)}</span>
      </button>
    {/each}
  </div>

  {#if !loaded}
    <p class="muted" role="status">Un momento…</p>
  {:else if all.length === 0}
    <div class="empty">
      <p>Nessuna registrazione, per ora.</p>
      <p class="muted">Programmane una con «Nuova registrazione», oppure con «Registra» su un programma della guida o un titolo di La mia serata.</p>
    </div>
  {:else}
    {#if (filter === 'tutte' || filter === 'active') && active.length > 0}
      <h2>In corso</h2>
      {#each active as r (r.id)}
        <div class="live">
          {#if r.state === 'recording'}<div class="live-prev"><LivePreview channelId={r.channelId} label={r.channelName} /></div>{/if}
          <div class="live-body">
            <div class="meta">{r.channelName} · {range(r.start, r.stop)} · con margini {range(r.recordFrom, r.recordTo)}</div>
            <div class="live-title">{r.title}</div>
            {#if r.state === 'recording'}
              <div class="bar" role="progressbar" aria-label="Avanzamento" aria-valuenow={progress(r)} aria-valuemin="0" aria-valuemax="100"><span style:width="{progress(r)}%"></span></div>
              <div class="meta">Mancano {minutes(Math.max(0, between(now.toISOString(), r.recordTo)))}</div>
              <div class="live-actions">
                <Button variant="primary" disabled={busyId === r.id} onclick={() => stop(r)}>Ferma ora</Button>
                <Button disabled={busyId === r.id} onclick={() => (endFor === r.id ? (endFor = null) : openEnd(r))}>Cambia la fine</Button>
              </div>
              {#if endFor === r.id}
                <div class="end-edit">
                  <label>Registra fino alle <input type="time" bind:value={endTime} /></label>
                  <Button variant="primary" disabled={busyId === r.id} onclick={() => saveEnd(r, endFromTime(endTime))}>Salva</Button>
                  <span class="muted small">oppure</span>
                  {#each [15, 30, 60] as m (m)}
                    <Button disabled={busyId === r.id} onclick={() => saveEnd(r, later(r, m))}>+{m === 60 ? '1 ora' : `${m} min`}</Button>
                  {/each}
                </div>
              {/if}
            {:else}
              <div class="meta">Unione degli spezzoni: il file sarà pronto tra poco.</div>
            {/if}
          </div>
        </div>
      {/each}
    {/if}

    {#if (filter === 'tutte' || filter === 'scheduled') && scheduled.length > 0}
      <h2>In programma <span class="sub">il flusso si sceglie 2 minuti prima dell'inizio</span></h2>
      <ul class="list">
        {#each scheduled as r (r.id)}
          <li>
            {@render poster(r)}
            <div class="when"><span>{day(r.start, now)}</span><span class="muted">{range(r.start, r.stop)}</span></div>
            <div class="what">
              <div class="line"><span class="title">{r.title}</span><Badge tone="blue">In programma</Badge></div>
              <div class="meta">{meta(r)}</div>
            </div>
            <!-- matita e cestino al posto delle scritte (Antonio, 23/11/2026); il cestino chiede conferma -->
            <div class="acts">
              <button type="button" class="ico" disabled={busyId === r.id} onclick={() => { editing = r; notice = null; }} aria-label="Modifica «{r.title}»" title="Modifica">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 20h9" /><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4z" /></svg>
              </button>
              <button type="button" class="ico del" disabled={busyId === r.id} onclick={() => cancel(r)} aria-label="Annulla «{r.title}»" title="Annulla la registrazione">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 6h18" /><path d="M8 6V4h8v2" /><path d="M19 6l-1 14H6L5 6" /><path d="M10 11v6M14 11v6" /></svg>
              </button>
            </div>
          </li>
        {/each}
      </ul>
    {/if}

    {#if filter === 'active' && active.length === 0}
      <p class="muted">Nessuna registrazione in corso.</p>
    {/if}

    {#if filter !== 'scheduled' && filter !== 'active' && shownFinished.length > 0}
      <h2>{filter === 'tutte' ? 'Finite' : filters.find((f) => f.id === filter)?.label}</h2>
      <ul class="list">
        {#each shownFinished as r (r.id)}
          {@const s = states[r.state]}
          <li class={r.state}>
            {@render poster(r)}
            <div class="when"><span>{day(r.start, now)}</span><span class="muted">{range(r.start, r.stop)}</span></div>
            <div class="what">
              <div class="line"><span class="title">{r.title}</span>{#if s}<Badge tone={s.tone}>{s.label}{r.state === 'incomplete' ? ` −${minutes(r.lostMinutes)}` : ''}</Badge>{/if}</div>
              <div class="meta">{meta(r)}</div>
              {#if note(r)}<div class="note">{note(r)}</div>{/if}
            </div>
            {#if r.libraryItemId != null || (r.file && ['completed', 'incomplete', 'stopped'].includes(r.state) && canConvert(r.file))}
              <div class="acts">
                <!-- il player è quello della Libreria: la registrazione finita vi entra da sola -->
                {#if r.libraryItemId != null}
                  <Button size="s" variant="primary" href="/guarda/{r.libraryItemId}" onclick={navigate} label="{r.format === 'mp3' ? 'Ascolta' : 'Guarda'} «{r.title}»">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M7 4v16l13-8z" /></svg>{r.format === 'mp3' ? 'Ascolta' : 'Guarda'}
                  </Button>
                {/if}
                {#if r.file && ['completed', 'incomplete', 'stopped'].includes(r.state) && canConvert(r.file)}
                  <Button size="s" variant="secondary" onclick={() => openConvert({ recordingId: r.id, name: fileName(r.file) })}>Converti...</Button>
                {/if}
              </div>
            {/if}
          </li>
        {/each}
      </ul>
    {:else if filter !== 'scheduled' && filter !== 'tutte'}
      <p class="muted">Nessuna registrazione in questo elenco.</p>
    {/if}
  {/if}
</section>

{#if adding}
  <NuovaRegistrazione onclose={() => (adding = false)} ondone={done} />
{:else if editing}
  {#key editing.id}<NuovaRegistrazione edit={editing} onclose={() => (editing = null)} ondone={done} />{/key}
{/if}

<style>
  .ico {
    display: inline-flex; align-items: center; justify-content: center; width: 40px; height: 40px; border-radius: var(--radius-s);
    border: 1px solid var(--line-btn); background: none; color: var(--text-2); cursor: pointer;
  }
  .ico:hover { color: var(--text); background: var(--raised); }
  .ico.del:hover { color: var(--rec-text); border-color: var(--rec); }
  .ico:disabled { opacity: 0.5; cursor: progress; }
  .page { padding: 28px var(--gutter) 64px; max-width: 1240px; display: flex; flex-direction: column; gap: 18px; }
  .top { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px; }
  h1 { font-size: 32px; }
  h2 { font-size: 20px; margin-top: 8px; }
  .sub { font-family: var(--font); font-size: 14px; font-weight: 500; color: var(--muted); }
  .muted { color: var(--muted); }
  .error { color: var(--rec-text); margin: 0; }
  .notice { display: flex; flex-direction: column; gap: 4px; background: var(--card); border: 1px solid var(--line-strong); border-radius: 12px; padding: 12px 16px; }
  .warn { color: var(--orange-text); font-size: 14px; }
  .filters { display: flex; flex-wrap: wrap; gap: 8px; }
  .chip {
    display: inline-flex; align-items: center; gap: 8px; min-height: 40px; padding: 0 16px; border-radius: 999px;
    border: 1px solid var(--line-strong); background: none; color: var(--text-2); font-weight: 600; font-size: 14px; cursor: pointer;
  }
  .chip.on { background: var(--text); color: var(--bg); border-color: transparent; }
  .n { font-size: 12px; font-weight: 700; border-radius: 999px; padding: 1px 7px; background: var(--raised); }
  .chip.on .n { background: rgba(13, 16, 21, 0.12); }
  .empty { background: var(--surface); border: 1px dashed var(--line-btn); border-radius: var(--radius-l); padding: 24px; }
  .empty p { margin: 0 0 6px; }
  .live { display: flex; flex-wrap: wrap; gap: 20px; background: var(--card); border: 1px solid #3a2a2e; border-radius: var(--radius-l); padding: 16px; }
  .live-body { flex: 1 1 380px; display: flex; flex-direction: column; gap: 10px; }
  .live-actions, .end-edit { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
  .end-edit input { margin-left: 6px; }
  .live-title { font-family: var(--font-display); font-size: 24px; font-weight: 700; }
  .bar { height: 8px; border-radius: 999px; background: #262c37; overflow: hidden; }
  .bar span { display: block; height: 100%; background: var(--rec); }
  .list { list-style: none; margin: 0; padding: 0; background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius-l); overflow: hidden; }
  .list li { display: flex; flex-wrap: wrap; align-items: center; gap: 12px 20px; padding: 14px 18px; border-bottom: 1px solid var(--line-soft); }
  .list li:last-child { border-bottom: 0; }
  .list li.incomplete { box-shadow: inset 3px 0 0 var(--orange); }
  .list li.failed { box-shadow: inset 3px 0 0 var(--rec); }
  .when { flex: 0 0 120px; display: flex; flex-direction: column; gap: 2px; font-weight: 600; }
  .when .muted { font-weight: 400; font-size: 13px; }
  .what { flex: 1 1 340px; min-width: 0; display: flex; flex-direction: column; gap: 4px; }
  .line { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; }
  .title { font-size: 16px; font-weight: 600; }
  .meta { font-size: 13px; color: var(--muted); overflow-wrap: anywhere; }
  .note { font-size: 13px; color: var(--orange-text); }
  .failed .note { color: var(--rec-text); }
  .acts { display: flex; gap: 8px; align-items: center; }
  .poster {
    flex: 0 0 48px; width: 48px; height: 72px; border-radius: 6px; overflow: hidden; background: var(--raised);
    display: flex; align-items: center; justify-content: center; text-decoration: none;
  }
  .poster.none { background: none; }
  a.poster:hover { outline: 2px solid var(--blue); outline-offset: 2px; }
  .poster img { width: 100%; height: 100%; object-fit: cover; display: block; }
  .ph { font-family: var(--font-display); font-size: 22px; font-weight: 700; color: var(--muted); }
  .live-prev { flex: 0 0 260px; max-width: 100%; }
</style>
