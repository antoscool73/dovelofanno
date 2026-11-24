<script lang="ts">
  // Home («La mia serata»): risponde a "stasera che si guarda?". In evidenza il titolo migliore della serata, poi
  // "Da vedere stasera" (la lista della mia serata), le liste che il profilo ha scelto (dinamiche: i risultati della loro
  // ricerca; a mano: i loro titoli), le registrazioni in corso e in arrivo, i film di stasera e della settimana per voto.
  import Badge from '../lib/components/Badge.svelte';
  import PosterImg from '../lib/components/PosterImg.svelte';
  import Button from '../lib/components/Button.svelte';
  import RecordButton from '../lib/components/RecordButton.svelte';
  import SeenButton from '../lib/components/SeenButton.svelte';
  import { seenState } from '../lib/seen.svelte';
  import Section from '../lib/components/Section.svelte';
  import ServiceIcon from '../lib/components/ServiceIcon.svelte';
  import { platforms } from '../lib/platforms.svelte';
  import AvailBadges from '../lib/components/AvailBadges.svelte';
  import { dragScroll } from '../lib/dragScroll';
  import AvailLegend from '../lib/components/AvailLegend.svelte';
  import { availOf, availOfHit, type Avail } from '../lib/avail';
  import { recordChoice } from '../lib/series.svelte';
  import { sectionsCommand } from '../lib/sections.svelte';
  import { inMySubscriptions, mySubscriptionsFor, payPlatform, streamingFor } from '../lib/pay';
  import {
    api, ApiError, type GuideProgramme, type LibraryItem, type ListEntry, type ListSummary, type Recording, type SearchHit, type SearchQuery, type TonightItem,
  } from '../lib/api/client';
  import { searchHash } from './Cerca.svelte';
  import { between, day, episodeOf, midnight, minutes, range, recordingTitle, time, vote } from '../lib/format';
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
  // un clic su una locandina apre sempre la scheda del titolo (23/11/2026, Antonio: niente più popup del programma)
  const openWork = (p: GuideProgramme) => { if (p.workId != null) router.go(`/opera/${p.workId}`); };
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
      weekFilms = dedupe(week).sort((a, b) => (b.rating ?? 0) - (a.rating ?? 0)).slice(0, 60);
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
  // le liste scelte (Preferenze › La mia lista, o «In La mia lista» in Cerca): si leggono a parte, perché una lista
  // dinamica è una ricerca (anche su TMDB) e può metterci qualche secondo; si rileggono ogni 10 minuti
  // more: la lista ha altri titoli oltre quelli mostrati (solo allora c'è «Tutta la lista» in fondo alla riga)
  type HomeRow = { list: ListSummary; hits: SearchHit[]; entries: ListEntry[]; loading: boolean; error: string; more: boolean };
  let homeRows = $state<HomeRow[]>([]);
  let homeLoaded = $state(false);
  // sezioni predefinite nascoste dal profilo (Preferenze › La mia lista)
  let hidden = $state<Set<string>>(new Set());
  const shown = (k: string) => !hidden.has(k);
  async function loadHome() {
    const prof = session.profile;
    if (!prof) { homeRows = []; homeLoaded = true; return; }
    api.homeSections(prof.id).then((h) => (hidden = new Set(h.hidden))).catch(() => {});
    try {
      const lists = (await api.lists(prof.id)).filter((l) => l.onHome && !l.isTonight);
      const old = new Map(homeRows.map((r) => [r.list.id, r]));
      homeRows = lists.map((l) => ({ list: l, hits: old.get(l.id)?.hits ?? [], entries: old.get(l.id)?.entries ?? [], loading: true, error: '', more: old.get(l.id)?.more ?? false }));
      homeLoaded = true;
      await Promise.all(homeRows.map(async (r) => {
        try {
          if (r.list.query) {
            const res = await api.listResults(prof.id, r.list.id, 36);
            r.hits = res.hits;
            r.more = !!res.more;
          } else {
            const items = (await api.list(prof.id, r.list.id)).items;
            r.entries = items.slice(0, 36);
            r.more = items.length > 36;
          }
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
      .map((i) => i.evening)
      .filter((n): n is GuideProgramme => !!n && new Date(n.start) < to)
      .sort((a, b) => (b.rating ?? 0) - (a.rating ?? 0));
    // con «Nascondi visti» il titolo in evidenza non è uno già visto
    const ok = (p: GuideProgramme) => !seenState.hidden(p.workId);
    const films = tonightFilms.filter(ok);
    return mine.filter(ok)[0] ?? films.find((p) => p.rating != null && recordable(p)) ?? films[0] ?? null;
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
    if (p.channelId == null || p.scheduled) { openWork(p); return; }
    const how = await recordChoice(p);
    if (how === null) return;
    if (how === 'series') { await load(); return; }
    busy = p.id;
    try {
      await api.scheduleRecording({ channelId: p.channelId, title: recordingTitle(p), start: p.start, stop: p.stop, ...how, programmeId: p.id });
      await load();
    } catch (e) {
      error = e instanceof ApiError ? e.message : 'Registrazione non programmata';
    } finally {
      busy = null;
    }
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

  // Banner solo dalle 20:00 alle 21:30 (punto 6 del 23/11, Antonio): è l'ora in cui si sceglie il film. Fuori da quella
  // fascia, al suo posto, «Il top per la serata»: tre titoli in TV e tre per ogni mio abbonamento (24/11/2026). Criteri:
  // - TV: film di stasera che il profilo vede (in chiaro o nei suoi abbonamenti), non iniziati da più di 20 minuti;
  // - un abbonamento: film della guida nel suo catalogo streaming;
  // con «Nascondi visti» acceso i visti escono (al loro posto entrano i successivi), altrimenti restano con la spunta
  // (Antonio, 24/11/2026); prima quelli della mia lista, poi per voto; un titolo
  // sta in una colonna sola (prima la TV, poi gli abbonamenti nell'ordine di rilievo) e non compare nelle altre righe della pagina.
  const heroTime = $derived.by(() => {
    const m = now.getHours() * 60 + now.getMinutes();
    return m >= 20 * 60 && m < 21 * 60 + 30;
  });
  const listIds = $derived(new Set(tonightList.map((i) => i.workId)));
  const inList = (p: GuideProgramme) => p.workId != null && listIds.has(p.workId);
  const byTop = (a: GuideProgramme, b: GuideProgramme) =>
    Number(inList(b)) - Number(inList(a)) || (b.rating ?? 0) - (a.rating ?? 0) || a.start.localeCompare(b.start);
  type TopCol = { key: string; title: string; service: string | null; list: GuideProgramme[] };
  const topCols = $derived.by((): TopCol[] => {
    const taken = new Set<string>();
    const k = (p: GuideProgramme) => (p.workId != null ? `w${p.workId}` : `t${p.title.trim().toLowerCase()}`);
    const pick = (cands: GuideProgramme[]) => {
      const out: GuideProgramme[] = [];
      for (const p of [...cands].sort(byTop)) {
        if (out.length === 3) break;
        if (seenState.hidden(p.workId) || taken.has(k(p))) continue;
        taken.add(k(p));
        out.push(p);
      }
      return out;
    };
    const late = now.getTime() - 20 * 60_000;
    const cols: TopCol[] = [{
      key: 'tv', service: null, title: now.getHours() >= 21 || now.getHours() < 2 ? 'Ancora stasera in TV' : 'Stasera in TV',
      list: pick(tonightFilms.filter((p) => inMySubscriptions(p, mySubs) && new Date(p.start).getTime() >= late && new Date(p.stop) > now)),
    }];
    for (const svc of platforms.sort(mySubs)) {
      const list = pick(guideFilms.filter((p) => streamingFor(p, [svc]).length > 0));
      if (list.length > 0) cols.push({ key: 's' + svc, service: svc, title: svc, list });
    }
    return cols;
  });
  // la top è un'unica riga che scorre in orizzontale: frecce ai lati, trascinamento col mouse, barra in basso (24/11/2026)
  let topsEl = $state<HTMLElement | null>(null);
  let topsAtStart = $state(true), topsAtEnd = $state(true);
  function topsEdges() {
    if (!topsEl) return;
    topsAtStart = topsEl.scrollLeft <= 2;
    topsAtEnd = topsEl.scrollLeft + topsEl.clientWidth >= topsEl.scrollWidth - 2;
  }
  function topsBy(dir: number) {
    if (!topsEl) return;
    const col = topsEl.querySelector<HTMLElement>('.topcol');
    topsEl.scrollBy({ left: dir * ((col?.offsetWidth ?? 320) + 24), behavior: 'smooth' });
  }
  $effect(() => {
    void topCols;
    if (!topsEl) return;
    topsEdges();
    const ro = new ResizeObserver(topsEdges);
    ro.observe(topsEl);
    return () => ro.disconnect();
  });
  // righe della pagina: almeno 18 titoli quando ci sono (Antonio, 24/11/2026: «almeno una quindicina»); i titoli della top e
  // i visti nascosti si tolgono PRIMA di tagliare, se no una riga restava con 5 locandine
  const ROW = 18;
  const fill = <T extends { workId?: number | null }>(l: T[]) =>
    l.filter((x) => !seenState.hidden(x.workId) && !(x.workId != null && topIds.has(x.workId))).slice(0, ROW);
  // opere della top: fuori dalle altre righe della pagina (solo quando la top si vede)
  const topIds = $derived(new Set(heroTime ? [] : topCols.flatMap((c) => c.list.map((p) => p.workId)).filter((x): x is number => x != null)));

  // la mia serata: si vede stasera un titolo che passa in TV di sera (dalle 20, entro le 2), che è in casa o che è in
  // streaming nei miei abbonamenti (es. un originale Prime Video); gli altri restano sotto, con il motivo. Un passaggio
  // di giorno (Geo alle 11) non è «stasera»: la locandina resta sotto con «Oggi 11:04» (scelta di Antonio, 20/11/2026)
  const onTvTonight = (i: TonightItem) => !!i.evening && new Date(i.evening.start) < eveningWindow(now).to;
  const tonightAvailable = $derived(tonightList.filter((i) => onTvTonight(i) || i.inHouse || streamingFor(i, mySubs).length > 0));
  // passati: già trasmessi negli ultimi 3 giorni e oggi non si vedono in nessun altro modo (nessun passaggio in arrivo,
  // non in casa, nessuna piattaforma): si mostrano solo con l'interruttore "Mostra programmazione passata"
  const onlyPast = (i: TonightItem) =>
    !!i.last && !i.next && !i.paidNext && !i.inHouse && (i.streaming?.length ?? 0) === 0 && (i.paid?.length ?? 0) === 0;
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
    rec?: GuideProgramme | null; drop?: () => void; dropLabel?: string;
    // «Visto» (23/11/2026): l'opera della locandina; onSeen ricarica dove il visto cambia l'elenco (la mia lista)
    workId?: number | null; onSeen?: () => void;
  };
  // canale e, per serie e programmi, il numero e il titolo della puntata (Antonio, 20/11/2026: va sempre esplicitato)
  const channelOf = (p: GuideProgramme) => [p.channelName || p.guideChannelName, episodeOf(p)].filter(Boolean).join(' · ');
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
    // oggi di giorno (dalle 6 alle 20): non è «stasera»
    if (midnight(s).getTime() === midnight(now).getTime() && s.getHours() >= 6 && s.getHours() < 20) return `Oggi ${r}`;
    return tonight ? r : `${day(p.start, now)} ${r}`;
  }
  const genreYear = (genre: string, year: number) => [genre, year || ''].filter(Boolean).join(' · ');
  // canale di un passaggio che non vedo (a pagamento, abbonamento che non ho)
  const paidChannel = (p: GuideProgramme) => `${channelOf(p)} · a pagamento`;

  function programmeCard(p: GuideProgramme, mode: 'tv' | 'ondemand' = 'tv'): Card {
    // gli altri passaggi dello stesso film nei prossimi giorni: uno che vedo e uno a pagamento danno TV ed € insieme
    const canSee = inMySubscriptions(p, mySubs);
    const others = p.workId != null ? guideFilms.filter((g) => g.workId === p.workId && g.id !== p.id) : [];
    const visible = canSee ? p : others.find((g) => inMySubscriptions(g, mySubs)) ?? null;
    const paidAiring = !canSee ? p : others.find((g) => !inMySubscriptions(g, mySubs)) ?? null;
    const avail = availOf(p, mySubs, mode === 'tv' ? visible : null, visible ? tvText(visible) : '', mode === 'tv' ? paidAiring : null);
    const meta = genreYear(p.sub || p.group, p.year);
    // su un canale che non vedo (Sky Cinema senza Sky) ma in streaming nei miei abbonamenti o in casa: si scrive come
    // lo vedo ("Su Netflix", "In casa") al posto del canale, senza orario (scelta di Antonio, 20/11/2026)
    const unseenTv = mode === 'tv' && !canSee;
    const lines = mode === 'ondemand' || (unseenTv && avail.mine.length > 0) ? [`Su ${shortNames(avail.mine)}`, meta]
      : unseenTv && avail.home ? ['In casa', meta]
      : [canSee ? channelOf(p) : paidChannel(p), slot(p), meta];
    const card: Card = {
      key: `p${p.id}`, poster: p.poster || p.icon, title: p.title, rating: p.rating ?? null, scheduled: p.scheduled, avail, rated: RATED_GROUPS.includes(p.group),
      lines, open: () => openWork(p), workId: p.workId,
    };
    return card;
  }

  function tonightCard(i: TonightItem, available: boolean): Card {
    const tv = onTvTonight(i) ? i.evening : null;
    // next ed evening sono passaggi che vedo; paidNext uno su un canale a pagamento che non ho (dal server)
    const avail = availOf(i, mySubs, i.next, i.next ? tvText(i.next) : '', i.paidNext);
    const g = i.next ?? i.paidNext ?? i.last;
    const meta = genreYear(g?.sub || g?.group || kindLabel[i.kind] || '', i.year);
    let lines: string[];
    if (tv) lines = [channelOf(tv), slot(tv), meta];
    else if (avail.mine.length > 0) lines = [`Su ${shortNames(avail.mine)}`, meta];
    else if (i.inHouse) lines = ['In casa', meta];
    else if (i.next) lines = [channelOf(i.next), slot(i.next), meta];
    else if (i.paidNext) lines = [paidChannel(i.paidNext), slot(i.paidNext), meta];
    else if (onlyPast(i) && i.last) lines = [channelOf(i.last), slot(i.last, true), meta];
    else lines = [laterReason(i), meta];
    return {
      key: `t${i.workId}`, poster: i.poster || g?.poster || g?.icon || '', title: i.title, rating: i.rating ?? null, rated: RATED_KINDS.includes(i.kind),
      scheduled: !!(tv ?? i.next)?.scheduled, avail, dim: !available,
      lines,
      open: () => router.go(`/opera/${i.workId}`),
      // si registra anche un passaggio di giorno (il prossimo)
      rec: recordable(tv ?? i.next) ? (tv ?? i.next) : null, drop: () => drop(i), dropLabel: 'Togli dalla mia lista',
      // segnato come visto esce dalla mia lista (lo fa il server): si rilegge
      workId: i.workId, onSeen: () => { load(); },
    };
  }

  // titolo di una lista dinamica (un risultato della ricerca): come in Cerca, in TV con il giorno e l'ora
  function hitCard(r: HomeRow, h: SearchHit): Card {
    // nextAiring è un passaggio che vedo, paidAiring uno su un canale a pagamento che non ho (dal server)
    const when = (t: string | null | undefined) => (t ? `${day(t, now).toLowerCase()} ${time(t)}` : '');
    const avail = availOfHit(h, mySubs, when(h.nextAiring), when(h.paidAiring));
    const meta = genreYear((h.genres ?? [])[0] ?? kindLabel[h.kind] ?? '', h.year);
    const lines = h.nextAiring ? [`In TV ${when(h.nextAiring)}`, meta]
      : avail.mine.length > 0 ? [`Su ${shortNames(avail.mine)}`, meta]
      : avail.home ? ['In casa', meta]
      : h.paidAiring ? [`${h.paidChannel} ${when(h.paidAiring)} · a pagamento`, meta]
      : avail.pay.length > 0 ? [`A pagamento: ${shortNames(avail.pay)}`, meta] : [meta];
    return {
      key: h.workId != null ? `w${h.workId}` : `t${h.tmdbKind}${h.tmdbId}`, poster: h.poster, title: h.title, rating: h.rating ?? null,
      rated: RATED_KINDS.includes(h.kind), scheduled: false, avail, lines, open: () => openHit(h),
      drop: h.workId != null ? () => noMore(r, h) : undefined, dropLabel: 'Non proporre più', workId: h.workId,
    };
  }
  // titolo di una lista a mano
  function entryCard(e: ListEntry): Card {
    const avail = availOf(e, mySubs, e.next, e.next ? tvText(e.next) : '', e.paidNext);
    const meta = genreYear(e.next?.sub || e.next?.group || kindLabel[e.kind] || '', e.year);
    const lines = e.next ? [channelOf(e.next), slot(e.next), meta]
      : avail.mine.length > 0 ? [`Su ${shortNames(avail.mine)}`, meta]
      : e.inHouse ? ['In casa', meta]
      : e.paidNext ? [paidChannel(e.paidNext), slot(e.paidNext), meta] : ['Per ora non si vede', meta];
    return {
      key: `e${e.workId}`, poster: e.poster || e.next?.poster || e.next?.icon || '', title: e.title, rating: e.rating ?? null,
      rated: RATED_KINDS.includes(e.kind), scheduled: !!e.next?.scheduled, avail, lines, open: () => router.go(`/opera/${e.workId}`),
      rec: e.next && recordable(e.next) ? e.next : null, workId: e.workId,
    };
  }

</script>

{#if loading}
  <p class="pad muted" role="status">Un momento…</p>
{:else}
  {#if error}<p class="pad error" role="alert">{error}</p>{/if}

  {#if !heroTime && topCols.some((c) => c.list.length > 0)}
    <!-- fuori dalle 20:00–21:30: «Il top per la serata» al posto del banner (24/11/2026) -->
    <section class="tops" aria-labelledby="tops-h">
      <h2 id="tops-h">Il top per la serata</h2>
      <div class="topsrow">
      {#if !topsAtStart}
        <button type="button" class="arrow left" aria-label="Colonne precedenti" onclick={() => topsBy(-1)}>
          <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M15 18l-6-6 6-6" /></svg></button>
      {/if}
      {#if !topsAtEnd}
        <button type="button" class="arrow right" aria-label="Colonne successive" onclick={() => topsBy(1)}>
          <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M9 18l6-6-6-6" /></svg></button>
      {/if}
      <div class="topcols" bind:this={topsEl} use:dragScroll onscroll={topsEdges}>
        {#each topCols as col (col.key)}
          <div class="topcol">
            <div class="kicker">{#if col.service}<ServiceIcon name={col.service} size={20} />{/if}{col.title.toUpperCase()}</div>
            {#if col.list.length === 0}
              <p class="muted small">Stasera in TV non resta niente da vedere dall'inizio.</p>
            {:else}
              <ol class="toplist">
                {#each col.list as p, n (p.id)}
                  <li>
                    <span class="topn" aria-hidden="true">{n + 1}</span>
                    <!-- due righe sempre (titolo; voto · anno · genere) e l'occhio sotto, come nelle altre righe; canale e orario
                         sono nella scheda (Antonio, 24/11/2026) -->
                    <button type="button" class="topposter" onclick={() => openWork(p)} aria-label="Apri la scheda di {p.title}" tabindex="-1">{#if p.poster || p.icon}<img src={p.poster || p.icon} alt="" loading="lazy" referrerpolicy="no-referrer" />{:else}<span class="ph" aria-hidden="true">{p.title.charAt(0)}</span>{/if}</button>
                    <div class="toptext">
                      <button type="button" class="topname" onclick={() => openWork(p)} aria-label="Apri la scheda di {p.title}">
                        <span class="toptitle">{p.title}</span>
                        <span class="topmeta">{[p.rating != null ? `★ ${vote(p.rating)}` : '', p.year ? String(p.year) : '', p.sub || p.group].filter(Boolean).join(' · ')}</span>
                      </button>
                      <SeenButton workId={p.workId} title={p.title} />
                    </div>
                  </li>
                {/each}
              </ol>
            {/if}
          </div>
        {/each}
      </div>
      </div>
    </section>
  {:else if hero && heroTime}
    <section class="hero">
      {#if hero.icon}<img class="backdrop" src={hero.icon} alt="" referrerpolicy="no-referrer" />{/if}
      <div class="hero-word" aria-hidden="true">{hero.title}</div>
      <div class="hero-body">
        <div class="kicker">{heroFromList ? 'DALLA MIA LISTA' : 'IL FILM DI STASERA'} · {when(hero).toUpperCase()} SU {(hero.channelName || hero.guideChannelName).toUpperCase()}</div>
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
          <Button variant="soft" size="l" onclick={() => openWork(hero)}>Dettagli</Button>
        </div>
      </div>
    </section>
  {/if}

  <div class="pad sections">
    <div class="toprow">
      <AvailLegend />
      <!-- apri o chiudi tutte le sezioni della pagina (Antonio, 20/11/2026) -->
      <div class="allbtns">
        <!-- «Nascondi visti» (23/11/2026, Antonio): toglie da tutte le righe i titoli segnati come visti -->
        {#if session.profile}
          <label class="switch seenswitch">
            <input type="checkbox" role="switch" checked={seenState.hide} onchange={(e) => seenState.setHide(e.currentTarget.checked)} />
            <span class="track" aria-hidden="true"></span>
            Nascondi visti
          </label>
        {/if}
        <button type="button" class="allbtn" onclick={() => sectionsCommand.all(false)} title="Apri tutte le sezioni">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M7 15l5 5 5-5" /><path d="M7 9l5-5 5 5" /></svg>Apri tutte</button>
        <button type="button" class="allbtn" onclick={() => sectionsCommand.all(true)} title="Chiudi tutte le sezioni">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M7 20l5-5 5 5" /><path d="M7 4l5 5 5-5" /></svg>Chiudi tutte</button>
      </div>
    </div>
    <!-- La mia lista (20/11/2026, Antonio): «Da vedere stasera» = i titoli della lista che si vedono stasera (in TV di sera,
         in casa, on demand nei miei abbonamenti); «I miei titoli» = gli altri, non in programma stasera né on demand -->
    <Section key="serata" title="Da vedere stasera">
      {#snippet aside()}{tonightList.length === 0 ? '' : `${tonightAvailable.length} ${tonightAvailable.length === 1 ? 'titolo' : 'titoli'} della tua lista`}{/snippet}
      {#if tonightAvailable.length > 0}
        {@render row(tonightAvailable.map((i) => tonightCard(i, true)))}
      {:else if tonightList.length === 0}
        <p class="muted small">Aggiungi i titoli che vuoi vedere (dalla guida, dalla scheda o con la lente): quando si possono vedere compaiono qui.</p>
      {:else}
        <p class="muted small">Stasera niente dalla tua lista: i titoli sono in «I miei titoli», qui sotto.</p>
      {/if}
    </Section>

    {#if tonightLater.length > 0 || tonightPast.length > 0}
      <Section key="miei-titoli" title="I miei titoli">
        {#snippet tools()}
          {#if tonightPast.length > 0}
            <label class="switch">
              <input type="checkbox" role="switch" checked={showPast} onchange={(e) => setShowPast(e.currentTarget.checked)} />
              <span class="track" aria-hidden="true"></span>
              Mostra programmazione passata ({tonightPast.length})
            </label>
          {/if}
        {/snippet}
        {#snippet aside()}non stasera e non on demand: quando passano in TV e dove{/snippet}
        {#if tonightLater.length > 0 || showPast}
          {@render row([...tonightLater.map((i) => tonightCard(i, true)), ...(showPast ? tonightPast.map((i) => tonightCard(i, true)) : [])])}
        {:else}
          <p class="muted small">{tonightPast.length === 1 ? 'Un titolo è già passato' : `${tonightPast.length} titoli sono già passati`} negli ultimi giorni.</p>
        {/if}
      </Section>
    {/if}

    {#if continuing.length > 0 && shown('continua')}
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
          <!-- la pennina subito dopo il nome: modifica la ricerca (lista dinamica) o apre la lista (Antonio, 20/11/2026) -->
          {#snippet after()}<a class="edit" {href} onclick={navigate} aria-label={dynamic ? `Modifica la ricerca di ${r.list.name}` : `Modifica la lista ${r.list.name}`} title={dynamic ? 'Modifica la ricerca' : 'Modifica la lista'}><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 20h9" /><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4z" /></svg></a>{/snippet}
          {#if r.error}<p class="error small">{r.error}</p>
          {:else if dynamic && r.hits.length > 0}
            {@render row(fill(r.hits).map((h) => hitCard(r, h)), r.more ? { href, label: 'Tutta la lista', hint: 'con la sua ricerca' } : null)}
          {:else if !dynamic && r.entries.length > 0}
            {@render row(fill(r.entries).map(entryCard), r.more ? { href, label: 'Tutta la lista', hint: `${r.list.count} ${r.list.count === 1 ? 'titolo' : 'titoli'}` } : null)}
          {:else if r.loading}<p class="muted small" role="status">Cerco i titoli…</p>
          {:else}<p class="muted small">{dynamic ? 'Per ora nessun titolo con questa ricerca.' : 'La lista è vuota.'}</p>{/if}
        </Section>
      {/each}
      {#if homeLoaded && homeRows.length === 0 && session.profile}
        <Section key="liste" title="Le mie liste">
          <p class="muted small">Qui compaiono le liste che scegli in <a href="/preferenze#serata" onclick={navigate}>Preferenze › La mia lista</a>. Una lista dinamica si crea dalla <a href="/cerca" onclick={navigate}>ricerca</a>: scegli i filtri e «Salva come lista».</p>
        </Section>
      {/if}
    </div>

    {#if (live.length > 0 || upcoming.length > 0) && shown('registrazioni')}
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
        {#each list.filter((c) => !seenState.hidden(c.workId) && !(c.workId != null && topIds.has(c.workId))) as c (c.key)}
          <li class:dim={c.dim}>
            <button type="button" class="card" data-k={c.key} onclick={c.open}>
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
            {#if c.rec || c.workId != null || c.drop}
              <div class="c-acts">
                {#if c.rec}
                  {@const r = c.rec}
                  <button type="button" class="mini rec" class:on={r.scheduled} disabled={busy === r.id} onclick={() => quickRecord(r)}
                    aria-pressed={r.scheduled} aria-label={r.scheduled ? 'Programmato: apri' : `Registra ${c.title}`} title={r.scheduled ? 'Programmato' : 'Registra'}>
                    <span class="dot" aria-hidden="true"></span>{r.scheduled ? 'Programmato' : 'Registra'}
                  </button>
                {/if}
                <SeenButton workId={c.workId} title={c.title} onchange={c.onSeen} />
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
      {#if shown('stasera-miei')}
      <Section key="stasera-miei" title="Stasera in TV · Nei miei abbonamenti">
        {#snippet aside()}<span class="mysubs">dal voto più alto · <a href="/preferenze#abbonamenti" onclick={navigate}>I miei abbonamenti</a>{#each platforms.sort(mySubs) as s (s)}<ServiceIcon name={s} size={24} />{/each}</span>{/snippet}
        <h3 class="subrow">In diretta <span class="muted small">stasera sui canali in chiaro e su quelli dei tuoi abbonamenti</span></h3>
        {#if filmsMine.length > 0}{@render row(fill(filmsMine).map((p) => programmeCard(p)), { filter: { kind: 'film', avail: ['tv'] }, label: 'Tutti i film in TV', hint: 'nei prossimi giorni, con i filtri' })}{:else}<p class="muted small">Nessun film stasera in chiaro o sui canali dei tuoi abbonamenti.</p>{/if}
        <h3 class="subrow">On demand <span class="muted small">da vedere quando vuoi nei cataloghi dei tuoi abbonamenti</span></h3>
        {#if onDemand.length > 0}
          {@render row(fill(onDemand).map((p) => programmeCard(p, 'ondemand')), { filter: { kind: 'film', avail: ['mine'] }, label: 'Tutti i film nei tuoi abbonamenti', hint: 'il catalogo completo, con i filtri' })}
        {:else}
          <p class="muted small">{mySubs.length === 0 ? 'Scegli i tuoi abbonamenti (Netflix, Prime Video…) in Preferenze: qui compaiono i film dei loro cataloghi.' : 'Nessun film della guida nei cataloghi dei tuoi abbonamenti, per ora.'}</p>
        {/if}
      </Section>
      {/if}
      {#if filmsOther.length > 0 && shown('stasera-altri')}
        <Section key="stasera-altri" title="Stasera in TV · Non nei miei abbonamenti">
          {#snippet aside()}a pagamento fuori dai tuoi abbonamenti ({otherPlatforms.join(', ')}){/snippet}
          {@render row(fill(filmsOther).map((p) => programmeCard(p)), { filter: { kind: 'film', avail: ['paytv'] }, label: 'Tutti i film in TV a pagamento', hint: 'sui canali che non hai, nei prossimi giorni' })}
        </Section>
      {/if}
    {/if}
    {#if weekFilms.length > 0 && shown('settimana')}
      <Section key="settimana" title="Questa settimana">
        {#snippet aside()}film con voto 7 e oltre nei prossimi giorni{/snippet}
        {@render row(fill(weekFilms).map((p) => programmeCard(p)), { filter: { kind: 'film', avail: ['tv'], minRating: 7 }, label: 'Tutti i film da 7 in su', hint: 'in TV nei prossimi giorni' })}
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


<style>
  .toprow { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 8px 16px; }
  .allbtns { display: flex; flex-wrap: wrap; align-items: center; gap: 4px; }
  .seenswitch { margin-right: 10px; }
  .allbtn { display: inline-flex; align-items: center; gap: 6px; min-height: 36px; padding: 0 10px; border: 0; border-radius: var(--radius-s); background: none; color: var(--muted); font: inherit; font-size: 13px; font-weight: 600; cursor: pointer; }
  .allbtn:hover { color: var(--text); background: var(--card); }
  .edit { display: inline-flex; align-items: center; justify-content: center; width: 36px; height: 36px; border-radius: var(--radius-s); color: var(--muted); }
  .edit:hover { color: var(--text); background: var(--card); }
  .pad { padding: 0 var(--gutter); }
  .muted { color: var(--muted); margin: 0; }
  .small { font-size: 13px; }
  .error { color: var(--rec-text); }
  .tops { padding: 24px var(--gutter) 8px; background: linear-gradient(180deg, #172033, transparent); display: flex; flex-direction: column; gap: 14px; }
  .tops h2 { font-size: 26px; margin: 0; }
  .topsrow { position: relative; }
  .arrow { position: absolute; top: calc(50% - 26px); z-index: 2; width: 44px; height: 44px; border-radius: 50%; border: 1px solid var(--line-btn);
    background: rgba(13, 16, 21, 0.85); color: var(--text); display: inline-flex; align-items: center; justify-content: center; cursor: pointer; box-shadow: 0 4px 16px rgba(0, 0, 0, 0.5); }
  .arrow.left { left: -14px; }
  .arrow.right { right: -14px; }
  .arrow:hover { background: var(--raised); }
  @media (hover: none) { .arrow { display: none; } }
  .topcols { display: grid; grid-auto-flow: column; grid-auto-columns: min(300px, 86vw); gap: 24px; overflow-x: auto; scroll-snap-type: x proximity; padding-bottom: 12px;
    scrollbar-width: thin; scrollbar-color: var(--line-btn) transparent; }
  .topcols::-webkit-scrollbar { height: 8px; }
  .topcols::-webkit-scrollbar-thumb { background: var(--line-btn); border-radius: 4px; }
  .topcol { scroll-snap-align: start; }
  :global(.topcols.dragging) { cursor: grabbing; scroll-snap-type: none; user-select: none; }
  .tops .kicker { display: flex; align-items: center; gap: 8px; }
  .topcol { display: flex; flex-direction: column; gap: 12px; min-width: 0; }
  .toplist { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 10px; }
  .toplist li { display: grid; grid-template-columns: 22px 56px minmax(0, 1fr); align-items: center; gap: 10px 12px; }
  .topn { font-family: var(--font-display); font-size: 26px; font-weight: 700; color: var(--muted); text-align: center; }
  .topname { display: flex; flex-direction: column; gap: 3px; padding: 0; border: 0; background: none; color: inherit; text-align: left; cursor: pointer; min-width: 0; }
  .topname:hover .toptitle, .topname:focus-visible .toptitle { text-decoration: underline; }
  .topposter { width: 56px; height: 84px; padding: 0; border: 0; cursor: pointer; border-radius: 6px; overflow: hidden; background: var(--raised); display: flex; align-items: center; justify-content: center; }
  .topposter img { width: 100%; height: 100%; object-fit: cover; display: block; }
  .topposter .ph { font-family: var(--font-display); font-size: 22px; font-weight: 700; color: var(--muted); }
  .toptext { display: flex; flex-direction: column; align-items: flex-start; gap: 8px; min-width: 0; }
  .toptitle { font-size: 16px; font-weight: 700; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .topmeta { font-size: 13px; color: var(--text-2); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
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
  /* come RecordButton: «Registra» scuro con il bordo chiaro, «Programmato» chiaro; sempre pallino rosso + testo */
  .mini.rec { flex: 1 1 auto; background: var(--bg); color: var(--text); border: 0; box-shadow: inset 0 0 0 1.5px var(--text); }
  .mini.rec.on { background: var(--text); color: var(--bg); box-shadow: none; }
  .mini.ghost { border-color: transparent; background: none; color: var(--muted); }
  .mini .dot { width: 9px; height: 9px; border-radius: 50%; background: var(--rec-dot); }
  .empty { background: var(--surface); border: 1px dashed var(--line-btn); border-radius: var(--radius-l); padding: 20px; }
  .empty p { margin: 0 0 6px; }
  @media (max-width: 720px) { .hero { min-height: 300px; } .tonight { grid-template-columns: 1fr; } .cards { grid-auto-columns: 140px; } }
</style>
