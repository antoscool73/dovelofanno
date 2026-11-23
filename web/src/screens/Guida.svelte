<script lang="ts">
  // Guida TV. Griglia a scorrimento continuo (23/11/2026, Antonio): una sola linea del tempo da oggi all'ultimo giorno
  // della guida, senza cambiare pagina; i giorni si caricano uno alla volta man mano che ci si arriva e si disegnano
  // solo i programmi nel tratto visibile. I pulsanti dei giorni si accendono secondo il giorno dell'orario sul bordo
  // sinistro della linea del tempo e, cliccati, ci scorrono (alle 19, oggi all'ora attuale). Zoom (23/11/2026): da
  // circa 9 ore a poco più di un'ora sullo schermo, con Ctrl+rotella (o il gesto del touchpad), lo slider o due dita sul
  // telefono; l'orario sotto il puntatore resta fermo e lo zoom si ricorda. L'elenco resta un giorno alla volta.
  import { tick } from 'svelte';
  import Badge from '../lib/components/Badge.svelte';
  import Button from '../lib/components/Button.svelte';
  import ProgrammaDialog from './ProgrammaDialog.svelte';
  import SeenButton from '../lib/components/SeenButton.svelte';
  import { seenState } from '../lib/seen.svelte';
  import { api, ApiError, type GuideProgramme, type GuideStatus } from '../lib/api/client';
  import { byChannelOrder, day, dayChip, episodeOf, midnight, range, time, vote } from '../lib/format';
  import { navigate } from '../lib/router.svelte';
  import { session } from '../lib/session.svelte';

  const DAY = 1440;                   // minuti in un giorno
  const ZMIN = 2, ZMAX = 16, ZDEF = 4; // pixel per minuto: 2 = un'ora 120 px, 4 = 240 px (come il prototipo), 16 = 960 px
  const ZKEY = 'dlf.guida.zoom';
  const ROW = 68;
  // colonna dei canali: più stretta sui telefoni
  const CH = window.innerWidth < 720 ? 96 : 150;
  const groups = ['Film', 'Serie TV', 'Documentari', 'Notizie', 'Sport', 'Intrattenimento', 'Bambini', 'Altro'];

  const clampZoom = (v: number) => Math.min(ZMAX, Math.max(ZMIN, v));
  function savedZoom(): number {
    try { const v = Number(localStorage.getItem(ZKEY)); return v ? clampZoom(v) : ZDEF; } catch { return ZDEF; }
  }

  let status = $state<GuideStatus | null>(null);
  let error = $state('');
  let now = $state(new Date());
  // inizio della linea del tempo: la mezzanotte di oggi (all'apertura della pagina)
  const origin = midnight(new Date());
  const dayAt = (i: number) => new Date(origin.getFullYear(), origin.getMonth(), origin.getDate() + i);
  const dayIndex = (d: Date) => Math.round((midnight(d).getTime() - origin.getTime()) / 86_400_000);
  let selectedDay = $state(initialDay());
  let mode = $state<'griglia' | 'elenco'>('griglia');
  let group = $state('');
  let minRating = $state(0);
  let text = $state('');
  let results = $state<GuideProgramme[] | null>(null);
  let open = $state<GuideProgramme | null>(null);
  let scroller = $state<HTMLDivElement | null>(null);
  let refreshing = $state(false);
  let ppm = $state(savedZoom());

  // il giorno dall'indirizzo (/guida#giorno=2026-11-24), per tornare allo stesso punto con Indietro o da un link
  function initialDay(): Date {
    const m = /giorno=(\d{4})-(\d{2})-(\d{2})/.exec(location.hash);
    if (!m) return origin;
    const d = new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
    return isNaN(d.getTime()) || d < origin ? origin : d;
  }
  const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  $effect(() => {
    const h = selectedDay.getTime() === origin.getTime() ? '' : `#giorno=${iso(selectedDay)}`;
    try { history.replaceState(history.state, '', '/guida' + h); } catch { /* indirizzo non aggiornato */ }
  });

  const days = $derived.by(() => {
    const out: Date[] = [];
    const until = status?.until ? new Date(status.until) : new Date(origin.getTime() + 5 * 86_400_000);
    // un giorno compare se la guida ne copre almeno 6 ore
    for (let i = 0, d = origin; d.getTime() + 6 * 3_600_000 < until.getTime() && out.length < 14; i++, d = dayAt(i)) out.push(d);
    return out.length ? out : [origin];
  });
  const totalMin = $derived(days.length * DAY);

  // giorni caricati: indice del giorno -> programmi (un giorno alla volta, quando serve)
  let cache = $state(new Map<number, GuideProgramme[]>());
  const pending = new Set<number>();
  async function ensureDay(i: number) {
    if (i < 0 || i >= days.length || cache.has(i) || pending.has(i)) return;
    pending.add(i);
    try {
      const from = dayAt(i);
      const list = await api.guide({ from, to: dayAt(i + 1), max: 5000 });
      const next = new Map(cache);
      next.set(i, list);
      cache = next;
    } catch (e) {
      error = e instanceof ApiError ? e.message : 'Guida non letta';
    } finally {
      pending.delete(i);
    }
  }
  // rilegge i giorni già caricati senza svuotare la griglia (resta dove si era: dopo una registrazione, un visto...)
  async function reloadAll() {
    await Promise.all([...cache.keys()].map(async (i) => {
      try {
        const list = await api.guide({ from: dayAt(i), to: dayAt(i + 1), max: 5000 });
        const next = new Map(cache);
        next.set(i, list);
        cache = next;
      } catch { /* resta la copia di prima */ }
    }));
  }

  // proposte del profilo: evidenziate in blu nella griglia e nell'elenco (passaggio proposto)
  let proposed = $state(new Set<number>());
  $effect(() => {
    const prof = session.profile;
    if (!prof) return;
    api.proposals(prof.id, 14).then((l) => (proposed = new Set(l.map((x) => x.programme.id)))).catch(() => {});
  });

  async function loadStatus() {
    try { status = await api.guideStatus(); } catch { /* lo stato è solo informativo */ }
  }

  loadStatus();
  $effect(() => { ensureDay(dayIndex(selectedDay)); });
  $effect(() => {
    const t = setInterval(() => (now = new Date()), 60_000);
    return () => clearInterval(t);
  });

  // programmi del giorno scelto (elenco) e di tutti i giorni caricati (griglia, senza doppioni a cavallo della mezzanotte)
  const programmes = $derived(cache.get(dayIndex(selectedDay)) ?? []);
  // «un momento»: nell'elenco finché il giorno scelto non c'è; nella griglia solo finché non c'è nessun giorno (poi i
  // giorni nuovi arrivano mentre si scorre, senza togliere la griglia)
  const loading = $derived(!cache.has(dayIndex(selectedDay)) && (mode === 'elenco' || cache.size === 0));
  const all = $derived.by(() => {
    const seen = new Set<number>();
    const out: GuideProgramme[] = [];
    for (const list of cache.values()) for (const p of list) if (!seen.has(p.id)) { seen.add(p.id); out.push(p); }
    return out;
  });
  const recordable = $derived(all.some((p) => p.channelId != null));

  const matches = (p: GuideProgramme) => (!group || p.group === group) && (!minRating || (p.rating ?? 0) >= minRating);
  const minOf = (iso: string) => (new Date(iso).getTime() - origin.getTime()) / 60_000;

  // righe della griglia: un canale per riga; con IPTV solo quelli delle playlist (registrabili). Ogni programma porta
  // inizio e fine in minuti dall'origine, così lo scorrimento non rilegge le date
  const rows = $derived.by(() => {
    const m = new Map<string, { key: string; name: string; order: number | null; number: number | null; items: { p: GuideProgramme; a: number; b: number }[] }>();
    for (const p of all) {
      if (recordable && p.channelId == null) continue;
      const key = p.channelId != null ? 'c' + p.channelId : 'g' + p.guideChannelId;
      const name = p.channelName || p.guideChannelName;
      const it = { p, a: Math.max(0, minOf(p.start)), b: Math.min(totalMin, minOf(p.stop)) };
      if (it.b <= it.a) continue;
      const r = m.get(key);
      if (r) r.items.push(it);
      else m.set(key, { key, name, order: p.channelOrder ?? null, number: p.channelNumber ?? null, items: [it] });
    }
    // nell'ordine della playlist, come RegistraTV
    return [...m.values()].sort(byChannelOrder);
  });

  // tratto visibile (con un margine di 2 ore per parte): solo lì si disegnano programmi e orari
  let viewL = $state(0);
  let viewW = $state(1200);
  const lo = $derived(Math.max(0, viewL / ppm - 120));
  const hi = $derived((viewL + viewW) / ppm + 120);
  const visibleRows = $derived(rows.map((r) => ({ ...r, shown: r.items.filter((x) => x.b > lo && x.a < hi) })));
  const tickStep = $derived(ppm >= 10 ? 15 : ppm >= 3 ? 30 : 60);
  const ticks = $derived.by(() => {
    const out: number[] = [];
    for (let t = Math.floor(lo / tickStep) * tickStep; t < Math.min(hi, totalMin); t += tickStep) out.push(t);
    return out;
  });

  // minuto sul bordo sinistro della linea del tempo (subito dopo la colonna dei canali) e il suo giorno
  const leftMin = $derived(viewL / ppm);
  let frame = 0;
  function onScroll() {
    if (frame) return;
    frame = requestAnimationFrame(() => {
      frame = 0;
      if (!scroller) return;
      viewL = scroller.scrollLeft;
      viewW = scroller.clientWidth;
      const i = Math.min(days.length - 1, Math.max(0, Math.floor(viewL / ppm / DAY)));
      if (i !== dayIndex(selectedDay)) selectedDay = dayAt(i);
      // il giorno sul bordo destro e quello dopo, prima di arrivarci
      const j = Math.floor((viewL + viewW) / ppm / DAY);
      ensureDay(i); ensureDay(j); ensureDay(j + 1);
    });
  }

  // posizione per un orario: oggi mezz'ora prima di adesso, gli altri giorni le 19
  function startOf(i: number): number {
    if (i === 0) return Math.max(0, minOf(new Date().toISOString()) - 30);
    return i * DAY + 19 * 60;
  }
  function goToDay(d: Date) {
    results = null; text = '';
    if (mode !== 'griglia' || !scroller) { selectedDay = d; return; }
    const i = dayIndex(d);
    ensureDay(i); ensureDay(i + 1);
    scroller.scrollTo({ left: startOf(i) * ppm, behavior: 'smooth' });
  }

  // all'apertura (e tornando alla griglia) si va al giorno scelto
  let placed = false;
  $effect(() => {
    if (!scroller || mode !== 'griglia' || loading) { if (mode !== 'griglia') placed = false; return; }
    if (placed) return;
    placed = true;
    scroller.scrollLeft = startOf(dayIndex(selectedDay)) * ppm;
    viewL = scroller.scrollLeft;
    viewW = scroller.clientWidth;
  });

  // zoom: l'orario in `anchorX` (px dal bordo del riquadro) resta fermo
  async function setZoom(v: number, anchorX?: number, anchorMin?: number) {
    if (!scroller) { ppm = clampZoom(v); return; }
    const x = anchorX ?? CH + (scroller.clientWidth - CH) / 2;
    const m = anchorMin ?? (scroller.scrollLeft + x - CH) / ppm;
    const z = clampZoom(v);
    if (z === ppm) return;
    ppm = z;
    try { localStorage.setItem(ZKEY, String(z)); } catch { /* solo per questa visita */ }
    await tick();
    scroller.scrollLeft = Math.max(0, m * z - (x - CH));
    viewL = scroller.scrollLeft;
  }
  // slider su scala logaritmica: ogni passo ingrandisce dello stesso tanto
  const zoomPos = $derived(Math.round((100 * Math.log(ppm / ZMIN)) / Math.log(ZMAX / ZMIN)));
  const fromPos = (pos: number) => ZMIN * Math.pow(ZMAX / ZMIN, pos / 100);
  const zoomLabel = $derived.by(() => {
    const h = (viewW - CH) / ppm / 60;
    return h >= 1.5 ? `circa ${Math.round(h)} ore sullo schermo` : `circa ${Math.round(h * 60)} minuti sullo schermo`;
  });

  // Ctrl+rotella e il gesto di zoom dei touchpad (arriva come rotella con Ctrl): va registrato a mano perché deve poter
  // fermare lo zoom della pagina (gli ascoltatori della rotella di Svelte sono passivi)
  $effect(() => {
    const el = scroller;
    if (!el) return;
    const onWheel = (e: WheelEvent) => {
      if (!e.ctrlKey) return;
      e.preventDefault();
      const x = e.clientX - el.getBoundingClientRect().left;
      if (x < CH) return;
      setZoom(ppm * Math.exp(-e.deltaY * (e.deltaMode === 1 ? 0.05 : 0.0025)), x);
    };
    el.addEventListener('wheel', onWheel, { passive: false });
    return () => el.removeEventListener('wheel', onWheel);
  });

  // due dita sul telefono: la distanza tra le dita fa lo zoom, il punto in mezzo resta fermo
  const touches = new Map<number, { x: number; y: number }>();
  let pinch: { d0: number; z0: number; x: number; m: number } | null = null;
  function pinchState() {
    const [a, b] = [...touches.values()] as [{ x: number; y: number }, { x: number; y: number }];
    return { d: Math.hypot(a.x - b.x, a.y - b.y) || 1, mid: (a.x + b.x) / 2 };
  }
  function onPointerDown(e: PointerEvent) {
    if (e.pointerType !== 'touch' || !scroller) return;
    touches.set(e.pointerId, { x: e.clientX, y: e.clientY });
    if (touches.size === 2) {
      const { d, mid } = pinchState();
      const x = mid - scroller.getBoundingClientRect().left;
      pinch = { d0: d, z0: ppm, x, m: (scroller.scrollLeft + x - CH) / ppm };
    }
  }
  function onPointerMove(e: PointerEvent) {
    if (!touches.has(e.pointerId)) return;
    touches.set(e.pointerId, { x: e.clientX, y: e.clientY });
    if (pinch && touches.size === 2) setZoom(pinch.z0 * (pinchState().d / pinch.d0), pinch.x, pinch.m);
  }
  function onPointerEnd(e: PointerEvent) {
    touches.delete(e.pointerId);
    if (touches.size < 2) pinch = null;
  }
  $effect(() => {
    const el = scroller;
    if (!el) return;
    el.addEventListener('pointerdown', onPointerDown);
    el.addEventListener('pointermove', onPointerMove);
    el.addEventListener('pointerup', onPointerEnd);
    el.addEventListener('pointercancel', onPointerEnd);
    return () => {
      el.removeEventListener('pointerdown', onPointerDown);
      el.removeEventListener('pointermove', onPointerMove);
      el.removeEventListener('pointerup', onPointerEnd);
      el.removeEventListener('pointercancel', onPointerEnd);
    };
  });

  const nowMin = $derived(minOf(now.toISOString()));
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
    try { await api.refreshGuide(); setTimeout(() => { loadStatus(); reloadAll(); refreshing = false; }, 4000); }
    catch (e) { error = e instanceof ApiError ? e.message : 'Aggiornamento non partito'; refreshing = false; }
  }

  function changed() { reloadAll(); if (results) onSearch(); }

  const hhmm = (m: number) => `${String(Math.floor(m / 60) % 24).padStart(2, '0')}:${String(Math.round(m % 60)).padStart(2, '0')}`;
