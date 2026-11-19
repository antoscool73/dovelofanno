<script lang="ts">
  // Home («La mia serata»): risponde a "stasera che si guarda?". In evidenza il titolo migliore della serata, poi
  // "Da vedere stasera" (la lista della mia serata), le liste che il profilo ha scelto (dinamiche: i risultati della loro
  // ricerca; a mano: i loro titoli), le registrazioni in corso e in arrivo, i film di stasera e della settimana per voto.
  import Badge from '../lib/components/Badge.svelte';
  import PosterImg from '../lib/components/PosterImg.svelte';
  import Button from '../lib/components/Button.svelte';
  import RecordButton from '../lib/components/RecordButton.svelte';
  import ProgrammaDialog from './ProgrammaDialog.svelte';
  import Section from '../lib/components/Section.svelte';
  import ServiceIcon from '../lib/components/ServiceIcon.svelte';
  import { platforms } from '../lib/platforms.svelte';
  import AvailBadges from '../lib/components/AvailBadges.svelte';
  import { dragScroll } from '../lib/dragScroll';
  import AvailLegend from '../lib/components/AvailLegend.svelte';
  import { availOf, type Avail } from '../lib/avail';
  import { inMySubscriptions, mySubscriptionsFor, payPlatform, streamingFor } from '../lib/pay';
  import {
    api, ApiError, type GuideProgramme, type LibraryItem, type ListEntry, type ListSummary, type Recording, type SearchHit, type SearchQuery, type TonightItem,
  } from '../lib/api/client';
  import { searchHash } from './Cerca.svelte';
  import { day, minutes, between, range, time, vote, midnight } from '../lib/format';
  import { navigate, router } from '../lib/router.svelte';
  import { session } from '../lib/session.svelte';

  let tonightList = $state<TonightItem[]>([]);
  let recordings = $state<Recording[]>([]);
  let tonightFilms = $state<GuideProgramme[]>([]);
  let weekFilms = $state<GuideProgramme[]>([]);
  let continuing = $state<LibraryItem[]>([]);
  let mySubs = $state<string[]>([]);
  // film della guida dei prossimi giorni: quelli nei cataloghi streaming dei miei abbonamenti sono l'"on demand"
  let guideFilms = $state<GuideProgramme[]>([]);
  let loading = $state(true);
  let error = $state('');
  let open = $state<GuideProgramme | null>(null);
  let busy = $state<number | null>(null);
  let now = $state(new Date());

  // "stasera": da adesso alle 2 di notte; dalle 2 alle 6 vale la sera dopo
  function eveningWindow(d: Date) {
    const base = d.getHours() < 2 ? new Date(d.getTime() - 86_400_000) : d;
    const end = new Date(base.getFullYear(), base.getMonth(), base.getDate() + 1, 2);
    return { from: d, to: end };
  }

  async function load() {
    error = '';
    now = new Date();
    const prof = session.profile;
    const { from, to } = eveningWindow(now);
    const tomorrow = new Date(midnight(now).getTime() + 86_400_000);
    try {
      const [list, recs, films, week, cont, subs, ahead] = await Promise.all([
        prof ? api.tonight(prof.id) : Promise.resolve([]),
        api.recordings(),
        api.guide({ from, to, group: 'Film', max: 500 }),
        api.guide({ from: tomorrow, to: new Date(tomorrow.getTime() + 6 * 86_400_000), group: 'Film', minRating: 7, max: 1000 }),
        prof ? api.continueWatching(prof.id).catch(() => []) : Promise.resolve([]),
        prof ? api.profileSubscriptions(prof.id).then((x) => x.services).catch(() => []) : Promise.resolve([]),
        api.guide({ from: now, to: new Date(now.getTime() + 8 * 86_400_000), group: 'Film', max: 5000 }),
      ]);
      guideFilms = ahead;
      mySubs = subs;
      continuing = cont;
      tonightList = list;
      recordings = recs;
      // stasera: film che iniziano dalle 20 alle 2; quelli già in onda contano solo se è già sera
      const evening = now.getHours() >= 20 || now.getHours() < 2;
      const isEvening = (d: Date) => d.getHours() >= 20 || d.getHours() < 2;
      tonightFilms = (films.filter((p) => (new Date(p.start) < now ? evening : isEvening(new Date(p.start)))))
        .sort((a, b) => (b.rating ?? 0) - (a.rating ?? 0) || a.start.localeCompare(b.start));
      weekFilms = dedupe(week).sort((a, b) => (b.rating ?? 0) - (a.rating ?? 0)).slice(0, 18);
    } catch (e) {
      error = e instanceof ApiError ? e.message : 'Stasera non letta';
    } finally {
      loading = false;
    }
  }
  load();
  $effect(() => {
    const t = setInterval(load, 60_000);
    return () => clearInterval(t);
  });

  // ---------------------------------------------------------------- le mie liste nella pagina
  // le liste scelte (Preferenze › La mia serata, o «In La mia serata» in Cerca): si leggono a parte, perché una lista
  // dinamica è una ricerca (anche su TMDB) e può metterci qualche secondo; si rileggono ogni 10 minuti
  type HomeRow = { list: ListSummary; hits: SearchHit[]; entries: ListEntry[]; loading: boolean; error: string };
  let homeRows = $state<HomeRow[]>([]);
  let homeLoaded = $state(false);
  async function loadHome() {
    const prof = session.profile;
    if (!prof) { homeRows = []; homeLoaded = true; return; }
    try {
      const lists = (await api.lists(prof.id)).filter((l) => l.onHome && !l.isTonight);
      const old = new Map(homeRows.map((r) => [r.list.id, r]));
      homeRows = lists.map((l) => ({ list: l, hits: old.get(l.id)?.hits ?? [], entries: old.get(l.id)?.entries ?? [], loading: true, error: '' }));
      homeLoaded = true;
      await Promise.all(homeRows.map(async (r) => {
        try {
          if (r.list.query) r.hits = (await api.listResults(prof.id, r.list.id, 18)).hits;
          else r.entries = (await api.list(prof.id, r.list.id)).items.slice(0, 18);
          r.error = '';
        } catch (e) { r.error = e instanceof ApiError ? e.message : 'Lista non letta'; }
        r.loading = false;
      }));
    } catch { homeLoaded = true; }
  }
  loadHome();
  $effect(() => {
    const t = setInterval(loadHome, 600_000);
    return () => clearInterval(t);
  });

  function dedupe(list: GuideProgramme[]) {
    const seen = new Set<string>();
    return list.filter((p) => {
      // lo stesso film su due canali (o con e senza anno nella guida) compare una volta sola
      const k = p.title.trim().toLowerCase();
      if (seen.has(k)) return false;
      seen.add(k);
      return true;
    });
  }

  const recordable = (p: GuideProgramme | null | undefined) => !!p && p.channelId != null;

  // in evidenza: prima un titolo della mia serata che passa stasera, poi il film migliore di stasera
  const hero = $derived.by(() => {
    const { to } = eveningWindow(now);
    const mine = tonightList
      .map((i) => i.next)
      .filter((n): n is GuideProgramme => !!n && new Date(n.start) < to)
      .sort((a, b) => (b.rating ?? 0) - (a.rating ?? 0));
    return mine[0] ?? tonightFilms.find((p) => p.rating != null && recordable(p)) ?? tonightFilms[0] ?? null;
  });
  const heroFromList = $derived(!!hero && tonightList.some((i) => i.workId === hero.workId));

  const live = $derived(recordings.filter((r) => r.state === 'recording' || r.state === 'merging'));
  const upcoming = $derived(
    recordings
      .filter((r) => r.state === 'scheduled' && new Date(r.recordFrom).getTime() - now.getTime() < 24 * 3_600_000)
      .sort((a, b) => a.recordFrom.localeCompare(b.recordFrom)),
  );

  function when(p: GuideProgramme) {
    const s = new Date(p.start);
    if (s <= now) return `In onda fino alle ${time(p.stop)}`;
    // dopo mezzanotte della stessa serata: "Stanotte 01:05", non "Domani 01:05"
    const nextDay = midnight(s).getTime() - midnight(now).getTime() === 86_400_000;
    if (nextDay && s.getHours() < 6 && now.getHours() >= 12) return `Stanotte ${time(p.start)}`;
    return `${day(p.start, now)} ${time(p.start)}`;
  }

  async function quickRecord(p: GuideProgramme) {
    if (p.channelId == null || p.scheduled) { open = p; return; }
    busy = p.id;
    try {
      await api.scheduleRecording({ channelId: p.channelId, title: p.title, start: p.start, stop: p.stop, beforeMin: null, afterMin: null, format: null, programmeId: p.id });
      await load();
    } catch (e) {
      error = e instanceof ApiError ? e.message : 'Registrazione non programmata';
    } finally {
      busy = null;
    }
  }

  async function seen(i: TonightItem) {
    const prof = session.profile;
    if (!prof) return;
    try { await api.watched(prof.id, i.workId); await load(); }
    catch (e) { error = e instanceof ApiError ? e.message : 'Non segnato'; }
  }
  async function drop(i: TonightItem) {
    const prof = session.profile;
    if (!prof) return;
    try { await api.removeTonight(prof.id, i.workId); await load(); }
    catch (e) { error = e instanceof ApiError ? e.message : 'Non tolto'; }
  }

  // «Non proporre più» su un titolo di una lista dinamica: sparisce dalle liste e dalle ricerche con i soli filtri
  async function noMore(r: HomeRow, h: SearchHit) {
    const prof = session.profile;
    if (!prof || h.workId == null) return;
    try { await api.ignore(prof.id, h.workId); r.hits = r.hits.filter((x) => x !== h); }
    catch (e) { error = e instanceof ApiError ? e.message : 'Non salvato'; }
  }
  // un titolo solo su TMDB diventa un'opera al primo tocco, poi si apre la scheda
  async function openHit(h: SearchHit) {
    let id = h.workId ?? null;
    if (id == null && h.tmdbId != null) {
      try {
        id = (await api.workFromTmdb({
          tmdbId: h.tmdbId, kind: h.tmdbKind, title: h.title, originalTitle: h.originalTitle || h.title, year: h.year,
          voteAverage: h.voteAverage, voteCount: h.voteCount, genreIds: h.genreIds,
        })).workId;
      } catch (e) { error = e instanceof ApiError ? e.message : 'Titolo non aperto'; }
    }
    if (id != null) router.go(`/opera/${id}`);
  }

  // dopo una notifica "nuove proposte" (/#proposte) si scende alle mie liste
  $effect(() => {
    if (!loading && homeLoaded && location.hash === '#proposte') setTimeout(() => document.getElementById('proposte')?.scrollIntoView({ behavior: 'smooth' }), 100);
  });

  // Stasera in TV divisa: quello che si vede (in chiaro o con i miei abbonamenti) e quello a pagamento fuori abbonamento
  // un film che passa sia su un canale visibile sia su uno a pagamento resta solo tra quelli visibili
  const filmsMine = $derived(dedupe(tonightFilms.filter((p) => inMySubscriptions(p, mySubs))));
  const filmsOther = $derived.by(() => {
    const mine = new Set(filmsMine.map((p) => p.title.trim().toLowerCase()));
    // un film che vedo comunque (in casa o in streaming nei miei abbonamenti) qui non c'è
    return dedupe(tonightFilms.filter((p) => !inMySubscriptions(p, mySubs) && !mine.has(p.title.trim().toLowerCase())
      && !p.inHouse && streamingFor(p, mySubs).length === 0));
  });
  // on demand: film nei cataloghi streaming dei miei abbonamenti (dai film della guida: TMDB li conosce), per voto
  const onDemand = $derived(
    dedupe(guideFilms.filter((p) => streamingFor(p, mySubs).length > 0)).sort((a, b) => (b.rating ?? 0) - (a.rating ?? 0)),
  );
  const otherPlatforms = $derived([...new Set(filmsOther.map((p) => payPlatform(p)).filter((x): x is string => !!x))]);

  // la mia serata: si vede stasera un titolo che passa in TV entro le 2, che è in casa o che è in streaming nei miei
  // abbonamenti (es. un originale Prime Video); gli altri restano sotto, con il motivo
  const onTvTonight = (i: TonightItem) => !!i.next && new Date(i.next.start) < eveningWindow(now).to;
  const tonightAvailable = $derived(tonightList.filter((i) => onTvTonight(i) || i.inHouse || streamingFor(i, mySubs).length > 0));
  // passati: già trasmessi negli ultimi 3 giorni e oggi non si vedono in nessun altro modo (nessun passaggio in arrivo,
  // non in casa, nessuna piattaforma): si mostrano solo con l'interruttore "Mostra programmazione passata"
  const onlyPast = (i: TonightItem) =>
    !!i.last && !i.next && !i.inHouse && (i.streaming?.length ?? 0) === 0 && (i.paid?.length ?? 0) === 0;
  const tonightPast = $derived(tonightList.filter((i) => !tonightAvailable.includes(i) && onlyPast(i)));
  const tonightLater = $derived(tonightList.filter((i) => !tonightAvailable.includes(i) && !onlyPast(i)));
  const PAST_KEY = 'dlf.serata.passati';
  let showPast = $state((() => { try { return localStorage.getItem(PAST_KEY) === '1'; } catch { return false; } })());
  function setShowPast(v: boolean) {
    showPast = v;
    try { if (v) localStorage.setItem(PAST_KEY, '1'); else localStorage.removeItem(PAST_KEY); } catch { /* solo per questa visita */ }
  }
  const kindLabel: Record<string, string> = { film: 'Film', series: 'Serie TV', documentary: 'Documentario', programme: 'Programma' };
  // i voti (IMDb o TMDB) esistono per film, serie e documentari: dove mancano si scrive N/A
  const RATED_KINDS = ['film', 'series', 'documentary'];
  const RATED_GROUPS = ['Film', 'Serie TV', 'Documentari'];
  function laterReason(i: TonightItem): string {
    if (i.next) return `In TV ${day(i.next.start, now).toLowerCase()} ${time(i.next.start)}`;
    const st = [...(i.streaming ?? []), ...(i.paid ?? [])];
    if (st.length > 0) return `Su ${[...new Set(st.map((s) => platforms.of(s).short))].join(', ')}, a pagamento`;
    return 'Per ora non si vede';
  }

  // una locandina di una riga: stessa forma per la mia serata, le proposte e i film (in diretta, on demand, settimana)
  type Card = {
    key: string; poster: string; title: string; rating: number | null; scheduled: boolean; avail: Avail;
    lines: string[]; note?: string; accent?: string; warn?: string; dim?: boolean; open: () => void; rated?: boolean;
    rec?: GuideProgramme | null; seen?: () => void; drop?: () => void; dropLabel?: string;
  };
  const channelOf = (p: GuideProgramme) => p.channelName || p.guideChannelName;
  const tvText = (p: GuideProgramme) => `${when(p).toLowerCase()}, ${channelOf(p)}`;
  const shortNames = (l: string[]) => l.map((s) => platforms.of(s).short).join(', ');

  // didascalie, sempre uguali: 1) canale del prossimo passaggio, 2) orario hh:mm–hh:mm (con il giorno se non è
  // stasera), 3) genere · anno. Un titolo che non passa in TV ha al posto del canale come lo vedo ("Su Netflix", "In casa")
  // e niente orario
  function slot(p: GuideProgramme, past = false): string {
    const s = new Date(p.start);
    const tonight = midnight(s).getTime() === midnight(now).getTime()
      || (midnight(s).getTime() - midnight(now).getTime() === 86_400_000 && s.getHours() < 6 && now.getHours() >= 12);
    const r = range(p.start, p.stop);
    if (past) return `Passato ${day(p.start, now).toLowerCase().replace('stasera', 'oggi')} ${r}`;
    return tonight ? r : `${day(p.start, now)} ${r}`;
  }
  const genreYear = (genre: string, year: number) => [genre, year || ''].filter(Boolean).join(' · ');

  function programmeCard(p: GuideProgramme, mode: 'tv' | 'ondemand' = 'tv'): Card {
    const avail = availOf(p, mySubs, mode === 'tv' ? p : null, tvText(p));
    const meta = genreYear(p.sub || p.group, p.year);
    const card: Card = {
      key: `p${p.id}`, poster: p.poster || p.icon, title: p.title, rating: p.rating ?? null, scheduled: p.scheduled, avail, rated: RATED_GROUPS.includes(p.group),
      lines: mode === 'ondemand' ? [`Su ${shortNames(avail.mine)}`, meta] : [channelOf(p), slot(p), meta], open: () => (open = p),
    };
    // passaggio su un canale che non vedo ma il titolo l'ho in casa o in streaming: il tocco apre la scheda (con Guarda)
    if (mode === 'tv' && !avail.tv && (avail.home || avail.mine.length > 0) && p.workId != null) card.open = () => router.go(`/opera/${p.workId}`);
    return card;
  }

  function tonightCard(i: TonightItem, available: boolean): Card {
    const tv = onTvTonight(i) ? i.next : null;
    const avail = availOf(i, mySubs, i.next, i.next ? tvText(i.next) : '');
    const g = i.next ?? i.last;
    const meta = genreYear(g?.sub || g?.group || kindLabel[i.kind] || '', i.year);
    let lines: string[];
    if (tv) lines = [channelOf(tv), slot(tv), meta];
    else if (avail.mine.length > 0) lines = [`Su ${shortNames(avail.mine)}`, meta];
    else if (i.inHouse) lines = ['In casa', meta];
    else if (i.next) lines = [channelOf(i.next), slot(i.next), meta];
    else if (onlyPast(i) && i.last) lines = [channelOf(i.last), slot(i.last, true), meta];
    else lines = [laterReason(i), meta];
    return {
      key: `t${i.workId}`, poster: i.poster || i.next?.poster || i.next?.icon || i.last?.poster || i.last?.icon || '', title: i.title, rating: i.rating ?? null, rated: RATED_KINDS.includes(i.kind),
      scheduled: !!tv?.scheduled, avail, dim: !available,
      lines,
      open: () => (tv ? (open = tv) : router.go(`/opera/${i.workId}`)),
      rec: tv && recordable(tv) ? tv : null, seen: () => seen(i), drop: () => drop(i), dropLabel: 'Togli dalla mia serata',
    };
  }

  // titolo di una lista dinamica (un risultato della ricerca): come in Cerca, in TV con il giorno e l'ora
  function hitCard(r: HomeRow, h: SearchHit): Card {
    let avail = availOf(h, mySubs);
    if (h.nextAiring) avail = { ...avail, tv: `${day(h.nextAiring, now).toLowerCase()} ${time(h.nextAiring)}`, pay: [] };
    const meta = genreYear((h.genres ?? [])[0] ?? kindLabel[h.kind] ?? '', h.year);
    const lines = h.nextAiring ? [`In TV ${day(h.nextAiring, now).toLowerCase()} ${time(h.nextAiring)}`, meta]
      : avail.mine.length > 0 ? [`Su ${shortNames(avail.mine)}`, meta]
      : avail.home ? ['In casa', meta]
      : avail.pay.length > 0 ? [`A pagamento: ${shortNames(avail.pay)}`, meta] : [meta];
    return {
      key: h.workId != null ? `w${h.workId}` : `t${h.tmdbKind}${h.tmdbId}`, poster: h.poster, title: h.title, rating: h.rating ?? null,
      rated: RATED_KINDS.includes(h.kind), scheduled: false, avail, lines, open: () => openHit(h),
      drop: h.workId != null ? () => noMore(r, h) : undefined, dropLabel: 'Non proporre più',
    };
  }
  // titolo di una lista a mano
  function entryCard(e: ListEntry): Card {
    const avail = availOf(e, mySubs, e.next, e.next ? tvText(e.next) : '');
    const meta = genreYear(e.next?.sub || e.next?.group || kindLabel[e.kind] || '', e.year);
    const lines = e.next ? [channelOf(e.next), slot(e.next), meta]
      : avail.mine.length > 0 ? [`Su ${shortNames(avail.mine)}`, meta]
      : e.inHouse ? ['In casa', meta] : ['Per ora non si vede', meta];
    return {
      key: `e${e.workId}`, poster: e.poster || e.next?.poster || e.next?.icon || '', title: e.title, rating: e.rating ?? null,
      rated: RATED_KINDS.includes(e.kind), scheduled: !!e.next?.scheduled, avail, lines, open: () => router.go(`/opera/${e.workId}`),
      rec: e.next && recordable(e.next) ? e.next : null,
    };
  }

  const serataCards = $derived([
    ...tonightAvailable.map((i) => tonightCard(i, true)),
    ...tonightLater.map((i) => tonightCard(i, false)),
    ...(showPast ? tonightPast.map((i) => tonightCard(i, false)) : []),
  ]);
