<script lang="ts" module>
  import { EMPTY_QUERY, type SearchQuery } from '../lib/api/client';
  import SeenButton from '../lib/components/SeenButton.svelte';
  // i filtri restano tornando indietro dalla Scheda (per la durata della pagina)
  let last: SearchQuery = { ...EMPTY_QUERY };

  /**
   * Filtri nell'indirizzo: /cerca#kind=film&avail=mine (così Stasera porta qui con i filtri di una riga). Con list=N la
   * pagina mostra e modifica la lista dinamica N (la sua ricerca salvata). sort = ordine dei risultati all'apertura
   * (rel, rating, year, title, when), per questa volta sola: «Stasera in TV» porta qui in ordine di data e ora.
   */
  export function searchHash(s: Partial<SearchQuery>, listId: number | null = null, sort = ''): string {
    const p = new URLSearchParams();
    if (listId) p.set('list', String(listId));
    if (s.q?.trim()) p.set('q', s.q.trim());
    if (s.kind && s.kind !== 'all') p.set('kind', s.kind);
    if (s.genres?.length) p.set('genres', s.genres.join(','));
    if (s.yearFrom) p.set('yearFrom', String(s.yearFrom));
    if (s.yearTo) p.set('yearTo', String(s.yearTo));
    if (s.country) p.set('country', s.country);
    if (s.minRating) p.set('minRating', String(s.minRating));
    if (s.avail?.length) p.set('avail', s.avail.join(','));
    if (s.hideSeen) p.set('hideSeen', '1');
    if (s.hideHome) p.set('hideHome', '1');
    if (s.services?.length) p.set('services', s.services.join(','));
    if (sort) p.set('sort', sort);
    const t = p.toString();
    return t ? '#' + t : '';
  }

  /** Lista dinamica e filtri dell'indirizzo; query null se l'indirizzo non ha filtri. */
  function fromHash(hash: string): { listId: number | null; query: SearchQuery | null; sort: string } {
    if (hash.length < 2) return { listId: null, query: null, sort: '' };
    const p = new URLSearchParams(hash.slice(1));
    const listId = Number(p.get('list')) || null;
    const sort = p.get('sort') ?? '';
    p.delete('list');
    p.delete('sort');
    if ([...p.keys()].length === 0) return { listId, query: null, sort };
    const kind = p.get('kind');
    const avail = (p.get('avail') ?? '').split(',').filter((a): a is SearchQuery['avail'][number] => ['tv', 'paytv', 'mine', 'paid', 'home'].includes(a));
    // genre (un genere solo) è dei link di prima
    const genres = [...(p.get('genres') ?? '').split(','), p.get('genre') ?? ''].map(Number).filter((g) => g > 0);
    return {
      listId, sort,
      query: {
        q: p.get('q') ?? '', kind: kind === 'film' || kind === 'series' || kind === 'documentary' ? kind : 'all',
        genres: [...new Set(genres)], yearFrom: Number(p.get('yearFrom')) || 0, yearTo: Number(p.get('yearTo')) || 0,
        country: p.get('country') ?? '', minRating: Number(p.get('minRating')) || 0, avail,
        hideSeen: p.get('hideSeen') === '1', hideHome: p.get('hideHome') === '1',
        services: [...new Set((p.get('services') ?? '').split(',').map((s) => s.trim()).filter(Boolean))],
      },
    };
  }
</script>

