<script lang="ts" module>
  import type { SearchQuery } from '../lib/api/client';
  // i filtri restano tornando indietro dalla Scheda (per la durata della pagina)
  let last: SearchQuery = { q: '', kind: 'all', genre: 0, yearFrom: 0, yearTo: 0, country: '', minRating: 0, avail: [] };
</script>

<script lang="ts">
  // Cerca (la lente in alto): l'unica ricerca dell'app, quella avanzata del prototipo. Titolo, regista o attore e filtri
  // facoltativi (tipo, genere, anni, paese, voto minimo, disponibilità); risultati con dove si vede; a destra gli avvisi
  // del titolo scelto. Prima le opere note (guida, casa, liste), poi TMDB (anche solo in streaming o non disponibili).
  import PosterImg from '../lib/components/PosterImg.svelte';
  import AvailBadges from '../lib/components/AvailBadges.svelte';
  import AlertPanel from '../lib/components/AlertPanel.svelte';
  import Button from '../lib/components/Button.svelte';
  import { availOf, type Avail } from '../lib/avail';
  import { platforms } from '../lib/platforms.svelte';
  import { api, ApiError, type SearchHit, type SearchOptions } from '../lib/api/client';
  import { day, time, vote } from '../lib/format';
  import { router } from '../lib/router.svelte';
  import { session } from '../lib/session.svelte';

  let f = $state<SearchQuery>({ ...last, avail: [...last.avail] });
  let yearFromText = $state(last.yearFrom ? String(last.yearFrom) : '');
  let yearToText = $state(last.yearTo ? String(last.yearTo) : '');
  let hits = $state<SearchHit[]>([]);
  let note = $state('');
  let error = $state('');
  let loading = $state(false);
  let searched = $state(false);
  let opening = $state<string | null>(null);
  let options = $state<SearchOptions | null>(null);
  let filtersOpen = $state(false);              // sul telefono i filtri stanno dietro un pulsante
  let selected = $state<{ workId: number; title: string } | null>(null);
  let input: HTMLInputElement | undefined = $state();

  let mySubs = $state<string[]>([]);
  $effect(() => {
    const p = session.profile;
    if (p) api.profileSubscriptions(p.id).then((s) => (mySubs = s.services)).catch(() => {});
  });
  api.searchOptions().then((o) => (options = o)).catch(() => {});
  $effect(() => { input?.focus(); });

  const kinds = [
    { id: 'film', label: 'Film' }, { id: 'series', label: 'Serie' }, { id: 'documentary', label: 'Documentari' }, { id: 'all', label: 'Tutto' },
  ] as const;
  const availItems = [
    { id: 'tv', label: 'In TV nei prossimi giorni' }, { id: 'mine', label: 'Nei miei abbonamenti' },
    { id: 'paid', label: 'A noleggio o acquisto' }, { id: 'home', label: 'In casa' },
  ] as const;
  const ratings = [{ v: 0, label: 'Qualsiasi' }, { v: 6, label: '6 e oltre' }, { v: 7, label: '7 e oltre' }, { v: 8, label: '8 e oltre' }];

  const activeFilters = $derived(
    (f.kind !== 'all' ? 1 : 0) + (f.genre ? 1 : 0) + (f.yearFrom || f.yearTo ? 1 : 0) + (f.country ? 1 : 0) + (f.minRating ? 1 : 0) + (f.avail.length ? 1 : 0),
  );
  const canSearch = $derived(f.q.trim().length >= 2 || activeFilters > 0);

  function toggleAvail(id: 'tv' | 'mine' | 'paid' | 'home') {
    f.avail = f.avail.includes(id) ? f.avail.filter((x) => x !== id) : [...f.avail, id];
  }
  function setYears() {
    const a = Number(yearFromText.trim()), b = Number(yearToText.trim());
    f.yearFrom = a >= 1900 && a <= 2100 ? a : 0;
    f.yearTo = b >= 1900 && b <= 2100 ? b : 0;
  }
  function reset() {
    f = { q: f.q, kind: 'all', genre: 0, yearFrom: 0, yearTo: 0, country: '', minRating: 0, avail: [] };
    yearFromText = ''; yearToText = '';
  }

  // cerca 400 ms dopo l'ultima modifica; una risposta vecchia non copre quella nuova
  let seq = 0;
  $effect(() => {
    const snap: SearchQuery = { ...f, avail: [...f.avail] };
    last = snap;
    if (!canSearch) { hits = []; note = ''; error = ''; searched = false; loading = false; return; }
    const my = ++seq;
    loading = true;
    const t = setTimeout(async () => {
      try {
        const r = await api.search(snap, session.profile?.id);
        if (my !== seq) return;
        hits = r.hits; note = r.tmdbNote; error = ''; searched = true;
      } catch (e) {
        if (my === seq) error = e instanceof ApiError ? e.message : 'Ricerca non riuscita';
      } finally {
        if (my === seq) loading = false;
      }
    }, 400);
    return () => clearTimeout(t);
  });

  const kindLabel: Record<string, string> = { film: 'Film', series: 'Serie TV', documentary: 'Documentario', programme: 'Programma' };
  const keyOf = (h: SearchHit) => (h.workId != null ? `w${h.workId}` : `t${h.tmdbKind}${h.tmdbId}`);
  // in TV: come le locandine, la € gialla solo se non si vede in altro modo
  function availFor(h: SearchHit): Avail {
    const a = availOf(h, mySubs);
    if (!h.nextAiring) return a;
    return { ...a, tv: `${day(h.nextAiring).toLowerCase()} ${time(h.nextAiring)}`, pay: [] };
  }
  // titolo solo su TMDB di cui non abbiamo chiesto le piattaforme: non si sa ancora dove si vede
  const unknownWhere = (h: SearchHit) => h.streaming == null && h.paid == null;
  function statusText(a: Avail): string {
    const names = (l: string[]) => l.map((s) => platforms.of(s).short).join(', ');
    return [
      a.home ? 'In casa' : '', a.tv ? `In TV ${a.tv}` : '', a.mine.length ? `Su ${names(a.mine)}` : '', a.pay.length ? `A pagamento: ${names(a.pay)}` : '',
    ].filter(Boolean).join(' · ');
  }

  // per aprire un titolo o farsi avvisare serve la sua opera: i titoli solo su TMDB la creano al primo tocco
  async function workOf(h: SearchHit): Promise<number | null> {
    if (h.workId != null) return h.workId;
    if (h.tmdbId == null) return null;
    opening = keyOf(h);
    try {
      const r = await api.workFromTmdb({
        tmdbId: h.tmdbId, kind: h.tmdbKind, title: h.title, originalTitle: h.originalTitle || h.title, year: h.year,
        voteAverage: h.voteAverage, voteCount: h.voteCount, genreIds: h.genreIds,
      });
      h.workId = r.workId;
      return r.workId;
    } catch (e) {
      error = e instanceof ApiError ? e.message : 'Titolo non aperto';
      return null;
    } finally {
      opening = null;
    }
  }
  async function open(h: SearchHit) {
    const id = await workOf(h);
    if (id != null) router.go(`/opera/${id}`);
  }
  async function alertFor(h: SearchHit) {
    const id = await workOf(h);
    if (id != null) {
      selected = { workId: id, title: h.title };
      setTimeout(() => document.getElementById('avvisi')?.scrollIntoView({ behavior: 'smooth', block: 'nearest' }), 50);
    }
  }

  const summary = $derived.by(() => {
    const parts: string[] = [];
    if (f.kind !== 'all') parts.push(kinds.find((k) => k.id === f.kind)!.label.toLowerCase());
    if (f.genre) parts.push(options?.genres.find((g) => g.id === String(f.genre))?.name.toLowerCase() ?? '');
    if (f.q.trim().length >= 2) parts.push(`«${f.q.trim()}»`);
    if (f.country) parts.push(options?.countries.find((c) => c.id === f.country)?.name ?? f.country);
    if (f.yearFrom || f.yearTo) parts.push(`${f.yearFrom || '…'}–${f.yearTo || '…'}`);
    if (f.minRating) parts.push(`voto ${f.minRating} e oltre`);
    if (f.avail.length) parts.push(f.avail.map((a) => availItems.find((x) => x.id === a)!.label.toLowerCase()).join(' o '));
    return parts.filter(Boolean).join(', ');
  });
