<script lang="ts" module>
  // il testo cercato resta tornando indietro dalla Scheda (per la durata della pagina)
  let lastText = '';
</script>

<script lang="ts">
  // Cerca (la lente in alto): qualunque film o serie per titolo. Prima i titoli in TV e in casa, poi quelli di TMDB,
  // anche solo in streaming (es. un originale Prime Video): un tocco apre la Scheda con «Dove lo vedo» e gli avvisi.
  import PosterImg from '../lib/components/PosterImg.svelte';
  import AvailBadges from '../lib/components/AvailBadges.svelte';
  import AvailLegend from '../lib/components/AvailLegend.svelte';
  import { availOf } from '../lib/avail';
  import { api, ApiError, type SearchHit } from '../lib/api/client';
  import { day, time, vote } from '../lib/format';
  import { router } from '../lib/router.svelte';
  import { session } from '../lib/session.svelte';

  // abbonamenti del profilo, per le icone delle locandine
  let mySubs = $state<string[]>([]);
  $effect(() => {
    const p = session.profile;
    if (p) api.profileSubscriptions(p.id).then((s) => (mySubs = s.services)).catch(() => {});
  });
  const availFor = (h: SearchHit) => ({
    ...availOf(h, mySubs),
    tv: h.nextAiring ? `${day(h.nextAiring).toLowerCase()} ${time(h.nextAiring)}` : '',
  });

  let text = $state(lastText);
  let hits = $state<SearchHit[]>([]);
  let note = $state('');
  let error = $state('');
  let loading = $state(false);
  let searched = $state('');
  let opening = $state<string | null>(null);
  let input: HTMLInputElement | undefined = $state();

  $effect(() => { input?.focus(); });

  // cerca 350 ms dopo l'ultima lettera; una risposta vecchia non copre quella nuova
  let seq = 0;
  $effect(() => {
    const q = text.trim();
    lastText = text;
    if (q.length < 2) { hits = []; note = ''; error = ''; searched = ''; loading = false; return; }
    const my = ++seq;
    loading = true;
    const t = setTimeout(async () => {
      try {
        const r = await api.search(q, session.profile?.id);
        if (my !== seq) return;
        hits = r.hits; note = r.tmdbNote; error = ''; searched = q;
      } catch (e) {
        if (my === seq) error = e instanceof ApiError ? e.message : 'Ricerca non riuscita';
      } finally {
        if (my === seq) loading = false;
      }
    }, 350);
    return () => clearTimeout(t);
  });

  const kindLabel: Record<string, string> = { film: 'Film', series: 'Serie TV', documentary: 'Documentario', programme: 'Programma' };
  const keyOf = (h: SearchHit) => (h.workId != null ? `w${h.workId}` : `t${h.tmdbKind}${h.tmdbId}`);

  async function open(h: SearchHit) {
    if (h.workId != null) { router.go(`/opera/${h.workId}`); return; }
    if (h.tmdbId == null) return;
    opening = keyOf(h);
    try {
      const r = await api.workFromTmdb({
        tmdbId: h.tmdbId, kind: h.tmdbKind, title: h.title, originalTitle: h.originalTitle || h.title, year: h.year,
        voteAverage: h.voteAverage, voteCount: h.voteCount, genreIds: h.genreIds,
      });
      router.go(`/opera/${r.workId}`);
    } catch (e) {
      error = e instanceof ApiError ? e.message : 'Scheda non aperta';
    } finally {
      opening = null;
    }
  }

  function status(h: SearchHit): string {
    if (h.nextAiring) return `In TV ${day(h.nextAiring).toLowerCase()} ${time(h.nextAiring)}`;
    if (h.inHouse) return 'In casa';
    return '';
  }
</script>

