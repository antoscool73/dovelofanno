<script lang="ts">
  // Home: risponde a "stasera che si guarda?". In evidenza il titolo migliore della serata, poi "La mia serata in TV",
  // le registrazioni in corso e in arrivo, i film di stasera e quelli della settimana per voto.
  import Badge from '../lib/components/Badge.svelte';
  import PosterImg from '../lib/components/PosterImg.svelte';
  import Button from '../lib/components/Button.svelte';
  import RecordButton from '../lib/components/RecordButton.svelte';
  import ProgrammaDialog from './ProgrammaDialog.svelte';
  import Section from '../lib/components/Section.svelte';
  import { inMySubscriptions, mySubscriptionsFor, payPlatform, streamingFor } from '../lib/pay';
  import { api, ApiError, type GuideProgramme, type LibraryItem, type Proposal, type Recording, type TonightItem } from '../lib/api/client';
  import { day, minutes, between, range, time, vote, midnight } from '../lib/format';
  import { navigate, router } from '../lib/router.svelte';
  import { session } from '../lib/session.svelte';

  let tonightList = $state<TonightItem[]>([]);
  let recordings = $state<Recording[]>([]);
  let tonightFilms = $state<GuideProgramme[]>([]);
  let weekFilms = $state<GuideProgramme[]>([]);
  let proposals = $state<Proposal[]>([]);
  let hasPreferences = $state(true);
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
      const [list, recs, films, week, props, prefs, cont, subs, ahead] = await Promise.all([
        prof ? api.tonight(prof.id) : Promise.resolve([]),
        api.recordings(),
        api.guide({ from, to, group: 'Film', max: 500 }),
        api.guide({ from: tomorrow, to: new Date(tomorrow.getTime() + 6 * 86_400_000), group: 'Film', minRating: 7, max: 1000 }),
        prof ? api.proposals(prof.id, 2) : Promise.resolve([]),
        prof ? api.preferences(prof.id) : Promise.resolve([]),
        prof ? api.continueWatching(prof.id).catch(() => []) : Promise.resolve([]),
        prof ? api.profileSubscriptions(prof.id).then((x) => x.services).catch(() => []) : Promise.resolve([]),
        api.guide({ from: now, to: new Date(now.getTime() + 8 * 86_400_000), group: 'Film', max: 5000 }),
      ]);
      guideFilms = ahead;
      mySubs = subs;
      continuing = cont;
      proposals = props;
      hasPreferences = prefs.length > 0;
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

  async function noMore(p: Proposal) {
    const prof = session.profile;
    if (!prof || p.programme.workId == null) return;
    try { await api.ignore(prof.id, p.programme.workId); proposals = proposals.filter((x) => x !== p); }
    catch (e) { error = e instanceof ApiError ? e.message : 'Non salvato'; }
  }

  // dopo una notifica "nuove proposte" (/#proposte) si scende alla sezione
  $effect(() => {
    if (!loading && location.hash === '#proposte') setTimeout(() => document.getElementById('proposte')?.scrollIntoView({ behavior: 'smooth' }), 100);
  });

  // Stasera in TV divisa: quello che si vede (in chiaro o con i miei abbonamenti) e quello a pagamento fuori abbonamento
  // un film che passa sia su un canale visibile sia su uno a pagamento resta solo tra quelli visibili
  const filmsMine = $derived(dedupe(tonightFilms.filter((p) => inMySubscriptions(p, mySubs))));
  const filmsOther = $derived.by(() => {
    const mine = new Set(filmsMine.map((p) => p.title.trim().toLowerCase()));
    return dedupe(tonightFilms.filter((p) => !inMySubscriptions(p, mySubs) && !mine.has(p.title.trim().toLowerCase())));
  });
  // on demand: film nei cataloghi streaming dei miei abbonamenti (dai film della guida: TMDB li conosce), per voto
  const onDemand = $derived(
    dedupe(guideFilms.filter((p) => streamingFor(p, mySubs).length > 0)).sort((a, b) => (b.rating ?? 0) - (a.rating ?? 0)),
  );
  const otherPlatforms = $derived([...new Set(filmsOther.map((p) => payPlatform(p)).filter((x): x is string => !!x))]);

  const tonightAvailable = $derived(tonightList.filter((i) => i.next && new Date(i.next.start) < eveningWindow(now).to));
  const tonightLater = $derived(tonightList.filter((i) => !tonightAvailable.includes(i)));
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
    <Section key="serata" title="La mia serata in TV">
      {#snippet aside()}{tonightList.length === 0 ? 'Aggiungi i titoli che vuoi vedere dalla guida: compaiono qui appena passano.' : `${tonightList.length} ${tonightList.length === 1 ? 'titolo' : 'titoli'}, ${tonightAvailable.length} stasera`}{/snippet}
      {#if tonightAvailable.length > 0}
        <ul class="tonight">
          {#each tonightAvailable as i (i.workId)}
            {@const n = i.next!}
            <li>
              <div class="t-main">
                <button type="button" class="t-title" onclick={() => (open = n)}>{i.title}</button>
                <div class="muted small">{[i.year || '', n.sub || n.group, i.rating != null ? `★ ${vote(i.rating)} ${i.ratingSource}` : ''].filter(Boolean).join(' · ')}</div>
                <div class="t-where"><Badge tone="blue">In onda</Badge> {when(n)} · {n.channelName || n.guideChannelName}</div>
              </div>
              <div class="t-acts">
                {#if recordable(n)}<RecordButton size="m" scheduled={n.scheduled} busy={busy === n.id} onclick={() => quickRecord(n)} />{/if}
                <Button size="s" onclick={() => seen(i)}>✓ Visto</Button>
              </div>
            </li>
          {/each}
        </ul>
      {/if}
      {#if tonightLater.length > 0}
        <p class="muted small later">
          Prossimamente dalla lista:
          {#each tonightLater as i, k (i.workId)}{k > 0 ? ', ' : ''}<button type="button" class="link" onclick={() => (i.next ? (open = i.next) : drop(i))} title={i.next ? '' : 'Nessun passaggio nei prossimi giorni: tocca per togliere'}>{i.title}</button> ({i.next ? `${day(i.next.start, now).toLowerCase()} ${time(i.next.start)}, ${i.next.channelName || i.next.guideChannelName}` : 'nessun passaggio in guida'}){/each}.
        </p>
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

    <Section key="proposte" id="proposte" title="Proposte per te">
      {#snippet aside()}<a href="/preferenze#proposte" onclick={navigate}>{hasPreferences ? 'Cambia le preferenze' : 'Scegli cosa proporti'}</a>{/snippet}
      {#if proposals.length > 0}
        <ul class="tonight">
          {#each proposals.slice(0, 12) as x (x.programme.id)}
            {@const n = x.programme}
            <li class:conflict={x.conflict}>
              <div class="t-main">
                <button type="button" class="t-title" onclick={() => (open = n)}>{n.title}</button>
                <div class="muted small">{[n.year || '', n.sub || n.group, n.rating != null ? `★ ${vote(n.rating)} ${n.ratingSource}` : '', `da «${x.preference}»`].filter(Boolean).join(' · ')}</div>
                <div class="t-where">{when(n)} · {n.channelName || n.guideChannelName}{x.otherAirings > 0 ? ` · altri ${x.otherAirings} passaggi` : ''}</div>
                {#if x.conflict}<div class="small warn">Si sovrappone ad altre registrazioni oltre il limite</div>{/if}
              </div>
              <div class="t-acts">
                {#if recordable(n)}<RecordButton size="m" scheduled={n.scheduled} busy={busy === n.id} onclick={() => quickRecord(n)} />{/if}
                <Button size="s" variant="ghost" onclick={() => noMore(x)}>Non proporre più</Button>
              </div>
            </li>
          {/each}
        </ul>
      {:else}
        <p class="muted small">{hasPreferences ? 'Niente di nuovo per oggi e domani dalle tue preferenze.' : 'Dì a DoveLoFanno cosa ti piace (generi, voto minimo, attori): qui compaiono i programmi giusti di oggi e domani.'}</p>
      {/if}
    </Section>

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

    {#snippet cards(list: GuideProgramme[], markMine = false, onDemandMode = false)}
      <ul class="cards">
        {#each list as p (p.id)}
          {@const subs = markMine ? mySubscriptionsFor(p, mySubs) : []}
          <li>
            <button type="button" class="card" class:mine={subs.length > 0} onclick={() => (open = p)}
              title={subs.length > 0 ? `Nei tuoi abbonamenti: ${subs.join(', ')}` : undefined}>
              <span class="poster">
                <PosterImg src={p.poster || p.icon} title={p.title} />
                {#if p.rating != null}<span class="stars">★ {vote(p.rating)}</span>{/if}
                {#if p.scheduled}<span class="recdot" aria-label="in programma"></span>{/if}
              </span>
              <span class="c-title">{p.title}</span>
              {#if onDemandMode}
                <span class="onsub small">Su {streamingFor(p, mySubs).join(', ')}</span>
              {:else}
                <span class="muted small">{when(p)} · {p.channelName || p.guideChannelName}</span>
              {/if}
              <span class="muted small">{[p.sub, p.year || '', p.country].filter(Boolean).join(' · ')}</span>
              {#if subs.length > 0}<span class="onsub small">Nei tuoi abbonamenti: {subs.join(', ')}</span>{/if}
            </button>
          </li>
        {/each}
      </ul>
    {/snippet}

    {#if tonightFilms.length > 0}
      <Section key="stasera-miei" title="Stasera in TV · Nei miei abbonamenti">
        {#snippet aside()}dal voto più alto · <a href="/preferenze#abbonamenti" onclick={navigate}>I miei abbonamenti</a>{/snippet}
        <h3 class="subrow">In diretta <span class="muted small">stasera sui canali in chiaro e su quelli dei tuoi abbonamenti</span></h3>
        {#if filmsMine.length > 0}{@render cards(filmsMine.slice(0, 18))}{:else}<p class="muted small">Nessun film stasera in chiaro o sui canali dei tuoi abbonamenti.</p>{/if}
        <h3 class="subrow">On demand <span class="muted small">da vedere quando vuoi nei cataloghi dei tuoi abbonamenti</span></h3>
        {#if onDemand.length > 0}
          {@render cards(onDemand.slice(0, 18), false, true)}
        {:else}
          <p class="muted small">{mySubs.length === 0 ? 'Scegli i tuoi abbonamenti (Netflix, Prime Video…) in Preferenze: qui compaiono i film dei loro cataloghi.' : 'Nessun film della guida nei cataloghi dei tuoi abbonamenti, per ora.'}</p>
        {/if}
      </Section>
      {#if filmsOther.length > 0}
        <Section key="stasera-altri" title="Stasera in TV · Non nei miei abbonamenti">
          {#snippet aside()}a pagamento fuori dai tuoi abbonamenti ({otherPlatforms.join(', ')}){/snippet}
          {@render cards(filmsOther.slice(0, 18))}
        </Section>
      {/if}
    {/if}
    {#if weekFilms.length > 0}
      <Section key="settimana" title="Questa settimana">
        {#snippet aside()}film con voto 7 e oltre nei prossimi giorni · <span class="legend"></span> nei tuoi abbonamenti{/snippet}
        {@render cards(weekFilms, true)}
      </Section>
    {/if}

    {#if !hero && tonightFilms.length === 0 && weekFilms.length === 0}
      <div class="empty">
        <p>La guida è vuota.</p>
        <p class="muted">Arriva dalla playlist (se la indica) o da una guida aggiunta in <a href="/impostazioni" onclick={navigate}>Impostazioni</a>. Senza la chiave TMDB i film non hanno voto: si aggiunge in Impostazioni › Voti dei film.</p>
      </div>
    {/if}
    <p class="muted small"><a href="/guida" onclick={(e) => { e.preventDefault(); router.go('/guida'); }}>Tutta la guida</a> · <a href="/libreria" onclick={navigate}>Continua a guardare nella Libreria</a></p>
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
  .tonight { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(340px, 1fr)); gap: 12px; }
  .tonight li { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px; background: var(--card); border: 1px solid var(--line-strong); border-radius: 12px; padding: 14px 16px; }
  .t-main { display: flex; flex-direction: column; gap: 4px; min-width: 0; flex: 1 1 200px; }
  .t-title { border: 0; background: none; padding: 0; color: var(--text); font-size: 17px; font-weight: 600; text-align: left; cursor: pointer; }
  .t-where { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; font-size: 14px; color: var(--text-2); }
  .t-acts { display: flex; flex-wrap: wrap; gap: 8px; }
  .later { margin-top: 10px; }
  .tonight li.conflict { border-color: #4a3a2a; }
  .warn { color: var(--orange-text); }
  .bar { height: 4px; border-radius: 2px; background: var(--raised); overflow: hidden; margin-top: 6px; }
  .bar span { display: block; height: 100%; background: var(--blue); }
  .link { border: 0; background: none; padding: 0; color: var(--link); font: inherit; cursor: pointer; }
  .recs { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 8px; }
  .recs li { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 14px; background: var(--card); border: 1px solid var(--line-strong); border-radius: 12px; padding: 12px 16px; }
  .rec-live { border-color: #3a2a2e; }
  .r-title { font-weight: 600; }
  .cards { list-style: none; margin: 0; padding: 0 0 8px; display: grid; grid-auto-flow: column; grid-auto-columns: 180px; gap: 16px; overflow-x: auto; scroll-snap-type: x proximity; }
  .cards li { scroll-snap-align: start; }
  .card { width: 100%; display: flex; flex-direction: column; gap: 4px; padding: 0; border: 0; background: none; color: var(--text); text-align: left; cursor: pointer; }
  .poster { position: relative; aspect-ratio: 2 / 3; border-radius: 12px; background: #2b3d63; overflow: hidden; display: block; margin-bottom: 4px; }
  .card:hover .poster, .card:focus-visible .poster { box-shadow: 0 0 0 2px var(--text); }
  /* nei miei abbonamenti: bordo verde del logo (#34C77B) */
  .card.mine .poster { box-shadow: 0 0 0 3px var(--green); }
  .card.mine:hover .poster, .card.mine:focus-visible .poster { box-shadow: 0 0 0 3px var(--green), 0 0 0 5px var(--text); }
  .onsub { color: var(--green-text); font-weight: 600; }
  .subrow { font-size: 16px; margin: 4px 0 10px; display: flex; flex-wrap: wrap; align-items: baseline; gap: 4px 12px; }
  .subrow + :global(ul), .subrow + p { margin-bottom: 20px; }
  .legend { display: inline-block; width: 12px; height: 12px; border-radius: 3px; border: 2px solid var(--green); vertical-align: -1px; }
  .stars { position: absolute; top: 8px; left: 8px; background: rgba(13, 16, 21, 0.8); border-radius: 6px; padding: 2px 7px; font-size: 12px; font-weight: 700; }
  .recdot { position: absolute; top: 10px; right: 10px; width: 10px; height: 10px; border-radius: 50%; background: var(--rec); }
  .c-title { font-weight: 600; font-size: 15px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .empty { background: var(--surface); border: 1px dashed var(--line-btn); border-radius: var(--radius-l); padding: 20px; }
  .empty p { margin: 0 0 6px; }
  @media (max-width: 720px) { .hero { min-height: 300px; } .tonight { grid-template-columns: 1fr; } .cards { grid-auto-columns: 140px; } }
</style>
