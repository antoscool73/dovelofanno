<script lang="ts">
  import Badge from '../lib/components/Badge.svelte';
  import Button from '../lib/components/Button.svelte';
  import ProgrammaDialog from './ProgrammaDialog.svelte';
  import { api, ApiError, type GuideProgramme, type GuideStatus } from '../lib/api/client';
  import { byChannelOrder, day, dayChip, midnight, range, time, vote } from '../lib/format';
  import { navigate } from '../lib/router.svelte';

  const PX_PER_MIN = 4;              // un'ora = 240 px, come nel prototipo
  const ROW = 68;
  // colonna dei canali: più stretta sui telefoni
  const CH = window.innerWidth < 720 ? 96 : 150;
  const groups = ['Film', 'Serie TV', 'Documentari', 'Notizie', 'Sport', 'Intrattenimento', 'Bambini', 'Altro'];

  let status = $state<GuideStatus | null>(null);
  let programmes = $state<GuideProgramme[]>([]);
  let loading = $state(true);
  let error = $state('');
  let selectedDay = $state(midnight(new Date()));
  let mode = $state<'griglia' | 'elenco'>('griglia');
  let group = $state('');
  let minRating = $state(0);
  let text = $state('');
  let results = $state<GuideProgramme[] | null>(null);
  let open = $state<GuideProgramme | null>(null);
  let now = $state(new Date());
  let scroller = $state<HTMLDivElement | null>(null);
  let refreshing = $state(false);

  const recordable = $derived(programmes.some((p) => p.channelId != null));
  const days = $derived.by(() => {
    const out: Date[] = [];
    const today = midnight(new Date());
    const until = status?.until ? new Date(status.until) : new Date(today.getTime() + 5 * 86_400_000);
    // un giorno compare se la guida ne copre almeno 6 ore
    for (let d = today; d.getTime() + 6 * 3_600_000 < until.getTime() && out.length < 14; d = new Date(d.getFullYear(), d.getMonth(), d.getDate() + 1)) out.push(d);
    return out.length ? out : [today];
  });

  async function loadStatus() {
    try { status = await api.guideStatus(); } catch { /* lo stato è solo informativo */ }
  }

  async function loadDay() {
    loading = true;
    error = '';
    try {
      const from = selectedDay;
      const to = new Date(from.getFullYear(), from.getMonth(), from.getDate() + 1);
      programmes = await api.guide({ from, to, max: 5000 });
    } catch (e) {
      error = e instanceof ApiError ? e.message : 'Guida non letta';
    } finally {
      loading = false;
    }
  }

  loadStatus();
  $effect(() => { void selectedDay; loadDay(); });
  $effect(() => {
    const t = setInterval(() => (now = new Date()), 60_000);
    return () => clearInterval(t);
  });

  // la griglia si apre sull'ora attuale (mezz'ora prima)
  const isToday = $derived(selectedDay.getTime() === midnight(now).getTime());
  const nowMin = $derived((now.getTime() - selectedDay.getTime()) / 60_000);
  $effect(() => {
    if (!scroller || loading || mode !== 'griglia') return;
    scroller.scrollLeft = isToday ? Math.max(0, (nowMin - 30) * PX_PER_MIN) : 19 * 60 * PX_PER_MIN;
  });

  const matches = (p: GuideProgramme) => (!group || p.group === group) && (!minRating || (p.rating ?? 0) >= minRating);

  // righe della griglia: un canale per riga; con IPTV solo quelli delle playlist (registrabili)
  const rows = $derived.by(() => {
    const m = new Map<string, { name: string; order: number | null; number: number | null; items: GuideProgramme[] }>();
    for (const p of programmes) {
      if (recordable && p.channelId == null) continue;
      const key = p.channelId != null ? 'c' + p.channelId : 'g' + p.guideChannelId;
      const name = p.channelName || p.guideChannelName;
      const r = m.get(key);
      if (r) r.items.push(p);
      else m.set(key, { name, order: p.channelOrder ?? null, number: p.channelNumber ?? null, items: [p] });
    }
    // nell'ordine della playlist, come RegistraTV
    return [...m.values()].sort(byChannelOrder);
  });

  function left(p: GuideProgramme) {
    return Math.max(0, (new Date(p.start).getTime() - selectedDay.getTime()) / 60_000) * PX_PER_MIN;
  }
  function width(p: GuideProgramme) {
    const a = Math.max(0, (new Date(p.start).getTime() - selectedDay.getTime()) / 60_000);
    const b = Math.min(24 * 60, (new Date(p.stop).getTime() - selectedDay.getTime()) / 60_000);
    return Math.max(8, (b - a) * PX_PER_MIN - 4);
  }

  const listItems = $derived(programmes.filter((p) => (!recordable || p.channelId != null) && matches(p)));

  let searchTimer: ReturnType<typeof setTimeout> | undefined;
  function onSearch() {
    clearTimeout(searchTimer);
    const q = text.trim();
    if (q.length < 2) { results = null; return; }
    searchTimer = setTimeout(async () => {
      try {
        const from = new Date();
        const to = status?.until ? new Date(status.until) : new Date(from.getTime() + 7 * 86_400_000);
        results = await api.guide({ from, to: to > from ? to : new Date(from.getTime() + 86_400_000), text: q, group: group || undefined, minRating: minRating || undefined, max: 300 });
      } catch (e) {
        error = e instanceof ApiError ? e.message : 'Ricerca non riuscita';
      }
    }, 250);
  }

  async function refresh() {
    refreshing = true;
    try { await api.refreshGuide(); setTimeout(() => { loadStatus(); loadDay(); refreshing = false; }, 4000); }
    catch (e) { error = e instanceof ApiError ? e.message : 'Aggiornamento non partito'; refreshing = false; }
  }

  function changed() { loadDay(); if (results) onSearch(); }

  const hours = Array.from({ length: 48 }, (_, i) => i * 30);
  const hhmm = (m: number) => `${String(Math.floor(m / 60) % 24).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`;