<div class="wrap">
  <form class="box" role="search" onsubmit={(e) => e.preventDefault()}>
    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" aria-hidden="true"><circle cx="11" cy="11" r="7" /><path d="M20 20l-4-4" /></svg>
    <input bind:this={input} bind:value={text} type="search" placeholder="Cerca un film o una serie" aria-label="Cerca un film o una serie" autocomplete="off" maxlength="100" />
    {#if text}<button type="button" class="clear" aria-label="Cancella" onclick={() => { text = ''; input?.focus(); }}>✕</button>{/if}
  </form>

  {#if error}<p class="error" role="alert">{error}</p>{/if}
  {#if text.trim().length < 2}
    <p class="muted">Scrivi il titolo: trovi quello che passa in TV, i film di casa e qualunque film o serie in streaming, anche quelli che in TV non passano.</p>
  {:else if loading && hits.length === 0}
    <p class="muted" role="status">Cerco…</p>
  {:else if searched && hits.length === 0}
    <p class="muted" role="status">Nessun titolo trovato per «{searched}».</p>
  {/if}

  {#if hits.length > 0}
    <AvailLegend />
    <ul class="grid" aria-busy={loading}>
      {#each hits as h (keyOf(h))}
        <li>
          <button type="button" class="card" onclick={() => open(h)} disabled={opening != null}>
            <span class="poster">
              <PosterImg src={h.poster} title={h.title} />
              {#if h.rating != null}<span class="stars">★ {vote(h.rating)}</span>{/if}
              <AvailBadges a={availFor(h)} />
              {#if opening === keyOf(h)}<span class="wait">Apro…</span>{/if}
            </span>
            <span class="t">{h.title}</span>
            <span class="m">{[kindLabel[h.kind] ?? '', h.year || ''].filter(Boolean).join(' · ')}</span>
            {#if status(h)}<span class="m">{status(h)}</span>{/if}
          </button>
        </li>
      {/each}
    </ul>
  {/if}
  {#if note && text.trim().length >= 2}<p class="muted small">{note}</p>{/if}
  {#if hits.length > 0}<p class="muted small">Titoli e locandine da TMDB. Apri un titolo per vedere dove si vede, aggiungerlo alla tua serata o farti avvisare.</p>{/if}
</div>

<style>
  .wrap { padding: 28px var(--gutter) 64px; max-width: 1340px; display: flex; flex-direction: column; gap: 18px; }
  .box {
    display: flex; align-items: center; gap: 10px; max-width: 720px; min-height: 52px; padding: 0 8px 0 16px;
    border-radius: 26px; border: 1px solid var(--line-btn); background: var(--card); color: var(--muted);
  }
  /* un solo bordo: l'anello di focus sta sulla casella intera, non anche sul campo di testo dentro */
  .box:focus-within { border-color: var(--blue); }
  .box:has(input:focus-visible) { box-shadow: var(--focus); }
  .box input { flex: 1 1 auto; min-width: 0; border: 0; background: none; color: var(--text); font: inherit; font-size: 18px; outline: none; }
  .box input:focus-visible { box-shadow: none; }
  .box input::-webkit-search-cancel-button { display: none; }
  .clear { width: 36px; height: 36px; border: 0; border-radius: 18px; background: none; color: var(--muted); font-size: 15px; cursor: pointer; }
  .clear:hover { background: var(--raised); color: var(--text); }
  .muted { margin: 0; color: var(--muted); }
  .small { font-size: 13px; }
  .error { margin: 0; color: var(--rec-text); }
  .grid { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(160px, 1fr)); gap: 20px 16px; align-items: start; }
  .grid[aria-busy='true'] { opacity: 0.6; }
  .card { width: 100%; display: flex; flex-direction: column; gap: 2px; padding: 0; border: 0; background: none; color: var(--text); text-align: left; cursor: pointer; }
  .card:disabled { cursor: progress; }
  .poster { position: relative; aspect-ratio: 2 / 3; border-radius: 12px; background: #2b3d63; overflow: hidden; display: block; margin-bottom: 6px; }
  .card:hover .poster, .card:focus-visible .poster { box-shadow: 0 0 0 2px var(--text); }
  .stars { position: absolute; top: 8px; left: 8px; background: rgba(13, 16, 21, 0.8); border-radius: 6px; padding: 2px 7px; font-size: 12px; font-weight: 700; }
  .wait { position: absolute; inset: 0; display: flex; align-items: center; justify-content: center; background: rgba(13, 16, 21, 0.6); font-weight: 600; }
  .t { font-weight: 600; font-size: 15px; line-height: 1.3; overflow: hidden; display: -webkit-box; -webkit-line-clamp: 2; line-clamp: 2; -webkit-box-orient: vertical; }
  .m { font-size: 13px; color: var(--muted); }
  @media (max-width: 720px) { .grid { grid-template-columns: repeat(auto-fill, minmax(130px, 1fr)); } .box input { font-size: 16px; } }
</style>
