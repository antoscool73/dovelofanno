<script lang="ts">
  // Abbinamento a mano di un file a un film o a una serie di TMDB, oppure solo con titolo e anno: per i file "Da
  // sistemare" e, dalla Scheda, per rifare un abbinamento sbagliato (title/startYear/startKind = l'opera di adesso).
  import Button from '../lib/components/Button.svelte';
  import { api, ApiError, type LibraryItem, type MatchCandidate } from '../lib/api/client';

  type Kind = 'film' | 'series' | 'documentary' | 'programme';
  let { itemId, hasTmdb, onclose, ondone, title = '', startYear = 0, startKind = null }: {
    itemId: number; hasTmdb: boolean; onclose: () => void; ondone: (it: LibraryItem) => void;
    title?: string; startYear?: number; startKind?: Kind | null;
  } = $props();

  let item = $state<LibraryItem | null>(null);
  let text = $state('');
  let year = $state('');
  // svelte-ignore state_referenced_locally
  let kind = $state<Kind>(startKind ?? 'film');
  let season = $state('');
  let episode = $state('');
  let results = $state<MatchCandidate[] | null>(null);
  let busy = $state(false);
  let error = $state('');

  $effect(() => {
    api.libraryItem(itemId).then((it) => {
      item = it;
      text = title || it.titleGuess || it.fileName.replace(/\.[^.]+$/, '');
      year = startYear ? String(startYear) : it.yearGuess ? String(it.yearGuess) : '';
      if (it.season != null && it.episode != null) {
        kind = 'series';
        season = String(it.season);
        episode = String(it.episode);
      }
    }).catch((e) => { error = e instanceof ApiError ? e.message : 'File non letto'; });
  });

  function dialog(node: HTMLDialogElement) {
    node.showModal();
    node.addEventListener('cancel', (e) => { e.preventDefault(); onclose(); });
  }

  const tv = $derived(kind === 'series');

  async function search(e: SubmitEvent) {
    e.preventDefault();
    if (!text.trim()) return;
    busy = true;
    error = '';
    try { results = await api.searchMatch(text.trim(), tv ? 'tv' : 'movie', Number(year) || undefined); }
    catch (err) { error = err instanceof ApiError ? err.message : 'Ricerca non riuscita'; }
    finally { busy = false; }
  }

  async function pick(c: MatchCandidate | null) {
    busy = true;
    error = '';
    try {
      const done = await api.matchItem(itemId, {
        title: c ? c.title : text.trim(),
        year: c ? c.year : Number(year) || 0,
        kind,
        tmdbId: c ? c.tmdbId : null,
        season: tv && season ? Number(season) : null,
        episode: tv && episode ? Number(episode) : null,
      });
      ondone(done);
    } catch (err) {
      error = err instanceof ApiError ? err.message : 'Abbinamento non salvato';
    } finally {
      busy = false;
    }
  }

  const kinds = [
    { id: 'film', label: 'Film' },
    { id: 'series', label: 'Serie' },
    { id: 'documentary', label: 'Documentario' },
    { id: 'programme', label: 'Programma' },
  ] as const;
</script>

<dialog use:dialog aria-labelledby="abbina-titolo">
  <form class="body" onsubmit={search}>
    <h2 id="abbina-titolo">Di che titolo si tratta?</h2>
    {#if item}<p class="muted small file">{item.fileName}</p>{/if}

    <div class="kinds" role="group" aria-label="Tipo">
      {#each kinds as k (k.id)}
        <button type="button" class="pill" class:on={kind === k.id} aria-pressed={kind === k.id} onclick={() => { kind = k.id; results = null; }}>{k.label}</button>
      {/each}
    </div>

    <div class="row">
      <label class="field grow">Titolo<input bind:value={text} required /></label>
      <label class="field y">Anno<input bind:value={year} inputmode="numeric" maxlength="4" placeholder="facoltativo" /></label>
    </div>
    {#if tv}
      <div class="row">
        <label class="field y">Stagione<input bind:value={season} inputmode="numeric" maxlength="2" /></label>
        <label class="field y">Episodio<input bind:value={episode} inputmode="numeric" maxlength="3" /></label>
      </div>
    {/if}

    <div class="actions">
      {#if hasTmdb}<Button variant="primary" type="submit" disabled={busy || !text.trim()}>{busy ? 'Cerco…' : 'Cerca su TMDB'}</Button>{/if}
      <Button disabled={busy || !text.trim()} onclick={() => pick(null)}>Usa solo titolo e anno</Button>
      <span class="grow"></span>
      <Button variant="ghost" onclick={onclose}>Annulla</Button>
    </div>
    {#if !hasTmdb}<p class="muted small">Con la chiave TMDB (Impostazioni › Voti dei film) si cerca il titolo e arrivano locandina, trama e voto.</p>{/if}
    {#if error}<p class="error" role="alert">{error}</p>{/if}

    {#if results}
      {#if results.length === 0}
        <p class="muted">Nessun risultato, nemmeno senza anno: prova con il titolo originale o con un tipo diverso (Film / Serie).</p>
      {:else}
        <ul class="res">
          {#each results as r (r.tmdbId)}
            <li>
              <span class="r-main"><strong>{r.title}</strong>{#if r.year}<span class="muted"> ({r.year})</span>{/if}
                {#if r.originalTitle && r.originalTitle !== r.title}<br /><span class="muted small">{r.originalTitle}</span>{/if}
                <br /><span class="muted small">{r.votes} voti su TMDB</span></span>
              <Button size="s" disabled={busy} onclick={() => pick(r)}>È questo</Button>
            </li>
          {/each}
        </ul>
      {/if}
    {/if}
  </form>
</dialog>

<style>
  dialog {
    width: min(640px, calc(100vw - 32px)); max-height: calc(100vh - 48px); padding: 0; border: 1px solid var(--line-strong);
    border-radius: var(--radius-l); background: var(--surface); color: var(--text); overflow: auto;
  }
  dialog::backdrop { background: rgba(5, 7, 10, 0.7); }
  .body { display: flex; flex-direction: column; gap: 14px; padding: 24px; }
  h2 { font-size: 24px; }
  .file { overflow-wrap: anywhere; }
  .kinds { display: flex; flex-wrap: wrap; gap: 6px; }
  .pill { min-height: 40px; padding: 0 16px; border-radius: 999px; border: 1px solid var(--line-strong); background: none; color: var(--text-2); font-size: 14px; cursor: pointer; }
  .pill.on { background: var(--text); color: var(--bg); border-color: var(--text); font-weight: 600; }
  .row { display: flex; flex-wrap: wrap; gap: 12px; }
  .grow { flex: 1 1 0; }
  .y { flex: 0 0 110px; }
  .field { display: flex; flex-direction: column; gap: 6px; font-size: 13px; color: var(--muted); }
  .field.grow { flex: 1 1 260px; }
  .field input { min-height: 42px; padding: 0 12px; border-radius: var(--radius-s); border: 1px solid var(--line-strong); background: var(--card); color: var(--text); font-size: 15px; }
  .actions { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; }
  .muted { margin: 0; color: var(--muted); }
  .small { font-size: 13px; }
  .error { margin: 0; color: var(--rec-text); }
  .res { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 8px; }
  .res li { display: flex; align-items: center; gap: 12px; background: var(--card); border: 1px solid var(--line-strong); border-radius: 10px; padding: 10px 14px; }
  .r-main { flex: 1 1 0; min-width: 0; overflow-wrap: anywhere; }
</style>