</script>

<div class="page">
  <header class="head">
    <h1>Cerca</h1>
    <p class="muted">Titolo, regista o attore, con i filtri che ti servono: in TV, nei tuoi abbonamenti, a noleggio, in casa. Se un titolo non è disponibile, fatti avvisare.</p>
  </header>

  <div class="cols">
    <form class="filters" aria-label="Filtri" onsubmit={(e) => e.preventDefault()}>
      <label class="field">Titolo, regista o attore
        <span class="box">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" aria-hidden="true"><circle cx="11" cy="11" r="7" /><path d="M20 20l-4-4" /></svg>
          <input bind:this={input} bind:value={f.q} type="search" placeholder="Es. Madden, Sergio Leone" autocomplete="off" maxlength="100" />
          {#if f.q}<button type="button" class="clear" aria-label="Cancella il testo" onclick={() => { f.q = ''; input?.focus(); }}>✕</button>{/if}
        </span>
      </label>

      <button type="button" class="more" aria-expanded={filtersOpen} onclick={() => (filtersOpen = !filtersOpen)}>
        Filtri{activeFilters ? ` (${activeFilters})` : ''} <span aria-hidden="true">{filtersOpen ? '▴' : '▾'}</span>
      </button>

      <div class="fbody" class:open={filtersOpen}>
        <fieldset>
          <legend>Tipo</legend>
          <div class="chips">
            {#each kinds as k (k.id)}
              <button type="button" class="chip" aria-pressed={f.kind === k.id} onclick={() => (f.kind = k.id)}>{k.label}</button>
            {/each}
          </div>
        </fieldset>

        <label class="field">Genere
          <select bind:value={f.genre}>
            <option value={0}>Tutti</option>
            {#each options?.genres ?? [] as g (g.id)}<option value={Number(g.id)}>{g.name}</option>{/each}
          </select>
        </label>

        <div class="two">
          <label class="field">Anno da<input inputmode="numeric" maxlength="4" placeholder="1960" bind:value={yearFromText} oninput={setYears} /></label>
          <label class="field">a<input inputmode="numeric" maxlength="4" placeholder="1990" bind:value={yearToText} oninput={setYears} /></label>
        </div>

        <label class="field">Paese di produzione
          <select bind:value={f.country}>
            <option value="">Tutti</option>
            {#each options?.countries ?? [] as c (c.id)}<option value={c.id}>{c.name}</option>{/each}
          </select>
        </label>

        <label class="field">Voto minimo
          <select bind:value={f.minRating}>
            {#each ratings as r (r.v)}<option value={r.v}>{r.label}</option>{/each}
          </select>
        </label>

        <fieldset>
          <legend>Disponibile</legend>
          <label class="check"><input type="checkbox" checked={f.avail.length === 0} onchange={() => (f.avail = [])} />Ovunque, anche non disponibile</label>
          {#each availItems as a (a.id)}
            <label class="check"><input type="checkbox" checked={f.avail.includes(a.id)} onchange={() => toggleAvail(a.id)} />{a.label}</label>
          {/each}
        </fieldset>

        {#if activeFilters > 0}<Button variant="ghost" onclick={reset}>Togli i filtri</Button>{/if}
      </div>
    </form>

    <section class="results" aria-label="Risultati" aria-busy={loading}>
      {#if error}<p class="error" role="alert">{error}</p>{/if}
      {#if !canSearch}
        <p class="muted">Scrivi almeno 2 lettere o scegli un filtro: trovi quello che passa in TV, i film di casa e qualunque film o serie, anche in streaming o non disponibile.</p>
      {:else if loading && hits.length === 0}
        <p class="muted" role="status">Cerco…</p>
      {:else if searched}
        <p class="sum" role="status">{hits.length === 0 ? 'Nessun titolo' : `${hits.length} ${hits.length === 1 ? 'titolo' : 'titoli'}`}{summary ? ` · ${summary}` : ''}</p>
      {/if}

      <ul class="rows">
        {#each hits as h (keyOf(h))}
          {@const a = availFor(h)}
          {@const st = statusText(a)}
          <li class:sel={selected != null && h.workId === selected.workId}>
            <button type="button" class="poster" onclick={() => open(h)} aria-label="Apri la scheda di {h.title}" disabled={opening != null}>
              <PosterImg src={h.poster} title={h.title} compact />
              {#if opening === keyOf(h)}<span class="wait">…</span>{/if}
            </button>
            <div class="body">
              <div class="top">
                <button type="button" class="title" onclick={() => open(h)} disabled={opening != null}>{h.title}</button>
                {#if h.year}<span class="muted">{h.year}</span>{/if}
                {#if h.rating != null}<span class="rate">★ {vote(h.rating)}{h.ratingSource ? ` ${h.ratingSource}` : ''}</span>
                {:else if h.kind !== 'programme'}<span class="rate na" title="Voto non ancora disponibile">★ N/A</span>{/if}
              </div>
              <div class="meta">{[kindLabel[h.kind] ?? '', (h.genres ?? []).slice(0, 3).join(', '), h.country].filter(Boolean).join(' · ')}</div>
              {#if h.credits}<div class="meta muted">{h.credits}</div>{:else if h.overview}<div class="meta muted plot">{h.overview}</div>{/if}
              <div class="status">
                <AvailBadges a={a} size={20} inline />
                <span class="muted">{st || (unknownWhere(h) ? 'Dove si vede: apri la scheda' : 'Per ora non si vede: né in TV nei prossimi giorni, né in casa, né in streaming')}</span>
                <span class="grow"></span>
                <Button size="s" variant="soft" onclick={() => alertFor(h)} disabled={opening != null}>Avvisami</Button>
              </div>
            </div>
          </li>
        {/each}
      </ul>
      {#if note && canSearch}<p class="muted small">{note}</p>{/if}
      {#if hits.length > 0}<p class="muted small">Titoli, locandine e piattaforme da TMDB e JustWatch.</p>{/if}
    </section>

    <aside id="avvisi" class="alerts" aria-label="Avvisi per il titolo scelto">
      {#if selected}
        <div class="muted small">Avvisami per</div>
        <h2>{selected.title}</h2>
        <AlertPanel workId={selected.workId} />
      {:else}
        <h2>Avvisami</h2>
        <p class="muted small">Tocca «Avvisami» su un titolo: qui scegli se avvisarti quando passa in TV, quando arriva nei tuoi abbonamenti o quando si noleggia.</p>
      {/if}
    </aside>
  </div>
</div>

<style>
  .page { padding: 24px var(--gutter) 64px; max-width: 1440px; display: flex; flex-direction: column; gap: 20px; }
  h1 { font-size: 30px; margin: 0 0 4px; }
  .muted { margin: 0; color: var(--muted); }
  .small { font-size: 13px; }
  .error { margin: 0; color: var(--rec-text); }
  .cols { display: flex; flex-wrap: wrap; gap: 24px; align-items: flex-start; }

  .filters { flex: 1 1 280px; max-width: 300px; display: flex; flex-direction: column; gap: 16px; background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius-l); padding: 18px; box-sizing: border-box; }
  .fbody { display: flex; flex-direction: column; gap: 16px; }
  .field { display: flex; flex-direction: column; gap: 6px; font-size: 13px; color: var(--muted); }
  .field input, .field select { min-height: 42px; padding: 0 12px; border-radius: 8px; border: 1px solid var(--line-strong); background: var(--card); color: var(--text); font: inherit; font-size: 15px; width: 100%; box-sizing: border-box; }
  .box { display: flex; align-items: center; gap: 8px; min-height: 42px; padding: 0 6px 0 12px; border-radius: 8px; border: 1px solid var(--line-strong); background: var(--card); color: var(--muted); }
  .box:focus-within { border-color: var(--blue); }
  .box input { flex: 1 1 auto; min-width: 0; min-height: 40px; border: 0; padding: 0; background: none; outline: none; }
  .box input:focus-visible { box-shadow: none; }
  .box:has(input:focus-visible) { box-shadow: var(--focus); }
  .box input::-webkit-search-cancel-button { display: none; }
  .clear { width: 30px; height: 30px; border: 0; border-radius: 15px; background: none; color: var(--muted); cursor: pointer; }
  .two { display: flex; gap: 10px; }
  .two .field { flex: 1 1 0; }
  fieldset { border: 0; padding: 0; margin: 0; display: flex; flex-direction: column; gap: 8px; }
  legend { font-size: 13px; color: var(--muted); margin-bottom: 8px; padding: 0; }
  .chips { display: flex; flex-wrap: wrap; gap: 6px; }
  .chip { min-height: 36px; padding: 0 14px; border-radius: 999px; border: 1px solid var(--line-btn); background: none; color: var(--text-2); font: inherit; font-size: 14px; cursor: pointer; }
  .chip[aria-pressed='true'] { background: var(--text); color: var(--bg); border-color: var(--text); font-weight: 600; }
  .check { display: flex; align-items: center; gap: 10px; font-size: 15px; color: var(--text); min-height: 30px; cursor: pointer; }
  .check input { width: 18px; height: 18px; accent-color: var(--blue); }
  .more { display: none; }

  .results { flex: 999 1 420px; min-width: 0; display: flex; flex-direction: column; gap: 12px; }
  .sum { margin: 0; font-size: 14px; color: var(--muted); }
  .rows { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 10px; }
  .rows li { display: flex; gap: 16px; background: var(--card); border: 1px solid var(--line-strong); border-radius: 14px; padding: 12px; }
  .rows li.sel { border-color: var(--blue); }
  .poster { position: relative; flex: 0 0 84px; aspect-ratio: 2 / 3; border-radius: 8px; overflow: hidden; border: 0; padding: 0; background: #2b3d63; cursor: pointer; }
  .wait { position: absolute; inset: 0; display: flex; align-items: center; justify-content: center; background: rgba(13, 16, 21, 0.6); font-weight: 700; }
  .body { flex: 1 1 0; min-width: 0; display: flex; flex-direction: column; gap: 5px; }
  .top { display: flex; flex-wrap: wrap; align-items: baseline; gap: 6px 10px; }
  .title { border: 0; background: none; padding: 0; color: var(--text); font-family: var(--font-display); font-size: 18px; font-weight: 700; text-align: left; cursor: pointer; }
  .rate { font-size: 13px; font-weight: 700; background: var(--raised); border-radius: 6px; padding: 2px 7px; }
  .rate.na { color: var(--muted); font-weight: 600; }
  .meta { font-size: 13px; color: var(--text-2); }
  .plot { overflow: hidden; display: -webkit-box; -webkit-line-clamp: 2; line-clamp: 2; -webkit-box-orient: vertical; }
  .status { display: flex; flex-wrap: wrap; align-items: center; gap: 6px 10px; margin-top: 4px; font-size: 13px; }
  .grow { flex: 1 1 0; }

  .alerts { flex: 1 1 340px; max-width: 420px; display: flex; flex-direction: column; gap: 10px; background: var(--surface); border: 1px solid var(--line-strong); border-radius: var(--radius-l); padding: 18px; box-sizing: border-box; position: sticky; top: 76px; }
  .alerts h2 { margin: 0; font-size: 20px; }

  @media (max-width: 900px) {
    .filters { max-width: none; flex-basis: 100%; }
    .more { display: inline-flex; align-self: flex-start; gap: 6px; min-height: 40px; padding: 0 14px; border-radius: 8px; border: 1px solid var(--line-btn); background: none; color: var(--text); font: inherit; font-size: 14px; font-weight: 600; cursor: pointer; }
    .fbody { display: none; }
    .fbody.open { display: flex; }
    .alerts { max-width: none; position: static; }
  }
</style>