<script lang="ts">
  // Cerca (la lente in alto): l'unica ricerca dell'app, quella avanzata del prototipo. Titolo, regista o attore e filtri
  // facoltativi (tipo, generi, anni, paese, voto minimo, disponibilità, senza i già visti o quelli in casa); risultati con
  // dove si vede; «Avvisami» apre gli avvisi del titolo in un popup. Prima le opere note (guida, casa, liste), poi TMDB (anche solo in
  // streaming o non disponibili). È anche la pagina delle liste dinamiche (19/11/2026): una ricerca si salva come lista,
  // e una lista dinamica (#list=N) si apre qui con la sua ricerca, che si cambia e si salva di nuovo.
  import PosterImg from '../lib/components/PosterImg.svelte';
  import AvailBadges from '../lib/components/AvailBadges.svelte';
  import AlertDialog from '../lib/components/AlertDialog.svelte';
  import { alertedState } from '../lib/alerted.svelte';
  import ListDialog from '../lib/components/ListDialog.svelte';
  import Button from '../lib/components/Button.svelte';
  import StableLabel from '../lib/components/StableLabel.svelte';
  import ServiceIcon from '../lib/components/ServiceIcon.svelte';
  import { SvelteMap } from 'svelte/reactivity';
  import { availOfHit, type Avail } from '../lib/avail';
  import { platforms } from '../lib/platforms.svelte';
  import { api, ApiError, fromSaved, toSaved, type ListSummary, type SearchHit, type SearchOptions } from '../lib/api/client';
  import { day, time, vote } from '../lib/format';
  import { navigate, router } from '../lib/router.svelte';
  import { session } from '../lib/session.svelte';

  // filtri: dall'indirizzo (link da Stasera, preferiti), dalla lista dinamica aperta o quelli dell'ultima ricerca
  const fromUrl = fromHash(location.hash);
  const start = fromUrl.query ?? (fromUrl.listId ? { ...EMPTY_QUERY } : last);
  const copy = (s: SearchQuery): SearchQuery => ({ ...s, genres: [...s.genres], avail: [...s.avail], services: [...(s.services ?? [])] });
  let f = $state<SearchQuery>(copy(start));
  // lista dinamica aperta: la sua ricerca si carica (se l'indirizzo non ha già filtri) e si può salvare di nuovo
  let listId = $state<number | null>(fromUrl.listId);
  let list = $state<ListSummary | null>(null);
  let savedJson = $state('');
  let ready = $state(fromUrl.listId == null);
  let yearFromText = $state(start.yearFrom ? String(start.yearFrom) : '');
  let yearToText = $state(start.yearTo ? String(start.yearTo) : '');
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
  async function openList(id: number) {
    const p = session.profile;
    if (!p) { ready = true; return; }
    try {
      const l = (await api.lists(p.id)).find((x) => x.id === id && x.query);
      if (!l) { error = 'Lista dinamica non trovata.'; listId = null; ready = true; return; }
      list = l;
      savedJson = JSON.stringify(toSaved(fromSaved(l.query)));
      if (!fromUrl.query) {
        f = fromSaved(l.query);
        yearFromText = f.yearFrom ? String(f.yearFrom) : '';
        yearToText = f.yearTo ? String(f.yearTo) : '';
      }
    } catch (e) { error = e instanceof ApiError ? e.message : 'Lista non letta'; }
    ready = true;
  }
  if (fromUrl.listId) openList(fromUrl.listId);
  $effect(() => { input?.focus(); });

  const kinds = [
    { id: 'film', label: 'Film' }, { id: 'series', label: 'Serie' }, { id: 'documentary', label: 'Documentari' }, { id: 'all', label: 'Tutto' },
  ] as const;
  const availItems = [
    // «In TV gratis»: un passaggio che vedo (canale in chiaro o di un mio abbonamento); «In TV a pagamento»: un passaggio
    // su un canale a pagamento che non ho (20/11/2026, richiesta di Antonio)
    { id: 'tv', label: 'In TV gratis' }, { id: 'paytv', label: 'In TV a pagamento' }, { id: 'mine', label: 'Nei miei abbonamenti' },
    { id: 'paid', label: 'A noleggio o acquisto' }, { id: 'home', label: 'In casa' },
  ] as const;
  type AvailId = (typeof availItems)[number]['id'];
  const iconOf = (id: AvailId): Avail =>
    ({ tv: id === 'tv' ? 'x' : '', home: id === 'home', mine: id === 'mine' ? ['x'] : [], pay: id === 'paid' || id === 'paytv' ? ['x'] : [] });
  const ratings = [{ v: 0, label: 'Qualsiasi' }, { v: 6, label: '6 e oltre' }, { v: 7, label: '7 e oltre' }, { v: 8, label: '8 e oltre' }];

  const activeFilters = $derived(
    (f.kind !== 'all' ? 1 : 0) + (f.genres.length ? 1 : 0) + (f.yearFrom || f.yearTo ? 1 : 0) + (f.country ? 1 : 0) + (f.minRating ? 1 : 0)
      + (f.avail.length ? 1 : 0) + (f.hideSeen || f.hideHome ? 1 : 0) + (f.services.length ? 1 : 0),
  );
  // servizi di streaming tra cui scegliere: i miei abbonamenti e quelli già scelti (anche da un link della top)
  const serviceChoices = $derived(platforms.sort([...new Set([...mySubs, ...f.services])]));
  function toggleService(s: string) {
    f.services = f.services.includes(s) ? f.services.filter((x) => x !== s) : [...f.services, s];
  }
  function toggleGenre(id: number) {
    f.genres = f.genres.includes(id) ? f.genres.filter((x) => x !== id) : [...f.genres, id];
  }
  const genreNames = $derived(f.genres.map((g) => options?.genres.find((x) => x.id === String(g))?.name ?? '').filter(Boolean));
  const canSearch = $derived(f.q.trim().length >= 2 || activeFilters > 0);

  function toggleAvail(id: AvailId) {
    f.avail = f.avail.includes(id) ? f.avail.filter((x) => x !== id) : [...f.avail, id];
  }
  function setYears() {
    const a = Number(yearFromText.trim()), b = Number(yearToText.trim());
    f.yearFrom = a >= 1900 && a <= 2100 ? a : 0;
    f.yearTo = b >= 1900 && b <= 2100 ? b : 0;
  }
  function reset() {
    f = { ...EMPTY_QUERY, q: f.q, genres: [], avail: [], services: [] };
    yearFromText = ''; yearToText = '';
  }

  // cerca 400 ms dopo l'ultima modifica; una risposta vecchia non copre quella nuova. I filtri vanno anche
  // nell'indirizzo (si possono salvare e condividere in casa)
  const PAGE = 40;
  // ordinati per voto, anno o titolo si chiedono subito più titoli: ordinare solo i primi 40 darebbe un ordine falso
  const SORT_PAGE = 200;
  let limit = PAGE;
  let more = $state(false);
  let current = $state<SearchQuery>(copy(start));
  let seq = 0;
  async function run(snap: SearchQuery, lim: number) {
    const my = ++seq;
    loading = true;
    try {
      const r = await api.search({ ...snap, limit: lim }, session.profile?.id);
      if (my !== seq) return;
      hits = r.hits; note = r.tmdbNote; more = r.more ?? false; error = ''; searched = true;
      limit = lim;
    } catch (e) {
      if (my === seq) error = e instanceof ApiError ? e.message : 'Ricerca non riuscita';
    } finally {
      if (my === seq) loading = false;
    }
  }
  // La ricerca parte con «Cerca» (o Invio nel testo), non a ogni filtro: con TMDB può metterci qualche secondo e i
  // risultati che cambiavano da soli mentre si sceglieva non si capivano (19/11/2026). Parte da sola solo all'apertura.
  let applied = $state('');                     // la ricerca dei risultati mostrati
  const changed = $derived(canSearch && JSON.stringify(toSaved(f)) !== applied);
  function search() {
    const snap: SearchQuery = copy(f);
    applied = JSON.stringify(toSaved(snap));
    if (!listId) last = snap;
    current = snap;
    // con una lista aperta l'indirizzo tiene solo la lista finché la ricerca non cambia
    const h = listId && applied === savedJson ? searchHash({}, listId) : searchHash(snap, listId);
    try { history.replaceState(history.state, '', '/cerca' + h); } catch { /* indirizzo non aggiornato: pazienza */ }
    if (!canSearch) { hits = []; note = ''; error = ''; searched = false; loading = false; more = false; seq++; return; }
    run(snap, sortKey === 'rel' ? PAGE : SORT_PAGE);
  }
  let started = false;
  $effect(() => {
    if (!ready || started) return;               // la lista dinamica non è ancora letta
    started = true;
    if (canSearch) search();
  });
  // caricamento progressivo: arrivati in fondo ai risultati se ne chiedono altri 40
  function loadMore() {
    if (more && !loading && canSearch) run(current, limit + PAGE);
  }
  let sentinel: HTMLElement | undefined = $state();
  $effect(() => {
    if (!sentinel) return;
    const io = new IntersectionObserver((e) => { if (e.some((x) => x.isIntersecting)) loadMore(); }, { rootMargin: '400px' });
    io.observe(sentinel);
    return () => io.disconnect();
  });

  // ---------------------------------------------------------------- ordine dei risultati
  // Pertinenza = l'ordine del server (prima le opere note, poi TMDB). Voto, anno e titolo: su o giù; i titoli senza voto
  // o senza anno vanno in fondo. L'ordine non è un filtro: si applica subito, senza «Cerca». Si ricorda su questo dispositivo.
  // Data e ora (27/11/2026): dal prossimo passaggio in TV (quello che vedo, se no quello a pagamento); senza passaggi in fondo.
  type SortKey = 'rel' | 'rating' | 'year' | 'title' | 'when';
  const SORT_STORE = 'dlf.cerca.ordine';
  const sorts: { id: SortKey; label: string; first: 'asc' | 'desc' }[] = [
    { id: 'rel', label: 'Pertinenza', first: 'desc' }, { id: 'rating', label: 'Voto', first: 'desc' },
    { id: 'year', label: 'Anno', first: 'desc' }, { id: 'title', label: 'Titolo', first: 'asc' },
    { id: 'when', label: 'Data e ora', first: 'asc' },
  ];
  // un ordine nell'indirizzo (link di Stasera) vale per questa apertura e non cambia quello ricordato
  const urlSort = sorts.find((x) => x.id === fromUrl.sort);
  const savedSort = urlSort ? { key: urlSort.id, dir: urlSort.first }
    : (() => { try { return JSON.parse(localStorage.getItem(SORT_STORE) ?? 'null'); } catch { return null; } })();
  let sortKey = $state<SortKey>(sorts.some((x) => x.id === savedSort?.key) ? savedSort.key : 'rel');
  let sortDir = $state<'asc' | 'desc'>(savedSort?.dir === 'asc' ? 'asc' : 'desc');
  function setSort(k: SortKey) {
    if (k === sortKey && k !== 'rel') sortDir = sortDir === 'asc' ? 'desc' : 'asc';
    else { sortKey = k; sortDir = sorts.find((x) => x.id === k)!.first; }
    try { localStorage.setItem(SORT_STORE, JSON.stringify({ key: sortKey, dir: sortDir })); } catch { /* solo per questa visita */ }
    if (sortKey !== 'rel' && more && !loading && canSearch && limit < SORT_PAGE) run(current, SORT_PAGE);
  }
  const collator = new Intl.Collator('it', { sensitivity: 'base', numeric: true });
  const shown = $derived.by(() => {
    if (sortKey === 'rel') return hits;
    const dir = sortDir === 'asc' ? 1 : -1;
    const at = (t: string | null | undefined) => (t ? Date.parse(t) : NaN);
    const val = (h: SearchHit): number | null => {
      if (sortKey === 'rating') return h.rating ?? null;
      if (sortKey === 'year') return h.year > 0 ? h.year : null;
      const t = Math.min(...[at(h.nextAiring), at(h.paidAiring)].filter((x) => !Number.isNaN(x)));
      return Number.isFinite(t) ? t : null;
    };
    return [...hits].sort((a, b) => {
      if (sortKey === 'title') return dir * collator.compare(a.title, b.title);
      const x = val(a), y = val(b);
      if (x == null || y == null) return x == null && y == null ? 0 : x == null ? 1 : -1;   // senza valore: in fondo
      return dir * (x - y) || collator.compare(a.title, b.title);
    });
  });

  const metaOf = (h: SearchHit) => {
    const k = kindLabel[h.kind] ?? '';
    const g = (h.genres ?? []).filter((x) => x !== k).slice(0, 3).join(', ');
    return [k, g, h.country].filter(Boolean).join(' · ');
  };
  const kindLabel: Record<string, string> = { film: 'Film', series: 'Serie TV', documentary: 'Documentario', programme: 'Programma' };
  const keyOf = (h: SearchHit) => (h.workId != null ? `w${h.workId}` : `t${h.tmdbKind}${h.tmdbId}`);
  // come le locandine: TV per il passaggio che vedo, € per uno su un canale a pagamento che non ho (anche insieme)
  function availFor(h: SearchHit): Avail {
    const when = (t: string | null | undefined) => (t ? `${day(t).toLowerCase()} ${time(t)}` : '');
    return availOfHit(h, mySubs, when(h.nextAiring), when(h.paidAiring));
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
    if (id != null) { selected = { workId: id, title: h.title }; if (h.tmdbId != null) madeWork.set(h.tmdbId, id); }
  }
  // «Avvisami» acceso quando l'opera ha un avviso (26/11/2026). Un titolo solo di TMDB diventa un'opera aprendo il popup:
  // si ricorda quale, per accendere il pulsante anche lì
  const madeWork = new SvelteMap<number, number>();
  $effect(() => { void session.profile; alertedState.sync(); });
  const hitAlerted = (h: SearchHit) => alertedState.has(h.workId ?? (h.tmdbId != null ? madeWork.get(h.tmdbId) : null));

  const summary = $derived.by(() => {
    // descrive la ricerca dei risultati mostrati, non i filtri che si stanno cambiando
    const c = current;
    const parts: string[] = [];
    if (c.kind !== 'all') parts.push(kinds.find((k) => k.id === c.kind)!.label.toLowerCase());
    const gn = c.genres.map((g) => options?.genres.find((x) => x.id === String(g))?.name ?? '').filter(Boolean);
    if (gn.length) parts.push(gn.map((n) => n.toLowerCase()).join(' o '));
    if (c.q.trim().length >= 2) parts.push(`«${c.q.trim()}»`);
    if (c.country) parts.push(options?.countries.find((x) => x.id === c.country)?.name ?? c.country);
    if (c.yearFrom || c.yearTo) parts.push(`${c.yearFrom || '…'}–${c.yearTo || '…'}`);
    if (c.minRating) parts.push(`voto ${c.minRating} e oltre`);
    if (c.avail.length) parts.push(c.avail.map((a) => availItems.find((x) => x.id === a)!.label.toLowerCase()).join(' o '));
    if (c.hideSeen) parts.push('senza i già visti');
    if (c.hideHome) parts.push('senza quelli in casa');
    if (c.services.length) parts.push('su ' + c.services.map((s) => platforms.of(s).short).join(' o '));
    return parts.filter(Boolean).join(', ');
  });

  // ---------------------------------------------------------------- liste dinamiche
  // «Salva come lista»: il popup comune delle liste (lo stesso di Libreria), lista nuova o una dinamica che c'è già
  let saveOpen = $state(false);
  let saved = $state('');
  const saveName = $derived.by(() => {
    const n = (f.q.trim() || summary || 'La mia ricerca').slice(0, 60);
    return n.charAt(0).toUpperCase() + n.slice(1);
  });
  function listSaved(l: ListSummary, created: boolean) {
    saveOpen = false;
    list = l;
    listId = l.id;
    savedJson = JSON.stringify(toSaved(f));
    saved = `${created ? 'Lista' : 'Aggiornata la lista'} «${l.name}»${l.onHome ? ': la trovi anche in La mia lista' : ''}.`;
  }
  function closeList() {
    list = null; listId = null; savedJson = ''; saved = '';
  }

  // «Non proporre più»: cercato per nome il titolo compare, segnato; qui lo si può riammettere
  async function readmit(h: SearchHit) {
    const p = session.profile;
    if (!p) return;
    try {
      const it = (await api.ignored(p.id)).find((i) => i.title === h.title);
      if (it) await api.unignore(p.id, it.workKey);
      h.excluded = false;
    } catch (err) { error = err instanceof ApiError ? err.message : 'Non riammesso'; }
  }
