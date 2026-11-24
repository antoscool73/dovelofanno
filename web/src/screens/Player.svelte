<script lang="ts">
  // Player della libreria: video nel browser con ripresa dal punto, salti, sottotitoli e "Segna come visto".
  // La posizione si salva ogni 10 secondi, in pausa e all'uscita; oltre il 90% il titolo diventa visto.
  import Avatar from '../lib/components/Avatar.svelte';
  import Button from '../lib/components/Button.svelte';
  import { api, ApiError, type LibraryItem } from '../lib/api/client';
  import { clock } from '../lib/format';
  import { router, navigate } from '../lib/router.svelte';
  import { session } from '../lib/session.svelte';

  let { id }: { id: number } = $props();

  let item = $state<LibraryItem | null>(null);
  let error = $state('');
  let video = $state<HTMLVideoElement | null>(null);
  let stage = $state<HTMLDivElement | null>(null);
  let playing = $state(false);
  let current = $state(0);
  let duration = $state(0);
  let muted = $state(false);
  let resumedFrom = $state(0);
  let subtitle = $state('');
  let seen = $state(false);
  let preparing = $state(false);
  let controls = $state(true);
  let notice = $state('');
  let lastSaved = 0;
  let hideTimer: ReturnType<typeof setTimeout> | undefined;

  async function load() {
    try {
      item = await api.libraryItem(id, session.profile?.id);
      seen = item.watched;
      duration = item.durationS;
      error = '';
      preparing = item.copyState === 'queued' || item.copyState === 'working';
    } catch (e) {
      error = e instanceof ApiError ? (e.status === 404 ? 'File non trovato: forse è stato spostato o cancellato.' : e.message) : 'File non letto';
    }
  }
  $effect(() => { void id; item = null; resumedFrom = 0; subtitle = ''; load(); });

  // copia per il browser in preparazione: si ricontrolla ogni 5 secondi
  $effect(() => {
    if (!preparing) return;
    const t = setInterval(async () => {
      await load();
      if (item?.playable) preparing = false;
    }, 5000);
    return () => clearInterval(t);
  });

  async function prepare() {
    if (!item) return;
    try { await api.prepareItem(item.id, session.profile?.id); preparing = true; notice = ''; }
    catch (e) { error = e instanceof ApiError ? e.message : 'Copia non avviata'; }
  }

  function onLoaded() {
    if (!video || !item) return;
    duration = video.duration || item.durationS;
    // si riprende dal punto, ma non negli ultimi 30 secondi
    if (item.positionS > 10 && item.positionS < duration - 30) {
      video.currentTime = item.positionS;
      resumedFrom = item.positionS;
    }
    video.play().catch(() => { /* il browser chiede un clic per partire: resta in pausa */ });
  }

  async function save(force = false, keepalive = false) {
    const prof = session.profile;
    if (!prof || !item || !video) return;
    const now = Date.now();
    if (!force && now - lastSaved < 10_000) return;
    lastSaved = now;
    const r = await api.saveProgress(prof.id, item.id, video.currentTime, duration, keepalive);
    if (r.watched && !seen) { seen = true; notice = 'Segnato come visto.'; }
  }

  function onTime() {
    if (!video) return;
    current = video.currentTime;
    save();
  }

  function toggle() {
    if (!video) return;
    if (video.paused) video.play().catch(() => {});
    else video.pause();
  }

  function skip(s: number) {
    if (!video) return;
    video.currentTime = Math.min(Math.max(0, video.currentTime + s), duration || video.duration);
    poke();
  }

  function seek(e: Event) {
    if (!video) return;
    video.currentTime = Number((e.currentTarget as HTMLInputElement).value);
  }

  function restart() {
    if (!video) return;
    video.currentTime = 0;
    resumedFrom = 0;
    video.play().catch(() => {});
  }

  function toggleMute() {
    if (!video) return;
    video.muted = !video.muted;
    muted = video.muted;
  }

  async function fullscreen() {
    if (!stage) return;
    if (document.fullscreenElement) await document.exitFullscreen().catch(() => {});
    else await stage.requestFullscreen().catch(() => {});
  }

  async function markSeen() {
    const prof = session.profile;
    if (!prof || !item) return;
    try { await api.itemWatched(prof.id, item.id); seen = true; notice = 'Segnato come visto.'; }
    catch (e) { error = e instanceof ApiError ? e.message : 'Non segnato'; }
  }

  // sottotitoli: una sola traccia attiva per volta
  $effect(() => {
    if (!video) return;
    const tracks = video.textTracks;
    for (let i = 0; i < tracks.length; i++) {
      const t = tracks[i]!;
      t.mode = t.id === subtitle ? 'showing' : 'disabled';
    }
  });

  function poke() {
    controls = true;
    clearTimeout(hideTimer);
    hideTimer = setTimeout(() => { if (playing) controls = false; }, 3000);
  }

  function onKey(e: KeyboardEvent) {
    const t = e.target as HTMLElement;
    if (t.tagName === 'INPUT' || t.tagName === 'SELECT' || t.tagName === 'BUTTON') return;
    if (e.key === ' ' || e.key === 'k') { e.preventDefault(); toggle(); }
    else if (e.key === 'ArrowLeft') { e.preventDefault(); skip(-10); }
    else if (e.key === 'ArrowRight') { e.preventDefault(); skip(30); }
    else if (e.key === 'f') fullscreen();
    else if (e.key === 'm') toggleMute();
  }

  function leave() {
    save(true, true);
    if (history.length > 1) history.back();
    else router.go('/libreria');
  }

  $effect(() => {
    const before = () => { save(true, true); };
    window.addEventListener('pagehide', before);
    return () => { window.removeEventListener('pagehide', before); before(); };
  });

  const ended = $derived(duration > 0 && current >= duration - 1 && !playing);
