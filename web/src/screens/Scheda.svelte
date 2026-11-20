<script lang="ts">
  // Scheda dell'opera: risponde in quest'ordine a "è in casa?", "passa in TV?", "dov'è in streaming?".
  import { openConvert } from '../lib/convert.svelte';
  import { recordChoice } from '../lib/series.svelte';
  import Badge from '../lib/components/Badge.svelte';
  import PosterImg from '../lib/components/PosterImg.svelte';
  import Button from '../lib/components/Button.svelte';
  import RecordButton from '../lib/components/RecordButton.svelte';
  import AlertPanel from '../lib/components/AlertPanel.svelte';
  import ServiceIcon from '../lib/components/ServiceIcon.svelte';
  import StableLabel from '../lib/components/StableLabel.svelte';
  import AbbinaDialog from './AbbinaDialog.svelte';
  import { inMySubscriptions, payPlatform, streamingFor } from '../lib/pay';
  import { platforms } from '../lib/platforms.svelte';
  import { api, ApiError, type GuideProgramme, type ListSummary, type WorkDetail } from '../lib/api/client';
  import { between, clock, day, episodeOf, fileName, minutes, range, recordingTitle, time, vote } from '../lib/format';
  import { router, navigate } from '../lib/router.svelte';
  import { session } from '../lib/session.svelte';

  let { id }: { id: number } = $props();

  let w = $state<WorkDetail | null>(null);
  let hasTmdbKey = $state(true);
  let error = $state('');
  let busy = $state<number | null>(null);
  let notice = $state('');
  // "Abbina di nuovo…" su un file in casa abbinato al titolo sbagliato: dopo, si va alla scheda giusta
  let rematch = $state<number | null>(null);
  function rematched(it: { workId?: number | null }) {
    rematch = null;
    if (it.workId != null && it.workId !== w?.id) router.go(`/opera/${it.workId}`);
    else { notice = 'Abbinamento salvato.'; load(); }
  }
  const kindOf = (k: string | undefined) => (k === 'film' || k === 'series' || k === 'documentary' || k === 'programme' ? k : null);

  let mySubs = $state<string[]>([]);
  let ignored = $state(false);

  // "Avvisami" si apre sotto i pulsanti, come le liste: niente da cercare in fondo alla pagina
  let alertsOpen = $state(false);
  let activeAlerts = $state(0);
  function openAlerts() {
    alertsOpen = true;
    listsOpen = false;
    setTimeout(() => document.getElementById('avvisami')?.scrollIntoView({ behavior: 'smooth', block: 'nearest' }), 50);
  }

  async function load() {
    try {
      w = await api.work(id, session.profile?.id);
      error = '';
      // la chiave TMDB serve solo a dire perché mancano le piattaforme (20/11/2026: il messaggio usciva anche con la chiave)
      if (!w.tmdbId) api.ratingSettings().then((r) => (hasTmdbKey = r.hasTmdbKey)).catch(() => {});
      const prof = session.profile;
      if (prof) {
        api.profileSubscriptions(prof.id).then((s) => (mySubs = s.services)).catch(() => {});
        api.ignored(prof.id).then((l) => (ignored = !!w && l.some((i) => i.title === w!.title))).catch(() => {});
        api.alerts(prof.id, id).then((l) => (activeAlerts = l.filter((a) => a.active).length)).catch(() => {});
      }
      if (location.hash === '#avvisami' && prof) openAlerts();
    } catch (e) {
      error = e instanceof ApiError ? (e.status === 404 ? 'Opera non trovata.' : e.message) : 'Scheda non letta';
    }
  }
  $effect(() => { void id; w = null; load(); });

  // trama, regia, attori, paese: dalla guida se TMDB non li ha
  const first = $derived(w?.airings[0] ?? null);
  const plot = $derived(w?.overview || first?.description || '');
  const runtime = $derived(w?.runtimeMin ?? (first ? Math.round(between(first.start, first.stop)) : null));
  const genre = $derived(first ? first.sub || first.group : '');
  const nextRecordable = $derived(w?.airings.find((a) => a.channelId != null) ?? null);

  const stars = [1, 2, 3, 4, 5];
  const words = ['Non votato', 'Brutto', 'Così così', 'Buono', 'Molto bello', 'Bellissimo'];

  async function setStars(n: number) {
    const prof = session.profile;
    if (!prof || !w) return;
    const value = w.stars === n ? null : n;
    try { await api.setStars(prof.id, w.id, value); w.stars = value; }
    catch (e) { error = e instanceof ApiError ? e.message : 'Voto non salvato'; }
  }

  async function record(a: GuideProgramme) {
    if (a.channelId == null) return;
    if (a.scheduled) { router.go('/registrazioni'); return; }
    const how = await recordChoice(a);
    if (how === null) return;
    if (how === 'series') { await load(); return; }
    busy = a.id;
    notice = '';
    try {
      const r = await api.scheduleRecording({ channelId: a.channelId, title: recordingTitle({ ...a, title: w?.title ?? a.title }), start: a.start, stop: a.stop, beforeMin: null, afterMin: null, format: null, programmeId: a.id });
      notice = ['Registrazione programmata.', ...r.warnings.map((x) => `Attenzione: ${x}.`)].join(' ');
      await load();
    } catch (e) {
      error = e instanceof ApiError ? e.message : 'Registrazione non programmata';
    } finally {
      busy = null;
    }
  }

  async function toggleTonight() {
    const prof = session.profile;
    if (!prof || !w) return;
    try {
      if (w.inTonight) await api.removeTonight(prof.id, w.id);
      else await api.addTonight(prof.id, w.id);
      w.inTonight = !w.inTonight;
    } catch (e) { error = e instanceof ApiError ? e.message : 'Lista non aggiornata'; }
  }

  async function noMore() {
    const prof = session.profile;
    if (!prof || !w) return;
    try { await api.ignore(prof.id, w.id); ignored = true; }
    catch (e) { error = e instanceof ApiError ? e.message : 'Non salvato'; }
  }

  // "Visto" è un interruttore: segna o toglie il visto (anche dei file in casa e degli episodi) e rilegge la scheda
  let seenBusy = $state(false);
  async function toggleSeen() {
    const prof = session.profile;
    if (!prof || !w || seenBusy) return;
    seenBusy = true;
    notice = '';
    try {
      const was = w.watched;
      if (was) await api.unwatched(prof.id, w.id);
      else await api.watched(prof.id, w.id);
      w = await api.work(w.id, prof.id);
      notice = was ? 'Segnato come non visto.' : `Segnato come visto${w.files.length > 0 ? ', anche i file in casa' : ''}. Se ti va, dagli le stelle qui sopra.`;
    } catch (e) {
      error = e instanceof ApiError ? e.message : 'Non segnato';
    } finally {
      seenBusy = false;
    }
  }

  // file in casa: si riprende quello iniziato, altrimenti il primo non visto (per le serie, in ordine di episodio)
  const playFile = $derived.by(() => {
    const f = w?.files ?? [];
    return f.find((x) => x.positionS > 0 && !x.watched) ?? f.find((x) => !x.watched) ?? f[0] ?? null;
  });
  const episodes = $derived((w?.files ?? []).some((f) => f.episode != null));

  // liste: si leggono quando si apre il menu
  let listsOpen = $state(false);
  let allLists = $state<ListSummary[]>([]);
  async function openLists() {
    const prof = session.profile;
    if (!prof) return;
    listsOpen = !listsOpen;
    if (listsOpen) {
      alertsOpen = false;
      try { allLists = (await api.lists(prof.id)).filter((l) => l.canEdit); }
      catch (e) { error = e instanceof ApiError ? e.message : 'Liste non lette'; }
    }
  }
  async function toggleList(l: ListSummary) {
    const prof = session.profile;
    if (!prof || !w) return;
    const inside = w.lists.some((x) => x.id === l.id);
    try {
      if (inside) await api.removeFromList(prof.id, l.id, w.id);
      else await api.addToList(prof.id, l.id, w.id);
      w = await api.work(w.id, prof.id);
    } catch (e) { error = e instanceof ApiError ? e.message : 'Lista non aggiornata'; }
  }

  // l'abbonamento si divide in due: le piattaforme che ho (si vede subito) e quelle che non ho
  const kinds: Record<string, string> = { mine: 'Nei tuoi abbonamenti', subscription: 'Non nei tuoi abbonamenti', free: 'Gratis', rent: 'A noleggio', buy: 'In vendita' };
  const byKind = $derived.by(() => {
    const m = new Map<string, string[]>();
    for (const a of w?.availability ?? []) {
      const k = a.kind === 'subscription' && streamingFor({ streaming: [a.platform] }, mySubs).length > 0 ? 'mine' : a.kind;
      m.set(k, [...(m.get(k) ?? []), a.platform]);
    }
    // "Amazon Prime Video with Ads" non si ripete se c'è già "Amazon Prime Video"
    const tidy = (l: string[]) => l.filter((n) => {
      const base = n.replace(/ (basic |standard )?with ads$/i, '');
      return base === n || !l.some((x) => x.toLowerCase() === base.toLowerCase());
    });
    return ['mine', 'subscription', 'free', 'rent', 'buy'].filter((k) => m.has(k)).map((k) => ({ kind: k, label: kinds[k] ?? k, names: tidy(m.get(k)!) }));
  });
  const watchLink = $derived(w?.availability.find((a) => a.link)?.link ?? '');
  // originale della piattaforma appena uscito: dedotto dall'uscita, JustWatch non lo ha ancora in catalogo
  const fromRelease = $derived((w?.availability ?? []).filter((a) => a.source === 'release').map((a) => a.platform));

  // banner: la locandina ingrandita e tagliata (più grande per non sgranare); senza locandina l'immagine di scena
  const heroImage = $derived((w?.poster || first?.icon || '').replace('/t/p/w500/', '/t/p/w780/') || w?.backdrop || '');

  // aperta da un link diretto non c'è una pagina precedente dell'app: si torna a Stasera
  function goBack() {
    if (history.length > 1) history.back();
    else router.go('/');
  }
  const recordLabel = (a: GuideProgramme) => {
    if (new Date(a.start).getTime() < Date.now()) return 'Registra da adesso';
    const d = day(a.start);
    return d === 'Stasera' || d === 'Oggi' ? `Registra ${d.toLowerCase()}` : `Registra ${d.toLowerCase()} ${time(a.start)}`;
  };