</script>


<section class="page">
  <div class="top">
    <h1>Guida TV</h1>
    <div class="days" role="group" aria-label="Giorno">
      {#each days as d (d.getTime())}
        <button type="button" class="chip" class:on={d.getTime() === selectedDay.getTime()} aria-pressed={d.getTime() === selectedDay.getTime()} onclick={() => goToDay(d)}>{dayChip(d, now)}</button>
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
    {#if mode === 'griglia'}
      <!-- zoom della griglia: anche Ctrl+rotella sulla griglia e due dita sul telefono -->
      <div class="zoom" role="group" aria-label="Zoom della griglia" title={zoomLabel}>
        <button type="button" aria-label="Meno zoom: più ore sullo schermo" disabled={ppm <= ZMIN} onclick={() => setZoom(ppm / 1.4)}>−</button>
        <input type="range" min="0" max="100" step="1" value={zoomPos} aria-label="Zoom della griglia" aria-valuetext={zoomLabel}
          oninput={(e) => setZoom(fromPos(Number(e.currentTarget.value)))} />
        <button type="button" aria-label="Più zoom: meno ore sullo schermo" disabled={ppm >= ZMAX} onclick={() => setZoom(ppm * 1.4)}>+</button>
      </div>
    {/if}
    <span class="grow"></span>
    <span class="legend"><span class="dot" aria-hidden="true"></span>Programmato</span>
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
          <span class="lwhat"><span class="ltitle">{p.title}{#if p.scheduled} <span class="dot" aria-label="in programma"></span>{/if}</span><span class="lmeta">{[episodeOf(p), p.group, p.sub, p.year || '', p.directors].filter(Boolean).join(' · ')}</span></span>
          {#if p.rating != null}<Badge tone="blue">★ {vote(p.rating)}</Badge>{/if}
        </button><SeenButton workId={p.workId} title={p.title} /></li>
      {/each}
    </ul>
  {:else if loading}
    <p class="muted" role="status">Un momento…</p>
  {:else if mode === 'griglia' ? all.length === 0 : programmes.length === 0}
    <div class="empty">
      <p>La guida è vuota per questo giorno.</p>
      <p class="muted">La guida arriva dalla playlist (se la indica) o da una guida XMLTV aggiunta in <a href="/impostazioni" onclick={navigate}>Impostazioni</a>; si aggiorna da sola ogni 12 ore.</p>
    </div>
  {:else if mode === 'griglia'}
    <div class="grid-box" bind:this={scroller} onscroll={onScroll}>
      <div class="grid" style:width="{CH + totalMin * ppm}px">
        <div class="ticks">
          <!-- il giorno dell'orario sul bordo sinistro: resta fermo in alto a sinistra mentre si scorre -->
          <div class="corner" style:width="{CH}px">{dayChip(selectedDay, now)}</div>
          {#each ticks as t (t)}
            <div class="tick" class:midnight={t % DAY === 0} style:left="{CH + t * ppm}px">{t % DAY === 0 ? dayChip(dayAt(t / DAY), now) : hhmm(t % DAY)}</div>
          {/each}
        </div>
        {#each visibleRows as r (r.key)}
          <div class="row" style:height="{ROW}px">
            <div class="chname" style:width="{CH}px">{#if r.number != null}<span class="chnum">{r.number}</span>{/if}{r.name}</div>
            {#each r.shown as x (x.p.id)}
              {@const p = x.p}
              <button type="button" class="prog" class:dim={!matches(p)} class:rec={p.scheduled} class:prop={proposed.has(p.id) && !p.scheduled} class:past={new Date(p.stop) <= now} class:seen={seenState.has(p.workId)}
                style:left="{CH + x.a * ppm}px" style:width="{Math.max(8, (x.b - x.a) * ppm - 4)}px" onclick={() => (open = p)}
                aria-label="{p.title}, {day(p.start, now)} {range(p.start, p.stop)}, {r.name}{p.scheduled ? ', in programma' : ''}{proposed.has(p.id) ? ', proposta per te' : ''}">
                <span class="ptitle">{#if p.scheduled}<span class="dot" aria-hidden="true"></span>{/if}{#if seenState.has(p.workId)}<span class="seenmark" aria-label="visto">✓</span>{/if}{p.title}{p.episode && p.group !== 'Film' ? ` · ${p.episode}` : ''}</span>
                <span class="pmeta">{time(p.start)}{p.rating != null ? ` · ★ ${vote(p.rating)}` : ''}{p.sub ? ` · ${p.sub}` : p.group !== 'Altro' ? ` · ${p.group}` : ''}</span>
              </button>
            {/each}
          </div>
        {/each}
        <!-- mezzanotte: una linea più marcata tra un giorno e l'altro -->
        {#each days.slice(1) as d, k (d.getTime())}
          <div class="dayline" style:left="{CH + (k + 1) * DAY * ppm}px" aria-hidden="true"></div>
        {/each}
        {#if nowMin >= 0 && nowMin <= totalMin}
          <div class="nowline" style:left="{CH + nowMin * ppm}px" aria-hidden="true"><span>{time(now)}</span></div>
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
          <span class="lwhat"><span class="ltitle">{p.title}{#if p.scheduled} <span class="dot" aria-label="in programma"></span>{/if}</span><span class="lmeta">{[episodeOf(p), p.group, p.sub, p.country, p.year || '', p.actors].filter(Boolean).join(' · ')}</span></span>
          {#if p.rating != null}<Badge tone="blue">★ {vote(p.rating)}</Badge>{/if}
        </button><SeenButton workId={p.workId} title={p.title} /></li>
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
  .zoom { display: flex; align-items: center; gap: 6px; }
  .zoom button { width: 38px; height: 38px; border-radius: var(--radius-s); border: 1px solid var(--line-strong); background: var(--card); color: var(--text); font-size: 20px; line-height: 1; cursor: pointer; }
  .zoom button:disabled { opacity: 0.4; cursor: default; }
  .zoom input { width: 120px; accent-color: var(--text); }
  .legend { display: inline-flex; align-items: center; gap: 8px; font-size: 13px; color: var(--muted); }
  .dot { display: inline-block; width: 8px; height: 8px; border-radius: 50%; background: var(--rec); flex: 0 0 8px; }
  .muted { color: var(--muted); margin: 0; }
  .small { font-size: 13px; }
  .error { color: var(--rec-text); margin: 0; }
  .empty { background: var(--surface); border: 1px dashed var(--line-btn); border-radius: var(--radius-l); padding: 20px; }
  .empty p { margin: 0 0 6px; }

  .grid-box { touch-action: pan-x pan-y; overflow: auto; max-height: calc(100vh - 260px); min-height: 320px; border: 1px solid var(--line); border-radius: var(--radius-l); background: var(--surface); }
  .grid { position: relative; }
  .ticks { position: sticky; top: 0; z-index: 3; height: 40px; background: var(--surface); border-bottom: 1px solid var(--line); }
  .corner { position: sticky; left: 0; z-index: 4; height: 40px; padding: 11px 12px 11px 16px; font-size: 13px; font-weight: 700; color: var(--text); background: var(--surface); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; box-shadow: 1px 0 0 var(--line); }
  .tick { position: absolute; top: 0; height: 40px; padding: 11px 8px; font-size: 13px; color: var(--muted); border-left: 1px solid var(--line); white-space: nowrap; }
  .tick.midnight { color: var(--text); font-weight: 700; border-left: 2px solid var(--text-2); }
  .dayline { position: absolute; top: 40px; bottom: 0; width: 2px; background: var(--line-btn); z-index: 1; pointer-events: none; }
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
  .prog.prop { background: rgba(110, 155, 255, 0.14); border: 2px solid var(--blue); }
  .prog.rec { background: rgba(255, 90, 95, 0.1); border-color: rgba(255, 90, 95, 0.5); }
  .prog.dim { opacity: 0.35; }
  .prog.past { opacity: 0.6; }
  .prog.seen { border-style: dashed; }
  .seenmark { color: var(--green); font-weight: 800; }
  .ptitle { display: flex; align-items: center; gap: 6px; font-size: 14px; font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .pmeta { font-size: 12px; color: var(--muted); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .nowline { position: absolute; top: 0; bottom: 0; width: 2px; background: var(--text); z-index: 1; pointer-events: none; }
  .nowline span { position: sticky; top: 8px; display: inline-block; transform: translateX(-50%); background: var(--text); color: var(--bg); font-size: 12px; font-weight: 700; border-radius: 6px; padding: 2px 6px; }

  .list { list-style: none; margin: 0; padding: 0; background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius-l); overflow: hidden; }
  .list li { display: flex; align-items: center; gap: 8px; padding-right: 12px; border-bottom: 1px solid var(--line-soft); }
  .list li:last-child { border-bottom: 0; }
  .lrow { flex: 1 1 auto; min-width: 0; width: 100%; display: flex; flex-wrap: wrap; align-items: center; gap: 8px 16px; padding: 12px 16px; border: 0; background: none; color: var(--text); text-align: left; cursor: pointer; }
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
