<script lang="ts">
  // Scheda dell'opera: risponde in quest'ordine a "è in casa?", "passa in TV?", "dov'è in streaming?".
  import Badge from '../lib/components/Badge.svelte';
  import Button from '../lib/components/Button.svelte';
  import RecordButton from '../lib/components/RecordButton.svelte';
  import { api, ApiError, type GuideProgramme, type WorkDetail } from '../lib/api/client';
  import { day, fileName, minutes, between, range, time, vote } from '../lib/format';
  import { router } from '../lib/router.svelte';
  import { session } from '../lib/session.svelte';

  let { id }: { id: number } = $props();

  let w = $state<WorkDetail | null>(null);
  let error = $state('');
  let busy = $state<number | null>(null);
  let notice = $state('');

  async function load() {
    try {
      w = await api.work(id, session.profile?.id);
      error = '';
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
    busy = a.id;
    notice = '';
    try {
      const r = await api.scheduleRecording({ channelId: a.channelId, title: w?.title ?? a.title, start: a.start, stop: a.stop, beforeMin: null, afterMin: null, format: null, programmeId: a.id });
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

  async function markSeen() {
    const prof = session.profile;
    if (!prof || !w) return;
    try { await api.watched(prof.id, w.id); w.watched = true; w.inTonight = false; }
    catch (e) { error = e instanceof ApiError ? e.message : 'Non segnato'; }
  }

  const kinds: Record<string, string> = { subscription: 'In abbonamento', free: 'Gratis', rent: 'A noleggio', buy: 'In vendita' };
  const byKind = $derived.by(() => {
    const m = new Map<string, string[]>();
    for (const a of w?.availability ?? []) m.set(a.kind, [...(m.get(a.kind) ?? []), a.platform]);
    return ['subscription', 'free', 'rent', 'buy'].filter((k) => m.has(k)).map((k) => ({ kind: k, label: kinds[k] ?? k, names: m.get(k)! }));
  });
  const watchLink = $derived(w?.availability[0]?.link ?? '');
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
    {#if w.backdrop}<img class="backdrop" src={w.backdrop} alt="" referrerpolicy="no-referrer" />{:else}<div class="hero-word" aria-hidden="true">{w.title}</div>{/if}
    <div class="hero-in">
      <div class="poster">
        {#if w.poster}<img src={w.poster} alt="Locandina di {w.title}" referrerpolicy="no-referrer" />{:else}<span class="ptitle">{w.title}</span>{/if}
      </div>
      <div class="info">
        <button type="button" class="back" onclick={() => history.back()}>← Indietro</button>
        <h1>{w.title}</h1>
        {#if w.originalTitle && w.originalTitle !== w.title}<div class="orig">{w.originalTitle}</div>{/if}
        <div class="facts">
          {#if w.year}<span>{w.year}</span>{/if}
          {#if genre}<span>{genre}</span>{/if}
          {#if runtime}<span>{minutes(runtime)}</span>{/if}
          {#if first?.country}<span>{first.country}</span>{/if}
          {#if w.rating != null}<Badge tone="blue">★ {vote(w.rating)} {w.ratingSource}</Badge>{/if}
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
          {#if nextRecordable}<RecordButton scheduled={nextRecordable.scheduled} busy={busy === nextRecordable.id} label={recordLabel(nextRecordable)} onclick={() => record(nextRecordable)} />{/if}
          {#if w.trailer}<Button variant="soft" size="l" href={w.trailer}>▶ Trailer</Button>{/if}
          {#if session.profile}
            <Button variant="soft" size="l" pressed={w.inTonight} onclick={toggleTonight}>{w.inTonight ? 'Nella mia serata ✓' : 'Aggiungi alla mia serata'}</Button>
            {#if !w.watched}<Button variant="soft" size="l" onclick={markSeen}>✓ Già visto</Button>{/if}
          {/if}
        </div>
        {#if notice}<p class="ok" role="status">{notice}</p>{/if}
        {#if error}<p class="error" role="alert">{error}</p>{/if}
        {#if nextRecordable && !nextRecordable.scheduled}<p class="muted small">Con le tue impostazioni: margini prima e dopo, formato e cartella predefiniti.</p>{/if}
      </div>
    </div>
  </section>

  <div class="pad cols">
    <div class="main">
      <section>
        <h2>In casa</h2>
        {#if w.inHouse.length === 0}
          <p class="box muted">Non c'è ancora nelle tue registrazioni.{nextRecordable ? ' Dopo la registrazione lo trovi qui.' : ''}</p>
        {:else}
          <ul class="rows">
            {#each w.inHouse as f (f.recordingId)}
              <li><span class="r-main"><strong>{fileName(f.file)}</strong><span class="muted small">Registrato {day(f.start).toLowerCase()} da {f.channelName}{f.lostMinutes ? ` · mancano ${minutes(f.lostMinutes)}` : ''}</span></span>
                {#if f.state === 'incomplete'}<Badge tone="orange">Incompleta</Badge>{:else if f.state === 'stopped'}<Badge tone="neutral">Fermata</Badge>{:else}<Badge tone="green">In casa</Badge>{/if}</li>
            {/each}
          </ul>
          <p class="muted small">Il player arriva con la libreria; intanto il file è nella cartella delle registrazioni.</p>
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
                <span class="r-main"><strong>{day(a.start)} · {range(a.start, a.stop)}</strong><span class="muted small">{a.channelName || a.guideChannelName}{a.channelId == null ? ' · non è nelle tue playlist' : ''}</span></span>
                {#if a.channelId != null}<RecordButton size="m" scheduled={a.scheduled} busy={busy === a.id} onclick={() => record(a)} />{/if}
              </li>
            {/each}
          </ul>
        {/if}
      </section>
    </div>

    <aside>
      <h2>Dove lo vedo</h2>
      <div class="where">
        {#if w.airings[0]}
          <div class="wrow"><span><span class="muted small">In onda</span><br /><strong>{w.airings[0].channelName || w.airings[0].guideChannelName}, {day(w.airings[0].start).toLowerCase()} {time(w.airings[0].start)}</strong></span><Badge tone="blue">Gratis</Badge></div>
        {/if}
        {#each byKind as k (k.kind)}
          <div class="wrow"><span><span class="muted small">{k.label}</span><br /><strong>{k.names.join(', ')}</strong></span>{#if watchLink}<a href={watchLink} target="_blank" rel="noopener noreferrer">Apri ↗</a>{/if}</div>
        {/each}
        {#if !w.airings[0] && byKind.length === 0}<div class="wrow muted">{w.tmdbId ? 'Nessuna disponibilità nota in Italia.' : 'Per «dove lo vedo» serve la chiave TMDB (Impostazioni › Voti dei film).'}</div>{/if}
      </div>
      {#if byKind.length > 0}<p class="muted small">Disponibilità: JustWatch tramite TMDB{w.detailsAt ? `, verificata ${day(w.detailsAt).toLowerCase()}` : ''}.</p>{/if}
    </aside>
  </div>
{/if}

<style>
  .pad { padding: 0 var(--gutter); }
  .muted { color: var(--muted); margin: 0; }
  .small { font-size: 13px; }
  .error { color: var(--rec-text); margin: 0; }
  .ok { color: var(--green-text); margin: 0; }
  .hero { position: relative; overflow: hidden; background: #172033; }
  .backdrop { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; opacity: 0.3; }
  .hero-word { position: absolute; top: 20px; right: var(--gutter); max-width: 70%; text-align: right; font-family: var(--font-display); font-weight: 700; font-size: clamp(60px, 10vw, 150px); line-height: 0.95; color: #1f2b44; text-transform: uppercase; overflow: hidden; display: -webkit-box; -webkit-line-clamp: 2; line-clamp: 2; -webkit-box-orient: vertical; }
  .hero-in { position: relative; display: flex; flex-wrap: wrap; gap: 40px; align-items: flex-end; padding: 56px var(--gutter) 36px; max-width: 1240px; }
  .poster { flex: 0 0 220px; aspect-ratio: 2 / 3; border-radius: 14px; background: #2b3d63; overflow: hidden; display: flex; align-items: flex-end; padding: 16px; box-shadow: 0 12px 32px rgba(0, 0, 0, 0.35); position: relative; }
  .poster img { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; }
  .ptitle { position: relative; min-width: 0; font-family: var(--font-display); font-size: 22px; line-height: 1.15; font-weight: 700; overflow-wrap: anywhere; hyphens: auto; }
  .info { flex: 1 1 420px; min-width: 0; display: flex; flex-direction: column; gap: 12px; }
  .back { align-self: flex-start; border: 0; background: none; padding: 0; color: var(--link); font-size: 14px; cursor: pointer; }
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
  .wrow a { font-size: 14px; font-weight: 600; text-decoration: none; white-space: nowrap; }
  @media (max-width: 720px) { .poster { flex-basis: 140px; padding: 12px; } .ptitle { font-size: 16px; } .hero-in { gap: 20px; padding-top: 24px; } }
</style>
