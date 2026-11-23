<script lang="ts">
  // La finestra di Registra (lib/series.svelte.ts). Prima la conferma (23/11/2026): cosa si registra, con quali minuti
  // prima/dopo e formato (riga compressa, «Modifica» apre i campi) e, per serie e programmi, «solo questa puntata» o
  // «tutta la serie» con il conto delle puntate prima di questa. Poi, con la serie, l'esito: le puntate trovate in
  // guida (programmate, già in programma, repliche saltate...), quelle che mancano e la promessa che le prossime si
  // programmano da sole quando la guida le riporta.
  import Button from './Button.svelte';
  import { seriesState, canBeSeries } from '../series.svelte';
  import { api, type GeneralSettings, type SeriesPreview } from '../api/client';
  import { day, range, episodeOf } from '../format';
  import { router } from '../router.svelte';

  const a = $derived(seriesState.asking);

  // impostazioni predefinite, lette una volta (servono a ogni Registra)
  let defaults = $state<GeneralSettings | null>(null);
  let how = $state<'one' | 'all'>('one');
  let editing = $state(false);
  let before = $state(''), after = $state(''), format = $state('mp4');
  let preview = $state<SeriesPreview | null>(null);
  let askErr = $state('');
  const formats: Record<string, string> = { mp4: 'Video (MP4)', mp3: 'Solo audio (MP3)' };
  $effect(() => {
    const cur = a;
    if (!cur) return;
    how = 'one'; editing = false; preview = null; askErr = '';
    const fill = (d: GeneralSettings) => { before = String(d.beforeMin); after = String(d.afterMin); format = d.format; };
    if (defaults) fill(defaults);
    else api.generalSettings().then((d) => { defaults = d; if (seriesState.asking === cur) fill(d); }).catch(() => { /* restano le predefinite del server */ });
    if (canBeSeries(cur.p)) api.seriesPreview(cur.p.id).then((v) => { if (seriesState.asking === cur) preview = v; }).catch(() => { preview = null; });
  });
  const started = $derived(a ? new Date(a.p.start).getTime() < Date.now() : false);
  const series = $derived(a ? canBeSeries(a.p) : false);
  const num = (s: string) => { const n = Number(s); return Number.isInteger(n) && n >= 0 && n <= 120 ? n : null; };
  const valid = $derived(num(before) != null && num(after) != null);
  const summary = (b: number | string, f: number | string, fmt: string) => `${b} min prima · ${f} min dopo · ${formats[fmt] ?? fmt}`;
  function confirm() {
    if (!a) return;
    if (how === 'all') { seriesState.answer({ how: 'all' }); return; }
    if (!valid) { askErr = 'Minuti tra 0 e 120.'; return; }
    // si manda solo quello che cambia rispetto alle predefinite (null = predefinita del server)
    const b = num(before)!, f = num(after)!;
    seriesState.answer({ how: 'one', opts: {
      beforeMin: defaults && b === defaults.beforeMin ? null : b,
      afterMin: defaults && f === defaults.afterMin ? null : f,
      format: defaults && format === defaults.format ? null : format,
    } });
  }
  function focusOk(node: HTMLElement) { queueMicrotask(() => node.focus()); }
  const r = $derived(seriesState.result);
  const open = $derived(!!a || !!r || seriesState.busy || !!seriesState.error);

  const statusText: Record<string, string> = {
    scheduled: 'Programmata ora', pending: 'Già in programma', replica: 'Replica: saltata', done: 'Già registrata',
    not_recordable: 'Canale non nelle tue playlist', limit: 'Oltre il limite di registrazioni contemporanee',
  };

  function close() {
    if (a) seriesState.answer(null);
    else seriesState.close();
  }
  function dialog(node: HTMLDialogElement) {
    node.showModal();
    node.addEventListener('cancel', (e) => { e.preventDefault(); close(); });
    node.addEventListener('click', (e) => { if (e.target === node) close(); });
  }
  const channelOf = (p: { channelName: string; guideChannelName: string }) => p.channelName || p.guideChannelName;
  const shown = $derived(r ? r.episodes.filter((e) => e.status !== 'replica') : []);
  const replicas = $derived(r ? r.episodes.filter((e) => e.status === 'replica').length : 0);
</script>