</script>

<div class="page">
  <!-- titolo e pulsanti restano in vista scorrendo i risultati: la barra sta nella pagina, non nell'intestazione -->
  <div class="titlebar">
    <div class="tt">
      {#if list}<a class="back" href="/libreria" onclick={navigate}>← Le mie liste</a>{/if}
      <h1>{list ? list.name : 'Make my day'}</h1>
    </div>
    <div class="topacts">
      <!-- filtri cambiati: «Cerca» si accende e un alone pulsa due volte, di nuovo a ogni modifica (la chiave lo ricrea) -->
      {#key changed ? JSON.stringify(toSaved(f)) : ''}
        <span class="pulse" class:on={changed}>
          <Button variant={changed ? 'primary' : 'soft'} disabled={!changed || loading} onclick={search}>{loading ? 'Cerco…' : 'Cerca'}</Button>
        </span>
      {/key}
      {#if session.profile}
        <Button variant="soft" disabled={!canSearch} onclick={() => { saved = ''; saveOpen = true; }}>{list ? 'Salva la lista' : 'Salva come lista'}</Button>
      {/if}
    </div>
  </div>
  <header class="head">
    {#if list}
      <p class="muted">Lista dinamica{list.family ? ' della famiglia' : ''}{list.onHome ? ', in La mia lista' : ''}: i titoli sono i risultati di questa ricerca e cambiano da soli. <button type="button" class="linkbtn" onclick={closeList}>Chiudi la lista</button></p>
    {:else}
      <p class="muted">Titolo, regista o attore, con i filtri che ti servono: in TV, nei tuoi abbonamenti, a noleggio, in casa. Se un titolo non è disponibile, fatti avvisare. Una ricerca che ti piace salvala come lista.</p>
    {/if}
    {#if saved}<p class="ok small" role="status">{saved}</p>{/if}
  </header>

  <div class="cols">
    <!-- Invio in un campo di testo cerca (23/11/2026, Antonio): con tanti campi e nessun pulsante di invio nel form il
         browser non fa l'invio implicito; il pulsante invisibile qui sotto (non display:none, che alcuni browser
         ignorano) lo riattiva. Il «Cerca» visibile sta nella barra in alto, fuori dal form -->
    <form class="filters" aria-label="Filtri" onsubmit={(e) => { e.preventDefault(); if (canSearch && !loading) search(); }}>
      <button type="submit" class="sr-only" tabindex="-1" aria-hidden="true">Cerca</button>
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

        <div class="field">
          <span id="lbl-generi">Generi</span>
          <details class="multi" data-dropdown aria-labelledby="lbl-generi">
            <summary>{genreNames.length === 0 ? 'Tutti' : genreNames.join(', ')}</summary>
            <div class="mlist">
              {#each options?.genres ?? [] as g (g.id)}
                <label class="check"><input type="checkbox" checked={f.genres.includes(Number(g.id))} onchange={() => toggleGenre(Number(g.id))} />{g.name}</label>
              {/each}
              {#if f.genres.length > 0}<button type="button" class="linkbtn" onclick={() => (f.genres = [])}>Tutti i generi</button>{/if}
            </div>
          </details>
          {#if f.genres.length > 1}<span class="hint">Vale uno qualsiasi</span>{/if}
        </div>

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

        {#if session.profile}
          <fieldset>
            <legend>Togli</legend>
            <label class="check"><input type="checkbox" bind:checked={f.hideSeen} />Quelli che ho già visto</label>
            <label class="check"><input type="checkbox" bind:checked={f.hideHome} />Quelli che ho in casa</label>
          </fieldset>
        {/if}

        {#if activeFilters > 0}<Button variant="ghost" onclick={reset}>Togli i filtri</Button>{/if}
      </div>
    </form>

    <section class="results" aria-label="Risultati" aria-busy={loading}>
      {#if error}<p class="error" role="alert">{error}</p>{/if}
      {#if !canSearch}
        <p class="muted">Scrivi almeno 2 lettere o scegli un filtro: trovi quello che passa in TV, i film di casa e qualunque film o serie, anche in streaming o non disponibile.</p>
      {:else if loading && hits.length === 0}
        <p class="muted" role="status">Cerco…</p>
      {:else if loading}
        <p class="sum" role="status">Cerco…</p>
      {:else if searched}
        <p class="sum" role="status">{hits.length === 0 ? 'Nessun titolo' : `${hits.length}${more ? '+' : ''} ${hits.length === 1 ? 'titolo' : 'titoli'}`}{summary ? ` · ${summary}` : ''}{sortKey !== 'rel' && more ? ` · in ordine i primi ${hits.length}` : ''}</p>
      {/if}

      <!-- durante una nuova ricerca i risultati di prima restano, sbiaditi -->
      {#if hits.length > 1}
        <div class="sortbar" role="group" aria-label="Ordina i risultati">
          <span class="muted small">Ordina</span>
          {#each sorts as o (o.id)}
            <button type="button" class="sortbtn" aria-pressed={sortKey === o.id} onclick={() => setSort(o.id)}
              title={o.id === 'rel' ? 'L\'ordine della ricerca' : sortKey === o.id ? 'Tocca per invertire' : ''}>
              {o.label}{#if sortKey === o.id && o.id !== 'rel'}<span class="arrow" aria-label={sortDir === 'asc' ? 'crescente' : 'decrescente'}>{sortDir === 'asc' ? '↑' : '↓'}</span>{/if}
            </button>
          {/each}
        </div>
      {/if}
      <!-- risultati di prima: sbiaditi mentre cerca, attenuati finché i filtri cambiati non sono cercati -->
      <ul class="rows" class:stale={loading && !more} class:outdated={changed && !loading}>
        {#each shown as h (keyOf(h))}
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
                {#if h.excluded}<span class="excl" title="L'hai tolto con «Non proporre più»: con i soli filtri non compare">Escluso</span>
                  <button type="button" class="linkbtn small" onclick={() => readmit(h)}>Riammetti</button>{/if}
              </div>
              <div class="meta" title={metaOf(h)}>{metaOf(h)}</div>
              <div class="meta muted" title={h.credits || h.overview}>{h.credits || h.overview || ' '}</div>
              <div class="status">
                <AvailBadges a={a} size={20} inline />
                <span class="muted line" title={st}>{st || (unknownWhere(h) ? 'Dove si vede: apri la scheda' : 'Per ora non si vede: né in TV, né in casa, né in streaming')}</span>
              </div>
            </div>
            <div class="act">
              <!-- «Visto» (23/11/2026): solo per le opere già note al server (un titolo solo di TMDB si segna dalla scheda) -->
              <SeenButton workId={h.workId} title={h.title} />
              {#if a.home}
                <Button size="s" variant="primary" onclick={() => open(h)} disabled={opening != null}>▶ Guarda</Button>
              {:else if a.mine.length > 0}
                <Button size="s" variant="soft" onclick={() => open(h)} disabled={opening != null}>Apri</Button>
              {:else}
                <span class="tip">
                  <Button size="s" variant={hitAlerted(h) ? 'primary' : 'soft'} pressed={hitAlerted(h)} onclick={() => alertFor(h)} disabled={opening != null}><StableLabel text={hitAlerted(h) ? 'Avvisi attivi' : 'Avvisami'} variants={['Avvisi attivi', 'Avvisami']} /></Button>
                  <span class="tipbox" role="tooltip">Per un titolo che oggi non vedi: scegli se avvisarti quando passa in TV, quando arriva nei tuoi abbonamenti o quando si noleggia. Si apre una finestra con le scelte.</span>
                </span>
              {/if}
            </div>
          </li>
        {/each}
      </ul>
      <div bind:this={sentinel} class="sentinel" aria-hidden="true"></div>
      {#if more && hits.length > 0}<p class="muted small" role="status">{loading ? 'Carico altri titoli…' : ''}{#if !loading}<button type="button" class="linkbtn" onclick={loadMore}>Altri titoli</button>{/if}</p>{/if}
      {#if note && canSearch}<p class="muted small">{note}</p>{/if}
      {#if hits.length > 0}<p class="muted small">Titoli, locandine e piattaforme da TMDB e JustWatch.</p>{/if}
    </section>

    {#if selected}
      <AlertDialog workId={selected.workId} title={selected.title} onclose={() => (selected = null)} />
    {/if}
    {#if saveOpen}
      <ListDialog query={toSaved(f)} currentListId={list?.id ?? null} suggestedName={saveName} onclose={() => (saveOpen = false)} ondone={listSaved} />
    {/if}
    <aside class="side">
      <fieldset class="panel">
        <legend>Disponibile</legend>
        <label class="check"><input type="checkbox" checked={f.avail.length === 0} onchange={() => (f.avail = [])} /><span class="ic none" aria-hidden="true"></span>Ovunque, anche non disponibile</label>
        {#each availItems as a (a.id)}
          <label class="check"><input type="checkbox" checked={f.avail.includes(a.id)} onchange={() => toggleAvail(a.id)} />
            <span class="ic" aria-hidden="true"><AvailBadges a={iconOf(a.id)} size={18} /></span>{a.label}</label>
        {/each}
        <span class="muted small">Basta una delle caselle scelte: un film su Rai 5 e su Rai Play compare con la sola «In TV gratis». «In TV a pagamento» = su un canale che non hai (Sky senza Sky).</span>
      </fieldset>
      <!-- servizi di streaming (27/11/2026): i miei abbonamenti e quelli scelti da un link della top di Stasera -->
      {#if serviceChoices.length > 0}
        <fieldset class="panel">
          <legend>Su questi servizi</legend>
          <label class="check"><input type="checkbox" checked={f.services.length === 0} onchange={() => (f.services = [])} /><span class="ic none" aria-hidden="true"></span>Tutti</label>
          {#each serviceChoices as sv (sv)}
            <label class="check"><input type="checkbox" checked={f.services.includes(sv)} onchange={() => toggleService(sv)} />
              <span class="ic" aria-hidden="true"><ServiceIcon name={sv} size={18} /></span>{platforms.of(sv).short}</label>
          {/each}
          <span class="muted small">Il titolo dev'essere in streaming su uno dei servizi scelti.</span>
        </fieldset>
      {/if}
    </aside>
  </div>
</div>

<style>
  .page { padding: 0 var(--gutter) 64px; max-width: 1440px; display: flex; flex-direction: column; gap: 20px; }
  h1 { font-size: 30px; margin: 0; }
  .head { display: flex; flex-direction: column; gap: 6px; }
  .titlebar { position: sticky; top: var(--header-h, 53px); z-index: 20; display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between;
    gap: 10px 20px; padding: 20px 0 12px; background: var(--bg); }
  .tt { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
  .topacts { display: flex; flex-wrap: wrap; gap: 10px; }
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
  .sortbar { display: flex; flex-wrap: wrap; align-items: center; gap: 6px; }
  .sortbar .muted { margin-right: 4px; }
  .sortbtn { display: inline-flex; align-items: center; gap: 4px; min-height: 34px; padding: 0 12px; border-radius: 999px; border: 1px solid var(--line-btn);
    background: none; color: var(--text-2); font: inherit; font-size: 13px; cursor: pointer; }
  .sortbtn[aria-pressed='true'] { background: var(--text); color: var(--bg); border-color: var(--text); font-weight: 600; }
  .arrow { font-weight: 700; }
  .rows { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 10px; }
  /* schede tutte alte uguali: locandina, quattro righe (titolo, genere, persone o trama, dove si vede), azione a destra */
  .rows li { display: grid; grid-template-columns: 64px minmax(0, 1fr) auto; align-items: center; gap: 0 14px; height: 120px; box-sizing: border-box;
    background: var(--card); border: 1px solid var(--line-strong); border-radius: 14px; padding: 12px; }
  .rows li.sel { border-color: var(--blue); }
  .poster { position: relative; width: 64px; aspect-ratio: 2 / 3; border-radius: 8px; overflow: hidden; border: 0; padding: 0; background: #2b3d63; cursor: pointer; }
  .wait { position: absolute; inset: 0; display: flex; align-items: center; justify-content: center; background: rgba(13, 16, 21, 0.6); font-weight: 700; }
  .body { min-width: 0; display: flex; flex-direction: column; gap: 4px; }
  .top { display: flex; align-items: baseline; gap: 8px; min-width: 0; white-space: nowrap; }
  .top > :not(.title) { flex: 0 0 auto; }
  .title { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; border: 0; background: none; padding: 0; color: var(--text); font-family: var(--font-display); font-size: 17px; font-weight: 700; text-align: left; cursor: pointer; }
  .act { display: flex; align-items: center; gap: 8px; }
  .rate { font-size: 13px; font-weight: 700; background: var(--raised); border-radius: 6px; padding: 2px 7px; }
  .rate.na { color: var(--muted); font-weight: 600; }
  .meta { font-size: 13px; color: var(--text-2); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .status { display: flex; align-items: center; gap: 8px; min-width: 0; font-size: 13px; }
  .status .line { min-width: 0; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .sentinel { height: 1px; }
  .linkbtn { border: 0; background: none; padding: 0; color: var(--link); font: inherit; cursor: pointer; text-align: left; }
  .back { font-size: 14px; font-weight: 600; text-decoration: none; }
  .multi { position: relative; }
  .multi summary { list-style: none; display: flex; align-items: center; min-height: 42px; padding: 0 30px 0 12px; border-radius: 8px; border: 1px solid var(--line-strong); background: var(--card); color: var(--text); font-size: 15px; cursor: pointer; overflow: hidden; white-space: nowrap; text-overflow: ellipsis; }
  .multi summary::-webkit-details-marker { display: none; }
  .multi summary::after { content: '▾'; position: absolute; right: 12px; color: var(--muted); }
  .multi[open] summary { border-color: var(--blue); }
  .mlist { display: flex; flex-direction: column; gap: 2px; margin-top: 6px; padding: 8px 10px; max-height: 280px; overflow-y: auto; border-radius: 8px; border: 1px solid var(--line-strong); background: var(--card); }
  .hint { font-size: 12px; color: var(--muted); }
  .ok { margin: 0; color: var(--green-text); }
  .excl { font-size: 12px; font-weight: 700; color: var(--orange-text); border: 1px solid currentColor; border-radius: 6px; padding: 1px 6px; }

  .side { flex: 1 1 300px; max-width: 380px; display: flex; flex-direction: column; gap: 16px; position: sticky; top: calc(var(--header-h, 53px) + 84px); }
  .panel { display: flex; flex-direction: column; gap: 8px; background: var(--surface); border: 1px solid var(--line-strong); border-radius: var(--radius-l); padding: 18px; box-sizing: border-box; margin: 0; min-width: 0; }
  fieldset.panel legend { float: left; width: 100%; margin-bottom: 4px; }
  /* icona come sulle locandine, rimessa nel flusso del testo (come nella legenda) */
  .ic { position: relative; display: inline-block; flex: 0 0 18px; width: 18px; height: 18px; }
  .ic :global(.badges) { right: 0; bottom: 0; }
  .rows { transition: opacity 0.2s; }
  .rows.stale { opacity: 0.45; pointer-events: none; }
  .rows.outdated { opacity: 0.55; }
  .pulse { position: relative; display: inline-flex; border-radius: var(--radius); }
  .pulse.on::after { content: ''; position: absolute; inset: 0; border-radius: inherit; pointer-events: none; animation: ring 1.1s ease-out 2; }
  @keyframes ring {
    0% { box-shadow: 0 0 0 0 rgba(242, 244, 247, 0.6); }
    100% { box-shadow: 0 0 0 14px rgba(242, 244, 247, 0); }
  }
  /* chi ha chiesto meno animazioni: un alone fermo al posto della pulsazione */
  @media (prefers-reduced-motion: reduce) {
    .pulse.on::after { animation: none; box-shadow: 0 0 0 3px rgba(242, 244, 247, 0.35); }
  }
  .tip { position: relative; display: inline-flex; }
  .tipbox { display: none; position: absolute; right: 0; bottom: calc(100% + 8px); z-index: 5; width: 280px; padding: 10px 12px; border-radius: 10px;
    background: var(--raised); border: 1px solid var(--line-btn); color: var(--text-2); font-size: 13px; line-height: 1.45; box-shadow: 0 8px 24px rgba(0, 0, 0, 0.4); }
  .tip:hover .tipbox, .tip:focus-within .tipbox { display: block; }

  @media (max-width: 900px) {
    .filters { max-width: none; flex-basis: 100%; }
    .more { display: inline-flex; align-self: flex-start; gap: 6px; min-height: 40px; padding: 0 14px; border-radius: 8px; border: 1px solid var(--line-btn); background: none; color: var(--text); font: inherit; font-size: 14px; font-weight: 600; cursor: pointer; }
    .fbody { display: none; }
    .fbody.open { display: flex; }
    .side { max-width: none; position: static; flex-basis: 100%; order: -1; }
  }
</style>
