<script lang="ts">
  // Home: risponde a "stasera che si guarda?". In evidenza il titolo migliore della serata, poi "La mia serata in TV",
  // le registrazioni in corso e in arrivo, i film di stasera e quelli della settimana per voto.
  import Badge from '../lib/components/Badge.svelte';
  import Button from '../lib/components/Button.svelte';
  import RecordButton from '../lib/components/RecordButton.svelte';
  import ProgrammaDialog from './ProgrammaDialog.svelte';
  import { api, ApiError, type GuideProgramme, type Recording, type TonightItem } from '../lib/api/client';
  import { day, minutes, between, range, time, vote, midnight } from '../lib/format';
  import { navigate, router } from '../lib/router.svelte';
  import { session } from '../lib/session.svelte';

  let tonightList = $state<TonightItem[]>([]);
  let recordings = $state<Recording[]>([]);
  let tonightFilms = $state<GuideProgramme[]>([]);
  let weekFilms = $state<GuideProgramme[]>([]);
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
      const [list, recs, films, week] = await Promise.all([
        prof ? api.tonight(prof.id) : Promise.resolve([]),
        api.recordings(),
        api.guide({ from, to, group: 'Film', max: 500 }),
        api.guide({ from: tomorrow, to: new Date(tomorrow.getTime() + 6 * 86_400_000), group: 'Film', minRating: 7, max: 1000 }),
      ]);
      tonightList = list;
      recordings = recs;
      // stasera: film che iniziano dalle 20 alle 2; quelli già in onda contano solo se è già sera
      const evening = now.getHours() >= 20 || now.getHours() < 2;
      const isEvening = (d: Date) => d.getHours() >= 20 || d.getHours() < 2;
      tonightFilms = dedupe(films.filter((p) => (new Date(p.start) < now ? evening : isEvening(new Date(p.start)))))
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
      const k = p.workKey || `${p.title}|${p.year}`;
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
          <Button variant="soft" size="l" onclick={() => (open = hero)}>Dettagli</Button>
        </div>
      </div>
    </section>
  {/if}

  <div class="pad sections">
    <section>
      <div class="sec-head">
        <h2>La mia serata in TV</h2>
        <span class="muted small">{tonightList.length === 0 ? 'Aggiungi i titoli che vuoi vedere dalla guida: compaiono qui appena passano.' : `${tonightList.length} ${tonightList.length === 1 ? 'titolo' : 'titoli'}, ${tonightAvailable.length} stasera`}</span>
      </div>
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
    </section>

    {#if live.length > 0 || upcoming.length > 0}
      <section>
        <div class="sec-head"><h2>In registrazione e in arrivo</h2><a href="/registrazioni" onclick={navigate}>Tutte le registrazioni</a></div>
        <ul class="recs">
          {#each live as r (r.id)}
            <li class="rec-live"><Badge tone="red">In registrazione</Badge><span class="r-title">{r.title}</span><span class="muted small">{r.channelName} · {range(r.start, r.stop)} · mancano {minutes(Math.max(0, between(now.toISOString(), r.recordTo)))}</span></li>
          {/each}
          {#each upcoming as r (r.id)}
            <li><Badge tone="blue">{day(r.start, now)} {time(r.start)}</Badge><span class="r-title">{r.title}</span><span class="muted small">{r.channelName} · {range(r.start, r.stop)}</span></li>
          {/each}
        </ul>
      </section>
    {/if}

    {#snippet row(title: string, note: string, list: GuideProgramme[])}
      <section>
        <div class="sec-head"><h2>{title}</h2><span class="muted small">{note}</span></div>
        <ul class="cards">
          {#each list as p (p.id)}
            <li>
              <button type="button" class="card" onclick={() => (open = p)}>
                <span class="poster" class:has-img={!!p.icon}>
                  {#if p.icon}<img src={p.icon} alt="" loading="lazy" referrerpolicy="no-referrer" />{:else}<span class="poster-title">{p.title}</span>{/if}
                  {#if p.rating != null}<span class="stars">★ {vote(p.rating)}</span>{/if}
                  {#if p.scheduled}<span class="recdot" aria-label="in programma"></span>{/if}
                </span>
                <span class="c-title">{p.title}</span>
                <span class="muted small">{when(p)} · {p.channelName || p.guideChannelName}</span>
                <span class="muted small">{[p.sub, p.year || '', p.country].filter(Boolean).join(' · ')}</span>
              </button>
            </li>
          {/each}
        </ul>
      </section>
    {/snippet}

    {#if tonightFilms.length > 0}
      {@render row('Stasera in TV', 'i film di stasera, dal voto più alto', tonightFilms.slice(0, 18))}
    {/if}
    {#if weekFilms.length > 0}
      {@render row('Questa settimana', 'film con voto 7 e oltre nei prossimi giorni', weekFilms)}
    {/if}

    {#if !hero && tonightFilms.length === 0 && weekFilms.length === 0}
      <div class="empty">
        <p>La guida è vuota.</p>
        <p class="muted">Arriva dalla playlist (se la indica) o da una guida aggiunta in <a href="/impostazioni" onclick={navigate}>Impostazioni</a>. Senza la chiave TMDB i film non hanno voto: si aggiunge in Impostazioni › Voti dei film.</p>
      </div>
    {/if}
    <p class="muted small">Le proposte su misura arrivano con le preferenze; «Continua a guardare» con la libreria e il player. <a href="/guida" onclick={(e) => { e.preventDefault(); router.go('/guida'); }}>Tutta la guida</a></p>
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
  .sec-head { display: flex; flex-wrap: wrap; align-items: baseline; justify-content: space-between; gap: 6px 16px; margin-bottom: 12px; }
  h2 { font-size: 22px; }
  .tonight { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(340px, 1fr)); gap: 12px; }
  .tonight li { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px; background: var(--card); border: 1px solid var(--line-strong); border-radius: 12px; padding: 14px 16px; }
  .t-main { display: flex; flex-direction: column; gap: 4px; min-width: 0; flex: 1 1 200px; }
  .t-title { border: 0; background: none; padding: 0; color: var(--text); font-size: 17px; font-weight: 600; text-align: left; cursor: pointer; }
  .t-where { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; font-size: 14px; color: var(--text-2); }
  .t-acts { display: flex; flex-wrap: wrap; gap: 8px; }
  .later { margin-top: 10px; }
  .link { border: 0; background: none; padding: 0; color: var(--link); font: inherit; cursor: pointer; }
  .recs { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 8px; }
  .recs li { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 14px; background: var(--card); border: 1px solid var(--line-strong); border-radius: 12px; padding: 12px 16px; }
  .rec-live { border-color: #3a2a2e; }
  .r-title { font-weight: 600; }
  .cards { list-style: none; margin: 0; padding: 0 0 8px; display: grid; grid-auto-flow: column; grid-auto-columns: 180px; gap: 16px; overflow-x: auto; scroll-snap-type: x proximity; }
  .cards li { scroll-snap-align: start; }
  .card { width: 100%; display: flex; flex-direction: column; gap: 4px; padding: 0; border: 0; background: none; color: var(--text); text-align: left; cursor: pointer; }
  .poster { position: relative; aspect-ratio: 2 / 3; border-radius: 12px; background: #2b3d63; overflow: hidden; display: flex; align-items: flex-end; padding: 12px; margin-bottom: 4px; }
  .card:hover .poster, .card:focus-visible .poster { box-shadow: 0 0 0 2px var(--text); }
  .poster img { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; }
  .poster-title { position: relative; font-family: var(--font-display); font-size: 20px; font-weight: 700; line-height: 1.1; }
  .stars { position: absolute; top: 8px; left: 8px; background: rgba(13, 16, 21, 0.8); border-radius: 6px; padding: 2px 7px; font-size: 12px; font-weight: 700; }
  .recdot { position: absolute; top: 10px; right: 10px; width: 10px; height: 10px; border-radius: 50%; background: var(--rec); }
  .c-title { font-weight: 600; font-size: 15px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .empty { background: var(--surface); border: 1px dashed var(--line-btn); border-radius: var(--radius-l); padding: 20px; }
  .empty p { margin: 0 0 6px; }
  @media (max-width: 720px) { .hero { min-height: 300px; } .tonight { grid-template-columns: 1fr; } .cards { grid-auto-columns: 140px; } }
</style>