</script>

{#if error && !w}
  <section class="pad"><p class="error" role="alert">{error}</p><Button onclick={() => history.back()}>Indietro</Button></section>
{:else if !w}
  <p class="pad muted" role="status">Un momento…</p>
{:else}
  <section class="hero">
    {#if heroImage}<img class="backdrop" src={heroImage} alt="" referrerpolicy="no-referrer" />{:else}<div class="hero-word" aria-hidden="true">{w.title}</div>{/if}
    <div class="topbar">
      <button type="button" class="back" onclick={goBack}>
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M15 18l-6-6 6-6" /></svg>
        Indietro
      </button>
    </div>
    <div class="hero-in">
      <div class="poster">
        <PosterImg src={w.poster || first?.icon || ''} title={w.title} />
      </div>
      <div class="info">
        {#if w.parentId}<a class="parent" href="/opera/{w.parentId}" onclick={navigate}>{w.parentTitle}</a>{/if}
        <h1>{w.title}</h1>
        {#if w.originalTitle && w.originalTitle !== w.title}<div class="orig">{w.originalTitle}</div>{/if}
        <div class="facts">
          {#if w.year}<span>{w.year}</span>{/if}
          {#if genre}<span>{genre}</span>{/if}
          {#if runtime}<span>{minutes(runtime)}</span>{/if}
          {#if first?.country}<span>{first.country}</span>{/if}
          {#if w.rating != null}<Badge tone="blue">★ {vote(w.rating)} {w.ratingSource}</Badge>{:else if w.kind !== 'programme' && w.kind !== 'episode'}<span title="Voto non ancora disponibile: IMDb non ha ancora il titolo e su TMDB ha meno di 20 voti">★ N/A</span>{/if}
          {#if w.watched}<Badge tone="green">Visto</Badge>{/if}
        </div>
        {#if plot}<p class="plot">{plot}</p>{/if}
        {#if first?.directors || first?.actors}
          <p class="credits">{first.directors ? `Regia di ${first.directors}` : ''}{first.directors && first.actors ? ' · ' : ''}{first.actors ? `Con ${first.actors}` : ''}</p>
        {/if}
        {#if session.profile}
          <div class="stars" role="group" aria-label="Il tuo voto">
            <span class="muted">Il tuo voto</span>
            {#each stars as n (n)}
              <button type="button" class="star" class:on={(w.stars ?? 0) >= n} aria-label="{n} {n === 1 ? 'stella' : 'stelle'}" aria-pressed={w.stars === n} onclick={() => setStars(n)}>★</button>
            {/each}
            <span class="muted small">{words[w.stars ?? 0]}</span>
          </div>
        {/if}
        <div class="actions">
          {#if playFile}
            <Button variant="primary" size="l" href="/guarda/{playFile.itemId}" onclick={navigate}>▶ {playFile.positionS > 0 && !playFile.watched ? `Riprendi da ${clock(playFile.positionS)}` : episodes && playFile.episode != null ? `Guarda S${playFile.season}E${String(playFile.episode).padStart(2, '0')}` : 'Guarda'}</Button>
          {/if}
          {#if nextRecordable}<RecordButton scheduled={nextRecordable.scheduled} busy={busy === nextRecordable.id} label={recordLabel(nextRecordable)} onclick={() => record(nextRecordable)} />{/if}
          {#if w.trailer}<Button variant="soft" size="l" href={w.trailer}>▶ Trailer</Button>{/if}
          {#if session.profile}
            <Button variant={w.inTonight ? "primary" : "soft"} size="l" pressed={w.inTonight} onclick={toggleTonight}><StableLabel text={w.inTonight ? 'Nella mia lista' : 'Aggiungi alla mia lista'} variants={['Nella mia lista', 'Aggiungi alla mia lista']} /></Button>
            <Button variant={w.watched ? "primary" : "soft"} size="l" pressed={w.watched} disabled={seenBusy} onclick={toggleSeen}><StableLabel text={w.watched ? 'Visto' : 'Segna come visto'} variants={['Visto', 'Segna come visto']} /></Button>
            <Button variant="soft" size="l" pressed={listsOpen} onclick={openLists}>Aggiungi a una lista</Button>
            <!-- pulsanti a stato (serata, visto, avvisi): acceso = colori invertiti, senza segno di spunta -->
            <Button variant={activeAlerts > 0 ? 'primary' : 'soft'} size="l" pressed={alertsOpen} onclick={() => (alertsOpen ? (alertsOpen = false) : openAlerts())}><StableLabel text={activeAlerts > 0 ? 'Avvisi attivi' : 'Avvisami'} variants={['Avvisi attivi', 'Avvisami']} /></Button>
            <Button variant="ghost" size="l" disabled={ignored} onclick={noMore}><StableLabel text={ignored ? 'Non lo propongo più' : 'Non proporre più'} variants={['Non lo propongo più', 'Non proporre più']} /></Button>
          {/if}
        </div>
        {#if listsOpen}
          <div class="listmenu" role="group" aria-label="Liste">
            {#each allLists.filter((l) => !l.isTonight) as l (l.id)}
              <label class="lcheck"><input type="checkbox" checked={w.lists.some((x) => x.id === l.id)} onchange={() => toggleList(l)} />{l.name}{#if l.family}<span class="muted small"> · famiglia</span>{/if}</label>
            {:else}
              <span class="muted small">Nessuna lista: creale in <a href="/libreria" onclick={navigate}>Libreria</a>.</span>
            {/each}
          </div>
        {/if}
        {#if w.lists.filter((l) => !l.isTonight).length > 0}
          <p class="muted small">Nelle liste: {w.lists.filter((l) => !l.isTonight).map((l) => l.name).join(', ')}</p>
        {/if}
        {#if notice}<p class="ok" role="status">{notice}</p>{/if}
        {#if error}<p class="error" role="alert">{error}</p>{/if}
        {#if nextRecordable && !nextRecordable.scheduled}<p class="muted small">Con le tue impostazioni: margini prima e dopo, formato e cartella predefiniti.</p>{/if}
      </div>
    </div>
    {#if alertsOpen && session.profile}
      <!-- fuori da .hero-in: la locandina resta dov'è; allineato alla colonna del testo -->
      <div id="avvisami" class="alertbox"><AlertPanel workId={w.id} onchange={(n) => (activeAlerts = n)} onclose={() => (alertsOpen = false)} /></div>
    {/if}
  </section>

  <div class="pad cols">
    <div class="main">
      <section>
        <h2>In casa</h2>
        {#if w.files.length > 0}
          <ul class="rows">
            {#each w.files as f (f.itemId)}
              <li>
                <span class="r-main">
                  <strong>{f.episode != null ? `S${f.season}E${String(f.episode).padStart(2, '0')}${f.episodeTitle ? ' · ' + f.episodeTitle : ''}` : f.name}</strong>
                  <span class="muted small">{[f.durationS ? clock(f.durationS) : '', f.width && f.height ? `${f.width}×${f.height}` : '', f.origin === 'recorded' ? 'registrazione' : 'dal NAS', f.positionS > 0 && !f.watched ? `fermo a ${clock(f.positionS)}` : ''].filter(Boolean).join(' · ')}</span>
                </span>
                {#if f.watched}<Badge tone="green">Visto</Badge>{/if}
                <Button size="s" variant={f.playable ? 'primary' : 'secondary'} href="/guarda/{f.itemId}" onclick={navigate}>{f.playable ? (f.positionS > 0 && !f.watched ? '▶ Riprendi' : '▶ Guarda') : 'Prepara e guarda'}</Button>
                {#if session.profile && !session.profile.isChild}
                  <Button size="s" variant="ghost" onclick={() => (rematch = f.itemId)}>Abbina di nuovo…</Button>
                  <Button size="s" variant="ghost" onclick={() => openConvert({ libraryItemId: f.itemId, name: f.name })}>Converti...</Button>
                {/if}
              </li>
            {/each}
          </ul>
        {:else if w.inHouse.length === 0}
          <p class="box muted">Non c'è ancora nelle tue registrazioni.{nextRecordable ? ' Dopo la registrazione lo trovi qui.' : ''}</p>
        {:else}
          <ul class="rows">
            {#each w.inHouse as f (f.recordingId)}
              <li><span class="r-main"><strong>{fileName(f.file)}</strong><span class="muted small">Registrato {day(f.start).toLowerCase()} da {f.channelName}{f.lostMinutes ? ` · mancano ${minutes(f.lostMinutes)}` : ''}</span></span>
                {#if f.state === 'incomplete'}<Badge tone="orange">Incompleta</Badge>{:else if f.state === 'stopped'}<Badge tone="neutral">Fermata</Badge>{:else}<Badge tone="green">In casa</Badge>{/if}
                <Button size="s" variant="ghost" onclick={() => openConvert({ recordingId: f.recordingId, name: fileName(f.file) })}>Converti...</Button></li>
            {/each}
          </ul>
          <p class="muted small">La registrazione entra nella libreria tra poco: poi la guardi da qui.</p>
        {/if}
      </section>

      <section>
        <h2>In TV</h2>
        {#if w.airings.length === 0}
          <p class="box muted">Nessun passaggio nei giorni della guida.</p>
        {:else}
          <ul class="rows">
            {#each w.airings as a (a.id)}
              <li class:hl={a.id === nextRecordable?.id}>
                <span class="r-main"><strong>{day(a.start)} · {range(a.start, a.stop)}</strong>{#if episodeOf(a)}<span class="ep">{episodeOf(a)}</span>{/if}<span class="muted small">{a.channelName || a.guideChannelName}{a.channelId == null ? ' · non è nelle tue playlist' : ''}</span></span>
                {#if a.channelId != null}<RecordButton size="m" scheduled={a.scheduled} busy={busy === a.id} onclick={() => record(a)} />{/if}
              </li>
            {/each}
          </ul>
        {/if}
      </section>
    </div>

    <aside>
      <h2>Dove Lo Fanno</h2>
      <div class="where">
        {#if w.airings[0]}
          <div class="wrow"><span><span class="muted small">In onda</span><br /><strong>{w.airings[0].channelName || w.airings[0].guideChannelName}, {day(w.airings[0].start).toLowerCase()} {time(w.airings[0].start)}</strong></span>{#if !payPlatform(w.airings[0])}<Badge tone="blue">Gratis</Badge>{:else if inMySubscriptions(w.airings[0], mySubs)}<Badge tone="green">Nel tuo abbonamento</Badge>{:else}<Badge tone="orange">A pagamento · {payPlatform(w.airings[0])}</Badge>{/if}</div>
        {/if}
        {#each byKind as k (k.kind)}
          <div class="wrow"><span><span class="muted small">{k.label}</span><span class="plats">{#each platforms.sort(k.names) as n (n)}<span class="plat" class:mine={k.kind === 'mine'}><ServiceIcon name={n} size={26} label={false} /><strong>{platforms.of(n).short}</strong></span>{/each}</span>{#if (k.kind === 'subscription' || k.kind === 'mine') && k.names.some((n) => fromRelease.includes(n))}<span class="muted small origin">Originale {fromRelease.map((n) => platforms.of(n).short).join(', ')}: lo dice la data di uscita. JustWatch non l'ha ancora in catalogo e lo aggiunge di solito in pochi giorni.</span>{/if}</span>{#if watchLink}<a href={watchLink} target="_blank" rel="noopener noreferrer">Apri ↗</a>{/if}</div>
        {/each}
        {#if !w.airings[0] && byKind.length === 0}<div class="wrow muted">{#if w.tmdbId}<span>Nessuna piattaforma nota in Italia, per ora. Per i titoli appena usciti JustWatch la aggiunge spesso dopo qualche giorno: con <a href="#avvisami" onclick={(e) => { e.preventDefault(); openAlerts(); }}>Avvisami</a> «nei miei abbonamenti» ti avviso quando arriva.</span>{:else if !hasTmdbKey}Per «Dove Lo Fanno» serve la chiave TMDB (Impostazioni › Voti dei film).{:else if w.kind === 'programme' || w.kind === 'episode'}Si vede quando passa in TV: per ora la guida non ha passaggi in arrivo. Con <a href="#avvisami" onclick={(e) => { e.preventDefault(); openAlerts(); }}>Avvisami</a> ti avviso quando torna.{:else}Titolo non trovato su TMDB: le piattaforme dove si vede non si conoscono.{/if}</div>{/if}
      </div>
      {#if byKind.length > 0}<p class="muted small">Disponibilità: JustWatch tramite TMDB{w.detailsAt ? `, verificata ${day(w.detailsAt).toLowerCase()}` : ''}.</p>{/if}
    </aside>
  </div>
  {#if rematch !== null}
    <AbbinaDialog itemId={rematch} hasTmdb={true} title={w.title} startYear={w.year}
      startKind={kindOf(w.kind)} onclose={() => (rematch = null)} ondone={rematched} />
  {/if}
{/if}

<style>
  .ep { font-size: 14px; color: var(--text); }
  .pad { padding: 0 var(--gutter); }
  .muted { color: var(--muted); margin: 0; }
  .small { font-size: 13px; }
  .error { color: var(--rec-text); margin: 0; }
  .ok { color: var(--green-text); margin: 0; }
  .hero { position: relative; overflow: hidden; background: #172033; }
  .backdrop { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; object-position: center 22%; opacity: 0.5; filter: blur(2px) saturate(1.1); transform: scale(1.04); }
  /* il testo resta leggibile: scuro a sinistra e in basso, la locandina si vede a destra */
  .hero::after { content: ''; position: absolute; inset: 0; pointer-events: none;
    background: linear-gradient(90deg, rgba(13, 16, 21, 0.92) 0%, rgba(13, 16, 21, 0.7) 45%, rgba(13, 16, 21, 0.25) 100%), linear-gradient(0deg, var(--bg) 0%, rgba(13, 16, 21, 0) 35%); }
  .hero > :not(.backdrop):not(.hero-word) { z-index: 1; }
  .topbar { position: relative; padding: 18px var(--gutter) 0; }
  .back {
    display: inline-flex; align-items: center; gap: 6px; min-height: 40px; padding: 0 16px 0 10px; border-radius: 20px;
    border: 1px solid rgba(255, 255, 255, 0.28); background: rgba(13, 16, 21, 0.55); color: var(--text);
    font-size: 15px; font-weight: 600; cursor: pointer; backdrop-filter: blur(6px);
  }
  .back:hover, .back:focus-visible { background: rgba(255, 255, 255, 0.14); border-color: var(--text); }
  .hero-word { position: absolute; top: 20px; right: var(--gutter); max-width: 70%; text-align: right; font-family: var(--font-display); font-weight: 700; font-size: clamp(60px, 10vw, 150px); line-height: 0.95; color: #1f2b44; text-transform: uppercase; overflow: hidden; display: -webkit-box; -webkit-line-clamp: 2; line-clamp: 2; -webkit-box-orient: vertical; }
  .hero-in { position: relative; display: flex; flex-wrap: wrap; gap: 40px; align-items: flex-end; padding: 28px var(--gutter) 36px; max-width: 1240px; }
  .poster { flex: 0 0 220px; aspect-ratio: 2 / 3; border-radius: 14px; background: #2b3d63; overflow: hidden; display: flex; align-items: flex-end; padding: 16px; box-shadow: 0 12px 32px rgba(0, 0, 0, 0.35); position: relative; }
  .info { flex: 1 1 420px; min-width: 0; display: flex; flex-direction: column; gap: 12px; }
  .parent { align-self: flex-start; font-size: 15px; font-weight: 600; text-decoration: none; }
  .listmenu { display: flex; flex-wrap: wrap; gap: 8px 16px; background: var(--card); border: 1px solid var(--line-strong); border-radius: 12px; padding: 12px 14px; }
  .alertbox { position: relative; max-width: 760px; margin: -12px var(--gutter) 32px calc(var(--gutter) + 260px); scroll-margin: 16px; }
  .lcheck { display: inline-flex; align-items: center; gap: 8px; min-height: 36px; font-size: 15px; cursor: pointer; }
  .lcheck input { width: 18px; height: 18px; accent-color: var(--blue); }
  h1 { font-size: clamp(36px, 5vw, 56px); line-height: 1; letter-spacing: -0.02em; }
  .orig { color: var(--muted); font-size: 16px; }
  .facts { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 14px; font-size: 15px; color: var(--text-2); }
  .plot { margin: 0; font-size: 17px; line-height: 1.55; color: var(--text-2); max-width: 680px; }
  .credits { margin: 0; font-size: 14px; color: var(--muted); line-height: 1.6; }
  .stars { display: flex; flex-wrap: wrap; align-items: center; gap: 2px; }
  .stars .muted:first-child { margin-right: 8px; font-size: 14px; }
  .star { width: 40px; height: 40px; border: 0; background: none; padding: 0; font-size: 28px; line-height: 1; color: #4a5262; cursor: pointer; }
  .star.on { color: var(--star); }
  .stars .small { margin-left: 6px; }
  .actions { display: flex; flex-wrap: wrap; gap: 12px; margin-top: 4px; }
  .cols { display: flex; flex-wrap: wrap; gap: 32px; padding-top: 32px; padding-bottom: 56px; max-width: 1240px; }
  .main { flex: 999 1 560px; min-width: 0; display: flex; flex-direction: column; gap: 28px; }
  aside { flex: 1 1 320px; min-width: 0; display: flex; flex-direction: column; gap: 12px; }
  h2 { font-size: 22px; margin-bottom: 12px; }
  aside h2 { margin-bottom: 0; }
  .box { background: var(--card); border: 1px dashed var(--line-btn); border-radius: 12px; padding: 16px; }
  .rows { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 10px; }
  .rows li { display: flex; flex-wrap: wrap; align-items: center; gap: 12px 16px; background: var(--card); border: 1px solid var(--line-strong); border-radius: 12px; padding: 14px 16px; }
  .rows li.hl { border: 2px solid var(--blue); }
  .r-main { flex: 1 1 260px; display: flex; flex-direction: column; gap: 2px; min-width: 0; overflow-wrap: anywhere; }
  .where { background: var(--card); border: 1px solid var(--line-strong); border-radius: 12px; overflow: hidden; }
  .wrow { display: flex; justify-content: space-between; align-items: center; gap: 12px; padding: 14px 16px; border-bottom: 1px solid var(--line); }
  .wrow:last-child { border-bottom: 0; }
  .origin { display: block; margin-top: 6px; line-height: 1.4; }
  .plats { display: flex; flex-wrap: wrap; gap: 6px 14px; margin-top: 6px; }
  .plat { display: inline-flex; align-items: center; gap: 8px; font-size: 15px; }
  .plat.mine :global(.ic) { box-shadow: 0 0 0 2px var(--green); }
  .wrow a { font-size: 14px; font-weight: 600; text-decoration: none; white-space: nowrap; }
  @media (max-width: 720px) { .poster { flex-basis: 140px; padding: 12px; } .hero-in { gap: 20px; padding-top: 24px; } }
  /* quando la locandina va sopra il testo (flex-wrap), il pannello parte dal margine */
  @media (max-width: 780px) { .alertbox { margin-left: var(--gutter); } }
</style>
