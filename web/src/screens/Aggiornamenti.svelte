<script lang="ts">
  // Impostazioni › Aggiornamenti (19/11/2026): versione installata, ultima versione pubblicata sul sito, controllo
  // automatico una volta al giorno (si può spegnere) e «Controlla ora». DoveLoFanno avvisa e basta: l'aggiornamento lo
  // fa chi amministra il server (nuova immagine Docker o nuovo pacchetto Windows).
  import Badge from '../lib/components/Badge.svelte';
  import Button from '../lib/components/Button.svelte';
  import { api, ApiError, type UpdateStatus } from '../lib/api/client';
  import { day, time } from '../lib/format';

  let s = $state<UpdateStatus | null>(null);
  let error = $state('');
  let message = $state('');
  let busy = $state(false);

  async function run(f: () => Promise<UpdateStatus>, ok: (r: UpdateStatus) => string) {
    busy = true;
    error = '';
    message = '';
    try {
      s = await f();
      message = ok(s);
    } catch (e) {
      error = e instanceof ApiError ? e.message : 'Non riuscito';
    } finally {
      busy = false;
    }
  }
  run(() => api.updates(), () => '');

  function checkNow() {
    run(() => api.checkUpdates(), (r) => (r.error ? '' : r.updateAvailable ? '' : 'Hai già l’ultima versione.'));
  }

  function dateIt(d: string) {
    const [y, m, g] = d.split('-');
    return `${Number(g)}/${Number(m)}/${y}`;
  }
</script>

<div class="head"><h2>Aggiornamenti</h2></div>
<p class="muted">Una volta al giorno DoveLoFanno guarda sul sito se è uscita una versione nuova e, se c'è, lo dice con una notifica. Non scarica e non installa niente da solo, e alla richiesta non aggiunge nessun dato tuo.</p>

{#if error}<p class="error" role="alert">{error}</p>{/if}
{#if message}<p class="ok" role="status">{message}</p>{/if}

{#if s}
  {#if s.updateAvailable && s.latest}
    <div class="card new">
      <div class="line"><strong class="big">È uscita la versione {s.latest}</strong><Badge tone="green">Nuova</Badge></div>
      {#if s.notes}<p class="notes">{s.notes}</p>{/if}
      {#if s.date}<p class="muted small">Pubblicata il {dateIt(s.date)}</p>{/if}
      {#if s.url}<div><a class="cta" href={s.url} target="_blank" rel="noopener noreferrer">Scarica e istruzioni ↗</a></div>{/if}
      <p class="muted small">Dati e registrazioni restano: il database si aggiorna da solo al primo avvio, dopo averne fatto una copia.</p>
    </div>
  {/if}

  <div class="card">
    <div class="row"><span class="label">Versione installata</span><span class="value">{s.current}</span></div>
    <div class="row">
      <span class="label">Ultima pubblicata</span>
      <span class="value">{s.latest ?? (s.checkedAt ? 'non nota' : 'non ancora controllata')}{#if s.latest && !s.updateAvailable} · sei aggiornato{/if}</span>
    </div>
    <div class="row">
      <span class="label">Ultimo controllo</span>
      <span class="value">{s.checkedAt ? `${day(s.checkedAt)} alle ${time(s.checkedAt)}` : 'mai'}</span>
    </div>
    {#if s.error}<div class="row"><span class="label">Ultimo errore</span><span class="value err">{s.error}</span></div>{/if}
    <div><Button disabled={busy} onclick={checkNow}>{busy ? 'Controllo…' : 'Controlla ora'}</Button></div>
  </div>

  <div class="card">
    <label class="toggle">
      <input type="checkbox" checked={s.enabled} disabled={busy}
        onchange={(e) => { const on = (e.currentTarget as HTMLInputElement).checked; run(() => api.setUpdateCheck(on), () => (on ? 'Controllo automatico acceso.' : 'Controllo automatico spento.')); }} />
      <span>
        <strong>Controlla ogni giorno</strong>
        <span class="muted small">Spento, DoveLoFanno non si collega al sito: le versioni nuove le scopri solo con «Controlla ora».</span>
      </span>
    </label>
  </div>
{/if}

<style>
  .head { display: flex; align-items: baseline; gap: 12px; }
  h2 { font-size: 24px; }
  .muted { color: var(--muted); margin: 0; }
  .small { font-size: 13px; }
  .error { color: var(--rec-text); margin: 0; }
  .ok { color: var(--green-text); margin: 0; }
  .card { background: var(--surface); border: 1px solid var(--line-strong); border-radius: 12px; padding: 16px 18px; display: flex; flex-direction: column; gap: 12px; }
  .card.new { border-color: var(--green); background: var(--green-soft); }
  .line { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; }
  .big { font-size: 18px; }
  .notes { margin: 0; color: var(--text-2); white-space: pre-line; }
  .cta { display: inline-flex; align-items: center; min-height: 40px; padding: 0 16px; border-radius: var(--radius-s); background: var(--text); color: var(--bg); font-weight: 700; text-decoration: none; }
  .cta:hover { background: #ffffff; color: var(--bg); }
  .row { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 20px; }
  .label { flex: 0 0 160px; font-weight: 600; }
  .value { flex: 1 1 240px; color: var(--text-2); overflow-wrap: anywhere; }
  .err { color: var(--rec-text); }
  .toggle { display: flex; gap: 12px; align-items: flex-start; cursor: pointer; }
  .toggle input { width: 20px; height: 20px; margin-top: 2px; flex: 0 0 20px; }
  .toggle span { display: flex; flex-direction: column; gap: 4px; }
</style>
