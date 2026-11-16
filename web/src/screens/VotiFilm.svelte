<script lang="ts">
  // Impostazioni › Voti dei film: chiave TMDB dell'utente (mai mostrata intera), voti IMDb facoltativi.
  import Button from '../lib/components/Button.svelte';
  import { api, ApiError, type RatingSettings } from '../lib/api/client';

  let s = $state<RatingSettings | null>(null);
  let key = $state('');
  let error = $state('');
  let message = $state('');
  let busy = $state(false);

  async function load() {
    try { s = await api.ratingSettings(); }
    catch (e) { error = e instanceof ApiError ? e.message : 'Impostazioni non lette'; }
  }
  load();

  async function save(patch: { tmdbKey?: string; clearTmdbKey?: boolean; imdbEnabled?: boolean }, ok: string) {
    busy = true;
    error = '';
    message = '';
    try {
      s = await api.updateRatingSettings({ tmdbKey: patch.tmdbKey ?? null, clearTmdbKey: patch.clearTmdbKey ?? null, imdbEnabled: patch.imdbEnabled ?? null });
      key = '';
      message = ok;
    } catch (e) {
      error = e instanceof ApiError ? e.message : 'Non salvato';
    } finally {
      busy = false;
    }
  }

  function submitKey(e: SubmitEvent) {
    e.preventDefault();
    save({ tmdbKey: key.trim() }, 'Chiave provata con TMDB e salvata: i voti arrivano nei prossimi minuti.');
  }
</script>

<div class="head"><h2>Voti dei film</h2></div>
<p class="muted">Voti, locandine e trame arrivano da TMDB con una tua chiave personale gratuita: si crea in due minuti su themoviedb.org (Impostazioni › API). Senza chiave tutto funziona, ma senza voti.</p>

{#if error}<p class="error" role="alert">{error}</p>{/if}
{#if message}<p class="ok" role="status">{message}</p>{/if}

{#if s}
  <div class="card">
    <div class="row">
      <span class="label">Chiave TMDB</span>
      <span class="value">{s.hasTmdbKey ? `•••• •••• •••• ${s.keyHint} · salvata` : 'Nessuna'}</span>
      {#if s.hasTmdbKey}<Button size="s" variant="ghost" disabled={busy} onclick={() => save({ clearTmdbKey: true }, 'Chiave tolta.')}>Togli</Button>{/if}
    </div>
    <form class="keyform" onsubmit={submitKey}>
      <label class="field">{s.hasTmdbKey ? 'Nuova chiave' : 'Chiave'} (32 caratteri, oppure il token lungo)
        <input bind:value={key} type="password" autocomplete="off" spellcheck="false" />
      </label>
      <Button variant="primary" type="submit" disabled={busy || key.trim().length < 20}>{busy ? 'Prova in corso…' : 'Prova e salva'}</Button>
    </form>
    <a href="https://www.themoviedb.org/settings/api" target="_blank" rel="noopener noreferrer" class="small">Come si ottiene la chiave ↗</a>
  </div>

  <div class="card">
    <label class="toggle">
      <input type="checkbox" checked={s.imdbEnabled} disabled={busy} onchange={(e) => save({ imdbEnabled: (e.currentTarget as HTMLInputElement).checked }, 'Preferenza salvata.')} />
      <span>
        <strong>Usa i voti IMDb</strong>
        <span class="muted small">Dal file ufficiale di IMDb, scaricato una volta a settimana. La sua licenza vale solo per uso personale e non commerciale: attivalo solo per il server di casa. Spento, si mostra il voto TMDB.</span>
      </span>
    </label>
  </div>

  <div class="card">
    <div class="row"><span class="label">Stato</span><span class="value">{s.status || (s.hasTmdbKey ? 'In attesa del prossimo giro' : 'Manca la chiave TMDB')}</span></div>
    <div class="row"><span class="label">Film cercati</span><span class="value">{s.filmsSearched} · trovati {s.filmsFound}</span></div>
    {#if s.error}<div class="row"><span class="label">Ultimo errore</span><span class="value err">{s.error}</span></div>{/if}
  </div>

  <p class="muted small">Dati di film e serie da TMDB (The Movie Database). Questo prodotto usa l'API di TMDB ma non è approvato né certificato da TMDB. Disponibilità sulle piattaforme da JustWatch tramite TMDB.</p>
{/if}

<style>
  .head { display: flex; align-items: baseline; gap: 12px; }
  h2 { font-size: 24px; }
  .muted { color: var(--muted); margin: 0; }
  .small { font-size: 13px; }
  .error { color: var(--rec-text); margin: 0; }
  .ok { color: var(--green-text); margin: 0; }
  .card { background: var(--surface); border: 1px solid var(--line-strong); border-radius: 12px; padding: 16px 18px; display: flex; flex-direction: column; gap: 12px; }
  .row { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 20px; }
  .label { flex: 0 0 160px; font-weight: 600; }
  .value { flex: 1 1 240px; color: var(--text-2); overflow-wrap: anywhere; }
  .err { color: var(--rec-text); }
  .keyform { display: flex; flex-wrap: wrap; align-items: flex-end; gap: 12px; }
  .field { flex: 1 1 320px; display: flex; flex-direction: column; gap: 6px; font-size: 13px; color: var(--muted); }
  .field input { min-height: 42px; padding: 0 12px; border-radius: var(--radius-s); border: 1px solid var(--line-strong); background: var(--card); color: var(--text); font-size: 15px; }
  .toggle { display: flex; gap: 12px; align-items: flex-start; cursor: pointer; }
  .toggle input { width: 20px; height: 20px; margin-top: 2px; flex: 0 0 20px; }
  .toggle span { display: flex; flex-direction: column; gap: 4px; }
</style>
