<script lang="ts">
  // Impostazioni › Registrazioni (20/11/2026, richiesta di Antonio): tutto quello che riguarda le registrazioni in un
  // posto solo. Predefinite (margini e formato, prima in Preferenze), limiti (registrazioni contemporanee e spazio,
  // prima nella scheda Limiti), cartella e come vengono divise, le serie in registrazione («Registra la serie»).
  import { ask } from '../lib/confirm.svelte';
  import Button from '../lib/components/Button.svelte';
  import { api, ApiError, type GeneralSettings, type SeriesRule } from '../lib/api/client';
  import { day, time } from '../lib/format';

  let general = $state<GeneralSettings | null>(null);
  let rules = $state<SeriesRule[]>([]);
  let error = $state('');
  let message = $state('');
  let busy = $state(false);

  let before = $state(''), after = $state(''), format = $state('mp4'), maxParallel = $state(''), minFree = $state('');

  async function load() {
    try {
      [general, rules] = await Promise.all([api.generalSettings(), api.seriesRules()]);
      before = String(general.beforeMin);
      after = String(general.afterMin);
      format = general.format;
      maxParallel = String(general.maxParallel);
      minFree = String(general.minFreeGb);
      error = '';
    } catch (e) {
      error = e instanceof ApiError ? e.message : 'Impostazioni non lette';
    }
  }
  load();

  async function act(fn: () => Promise<unknown>, ok: string) {
    busy = true;
    message = '';
    try { await fn(); await load(); message = ok; error = ''; }
    catch (e) { error = e instanceof ApiError ? e.message : 'Non salvato'; }
    finally { busy = false; }
  }

  function save(e: SubmitEvent) {
    e.preventDefault();
    act(() => api.updateGeneralSettings({ beforeMin: Number(before), afterMin: Number(after), format, maxParallel: Number(maxParallel), minFreeGb: Number(minFree) }),
      'Salvato: vale per le prossime registrazioni.');
  }

  async function stop(r: SeriesRule) {
    if (!(await ask({ title: `Smettere di registrare «${r.title}»?`, text: r.scheduled > 0 ? `Le ${r.scheduled} puntate in programma si annullano; quelle già registrate restano.` : 'Le puntate già registrate restano.', ok: 'Smetti di registrare' }))) return;
    act(() => api.stopSeries(r.id), `«${r.title}»: non si registra più.`);
  }
  function check(r: SeriesRule) {
    act(async () => {
      const c = await api.checkSeries(r.id);
      message = c.newlyScheduled > 0 ? `«${r.title}»: programmate ${c.newlyScheduled} puntate nuove.` : `«${r.title}»: nessuna puntata nuova in guida.`;
    }, '');
  }
</script>

<div class="head"><h2>Registrazioni</h2></div>
{#if error}<p class="f-error" role="alert">{error}</p>{/if}
{#if message}<p class="f-ok" role="status">{message}</p>{/if}

{#if general}
  <form class="card" onsubmit={save}>
    <h3>Registrazione predefinita</h3>
    <p class="f-muted">Quello che usa il pulsante «Registra» in un tocco e la registrazione delle serie. Vale per tutta la famiglia.</p>
    <div class="f-row">
      <label class="f-field">Minuti prima<input bind:value={before} inputmode="numeric" size="4" /></label>
      <label class="f-field">Minuti dopo<input bind:value={after} inputmode="numeric" size="4" /></label>
      <label class="f-field">Formato
        <select bind:value={format}><option value="mp4">Video (MP4)</option><option value="mp3">Solo audio (MP3)</option></select>
      </label>
    </div>
    <h3>Limiti</h3>
    <p class="f-muted">Prima di programmare, DoveLoFanno avvisa se si supera uno di questi limiti (si può procedere lo stesso). Le serie e le proposte non programmano oltre il limite.</p>
    <div class="f-row">
      <label class="f-field">Registrazioni contemporanee al massimo (0 = nessun limite)<input bind:value={maxParallel} inputmode="numeric" size="4" /></label>
      <label class="f-field">Spazio libero da lasciare (GB)<input bind:value={minFree} inputmode="numeric" size="5" /></label>
    </div>
    <div><Button variant="primary" type="submit" disabled={busy}>Salva</Button></div>
  </form>

  <div class="card">
    <h3>Cartella</h3>
    <p class="f-muted">Le registrazioni vanno sul server in <code>{general.recordingsDir}</code>{general.freeGb != null ? ` (${general.freeGb} GB liberi)` : ''}, divise come le legge Jellyfin:
      <strong>Film</strong> (una cartella per film, con il .nfo), <strong>Serie TV</strong> (cartella della serie e stagione), <strong>Programmi</strong> (una cartella per titolo) e <strong>Audio</strong> (le registrazioni in MP3).
      Una registrazione occupa circa 2,5 GB all'ora. La cartella si sceglie all'installazione (variabile DoveLoFanno__RecordingsDir).</p>
  </div>
{/if}

<div class="card">
  <h3>Serie in registrazione</h3>
  <p class="f-muted">Con «Registra» su una puntata e «Tutte le puntate»: le puntate in guida si programmano subito, le prossime da sole quando la guida le riporta (controllo ogni mezz'ora), una volta per puntata; le repliche si saltano.</p>
  {#if rules.length === 0}
    <p class="f-muted">Nessuna serie. Premi «Registra» su una puntata di una serie o di un programma e scegli «Tutte le puntate».</p>
  {:else}
    <ul class="rules">
      {#each rules as r (r.id)}
        <li>
          <div class="grow">
            <div class="rt">{r.title}</div>
            <div class="f-muted small">
              {r.scheduled === 1 ? '1 puntata in programma' : `${r.scheduled} puntate in programma`} · {r.recorded === 1 ? '1 registrata' : `${r.recorded} registrate`}
              {#if r.nextStart} · prossima {day(r.nextStart).toLowerCase()} {time(r.nextStart)}{r.nextChannel ? ` su ${r.nextChannel}` : ''}{/if}
            </div>
          </div>
          <Button size="s" variant="secondary" disabled={busy} onclick={() => check(r)}>Cerca puntate nuove</Button>
          <Button size="s" disabled={busy} onclick={() => stop(r)}>Smetti di registrare</Button>
        </li>
      {/each}
    </ul>
  {/if}
</div>

<style>
  .card { display: flex; flex-direction: column; gap: 12px; background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius-l); padding: 18px 20px; }
  h3 { font-size: 17px; margin: 0; }
  .rules { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 8px; }
  .rules li { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 12px; padding: 10px 14px; border-radius: var(--radius-s); background: var(--card); }
  .grow { flex: 1 1 220px; min-width: 0; }
  .rt { font-weight: 600; }
  .small { font-size: 13px; }
  code { font-size: 13px; }
</style>