</script>

<svelte:window onkeydown={onKey} />

<div class="stage" bind:this={stage} onmousemove={poke} role="presentation" class:hide={!controls && playing}>
  {#if error && !item}
    <div class="center"><p class="error" role="alert">{error}</p><Button onclick={leave}>Indietro</Button></div>
  {:else if !item}
    <p class="center muted" role="status">Un momento…</p>
  {:else}
    {#if item.playable}
      <!-- svelte-ignore a11y_media_has_caption -->
      <video
        bind:this={video}
        src={api.streamUrl(item.id)}
        preload="metadata"
        playsinline
        onloadedmetadata={onLoaded}
        ontimeupdate={onTime}
        onplay={() => { playing = true; poke(); }}
        onpause={() => { playing = false; controls = true; save(true); }}
        onended={() => { playing = false; controls = true; save(true); }}
        onclick={toggle}
        onerror={() => { error = 'Il browser non riesce a leggere questo video.'; }}
      >
        {#each item.subtitles as s (s.id)}
          <track id={s.id} kind="subtitles" label={s.label} srclang={s.language || undefined} src={api.subtitleUrl(item.id, s.id)} />
        {/each}
      </video>
    {:else}
      <div class="center prep">
        <h2>{item.title}</h2>
        {#if preparing}
          <p role="status">Preparo il file per il browser… Con video e audio già adatti bastano pochi minuti; con una ricodifica completa può volerci quanto la durata del film. Puoi chiudere: la copia va avanti sul server e quando è pronta arriva una notifica.</p>
        {:else if item.copyState === 'failed'}
          <p class="error">Il file non si legge o la copia non è riuscita. Prova ad aprirlo con un altro programma.</p>
          <Button variant="primary" onclick={prepare}>Riprova</Button>
        {:else}
          <p>Questo file ({item.videoCodec.toUpperCase() || 'video'}{item.audioCodec ? ' + ' + item.audioCodec.toUpperCase() : ''}) va preparato per il browser: il server ne fa una copia che si guarda ovunque, senza toccare l'originale.</p>
          <Button variant="primary" size="l" onclick={prepare}>Prepara per il browser</Button>
        {/if}
      </div>
    {/if}

    <div class="top">
      <button type="button" class="round" aria-label="Indietro" onclick={leave}>
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M15 18l-6-6 6-6" /></svg>
      </button>
      <div class="ttl">
        <h1>{item.title}</h1>
        {#if item.subtitle}<div class="sub">{item.subtitle}</div>{/if}
      </div>
      <div class="spacer"></div>
      {#if session.profile}
        <a href="/profili" onclick={navigate} class="prof" aria-label="Profilo attivo: {session.profile.name}. Cambia profilo">
          <Avatar name={session.profile.name} color={session.profile.avatar} icon={session.profile.icon} /><span>{session.profile.name}</span>
        </a>
      {/if}
    </div>

    {#if resumedFrom > 0 && current < resumedFrom + 20}
      <div class="resume" role="status">
        <span>Ripreso da {clock(resumedFrom)}</span>
        <button type="button" class="ghost" onclick={restart}>Ricomincia dall'inizio</button>
      </div>
    {/if}
    {#if ended && item.nextItemId}
      <div class="resume">
        <span>Episodio finito.</span>
        <a class="ghost" href="/guarda/{item.nextItemId}" onclick={navigate}>Episodio successivo ›</a>
      </div>
    {/if}

    {#if item.playable}
      <div class="bar">
        <div class="seek">
          <span>{clock(current)}</span>
          <input type="range" min="0" max={Math.max(1, Math.floor(duration))} step="1" value={Math.floor(current)} oninput={seek} aria-label="Posizione nel video" />
          <span>{clock(duration)}</span>
        </div>
        <div class="btns">
          <button type="button" class="play" aria-label={playing ? 'Pausa' : 'Riproduci'} onclick={toggle}>
            {#if playing}
              <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><rect x="6" y="5" width="4" height="14" rx="1" /><rect x="14" y="5" width="4" height="14" rx="1" /></svg>
            {:else}
              <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M8 5v14l11-7z" /></svg>
            {/if}
          </button>
          <button type="button" class="txt" aria-label="Indietro di 10 secondi" onclick={() => skip(-10)}>−10 s</button>
          <button type="button" class="txt" aria-label="Avanti di 30 secondi" onclick={() => skip(30)}>+30 s</button>
          <button type="button" class="icon" aria-label={muted ? 'Riattiva l\'audio' : 'Togli l\'audio'} aria-pressed={muted} onclick={toggleMute}>
            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 9v6h4l5 4V5L8 9z" />{#if muted}<path d="M17 9l5 5M22 9l-5 5" />{:else}<path d="M16.5 8.5a5 5 0 010 7" />{/if}</svg>
          </button>
          <div class="spacer"></div>
          {#if item.audioTracks.length > 1}
            <span class="info" title="Nel browser si sente la prima traccia; la copia per il browser mette l'italiano per prima">Audio: {item.audioTracks[0]?.title}</span>
          {/if}
          {#if item.subtitles.length > 0}
            <label class="sel">
              <span class="sr-only">Sottotitoli</span>
              <select bind:value={subtitle}>
                <option value="">Sottotitoli: no</option>
                {#each item.subtitles as s (s.id)}<option value={s.id}>{s.label}</option>{/each}
              </select>
            </label>
          {/if}
          <button type="button" class="out" aria-pressed={seen} onclick={markSeen} disabled={seen}>
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M5 12l5 5L20 7" /></svg>{seen ? 'Visto' : 'Segna come visto'}
          </button>
          {#if item.nextItemId}<a class="out" href="/guarda/{item.nextItemId}" onclick={navigate}>Episodio successivo</a>{/if}
          <button type="button" class="icon" aria-label="Schermo intero" onclick={fullscreen}>
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5" /></svg>
          </button>
        </div>
        {#if notice}<p class="ok" role="status">{notice}</p>{/if}
        {#if error}<p class="error" role="alert">{error}</p>{/if}
      </div>
    {/if}
  {/if}
</div>

<style>
  .stage { position: fixed; inset: 0; background: #000; color: var(--text); display: flex; flex-direction: column; z-index: 10; }
  .stage.hide { cursor: none; }
  .stage.hide .top, .stage.hide .bar { opacity: 0; pointer-events: none; }
  video { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: contain; background: #000; }
  .center { position: absolute; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 16px; padding: 24px; text-align: center; }
  .prep { max-width: 640px; margin: 0 auto; }
  .prep h2 { font-size: 26px; }
  .prep p { color: var(--text-2); line-height: 1.6; margin: 0; }
  .muted { color: var(--muted); }
  .error { color: var(--rec-text); margin: 0; }
  .ok { color: var(--green-text); margin: 0; font-size: 14px; }
  .top { position: relative; display: flex; align-items: center; gap: 16px; padding: 20px 32px; background: linear-gradient(rgba(0, 0, 0, 0.7), rgba(0, 0, 0, 0)); transition: opacity 0.3s; }
  .round { width: 44px; height: 44px; border-radius: 50%; border: 0; display: flex; align-items: center; justify-content: center; background: rgba(255, 255, 255, 0.12); color: var(--text); cursor: pointer; flex: 0 0 auto; }
  .ttl { min-width: 0; display: flex; flex-direction: column; gap: 2px; }
  h1 { font-size: 20px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .sub { font-size: 13px; color: var(--text-2); }
  .spacer { flex: 1 1 0; }
  .prof { display: flex; align-items: center; gap: 10px; min-height: 44px; padding: 4px 12px 4px 4px; border-radius: 12px; background: rgba(255, 255, 255, 0.12); color: var(--text); text-decoration: none; font-size: 14px; font-weight: 600; }
  .resume { position: absolute; left: 50%; bottom: 150px; transform: translateX(-50%); display: flex; flex-wrap: wrap; align-items: center; gap: 14px; background: rgba(13, 16, 21, 0.88); border: 1px solid var(--line-strong); border-radius: 12px; padding: 10px 12px 10px 18px; font-size: 14px; }
  .ghost { min-height: 36px; padding: 0 14px; border-radius: 8px; border: 1px solid var(--line-btn); background: none; color: var(--text); font-size: 14px; cursor: pointer; display: inline-flex; align-items: center; text-decoration: none; }
  .bar { position: absolute; left: 0; right: 0; bottom: 0; padding: 18px 32px 26px; background: linear-gradient(rgba(0, 0, 0, 0), rgba(0, 0, 0, 0.8)); display: flex; flex-direction: column; gap: 12px; transition: opacity 0.3s; }
  .seek { display: flex; align-items: center; gap: 14px; font-size: 13px; color: var(--text-2); font-variant-numeric: tabular-nums; }
  .seek input { flex: 1 1 0; accent-color: var(--blue); height: 24px; margin: 0; cursor: pointer; }
  .btns { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
  .play { width: 48px; height: 48px; border-radius: 50%; border: 0; background: var(--text); color: var(--bg); display: flex; align-items: center; justify-content: center; cursor: pointer; }
  .txt { min-width: 48px; height: 48px; padding: 0 10px; border-radius: 10px; border: 0; background: none; color: var(--text); font-size: 14px; font-weight: 600; cursor: pointer; }
  .icon { width: 48px; height: 48px; border-radius: 10px; border: 0; background: none; color: var(--text); display: flex; align-items: center; justify-content: center; cursor: pointer; }
  .out { display: inline-flex; align-items: center; gap: 8px; min-height: 44px; padding: 0 14px; border-radius: 10px; border: 1px solid var(--line-btn); background: none; color: var(--text); font-size: 14px; cursor: pointer; text-decoration: none; }
  .out:disabled { color: var(--green-text); border-color: transparent; cursor: default; }
  .info { font-size: 13px; color: var(--text-2); padding: 0 6px; }
  .sel select { min-height: 44px; padding: 0 12px; border-radius: 10px; border: 1px solid var(--line-btn); background: rgba(13, 16, 21, 0.8); color: var(--text); font-size: 14px; }
  @media (max-width: 720px) {
    .top { padding: 12px 16px; }
    .prof span { display: none; }
    .bar { padding: 12px 16px 18px; }
    .resume { bottom: 170px; width: calc(100% - 32px); }
  }
</style>