{#if open}
  <dialog use:dialog aria-labelledby="sr-title">
    <div class="box">
      {#if a}
        <form class="ask" onsubmit={(e) => { e.preventDefault(); confirm(); }}>
          <h2 id="sr-title">Registrare «{a.p.title}»?</h2>
          <div class="prog">
            {#if episodeOf(a.p)}<div class="ep">{episodeOf(a.p)}</div>{/if}
            <div class="slot">{day(a.p.start)} · {range(a.p.start, a.p.stop)} · {channelOf(a.p)}</div>
            {#if started}<div class="warn">È già iniziato: registro da adesso.</div>{/if}
          </div>

          {#if series}
            <fieldset class="choice">
              <legend class="sr-only">Cosa registrare</legend>
              <label class:on={how === 'one'}><input type="radio" bind:group={how} value="one" />
                <span><b>Solo questa puntata</b>{a.p.episode ? ` (${a.p.episode.trim()})` : ''}</span></label>
              <label class:on={how === 'all'}><input type="radio" bind:group={how} value="all" />
                <span><b>Tutta la serie</b>: le puntate in guida, una volta ciascuna (le repliche si saltano), e le prossime da sole</span></label>
            </fieldset>
            {#if preview}
              <ul class="facts">
                {#if preview.ruleActive}<li class="on">La serie è già in registrazione.</li>{/if}
                <li>In guida nei prossimi giorni: {preview.episodes === 1 ? '1 puntata' : `${preview.episodes} puntate`}{preview.recordable < preview.episodes ? `, ${preview.recordable} registrabili (le altre su canali non nelle tue playlist)` : ''}.</li>
                {#if preview.before > 0}
                  <li>Prima di questa ({preview.episode}) {preview.before === 1 ? 'è andata in onda 1 puntata' : `sono andate in onda ${preview.before} puntate`}:
                    {[preview.recorded ? `${preview.recorded} già registrat${preview.recorded === 1 ? 'a' : 'e'}` : '',
                      preview.recoverable ? `${preview.recoverable} recuperabil${preview.recoverable === 1 ? 'e' : 'i'} con le repliche in guida` : '',
                      preview.lost ? `${preview.lost} pers${preview.lost === 1 ? 'a' : 'e'}` : ''].filter(Boolean).join(', ')}.
                    {#if preview.recoverable > 0}<span class="hint">{how === 'all' ? 'Con «Tutta la serie» le recuperabili si registrano.' : 'Le recuperabili si registrano con «Tutta la serie».'}</span>{/if}
                  </li>
                {/if}
              </ul>
            {/if}
          {/if}

          {#if how === 'one'}
            {#if !editing}
              <div class="opts">
                <span>{summary(before, after, format)}</span>
                <button type="button" class="linkbtn" onclick={() => (editing = true)}>Modifica</button>
              </div>
            {:else}
              <div class="fields">
                <label>Minuti prima<input inputmode="numeric" maxlength="3" bind:value={before} /></label>
                <label>Minuti dopo<input inputmode="numeric" maxlength="3" bind:value={after} /></label>
                <label>Formato<select bind:value={format}>{#each Object.entries(formats) as [v, l] (v)}<option value={v}>{l}</option>{/each}</select></label>
              </div>
              <p class="note">Vale solo per questa registrazione. Le predefinite si cambiano in Impostazioni › Registrazioni.</p>
            {/if}
          {:else if defaults}
            <p class="note">Le puntate usano la registrazione predefinita ({summary(defaults.beforeMin, defaults.afterMin, defaults.format)}), che si cambia in Impostazioni › Registrazioni.</p>
          {/if}
          {#if askErr}<p class="err" role="alert">{askErr}</p>{/if}

          <div class="actions">
            <button type="button" class="ghost" onclick={() => seriesState.answer(null)}>Annulla</button>
            <button type="submit" class="okbtn" use:focusOk><span class="dot" aria-hidden="true"></span>{how === 'all' ? 'Registra la serie' : 'Registra'}</button>
          </div>
        </form>
      {:else if seriesState.busy}
        <h2 id="sr-title">Cerco le puntate in guida…</h2>
      {:else if seriesState.error}
        <h2 id="sr-title">Serie non programmata</h2>
        <p class="err" role="alert">{seriesState.error}</p>
        <div class="actions"><Button variant="primary" onclick={close}>Chiudi</Button></div>
      {:else if r}
        <h2 id="sr-title">«{r.rule.title}»: tutte le puntate</h2>
        <p>
          {r.newlyScheduled === 0 ? 'Nessuna puntata nuova da programmare adesso.' : r.newlyScheduled === 1 ? 'Programmata 1 puntata.' : `Programmate ${r.newlyScheduled} puntate.`}
          {replicas > 0 ? ` ${replicas === 1 ? 'Una replica saltata' : `${replicas} repliche saltate`}.` : ''}
        </p>
        {#if shown.length > 0}
          <ul class="eps">
            {#each shown as e (e.programme.id)}
              <li class:muted={e.status !== 'scheduled' && e.status !== 'pending'}>
                <span class="when">{day(e.programme.start)} {range(e.programme.start, e.programme.stop)}</span>
                <span class="what">{channelOf(e.programme)}{e.programme.episode ? ` · ${e.programme.episode}` : ''}{e.programme.subtitle ? ` · ${e.programme.subtitle}` : ''}</span>
                <span class="st" class:on={e.status === 'scheduled' || e.status === 'pending'}>{statusText[e.status] ?? e.status}</span>
              </li>
            {/each}
          </ul>
        {:else}
          <p>Per ora la guida non riporta puntate di questo titolo.</p>
        {/if}
        {#if r.missing.length > 0}
          <p class="warn">Nella guida mancano le puntate {r.missing.slice(0, 12).join(', ')}{r.missing.length > 12 ? ` e altre ${r.missing.length - 12}` : ''}.</p>
        {/if}
        <p class="note">Le registrazioni delle prossime puntate si attiveranno automaticamente sulla base delle programmazioni
          rilevate dalla guida (che arriva a 4-5 giorni). Arriva una notifica ogni volta. La serie si ferma da Impostazioni › Registrazioni.</p>
        <div class="actions">
          <button type="button" class="ghost" onclick={() => { seriesState.close(); router.go('/registrazioni'); }}>Vedi in Registrazioni</button>
          <Button variant="primary" onclick={close}>Chiudi</Button>
        </div>
      {/if}
    </div>
  </dialog>
{/if}

<style>
  dialog {
    width: min(620px, calc(100vw - 32px)); padding: 0; border: 1px solid var(--line-strong);
    border-radius: var(--radius-l); background: var(--surface); color: var(--text);
  }
  dialog::backdrop { background: rgba(5, 7, 10, 0.7); }
  .box { display: flex; flex-direction: column; gap: 12px; padding: 22px 24px; max-height: calc(100vh - 64px); overflow: auto; }
  h2 { font-size: 20px; margin: 0; }
  p { margin: 0; font-size: 15px; line-height: 1.5; color: var(--text-2); }
  .note { font-size: 14px; color: var(--muted); }
  .warn { color: #f5b800; }
  .err { color: var(--rec); }
  .actions { display: flex; justify-content: flex-end; flex-wrap: wrap; gap: 10px; margin-top: 6px; }
  .ghost { min-height: 44px; padding: 0 18px; border-radius: var(--radius); border: 1px solid var(--line-btn); background: none; color: var(--text); font: inherit; font-size: 15px; font-weight: 600; cursor: pointer; }
  .ghost:hover { background: var(--raised); }
  .eps { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 6px; }
  .eps li { display: flex; flex-wrap: wrap; gap: 4px 12px; align-items: baseline; padding: 8px 12px; border-radius: var(--radius-s); background: var(--card); font-size: 14px; }
  .eps li.muted { opacity: 0.65; }
  .when { font-weight: 600; min-width: 170px; }
  .what { flex: 1 1 200px; color: var(--text-2); }
  .st { font-size: 13px; color: var(--muted); }
  .st.on { color: var(--text); }
  /* conferma di Registra (23/11/2026) */
  .ask { display: flex; flex-direction: column; gap: 14px; }
  .prog { display: flex; flex-direction: column; gap: 2px; padding: 10px 14px; border-radius: var(--radius-s); background: var(--card); }
  .ep { font-weight: 700; }
  .slot { color: var(--text-2); font-size: 14px; }
  .prog .warn { font-size: 14px; }
  .choice { border: 0; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 8px; }
  .choice label { display: flex; gap: 10px; align-items: flex-start; padding: 10px 12px; border: 1px solid var(--line); border-radius: var(--radius-s); cursor: pointer; font-size: 15px; color: var(--text-2); }
  .choice label.on { border-color: var(--text); color: var(--text); }
  .choice input { margin-top: 4px; accent-color: var(--text); }
  .facts { margin: 0; padding-left: 18px; display: flex; flex-direction: column; gap: 4px; font-size: 14px; color: var(--text-2); }
  .facts .on { color: var(--text); }
  .hint { color: var(--muted); }
  .opts { display: flex; flex-wrap: wrap; align-items: center; gap: 4px 14px; font-size: 15px; }
  .linkbtn { background: none; border: 0; padding: 0; color: var(--link); font: inherit; cursor: pointer; text-decoration: underline; }
  .fields { display: flex; flex-wrap: wrap; gap: 12px; }
  .fields label { display: flex; flex-direction: column; gap: 6px; font-size: 14px; color: var(--text-2); }
  .fields input, .fields select { min-height: 44px; padding: 0 12px; border-radius: var(--radius-s); border: 1px solid var(--line-strong); background: var(--bg); color: var(--text); font: inherit; }
  .fields input { width: 110px; }
  /* come RecordButton: pallino rosso + testo, qui chiaro perché è il pulsante predefinito */
  .okbtn { display: inline-flex; align-items: center; gap: 10px; min-height: 44px; padding: 0 20px; border: 0; border-radius: var(--radius); background: var(--text); color: var(--bg); font: inherit; font-size: 15px; font-weight: 700; cursor: pointer; }
  .okbtn .dot { width: 12px; height: 12px; border-radius: 50%; background: var(--rec-dot); }
</style>
