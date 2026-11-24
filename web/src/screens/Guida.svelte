<script lang="ts">
  import RegionPicker from '../lib/components/RegionPicker.svelte';
  // Guida TV. Griglia a scorrimento continuo (23/11/2026, Antonio): una sola linea del tempo da oggi all'ultimo giorno
  // della guida, senza cambiare pagina; i giorni si caricano uno alla volta man mano che ci si arriva e si disegnano
  // solo i programmi nel tratto visibile. I pulsanti dei giorni si accendono secondo il giorno dell'orario sul bordo
  // sinistro della linea del tempo e, cliccati, ci scorrono (alle 19, oggi all'ora attuale). Zoom (23/11/2026): da
  // circa 9 ore a poco più di un'ora sullo schermo, con Ctrl+rotella (o il gesto del touchpad), lo slider o due dita sul
  // telefono; l'orario sotto il puntatore resta fermo e lo zoom si ricorda. L'elenco resta un giorno alla volta.
  import { tick } from 'svelte';
  import Badge from '../lib/components/Badge.svelte';
  import Button from '../lib/components/Button.svelte';
  import NuovaRegistrazione from './NuovaRegistrazione.svelte';
  import LivePreview from '../lib/components/LivePreview.svelte';
  import SeenButton from '../lib/components/SeenButton.svelte';
  import { seenState } from '../lib/seen.svelte';
  import { api, ApiError, type Channel, type GuideProgramme, type GuideStatus } from '../lib/api/client';
  import { byChannelOrder, day, dayChip, episodeOf, midnight, range, time, vote } from '../lib/format';
  import { navigate, router } from '../lib/router.svelte';
  import { dragScroll } from '../lib/dragScroll';
  import { SCOPES, loadFilter, saveFilter, shownInGuide, type ChannelFilter, type Scope } from '../lib/channels';
  import { session } from '../lib/session.svelte';

  const DAY = 1440;                   // minuti in un giorno
  const ZMIN = 2, ZMAX = 16, ZDEF = 4; // pixel per minuto: 2 = un'ora 120 px, 4 = 240 px (come il prototipo), 16 = 960 px
  const ZKEY = 'dlf.guida.zoom';
  const ROW = 68;
  // colonna dei canali: più stretta sui telefoni
  const CH = window.innerWidth < 720 ? 96 : 190;   // 190 dal 23/11/2026: c'è anche il logo
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
  // un clic su un programma apre sempre la sua scheda (23/11/2026, Antonio: niente più popup del programma)
  const openWork = (p: GuideProgramme) => { if (p.workId != null) router.go(`/opera/${p.workId}`); };
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
  // un giorno della guida: con una playlist solo i canali registrabili (la griglia mostra solo quelli), così un giorno
  // resta sotto il limite; prima si chiedevano tutti i 150 canali con un massimo di 5000 e la sera spariva (23/11/2026)
  const dayQuery = async (i: number) => {
    await statusReady;
    return api.guide({ from: dayAt(i), to: dayAt(i + 1), onlyRecordable: (status?.linkedChannels ?? 0) > 0, max: 20000 });
  };
  async function ensureDay(i: number) {
    if (i < 0 || i >= days.length || cache.has(i) || pending.has(i)) return;
    pending.add(i);
    try {
      const list = await dayQuery(i);
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
        const list = await dayQuery(i);
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

  const statusReady = loadStatus();
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

  // canali delle playlist senza programmi in guida (23/11/2026, Antonio: «vedo sempre pochi canali»): con l'interruttore
  // hanno una riga in fondo, vuota, da cui si registra a orario libero. Spento di base: con playlist di canali locali
  // sarebbero centinaia di righe. Si ricorda sul dispositivo.
  const NOGUIDE_KEY = 'dlf.guida.senzaGuida';
  let showNoGuide = $state((() => { try { return localStorage.getItem(NOGUIDE_KEY) === '1'; } catch { return false; } })());
  function setShowNoGuide(v: boolean) {
    showNoGuide = v;
    try { if (v) localStorage.setItem(NOGUIDE_KEY, '1'); else localStorage.removeItem(NOGUIDE_KEY); } catch { /* solo per questa visita */ }
  }
  let channels = $state<Channel[]>([]);
  api.channels().then((l) => (channels = l.filter((c) => !c.excluded && c.streams > 0))).catch(() => { /* senza elenco: niente righe in più */ });
  const chById = $derived(new Map(channels.map((c) => [c.id, c])));

  // filtro «Canali» (lib/channels.ts): gruppi da vedere e condizioni, sul dispositivo; regione della famiglia dal server
  let chFilter = $state<ChannelFilter>(loadFilter());
  let famRegion = $state('');
  let regions = $state<string[]>([]);
  api.generalSettings().then((g) => { famRegion = g.region; regions = g.regions; }).catch(() => { /* senza regione: tutti i canali */ });
  // la regione è della famiglia: si cambia da qui o da Preferenze › Canali nella guida, e vale per tutti
  async function setRegion(v: string) {
    try { famRegion = (await api.updateGeneralSettings({ region: v })).region; }
    catch (e) { error = e instanceof ApiError ? e.message : 'Regione non salvata'; }
  }
  function setFilter(f: ChannelFilter) { chFilter = f; saveFilter(f); }
  function toggleScope(sc: Scope) {
    const has = chFilter.scopes.includes(sc);
    setFilter({ ...chFilter, scopes: has ? chFilter.scopes.filter((x) => x !== sc) : [...chFilter.scopes, sc] });
  }
  // una riga della griglia resta se il suo canale passa il filtro (le righe della sola guida, senza playlist, sempre)
  function keep(key: string): boolean {
    if (!key.startsWith('c')) return true;
    const c = chById.get(Number(key.slice(1)));
    return !c || shownInGuide(c, chFilter, famRegion);
  }
  const logoOf = (key: string) => (key.startsWith('c') ? chById.get(Number(key.slice(1)))?.logo ?? '' : '');
  const noGuide = $derived.by(() => {
    if (!recordable) return [];
    const has = new Set(rows.map((r) => r.key));
    return channels.filter((c) => !has.has('c' + c.id) && shownInGuide(c, chFilter, famRegion)).sort(byChannelOrder);
  });
  // quanti canali senza guida toglie il filtro «Canali»: lo si dice accanto all'interruttore, se no «2» sembra un errore
  const noGuideHidden = $derived.by(() => {
    if (!recordable) return 0;
    const has = new Set(rows.map((r) => r.key));
    return channels.filter((c) => !has.has('c' + c.id)).length - noGuide.length;
  });
  let freeFor = $state<number | null>(null);

  // giorni ancora quasi vuoti: la fonte della guida pubblica gli ultimi giorni un po' alla volta (epgshare01 il 23/11:
  // 858 programmi per il quarto giorno contro ~5000 dei primi); lo si dice invece di mostrare una griglia vuota
  const thin = $derived.by(() => {
    const counts = [...cache.entries()].map(([i, l]) => [i, l.filter((p) => new Date(p.start) >= dayAt(i)).length] as const);
    const most = Math.max(0, ...counts.map(([, n]) => n));
    return new Set(counts.filter(([, n]) => most > 0 && n < most * 0.3).map(([i]) => i));
  });
  const thinNow = $derived(mode === 'griglia' && thin.has(dayIndex(selectedDay)));

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
  const gridRows = $derived(rows.filter((r) => keep(r.key)));
  const visibleRows = $derived(gridRows.map((r) => ({ ...r, shown: r.items.filter((x) => x.b > lo && x.a < hi) })));
  const shownCount = $derived(gridRows.length + (showNoGuide ? noGuide.length : 0));
  const tickStep = $derived(ppm >= 10 ? 15 : ppm >= 3 ? 30 : 60);
  const ticks = $derived.by(() => {
    const out: number[] = [];
    for (let t = Math.floor(lo / tickStep) * tickStep; t < Math.min(hi, totalMin); t += tickStep) out.push(t);
    return out;
  });

  // minuto sul bordo sinistro della linea del tempo (subito dopo la colonna dei canali) e il suo giorno
  const leftMin = $derived(viewL / ppm);
  // popup con il testo completo quando il mouse si ferma su un programma (23/11/2026, richiesta di Antonio):
  // nella griglia i titoli lunghi restano tagliati. Solo col mouse: al tocco il clic apre già la scheda.
  let tip = $state<{ p: GuideProgramme; ch: string; x: number; top: number; bottom: number } | null>(null);
  let tipTimer: ReturnType<typeof setTimeout> | undefined;
  function tipIn(e: PointerEvent, p: GuideProgramme, ch: string) {
    if (e.pointerType !== 'mouse' || e.buttons) return;
    clearTimeout(tipTimer);
    const r = (e.currentTarget as HTMLElement).getBoundingClientRect();
    const x = e.clientX;
    tipTimer = setTimeout(() => (tip = { p, ch, x, top: r.top, bottom: r.bottom }), 450);
  }
  function tipOut() { clearTimeout(tipTimer); tip = null; }
  let tipEl = $state<HTMLDivElement | null>(null);
  let tipPos = $state({ left: 0, top: 0 });
  $effect(() => {
    if (!tip || !tipEl) return;
    const w = tipEl.offsetWidth, h = tipEl.offsetHeight;
    const left = Math.max(8, Math.min(window.innerWidth - w - 8, tip.x - 24));
    const top = tip.bottom + 6 + h <= window.innerHeight - 8 ? tip.bottom + 6 : Math.max(8, tip.top - 6 - h);
    tipPos = { left, top };
  });

  let frame = 0;
  function onScroll() {
    tipOut();
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

  // Anteprima sopra la griglia, come nel prototipo (23/11/2026, Antonio: «facciamola facile, serve a capire più o meno
  // cosa gira, disattivabile»): il fotogramma del canale che il server rinnova ogni pochi secondi (LivePreview), con
  // «Ora» e «Dopo». Un clic sul nome di un canale lo mette nell'anteprima. Nascosta non chiede niente al server.
  // Si ricordano sul dispositivo il canale e se è nascosta.
  const PREV_KEY = 'dlf.guida.anteprima';
  const savedPrev = (() => { try { return localStorage.getItem(PREV_KEY); } catch { return null; } })();
  let previewOn = $state(savedPrev !== 'no');
  let previewCh = $state<number | null>(savedPrev && savedPrev !== 'no' ? Number(savedPrev) : null);
  function savePreview() {
    try { localStorage.setItem(PREV_KEY, previewOn ? String(previewCh ?? '') : 'no'); } catch { /* solo per questa visita */ }
  }
  function setPreview(on: boolean) { previewOn = on; savePreview(); }
  function pickPreview(key: string) {
    previewCh = Number(key.slice(1));
    previewOn = true;
    savePreview();
  }
  // anche un canale senza guida si può mettere nell'anteprima (23/11/2026): riga senza programmi, quindi niente «Ora/Dopo»
  const previewRow = $derived.by(() => {
    const r = rows.find((x) => x.key === 'c' + previewCh);
    if (r) return r;
    const c = previewCh != null ? chById.get(previewCh) : undefined;
    if (c) return { key: 'c' + c.id, name: c.name, order: c.order, number: c.number, items: [] as { p: GuideProgramme; a: number; b: number }[] };
    return rows.find((x) => x.key.startsWith('c')) ?? null;
  });
  const previewId = $derived(previewRow ? Number(previewRow.key.slice(1)) : null);
  // testata unica (23/11/2026, Antonio): opzioni a sinistra e anteprima a destra, ferma in alto; sotto scorre solo la griglia,
  // che riempie il resto della finestra. Serve l'altezza della barra dell'app, che è fissa in alto e può andare a capo.
  const pvShown = $derived(mode === 'griglia' && !results && recordable && previewOn && previewRow != null && previewId != null);
  const fill = $derived(mode === 'griglia' && !results);
  let hdrH = $state(64);
  $effect(() => {
    // la barra può andare a capo (finestre strette, menu del profilo): si rilegge a ogni cambio di misura e di finestra
    const measure = () => { const h = document.querySelector('body header'); if (h) hdrH = (h as HTMLElement).offsetHeight; };
    measure();
    const h = document.querySelector('body header');
    const ro = new ResizeObserver(measure);
    if (h) ro.observe(h);
    window.addEventListener('resize', measure);
    const t = setTimeout(measure, 500);
    return () => { ro.disconnect(); window.removeEventListener('resize', measure); clearTimeout(t); };
  });
  const previewNow = $derived(previewRow?.items.find((x) => x.a <= nowMin && x.b > nowMin)?.p ?? null);
  const previewNext = $derived(previewRow?.items.filter((x) => x.a > nowMin).sort((a, b) => a.a - b.a)[0]?.p ?? null);
  const listItems = $derived(programmes.filter((p) => (!recordable || p.channelId != null) && matches(p) && (p.channelId == null || keep('c' + p.channelId))));

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


  const hhmm = (m: number) => `${String(Math.floor(m / 60) % 24).padStart(2, '0')}:${String(Math.round(m % 60)).padStart(2, '0')}`;
</script>


<section class="page" class:fill style:--hdr="{hdrH}px">
  <div class="head" class:withpv={pvShown}>
  <div class="opts">
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
    {#if recordable && channels.length > 0}
      <!-- regione della famiglia (23/11/2026, Antonio): anche qui, oltre che in Preferenze › Canali nella guida; «nessuna» = tutti -->
      <label class="sel" title="La regione della famiglia: vale per tutti e si cambia anche in Preferenze › Canali nella guida"><span class="sr-only">Regione</span>
        <RegionPicker value={famRegion} {regions} prefix="Regioni: " onchange={setRegion} />
      </label>
    {/if}
  </div>
  {#if mode === 'griglia' && recordable}
  <div class="filters checks">
    <!-- l'anteprima è una casella con la sua spiegazione (23/11/2026, Antonio); spenta, il riquadro non c'è -->
      <label class="check">
        <input type="checkbox" checked={previewOn} onchange={(e) => setPreview(e.currentTarget.checked)} />
        <span><strong>Anteprima</strong> <span class="muted">un fotogramma ogni pochi secondi, senza audio</span></span>
      </label>
  </div>
  {/if}

  </div>
  {#if pvShown && previewRow && previewId != null}
    <section class="preview" aria-label="Anteprima del canale">
          <div class="pv">{#key previewId}<LivePreview channelId={previewId} label={previewRow.name} />{/key}</div>
          <div class="pinfo">
            <div class="muted small">{previewRow.name} · clic sul nome di un canale per cambiarlo</div>
            {#if previewNow}
              {@const pn = previewNow}
              <button type="button" class="plink now" onclick={() => openWork(pn)}>Ora: {pn.title}{episodeOf(pn) ? ` · ${episodeOf(pn)}` : ''} <span class="muted">fino alle {time(pn.stop)}</span></button>
            {/if}
            {#if previewNext}
              {@const px = previewNext}
              <button type="button" class="plink" onclick={() => openWork(px)}>Dopo: {px.title} · {range(px.start, px.stop)}{px.rating != null ? ` · ★ ${vote(px.rating)}` : ''}</button>
            {/if}
          </div>
    </section>
  {/if}
  </div>
  <!-- comandi della griglia, subito sopra (23/11/2026, disposizione di Antonio nel prototipo): vista e canali senza guida a
       sinistra, legenda e zoom a destra -->
  <div class="gbar">
    <div class="modes" role="group" aria-label="Vista">
      <button type="button" class:on={mode === 'griglia'} aria-pressed={mode === 'griglia'} onclick={() => (mode = 'griglia')}>Griglia</button>
      <button type="button" class:on={mode === 'elenco'} aria-pressed={mode === 'elenco'} onclick={() => (mode = 'elenco')}>Elenco</button>
    </div>
    {#if recordable && channels.length > 0}
      <!-- filtro «Canali» (23/11/2026; accanto alla vista per scelta di Antonio): regione, gruppi e condizioni; i singoli canali in Preferenze › Canali nella guida -->
      <details class="chfilter" data-dropdown>
        <summary>Canali: {mode === 'griglia' ? shownCount : 'filtro'} ▾</summary>
        <div class="chpanel">
          <p class="muted small">Quali canali vedere nella guida. I gruppi valgono su questo dispositivo; quelli scelti a mano in <a href="/preferenze#canali" onclick={navigate}>Preferenze › Canali nella guida</a> restano come li hai messi.</p>
          {#each SCOPES as sc (sc.id)}
            <label class="check" class:off={!famRegion}>
              <input type="checkbox" checked={chFilter.scopes.includes(sc.id)} disabled={!famRegion} onchange={() => toggleScope(sc.id)} />
              <span><strong>{sc.label}</strong> <span class="muted">{sc.id === 'regione' && famRegion ? famRegion : sc.hint}</span></span>
            </label>
          {/each}
          {#if !famRegion}<p class="muted small">Con «Regione: nessuna» i gruppi non contano: si vedono tutti i canali (con le due condizioni qui sotto).</p>{/if}
          <hr />
          <label class="check"><input type="checkbox" checked={chFilter.onlyWorking} onchange={(e) => setFilter({ ...chFilter, onlyWorking: e.currentTarget.checked })} />
            <span><strong>Solo i canali che funzionano</strong> <span class="muted">con un flusso che ha risposto al controllo</span></span></label>
          <label class="check"><input type="checkbox" checked={chFilter.noShop} onchange={(e) => setFilter({ ...chFilter, noShop: e.currentTarget.checked })} />
            <span><strong>Senza televendite e religiosi</strong> <span class="muted">e senza i canali chiusi</span></span></label>
        </div>
      </details>
    {/if}
    {#if mode === 'griglia' && recordable && (noGuide.length > 0 || noGuideHidden > 0)}
      <label class="switch">
        <input type="checkbox" role="switch" checked={showNoGuide} onchange={(e) => setShowNoGuide(e.currentTarget.checked)} />
        <span class="track" aria-hidden="true"></span>
        Canali senza guida ({noGuide.length}){#if noGuideHidden > 0}<span class="muted">&nbsp;· altri {noGuideHidden} esclusi dal filtro «Canali»</span>{/if}
      </label>
    {/if}
    <span class="spacer"></span>
    {#if mode === 'griglia'}<span class="legend"><span class="propsw" aria-hidden="true"></span>Proposta per te</span>{/if}
    <span class="legend"><span class="dot" aria-hidden="true"></span>Programmato</span>
    {#if mode === 'griglia'}
      <!-- zoom della griglia: anche Ctrl+rotella sulla griglia e due dita sul telefono -->
      <div class="zoom" role="group" aria-label="Zoom della griglia" title={zoomLabel}>
        <button type="button" aria-label="Meno zoom: più ore sullo schermo" disabled={ppm <= ZMIN} onclick={() => setZoom(ppm / 1.4)}>−</button>
        <input type="range" min="0" max="100" step="1" value={zoomPos} aria-label="Zoom della griglia" aria-valuetext={zoomLabel}
          oninput={(e) => setZoom(fromPos(Number(e.currentTarget.value)))} />
        <button type="button" aria-label="Più zoom: meno ore sullo schermo" disabled={ppm >= ZMAX} onclick={() => setZoom(ppm * 1.4)}>+</button>
      </div>
    {/if}
  </div>
  {#if error}<p class="error" role="alert">{error}</p>{/if}
  {#if thinNow}
    <p class="thin" role="status">Per {dayChip(selectedDay, now).toLowerCase()} la guida ha ancora pochi programmi: la fonte li aggiunge con i prossimi aggiornamenti (la guida si aggiorna ogni 12 ore).</p>
  {/if}

  {#if results}
    <h2>Risultati per «{text.trim()}» <span class="sub">{results.length === 300 ? 'i primi 300' : results.length}</span></h2>
    {#if results.length === 0}<p class="muted">Nessun programma nei giorni della guida.</p>{/if}
    <ul class="list">
      {#each results as p (p.id)}
        <li><button type="button" class="lrow" onclick={() => openWork(p)}>
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
    <!-- col mouse si trascina in orizzontale e in verticale (23/11/2026, Antonio); al tocco scorre come sempre -->
    <div class="grid-box" data-row bind:this={scroller} onscroll={onScroll} use:dragScroll={{ vertical: true }}>
      <div class="grid" style:width="{CH + totalMin * ppm}px" style:--ch="{CH}px">
        <div class="ticks">
          <!-- il giorno dell'orario sul bordo sinistro: resta fermo in alto a sinistra mentre si scorre -->
          <div class="corner" style:width="{CH}px">{dayChip(selectedDay, now)}</div>
          {#each ticks as t (t)}
            <div class="tick" class:midnight={t % DAY === 0} style:left="{CH + t * ppm}px">{t % DAY === 0 ? dayChip(dayAt(t / DAY), now) : hhmm(t % DAY)}</div>
          {/each}
        </div>
        {#each visibleRows as r (r.key)}
          <div class="row" style:height="{ROW}px">
            <div class="chname" style:width="{CH}px">
              {#if recordable && r.key.startsWith('c')}
                <button type="button" class="chbtn" class:on={previewOn && previewRow?.key === r.key} onclick={() => pickPreview(r.key)} title="Mostra nell'anteprima" aria-label="Mostra {r.name} nell'anteprima">{#if logoOf(r.key)}<img class="chlogo" src={logoOf(r.key)} alt="" loading="lazy" referrerpolicy="no-referrer" />{/if}{#if r.number != null}<span class="chnum">{r.number}</span>{/if}{r.name}</button>
              {:else}{#if r.number != null}<span class="chnum">{r.number}</span>{/if}{r.name}{/if}
            </div>
            {#each r.shown as x (x.p.id)}
              {@const p = x.p}
              <button type="button" class="prog" class:dim={!matches(p)} class:rec={p.scheduled} class:prop={proposed.has(p.id) && !p.scheduled} class:past={new Date(p.stop) <= now} class:seen={seenState.has(p.workId)}
                style:left="{CH + x.a * ppm}px" style:width="{Math.max(8, (x.b - x.a) * ppm - 4)}px" onclick={() => { tipOut(); openWork(p); }}
                onpointerenter={(e) => tipIn(e, p, r.name)} onpointerleave={tipOut} onpointerdown={tipOut}
                aria-label="{p.title}, {day(p.start, now)} {range(p.start, p.stop)}, {r.name}{p.scheduled ? ', in programma' : ''}{proposed.has(p.id) ? ', proposta per te' : ''}">
                <span class="ptitle">{#if p.scheduled}<span class="dot" aria-hidden="true"></span>{/if}{#if seenState.has(p.workId)}<span class="seenmark" aria-label="visto">✓</span>{/if}{p.title}{p.episode && p.group !== 'Film' ? ` · ${p.episode}` : ''}</span>
                <span class="pmeta">{time(p.start)}{p.rating != null ? ` · ★ ${vote(p.rating)}` : ''}{p.sub ? ` · ${p.sub}` : p.group !== 'Altro' ? ` · ${p.group}` : ''}</span>
              </button>
            {/each}
          </div>
        {/each}
        {#if showNoGuide}
          {#each noGuide as c (c.id)}
            <div class="row noguide" style:height="{ROW}px">
              <div class="chname" style:width="{CH}px">
                <button type="button" class="chbtn" class:on={previewOn && previewRow?.key === 'c' + c.id} onclick={() => pickPreview('c' + c.id)} title="Mostra nell'anteprima" aria-label="Mostra {c.name} nell'anteprima">{#if c.logo}<img class="chlogo" src={c.logo} alt="" loading="lazy" referrerpolicy="no-referrer" />{/if}{#if c.number != null}<span class="chnum">{c.number}</span>{/if}{c.name}</button>
              </div>
              <div class="ng">
                <span>Nessuna guida per questo canale</span>
                <Button size="s" variant="soft" onclick={() => (freeFor = c.id)}>Registra a orario libero</Button>
              </div>
            </div>
          {/each}
        {/if}
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
        <li><button type="button" class="lrow" class:past={new Date(p.stop) <= now} onclick={() => openWork(p)}>
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

{#if tip}
  {@const p = tip.p}
  <div class="tip" bind:this={tipEl} style:left="{tipPos.left}px" style:top="{tipPos.top}px" role="tooltip">
    <strong>{p.title}</strong>
    {#if p.subtitle && p.subtitle !== p.title}<span class="tsub">{p.subtitle}</span>{/if}
    <span class="tmeta">{day(p.start, now)} {range(p.start, p.stop)} · {tip.ch}</span>
    <span class="tmeta">{[p.episode && p.group !== 'Film' ? p.episode : '', p.sub || (p.group !== 'Altro' ? p.group : ''), p.year > 0 ? String(p.year) : '', p.country, p.rating != null ? `★ ${vote(p.rating)}` : ''].filter(Boolean).join(' · ')}</span>
    {#if p.scheduled || proposed.has(p.id) || seenState.has(p.workId)}
      <span class="tflags">{[p.scheduled ? '● In programma' : '', proposed.has(p.id) && !p.scheduled ? 'Proposta per te' : '', seenState.has(p.workId) ? '✓ Già visto' : ''].filter(Boolean).join(' · ')}</span>
    {/if}
    {#if p.description}<span class="tdesc">{p.description}</span>{/if}
  </div>
{/if}
{#if freeFor != null}
  {#key freeFor}<NuovaRegistrazione channel={freeFor} onclose={() => (freeFor = null)} ondone={() => (freeFor = null)} />{/key}
{/if}

<style>
  .tip { position: fixed; z-index: 60; width: max-content; max-width: min(380px, calc(100vw - 16px)); display: flex; flex-direction: column; gap: 4px; padding: 10px 12px; border-radius: var(--radius-s); border: 1px solid var(--line-strong); background: var(--raised); color: var(--text); box-shadow: 0 8px 24px rgba(0, 0, 0, 0.35); pointer-events: none; font-size: 13px; }
  .tip strong { font-size: 14px; line-height: 1.3; }
  .tsub { color: var(--text); }
  .tmeta { color: var(--muted); font-size: 12px; }
  .tmeta:empty { display: none; }
  .tflags { color: var(--rec-text); font-size: 12px; }
  .tdesc { color: var(--muted); line-height: 1.4; display: -webkit-box; -webkit-line-clamp: 5; line-clamp: 5; -webkit-box-orient: vertical; overflow: hidden; }
  .page { padding: 20px var(--gutter) 48px; display: flex; flex-direction: column; gap: 14px; }
  .head { display: flex; gap: 24px; align-items: stretch; padding: 16px 20px; border: 1px solid var(--line); border-radius: var(--radius-l); background: var(--surface); }
  .opts { flex: 1 1 auto; min-width: 0; display: flex; flex-direction: column; justify-content: space-between; gap: 12px; }
  @media (min-width: 721px) {
    .head { position: sticky; top: var(--hdr); z-index: 30; }
    /* griglia: la pagina non scorre, la griglia occupa il resto della finestra */
    .page.fill { height: calc(100dvh - var(--hdr)); padding-bottom: 14px; overflow: hidden; }
    .page.fill .head, .page.fill .gbar { position: static; flex: 0 0 auto; }
    .page.fill .grid-box { flex: 1 1 0; min-height: 0; max-height: none; }
  }
  .top { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 16px; }
  h1 { font-size: 26px; }
  h2 { font-size: 20px; }
  .sub { font-family: var(--font); font-size: 14px; font-weight: 500; color: var(--muted); }
  .days { display: flex; flex-wrap: wrap; gap: 6px; }
  .chip { min-height: 34px; padding: 0 14px; border-radius: 999px; border: 1px solid var(--line-strong); background: none; color: var(--text-2); font-size: 14px; cursor: pointer; }
  .chip.on { background: var(--text); color: var(--bg); border-color: transparent; font-weight: 600; }
  .filters { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; }
  .search { display: flex; align-items: center; gap: 8px; min-height: 40px; padding: 0 12px; border-radius: var(--radius-s); border: 1px solid var(--line-strong); background: var(--card); flex: 1 1 260px; color: var(--muted); }
  .search input { flex: 1; min-width: 0; border: 0; background: none; color: var(--text); font-size: 14px; outline: none; }
  .sel select { min-height: 40px; padding: 0 12px; border-radius: var(--radius-s); border: 1px solid var(--line-strong); background: var(--card); color: var(--text); font-size: 14px; }
  .modes { display: flex; border: 1px solid var(--line-strong); border-radius: var(--radius-s); overflow: hidden; }
  .modes button { min-height: 38px; padding: 0 14px; border: 0; background: none; color: var(--muted); font-size: 14px; cursor: pointer; }
  .modes button.on { background: var(--raised); color: var(--text); font-weight: 600; }
  .switch { display: inline-flex; align-items: center; gap: 8px; font-size: 13px; color: var(--text-2); cursor: pointer; }
  .switch input { position: absolute; opacity: 0; width: 1px; height: 1px; }
  .switch .track { position: relative; width: 34px; height: 20px; border-radius: 999px; background: var(--line-btn); transition: background 0.15s; }
  .switch .track::after { content: ''; position: absolute; top: 3px; left: 3px; width: 14px; height: 14px; border-radius: 50%; background: var(--text); transition: transform 0.15s; }
  .switch input:checked + .track { background: var(--blue); }
  .switch input:checked + .track::after { transform: translateX(14px); }
  .switch input:focus-visible + .track { outline: 2px solid var(--blue); outline-offset: 2px; }
  /* riga di un canale senza guida: il messaggio resta agganciato al bordo sinistro mentre si scorre */
  .row.noguide { display: flex; align-items: center; }
  .row.noguide .chname { color: var(--text-2); flex: 0 0 auto; }
  .ng { position: sticky; left: calc(var(--ch) + 12px); display: inline-flex; align-items: center; gap: 12px; height: 100%; font-size: 13px; color: var(--muted); }
  .zoom { display: flex; align-items: center; gap: 6px; }
  .zoom button { width: 38px; height: 38px; border-radius: var(--radius-s); border: 1px solid var(--line-strong); background: var(--card); color: var(--text); font-size: 20px; line-height: 1; cursor: pointer; }
  .zoom button:disabled { opacity: 0.4; cursor: default; }
  /* slider disegnato a mano: con accent-color chiaro su fondo scuro, in Opera binario e cursore erano dello stesso colore */
  .zoom input { width: 120px; height: 24px; margin: 0; background: none; cursor: pointer; appearance: none; -webkit-appearance: none; }
  .zoom input::-webkit-slider-runnable-track { height: 4px; border-radius: 2px; background: var(--line-btn); }
  .zoom input::-moz-range-track { height: 4px; border-radius: 2px; background: var(--line-btn); }
  .zoom input::-webkit-slider-thumb { -webkit-appearance: none; width: 18px; height: 18px; margin-top: -7px; border-radius: 50%; background: var(--text); border: 2px solid var(--bg); box-shadow: 0 0 0 1px var(--line-btn); }
  .zoom input::-moz-range-thumb { width: 14px; height: 14px; border-radius: 50%; background: var(--text); border: 2px solid var(--bg); }
  .zoom input:focus-visible { outline: 2px solid var(--blue); outline-offset: 4px; border-radius: 4px; }
  .thin { margin: 0; padding: 10px 14px; border-radius: var(--radius-s); background: var(--orange-soft, rgba(245, 162, 93, 0.16)); color: var(--orange-text, #ffb27a); font-size: 14px; }
  .grid-box:global(.dragging) { cursor: grabbing; user-select: none; }
  .gbar { position: relative; z-index: 25; display: flex; flex-wrap: wrap; align-items: center; gap: 10px 16px; }
  .spacer { flex: 1 1 0; }
  .checks { gap: 10px 24px; }
  .propsw { width: 18px; height: 12px; border-radius: 3px; border: 2px solid var(--blue); background: rgba(110, 155, 255, 0.14); box-sizing: border-box; }
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
  /* logo del canale dalla playlist (23/11/2026): piccolo, su un fondo chiaro perché molti loghi sono scuri */
  .chlogo { flex: 0 0 auto; width: 28px; height: 28px; margin-right: 8px; object-fit: contain; border-radius: 6px; background: #e9edf3; padding: 2px; }
  .chfilter { position: relative; }
  .chfilter summary { list-style: none; min-height: 40px; display: inline-flex; align-items: center; padding: 0 12px; border-radius: var(--radius-s); border: 1px solid var(--line-strong); background: var(--card); color: var(--text); font-size: 14px; cursor: pointer; }
  .chfilter summary::-webkit-details-marker { display: none; }
  .chpanel { position: absolute; z-index: 20; top: 46px; left: 0; width: min(420px, calc(100vw - 32px)); max-height: 55vh; overflow-y: auto; display: flex; flex-direction: column; gap: 10px; padding: 14px 16px; border: 1px solid var(--line-strong); border-radius: var(--radius); background: var(--surface); box-shadow: 0 12px 32px rgba(0, 0, 0, 0.45); }
  .check.off { opacity: 0.5; cursor: default; }
  .chpanel hr { border: 0; border-top: 1px solid var(--line); margin: 2px 0; width: 100%; }
  .chbtn { display: flex; align-items: center; width: 100%; height: 100%; padding: 0; border: 0; background: none; color: inherit; font: inherit; text-align: left; cursor: pointer; }
  .chbtn:hover { color: var(--blue); }
  .chbtn.on { color: var(--blue); }
  .preview { flex: 0 0 auto; display: flex; gap: 16px; align-items: center; padding-left: 24px; border-left: 1px solid var(--line); }
  .pv { flex: 0 0 240px; max-width: 100%; }
  .pinfo { flex: 0 0 260px; display: flex; flex-direction: column; gap: 8px; min-width: 0; }
  .pinfo .plink { overflow: hidden; display: -webkit-box; -webkit-line-clamp: 2; line-clamp: 2; -webkit-box-orient: vertical; }
  .plink { padding: 0; border: 0; background: none; color: var(--text); font: inherit; font-size: 13px; text-align: left; cursor: pointer; }
  .plink.now { font-size: 15px; font-weight: 600; }
  .plink:hover { color: var(--link); }
  .check { display: inline-flex; align-items: center; gap: 8px; font-size: 13px; color: var(--text-2); cursor: pointer; max-width: 420px; }
  .check input { width: 18px; height: 18px; margin: 0; flex: 0 0 auto; accent-color: var(--blue); }
  .check strong { color: var(--text); font-weight: 600; }
  .chnum { min-width: 28px; margin-right: 8px; color: var(--muted); font-variant-numeric: tabular-nums; text-align: right; }
  .prog {
    position: absolute; top: 6px; bottom: 6px; display: flex; flex-direction: column; justify-content: center; gap: 3px;
    padding: 6px 10px; border-radius: var(--radius-s); border: 1px solid var(--line-strong); background: #1a1f28;
    color: var(--text); text-align: left; overflow: clip; cursor: pointer;
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
  /* titolo e orario restano agganciati al bordo sinistro della linea del tempo, così un programma iniziato prima del
     tratto visibile si legge lo stesso (23/11/2026); serve overflow: clip sulla casella, perché hidden fermerebbe sticky */
  .ptitle, .pmeta { position: sticky; left: calc(var(--ch) + 10px); align-self: flex-start; max-width: 100%; }
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
  /* finestre strette: l'anteprima resta a destra, più piccola e senza «Ora/Dopo» (il popup sui programmi li dice già) */
  @media (max-width: 1300px) { .pinfo { display: none; } .pv { flex-basis: 220px; } .preview { padding-left: 20px; } }
  @media (max-width: 720px) { .head.withpv { flex-direction: column; align-items: stretch; } .head.withpv .preview { padding-left: 0; border-left: 0; } .pinfo { display: flex; flex: 1 1 auto; } }
  @media (max-width: 720px) { .grid-box { max-height: none; } .lch { flex-basis: auto; } .chnum { display: none; } .chlogo { width: 22px; height: 22px; margin-right: 6px; } }
</style>