</script>

{#if loading}
  <p class="pad muted" role="status">Un momento…</p>
{:else}
  {#if error}<p class="pad error" role="alert">{error}</p>{/if}

  {#if hero}
    <section class="hero">
      {#if hero.icon}<img class="backdrop" src={hero.icon} alt="" referrerpolicy="no-referrer" />{/if}
      <div class="hero-word" aria-hidden="true">{hero.title}</div>
      <div class="hero-body">
        <div class="kicker">{heroFromList ? 'DALLA MIA SERATA IN TV' : 'IL FILM DI STASERA'} · {when(hero).toUpperCase()} SU {(hero.channelName || hero.guideChannelName).toUpperCase()}</div>
        <h1>{hero.title}</h1>
        <div class="facts">
          {#if hero.year}<span>{hero.year}</span>{/if}
          {#if hero.sub || hero.group}<span>{hero.sub || hero.group}</span>{/if}
          <span>{minutes(between(hero.start, hero.stop))}</span>
          {#if hero.country}<span>{hero.country}</span>{/if}
          {#if hero.rating != null}<Badge tone="blue">★ {vote(hero.rating)} {hero.ratingSource}</Badge>{/if}
        </div>
        {#if hero.description}<p class="plot">{hero.description}</p>{/if}
        {#if hero.actors || hero.directors}<p class="credits">{hero.actors ? `Con ${hero.actors}` : ''}{hero.actors && hero.directors ? ' · ' : ''}{hero.directors ? `Regia di ${hero.directors}` : ''}</p>{/if}
        <div class="actions">
          {#if recordable(hero)}<RecordButton scheduled={hero.scheduled} busy={busy === hero.id} onclick={() => quickRecord(hero)} />{/if}
          <Button variant="soft" size="l" onclick={() => (hero.workId != null ? router.go(`/opera/${hero.workId}`) : (open = hero))}>Dettagli</Button>
        </div>
      </div>
    </section>
  {/if}

  <div class="pad sections">
    <AvailLegend />
    <Section key="serata" title="Da vedere stasera">
      {#snippet tools()}
        {#if tonightPast.length > 0}
          <label class="switch">
            <input type="checkbox" role="switch" checked={showPast} onchange={(e) => setShowPast(e.currentTarget.checked)} />
            <span class="track" aria-hidden="true"></span>
            Mostra programmazione passata ({tonightPast.length})
          </label>
        {/if}
      {/snippet}
      {#snippet aside()}{tonightList.length === 0 ? 'Aggiungi i titoli che vuoi vedere (dalla guida o con la lente): compaiono qui quando si possono vedere.' : `${tonightList.length} ${tonightList.length === 1 ? 'titolo' : 'titoli'}, ${tonightAvailable.length} da vedere stasera`}{/snippet}
      {#if serataCards.length > 0}
        {@render row(serataCards)}
        {#if serataCards.some((c) => c.dim)}<p class="muted small">Le locandine più chiare non si vedono stasera nei tuoi abbonamenti: un tocco apre la scheda.</p>{/if}
      {:else if tonightPast.length > 0}
        <p class="muted small">Niente da vedere stasera dalla tua lista: {tonightPast.length === 1 ? 'un titolo è già passato' : `${tonightPast.length} titoli sono già passati`} negli ultimi giorni.</p>
      {/if}
    </Section>

    {#if continuing.length > 0}
      <Section key="continua" title="Continua a guardare">
        {#snippet aside()}<a href="/libreria" onclick={navigate}>Libreria</a>{/snippet}
        <ul class="tonight">
          {#each continuing.slice(0, 6) as it (it.id)}
            <li>
              <div class="t-main">
                <span class="t-title">{it.title}</span>
                {#if it.subtitle}<div class="muted small">{it.subtitle}</div>{/if}
                <div class="bar" aria-label="Visto al {Math.round((100 * it.positionS) / Math.max(1, it.durationS))}%"><span style:width="{Math.min(100, (100 * it.positionS) / Math.max(1, it.durationS))}%"></span></div>
              </div>
              <div class="t-acts"><Button size="s" variant="primary" onclick={() => router.go(`/guarda/${it.id}`)}>▶ Riprendi</Button></div>
            </li>
          {/each}
        </ul>
      </Section>
    {/if}

    <div id="proposte" class="mylists">
      {#each homeRows as r (r.list.id)}
        {@const dynamic = !!r.list.query}
        {@const href = dynamic ? `/cerca#list=${r.list.id}` : `/libreria/liste/${r.list.id}`}
        <Section key={'lista-' + r.list.id} title={r.list.name}>
          {#snippet aside()}<a {href} onclick={navigate}>{dynamic ? 'Cambia la ricerca' : 'Apri la lista'}</a>{/snippet}
          {#if r.error}<p class="error small">{r.error}</p>
          {:else if dynamic && r.hits.length > 0}
            {@render row(r.hits.map((h) => hitCard(r, h)), { href, label: 'Tutta la lista', hint: 'con la sua ricerca' })}
          {:else if !dynamic && r.entries.length > 0}
            {@render row(r.entries.map(entryCard), { href, label: 'Tutta la lista', hint: `${r.list.count} ${r.list.count === 1 ? 'titolo' : 'titoli'}` })}
          {:else if r.loading}<p class="muted small" role="status">Cerco i titoli…</p>
          {:else}<p class="muted small">{dynamic ? 'Per ora nessun titolo con questa ricerca.' : 'La lista è vuota.'}</p>{/if}
        </Section>
      {/each}
      {#if homeLoaded && homeRows.length === 0 && session.profile}
        <Section key="liste" title="Le mie liste">
          <p class="muted small">Qui compaiono le liste che scegli in <a href="/preferenze#serata" onclick={navigate}>Preferenze › La mia serata</a>. Una lista dinamica si crea dalla <a href="/cerca" onclick={navigate}>ricerca</a>: scegli i filtri e «Salva come lista».</p>
        </Section>
      {/if}
    </div>

    {#if live.length > 0 || upcoming.length > 0}
      <Section key="registrazioni" title="In registrazione e in arrivo">
        {#snippet aside()}<a href="/registrazioni" onclick={navigate}>Tutte le registrazioni</a>{/snippet}
        <ul class="recs">
          {#each live as r (r.id)}
            <li class="rec-live"><Badge tone="red">In registrazione</Badge><span class="r-title">{r.title}</span><span class="muted small">{r.channelName} · {range(r.start, r.stop)} · mancano {minutes(Math.max(0, between(now.toISOString(), r.recordTo)))}</span></li>
          {/each}
          {#each upcoming as r (r.id)}
            <li><Badge tone="blue">{day(r.start, now)} {time(r.start)}</Badge><span class="r-title">{r.title}</span><span class="muted small">{r.channelName} · {range(r.start, r.stop)}</span></li>
          {/each}
        </ul>
      </Section>
    {/if}

    {#snippet row(list: Card[], all: { filter?: Partial<SearchQuery>; href?: string; label: string; hint: string } | null = null)}
      <ul class="cards" use:dragScroll>
        {#each list as c (c.key)}
          <li class:dim={c.dim}>
            <button type="button" class="card" onclick={c.open}>
              <span class="poster">
                <PosterImg src={c.poster} title={c.title} />
                {#if c.rating != null}<span class="stars">★ {vote(c.rating)}</span>{:else if c.rated}<span class="stars na" title="Voto non ancora disponibile">★ N/A</span>{/if}
                {#if c.scheduled}<span class="recdot" aria-label="in programma"></span>{/if}
                <AvailBadges a={c.avail} />
              </span>
              <span class="c-title">{c.title}</span>
              {#each c.lines.filter(Boolean) as l, k (k)}<span class="line muted small">{l}</span>{/each}
              {#if c.note}<span class="note small" title={c.note}>{c.note}</span>{/if}
              {#if c.accent}<span class="line accent small">{c.accent}</span>{/if}
              {#if c.warn}<span class="line warn small">{c.warn}</span>{/if}
            </button>
            {#if c.rec || c.seen || c.drop}
              <div class="c-acts">
                {#if c.rec}
                  {@const r = c.rec}
                  <button type="button" class="mini rec" class:on={r.scheduled} disabled={busy === r.id} onclick={() => quickRecord(r)}
                    aria-pressed={r.scheduled} aria-label={r.scheduled ? 'In programma: apri' : `Registra ${c.title}`} title={r.scheduled ? 'In programma' : 'Registra'}>
                    <span class="dot" aria-hidden="true"></span>{r.scheduled ? 'In programma' : 'Registra'}
                  </button>
                {/if}
                {#if c.seen}<button type="button" class="mini" onclick={c.seen} aria-label="Visto: {c.title}" title="Visto">✓</button>{/if}
                {#if c.drop}<button type="button" class="mini ghost" onclick={c.drop} aria-label="{c.dropLabel}: {c.title}" title={c.dropLabel}>✕</button>{/if}
              </div>
            {/if}
          </li>
        {/each}
        {#if all}
          <!-- ultima locandina: la lista completa è la ricerca con i filtri di questa riga -->
          <li>
            <a class="card all" href={all.href ?? '/cerca' + searchHash(all.filter ?? {})} onclick={navigate}>
              <span class="poster more"><span class="plus" aria-hidden="true">→</span><span>{all.label}</span></span>
              <span class="c-title">Vedi tutti</span>
              <span class="line muted small">{all.hint}</span>
            </a>
          </li>
        {/if}
      </ul>
    {/snippet}

    {#if tonightFilms.length > 0}
      <Section key="stasera-miei" title="Stasera in TV · Nei miei abbonamenti">
        {#snippet aside()}<span class="mysubs">dal voto più alto · <a href="/preferenze#abbonamenti" onclick={navigate}>I miei abbonamenti</a>{#each platforms.sort(mySubs) as s (s)}<ServiceIcon name={s} size={24} />{/each}</span>{/snippet}
        <h3 class="subrow">In diretta <span class="muted small">stasera sui canali in chiaro e su quelli dei tuoi abbonamenti</span></h3>
        {#if filmsMine.length > 0}{@render row(filmsMine.slice(0, 18).map((p) => programmeCard(p)), { filter: { kind: 'film', avail: ['tv'] }, label: 'Tutti i film in TV', hint: 'nei prossimi giorni, con i filtri' })}{:else}<p class="muted small">Nessun film stasera in chiaro o sui canali dei tuoi abbonamenti.</p>{/if}
        <h3 class="subrow">On demand <span class="muted small">da vedere quando vuoi nei cataloghi dei tuoi abbonamenti</span></h3>
        {#if onDemand.length > 0}
          {@render row(onDemand.slice(0, 18).map((p) => programmeCard(p, 'ondemand')), { filter: { kind: 'film', avail: ['mine'] }, label: 'Tutti i film nei tuoi abbonamenti', hint: 'il catalogo completo, con i filtri' })}
        {:else}
          <p class="muted small">{mySubs.length === 0 ? 'Scegli i tuoi abbonamenti (Netflix, Prime Video…) in Preferenze: qui compaiono i film dei loro cataloghi.' : 'Nessun film della guida nei cataloghi dei tuoi abbonamenti, per ora.'}</p>
        {/if}
      </Section>
      {#if filmsOther.length > 0}
        <Section key="stasera-altri" title="Stasera in TV · Non nei miei abbonamenti">
          {#snippet aside()}a pagamento fuori dai tuoi abbonamenti ({otherPlatforms.join(', ')}){/snippet}
          {@render row(filmsOther.slice(0, 18).map((p) => programmeCard(p)), { filter: { kind: 'film', avail: ['paid'] }, label: 'Tutti i film a pagamento', hint: 'noleggio, acquisto, altri abbonamenti' })}
        </Section>
      {/if}
    {/if}
    {#if weekFilms.length > 0}
      <Section key="settimana" title="Questa settimana">
        {#snippet aside()}film con voto 7 e oltre nei prossimi giorni{/snippet}
        {@render row(weekFilms.map((p) => programmeCard(p)), { filter: { kind: 'film', avail: ['tv'], minRating: 7 }, label: 'Tutti i film da 7 in su', hint: 'in TV nei prossimi giorni' })}
      </Section>
    {/if}

    {#if !hero && tonightFilms.length === 0 && weekFilms.length === 0}
      <div class="empty">
        <p>La guida è vuota.</p>
        <p class="muted">Arriva dalla playlist (se la indica) o da una guida aggiunta in <a href="/impostazioni" onclick={navigate}>Impostazioni</a>. Senza la chiave TMDB i film non hanno voto: si aggiunge in Impostazioni › Voti dei film.</p>
      </div>
    {/if}
  </div>
{/if}

{#if open}
  <ProgrammaDialog p={open} onclose={() => (open = null)} onchange={load} />
{/if}

<style>
  .pad { padding: 0 var(--gutter); }
  .muted { color: var(--muted); margin: 0; }
  .small { font-size: 13px; }
  .error { color: var(--rec-text); }
  .hero { position: relative; overflow: hidden; background: #172033; min-height: 360px; display: flex; align-items: flex-end; }
  .backdrop { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; opacity: 0.35; }
  .hero-word {
    position: absolute; top: 16px; right: var(--gutter); max-width: 60%; font-family: var(--font-display); font-weight: 700;
    font-size: clamp(60px, 10vw, 150px); line-height: 0.95; color: #1f2b44; text-transform: uppercase; text-align: right;
    overflow: hidden; display: -webkit-box; -webkit-line-clamp: 2; line-clamp: 2; -webkit-box-orient: vertical;
  }
  .hero-body { position: relative; padding: 64px var(--gutter) 36px; max-width: 900px; display: flex; flex-direction: column; gap: 12px; }
  .kicker { font-size: 13px; font-weight: 700; letter-spacing: 0.04em; color: var(--link-hover); }
  .hero h1 { font-size: clamp(36px, 5vw, 56px); line-height: 1; letter-spacing: -0.02em; }
  .facts { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 14px; color: var(--text-2); font-size: 15px; }
  .plot { margin: 0; font-size: 17px; line-height: 1.55; color: var(--text-2); max-width: 680px; display: -webkit-box; -webkit-line-clamp: 3; line-clamp: 3; -webkit-box-orient: vertical; overflow: hidden; }
  .credits { margin: 0; font-size: 14px; color: var(--muted); }
  .actions { display: flex; flex-wrap: wrap; gap: 12px; margin-top: 4px; }
  .sections { display: flex; flex-direction: column; gap: 32px; padding-top: 28px; padding-bottom: 56px; }
  .mylists { display: flex; flex-direction: column; gap: 32px; }
  .mylists:empty { display: none; }
  .tonight { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(340px, 1fr)); gap: 12px; }
  .tonight li { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px; background: var(--card); border: 1px solid var(--line-strong); border-radius: 12px; padding: 14px 16px; }
  .t-main { display: flex; flex-direction: column; gap: 4px; min-width: 0; flex: 1 1 200px; }
  .t-title { border: 0; background: none; padding: 0; color: var(--text); font-size: 17px; font-weight: 600; text-align: left; cursor: pointer; }
  .t-acts { display: flex; flex-wrap: wrap; gap: 8px; }
  .warn { color: var(--orange-text); }
  .bar { height: 4px; border-radius: 2px; background: var(--raised); overflow: hidden; margin-top: 6px; }
  .bar span { display: block; height: 100%; background: var(--blue); }
  .recs { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 8px; }
  .recs li { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 14px; background: var(--card); border: 1px solid var(--line-strong); border-radius: 12px; padding: 12px 16px; }
  .rec-live { border-color: #3a2a2e; }
  .r-title { font-weight: 600; }
  .cards { list-style: none; margin: 0; padding: 0 0 8px; display: grid; align-items: start; grid-auto-flow: column; grid-auto-columns: 180px; gap: 16px; overflow-x: auto; scroll-snap-type: x proximity; }
  .cards li { scroll-snap-align: start; }
  :global(.cards.dragging), :global(.row-scroll.dragging) { cursor: grabbing; scroll-snap-type: none; user-select: none; }
  .switch { display: inline-flex; align-items: center; gap: 8px; font-size: 13px; color: var(--text-2); cursor: pointer; }
  .switch input { position: absolute; opacity: 0; width: 1px; height: 1px; }
  .track { position: relative; width: 34px; height: 20px; border-radius: 10px; background: var(--line-btn); transition: background 0.15s; }
  .track::after { content: ""; position: absolute; top: 3px; left: 3px; width: 14px; height: 14px; border-radius: 50%; background: var(--text); transition: left 0.15s; }
  .switch input:checked + .track { background: var(--blue); }
  .switch input:checked + .track::after { left: 17px; }
  .switch input:focus-visible + .track { box-shadow: var(--focus); }
  .card { width: 100%; display: flex; flex-direction: column; gap: 2px; padding: 0; border: 0; background: none; color: var(--text); text-align: left; cursor: pointer; }
  .poster { position: relative; aspect-ratio: 2 / 3; border-radius: 12px; background: #2b3d63; overflow: hidden; display: block; margin-bottom: 6px; }
  /* una riga per voce: un testo lungo non deve allungare tutta la fila */
  .line { display: block; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; line-height: 1.35; }
  .line :global(.ic) { margin: -2px 4px 0 0; }
  .mysubs { display: inline-flex; flex-wrap: wrap; align-items: center; gap: 6px; }
  .card:hover .poster, .card:focus-visible .poster { box-shadow: 0 0 0 2px var(--text); }
  .subrow { font-size: 16px; margin: 4px 0 10px; display: flex; flex-wrap: wrap; align-items: baseline; gap: 4px 12px; }
  .subrow + :global(ul), .subrow + p { margin-bottom: 20px; }
  .stars { position: absolute; top: 8px; left: 8px; background: rgba(13, 16, 21, 0.8); border-radius: 6px; padding: 2px 7px; font-size: 12px; font-weight: 700; }
  .stars.na { color: var(--muted); font-weight: 600; }
  .recdot { position: absolute; top: 10px; right: 10px; width: 10px; height: 10px; border-radius: 50%; background: var(--rec); }
  .c-title { font-weight: 600; font-size: 15px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .accent { color: var(--link); }
  /* "Vedi tutti": ultima locandina della riga, porta alla ricerca con i filtri della riga */
  .card.all { text-decoration: none; }
  .poster.more { display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 10px; padding: 16px; box-sizing: border-box; text-align: center; background: var(--surface); border: 1px dashed var(--line-btn); color: var(--text-2); font-weight: 600; font-size: 14px; }
  .card.all:hover .poster.more { border-color: var(--text-2); color: var(--text); }
  .plus { font-size: 30px; line-height: 1; color: var(--link); }
  /* nota su due righe al massimo: il passaggio su un canale che non ho */
  .note { color: var(--muted); font-size: 12px; line-height: 1.3; opacity: 0.85; overflow: hidden; display: -webkit-box; -webkit-line-clamp: 2; line-clamp: 2; -webkit-box-orient: vertical; }
  /* titoli della mia serata che stasera non si vedono nei miei abbonamenti: più chiari, ma si aprono lo stesso */
  .cards li.dim .poster { opacity: 0.45; }
  .cards li.dim .c-title { color: var(--text-2); }
  .c-acts { display: flex; gap: 6px; margin-top: 8px; }
  .mini {
    display: inline-flex; align-items: center; justify-content: center; gap: 6px; min-width: 36px; height: 34px; padding: 0 10px;
    border-radius: 8px; border: 1px solid var(--line-btn); background: var(--card); color: var(--text); font: inherit; font-size: 13px; font-weight: 600; cursor: pointer;
  }
  .mini:hover { border-color: var(--text-2); }
  .mini.rec { flex: 1 1 auto; background: var(--text); color: var(--bg); border: 0; }
  .mini.rec.on { background: var(--rec-soft); color: var(--text); box-shadow: inset 0 0 0 1px var(--rec); }
  .mini.ghost { border-color: transparent; background: none; color: var(--muted); }
  .mini .dot { width: 9px; height: 9px; border-radius: 50%; background: var(--rec-dot); }
  .empty { background: var(--surface); border: 1px dashed var(--line-btn); border-radius: var(--radius-l); padding: 20px; }
  .empty p { margin: 0 0 6px; }
  @media (max-width: 720px) { .hero { min-height: 300px; } .tonight { grid-template-columns: 1fr; } .cards { grid-auto-columns: 140px; } }
</style>