</script>

<section class="page">
  <div class="top">
    <h1>Guida TV</h1>
    <div class="days" role="group" aria-label="Giorno">
      {#each days as d (d.getTime())}
        <button type="button" class="chip" class:on={d.getTime() === selectedDay.getTime()} aria-pressed={d.getTime() === selectedDay.getTime()} onclick={() => { selectedDay = d; results = null; text = ''; }}>{dayChip(d, now)}</button>
      {/each}
    </div>
  </div>

  <div class="filters">
    <label class="search">
      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><circle cx="11" cy="11" r="7" /><path d="M20 20l-4-4" /></svg>
      <input type="search" bind:value={text} oninput={onSearch} placeholder="Titolo, attore, regista: su tutti i giorni" aria-label="Cerca nella guida" />
    </label>
    <label class="sel"><span class="sr-only">Genere</span>
      <select bind:value={group} onchange={onSearch}>
        <option value="">Genere: tutti</option>
        {#each groups as g (g)}<option value={g}>{g}</option>{/each}
      </select>
    </label>
    <label class="sel"><span class="sr-only">Voto minimo</span>
      <select bind:value={minRating} onchange={onSearch}>
        <option value={0}>Voto: qualsiasi</option>
        <option value={6}>Voto 6 e oltre</option>
        <option value={7}>Voto 7 e oltre</option>
        <option value={8}>Voto 8 e oltre</option>
      </select>
    </label>
    <div class="modes" role="group" aria-label="Vista">
      <button type="button" class:on={mode === 'griglia'} aria-pressed={mode === 'griglia'} onclick={() => (mode = 'griglia')}>Griglia</button>
      <button type="button" class:on={mode === 'elenco'} aria-pressed={mode === 'elenco'} onclick={() => (mode = 'elenco')}>Elenco</button>
    </div>
    <span class="grow"></span>
    <span class="legend"><span class="dot" aria-hidden="true"></span>In programma</span>
  </div>

  {#if error}<p class="error" role="alert">{error}</p>{/if}

  {#if results}
    <h2>Risultati per «{text.trim()}» <span class="sub">{results.length === 300 ? 'i primi 300' : results.length}</span></h2>
    {#if results.length === 0}<p class="muted">Nessun programma nei giorni della guida.</p>{/if}
    <ul class="list">
      {#each results as p (p.id)}
        <li><button type="button" class="lrow" onclick={() => (open = p)}>
          <span class="lwhen"><strong>{day(p.start, now)}</strong><span>{range(p.start, p.stop)}</span></span>
          <span class="lch">{p.channelName || p.guideChannelName}</span>
          <span class="lwhat"><span class="ltitle">{p.title}{#if p.scheduled} <span class="dot" aria-label="in programma"></span>{/if}</span><span class="lmeta">{[p.group, p.sub, p.year || '', p.directors].filter(Boolean).join(' · ')}</span></span>
          {#if p.rating != null}<Badge tone="blue">★ {vote(p.rating)}</Badge>{/if}
        </button></li>
      {/each}
    </ul>
  {:else if loading}
    <p class="muted" role="status">Un momento…</p>
  {:else if programmes.length === 0}
    <div class="empty">
      <p>La guida è vuota per questo giorno.</p>
      <p class="muted">La guida arriva dalla playlist (se la indica) o da una guida XMLTV aggiunta in <a href="/impostazioni" onclick={navigate}>Impostazioni</a>; si aggiorna da sola ogni 12 ore.</p>
    </div>
  {:else if mode === 'griglia'}
    <div class="grid-box" bind:this={scroller}>
      <div class="grid" style:width="{CH + 24 * 60 * PX_PER_MIN}px">
        <div class="ticks">
          <div class="corner" style:width="{CH}px">Canale</div>
          {#each hours as h (h)}<div class="tick" style:left="{CH + h * PX_PER_MIN}px">{hhmm(h)}</div>{/each}
        </div>
        {#each rows as r (r.name)}
          <div class="row" style:height="{ROW}px">
            <div class="chname" style:width="{CH}px">{#if r.number != null}<span class="chnum">{r.number}</span>{/if}{r.name}</div>
            {#each r.items as p (p.id)}
              <button type="button" class="prog" class:dim={!matches(p)} class:rec={p.scheduled} class:past={new Date(p.stop) <= now}
                style:left="{CH + left(p)}px" style:width="{width(p)}px" onclick={() => (open = p)}
                aria-label="{p.title}, {range(p.start, p.stop)}, {r.name}{p.scheduled ? ', in programma' : ''}">
                <span class="ptitle">{#if p.scheduled}<span class="dot" aria-hidden="true"></span>{/if}{p.title}</span>
                <span class="pmeta">{time(p.start)}{p.rating != null ? ` · ★ ${vote(p.rating)}` : ''}{p.sub ? ` · ${p.sub}` : p.group !== 'Altro' ? ` · ${p.group}` : ''}</span>
              </button>
            {/each}
          </div>
        {/each}
        {#if isToday && nowMin >= 0 && nowMin <= 1440}
          <div class="nowline" style:left="{CH + nowMin * PX_PER_MIN}px" aria-hidden="true"><span>{time(now)}</span></div>
        {/if}
      </div>
    </div>
  {:else if listItems.length === 0}
    <p class="muted">Nessun programma con questi filtri in questo giorno{recordable ? ' sui canali delle tue playlist' : ''}.</p>
  {:else}
    <ul class="list">
      {#each listItems as p (p.id)}
        <li><button type="button" class="lrow" class:past={new Date(p.stop) <= now} onclick={() => (open = p)}>
          <span class="lwhen"><strong>{time(p.start)}</strong><span>{time(p.stop)}</span></span>
          <span class="lch">{p.channelName || p.guideChannelName}</span>
          <span class="lwhat"><span class="ltitle">{p.title}{#if p.scheduled} <span class="dot" aria-label="in programma"></span>{/if}</span><span class="lmeta">{[p.group, p.sub, p.country, p.year || '', p.actors].filter(Boolean).join(' · ')}</span></span>
          {#if p.rating != null}<Badge tone="blue">★ {vote(p.rating)}</Badge>{/if}
        </button></li>
      {/each}
    </ul>
  {/if}

  {#if status}
    <p class="muted small foot">
      {[
        `${status.programmes.toLocaleString('it-IT')} programmi su ${status.channels} canali, ${status.linkedChannels} registrabili`,
        status.lastLoadedAt ? `aggiornata ${day(status.lastLoadedAt, now).toLowerCase()} alle ${time(status.lastLoadedAt)}` : '',
        status.until ? `fino a ${day(status.until, now).toLowerCase()} alle ${time(status.until)}` : '',
      ].filter(Boolean).join(' · ')}.
      {#if !recordable && programmes.length > 0} Nessuna playlist: si consulta e basta; per registrare aggiungine una in <a href="/impostazioni" onclick={navigate}>Impostazioni</a>.{/if}
      <Button size="s" variant="ghost" disabled={refreshing} onclick={refresh}>{refreshing ? 'Aggiornamento…' : 'Aggiorna guida'}</Button>
    </p>
  {/if}
</section>

{#if open}
  <ProgrammaDialog p={open} onclose={() => (open = null)} onchange={changed} />
{/if}

<style>
  .page { padding: 28px var(--gutter) 48px; display: flex; flex-direction: column; gap: 16px; }
  .top { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 16px; }
  h1 { font-size: 32px; }
  h2 { font-size: 20px; }
  .sub { font-family: var(--font); font-size: 14px; font-weight: 500; color: var(--muted); }
  .days { display: flex; flex-wrap: wrap; gap: 6px; }
  .chip { min-height: 40px; padding: 0 16px; border-radius: 999px; border: 1px solid var(--line-strong); background: none; color: var(--text-2); font-size: 14px; cursor: pointer; }
  .chip.on { background: var(--text); color: var(--bg); border-color: transparent; font-weight: 600; }
  .filters { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; }
  .search { display: flex; align-items: center; gap: 8px; min-height: 40px; padding: 0 12px; border-radius: var(--radius-s); border: 1px solid var(--line-strong); background: var(--card); flex: 0 1 340px; color: var(--muted); }
  .search input { flex: 1; min-width: 0; border: 0; background: none; color: var(--text); font-size: 14px; outline: none; }
  .sel select { min-height: 40px; padding: 0 12px; border-radius: var(--radius-s); border: 1px solid var(--line-strong); background: var(--card); color: var(--text); font-size: 14px; }
  .modes { display: flex; border: 1px solid var(--line-strong); border-radius: var(--radius-s); overflow: hidden; }
  .modes button { min-height: 38px; padding: 0 14px; border: 0; background: none; color: var(--muted); font-size: 14px; cursor: pointer; }
  .modes button.on { background: var(--raised); color: var(--text); font-weight: 600; }
  .grow { flex: 1 1 0; }
  .legend { display: inline-flex; align-items: center; gap: 8px; font-size: 13px; color: var(--muted); }
  .dot { display: inline-block; width: 8px; height: 8px; border-radius: 50%; background: var(--rec); flex: 0 0 8px; }
  .muted { color: var(--muted); margin: 0; }
  .small { font-size: 13px; }
  .error { color: var(--rec-text); margin: 0; }
  .empty { background: var(--surface); border: 1px dashed var(--line-btn); border-radius: var(--radius-l); padding: 20px; }
  .empty p { margin: 0 0 6px; }

  .grid-box { overflow: auto; max-height: calc(100vh - 260px); min-height: 320px; border: 1px solid var(--line); border-radius: var(--radius-l); background: var(--surface); }
  .grid { position: relative; }
  .ticks { position: sticky; top: 0; z-index: 3; height: 40px; background: var(--surface); border-bottom: 1px solid var(--line); }
  .corner { position: sticky; left: 0; z-index: 4; height: 40px; padding: 11px 16px; font-size: 13px; color: var(--muted); background: var(--surface); }
  .tick { position: absolute; top: 0; height: 40px; padding: 11px 8px; font-size: 13px; color: var(--muted); border-left: 1px solid var(--line); }
  .row { position: relative; border-bottom: 1px solid var(--line-soft); }
  .chname {
    position: sticky; left: 0; z-index: 2; height: 100%; display: flex; align-items: center; padding: 0 16px;
    font-size: 14px; font-weight: 600; background: var(--surface); box-shadow: 1px 0 0 var(--line);
  }
  .chnum { min-width: 28px; margin-right: 8px; color: var(--muted); font-variant-numeric: tabular-nums; text-align: right; }
  .prog {
    position: absolute; top: 6px; bottom: 6px; display: flex; flex-direction: column; justify-content: center; gap: 3px;
    padding: 6px 10px; border-radius: var(--radius-s); border: 1px solid var(--line-strong); background: #1a1f28;
    color: var(--text); text-align: left; overflow: hidden; cursor: pointer;
  }
  .prog:hover { border-color: var(--line-btn); background: var(--raised); }
  .prog.rec { background: rgba(255, 90, 95, 0.1); border-color: rgba(255, 90, 95, 0.5); }
  .prog.dim { opacity: 0.35; }
  .prog.past { opacity: 0.6; }
  .ptitle { display: flex; align-items: center; gap: 6px; font-size: 14px; font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .pmeta { font-size: 12px; color: var(--muted); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .nowline { position: absolute; top: 0; bottom: 0; width: 2px; background: var(--text); z-index: 1; pointer-events: none; }
  .nowline span { position: sticky; top: 8px; display: inline-block; transform: translateX(-50%); background: var(--text); color: var(--bg); font-size: 12px; font-weight: 700; border-radius: 6px; padding: 2px 6px; }

  .list { list-style: none; margin: 0; padding: 0; background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius-l); overflow: hidden; }
  .list li { border-bottom: 1px solid var(--line-soft); }
  .list li:last-child { border-bottom: 0; }
  .lrow { width: 100%; display: flex; flex-wrap: wrap; align-items: center; gap: 8px 16px; padding: 12px 16px; border: 0; background: none; color: var(--text); text-align: left; cursor: pointer; }
  .lrow:hover { background: var(--card); }
  .lrow.past { opacity: 0.6; }
  .lwhen { flex: 0 0 110px; display: flex; flex-direction: column; font-size: 13px; color: var(--muted); }
  .lwhen strong { font-size: 15px; color: var(--text); }
  .lch { flex: 0 0 120px; font-size: 14px; color: var(--muted); }
  .lwhat { flex: 1 1 320px; min-width: 0; display: flex; flex-direction: column; gap: 2px; }
  .ltitle { display: flex; align-items: center; gap: 8px; font-size: 16px; font-weight: 600; }
  .lmeta { font-size: 13px; color: var(--muted); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .foot { display: flex; flex-wrap: wrap; align-items: center; gap: 6px 10px; }
  @media (max-width: 720px) { .grid-box { max-height: none; } .lch { flex-basis: auto; } }
</style>
