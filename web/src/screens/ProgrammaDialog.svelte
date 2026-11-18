<script lang="ts">
  // Dettaglio di un programma della guida: un tocco su Registra programma con margini e formato predefiniti.
  import Badge from '../lib/components/Badge.svelte';
  import Button from '../lib/components/Button.svelte';
  import RecordButton from '../lib/components/RecordButton.svelte';
  import LivePreview from '../lib/components/LivePreview.svelte';
  import { api, ApiError, type GuideProgramme } from '../lib/api/client';
  import { day, minutes, between, range, vote } from '../lib/format';
  import { router } from '../lib/router.svelte';
  import { session } from '../lib/session.svelte';

  let { p, onclose, onchange }: { p: GuideProgramme; onclose: () => void; onchange: () => void } = $props();

  // stato iniziale letto all'apertura: la finestra si ricrea per ogni programma, poi lo aggiorna Registra
  // svelte-ignore state_referenced_locally
  let scheduled = $state(p.scheduled);
  let busy = $state(false);
  let error = $state('');
  let warnings = $state<string[]>([]);
  const ended = $derived(new Date(p.stop).getTime() <= Date.now());
  const onAir = $derived(new Date(p.start).getTime() <= Date.now() && !ended);
  let ignored = $state(false);
  async function noMore() {
    const prof = session.profile;
    if (!prof || p.workId == null) return;
    try { await api.ignore(prof.id, p.workId); ignored = true; onchange(); }
    catch (e) { error = e instanceof ApiError ? e.message : 'Non salvato'; }
  }

  // "Aggiungi alla mia serata": si sa se c'è già leggendo la lista del profilo
  let inTonight = $state<boolean | null>(null);
  $effect(() => {
    const prof = session.profile;
    if (!prof || p.workId == null) return;
    api.tonight(prof.id).then((items) => (inTonight = items.some((i) => i.workId === p.workId))).catch(() => (inTonight = null));
  });
  async function toggleTonight() {
    const prof = session.profile;
    if (!prof || p.workId == null || inTonight === null) return;
    try {
      if (inTonight) await api.removeTonight(prof.id, p.workId);
      else await api.addTonight(prof.id, p.workId);
      inTonight = !inTonight;
      onchange();
    } catch (e) {
      error = e instanceof ApiError ? e.message : 'Lista non aggiornata';
    }
  }

  // l'indirizzo si calcola prima di chiudere: chiudendo, il programma passato alla finestra diventa null
  function goTo(path: string) {
    onclose();
    router.go(path);
  }

  function dialog(node: HTMLDialogElement) {
    node.showModal();
    node.addEventListener('cancel', (e) => { e.preventDefault(); onclose(); });
  }

  async function record() {
    if (scheduled) { goTo('/registrazioni'); return; }
    if (p.channelId == null) return;
    busy = true;
    error = '';
    try {
      const title = p.subtitle && p.subtitle.length <= 60 && p.group !== 'Film' ? `${p.title} - ${p.subtitle}` : p.title;
      const r = await api.scheduleRecording({ channelId: p.channelId, title, start: p.start, stop: p.stop, beforeMin: null, afterMin: null, format: null, programmeId: p.id });
      scheduled = true;
      warnings = r.warnings;
      onchange();
    } catch (e) {
      error = e instanceof ApiError ? e.message : 'Registrazione non programmata';
    } finally {
      busy = false;
    }
  }

  const facts = $derived(
    [p.group + (p.sub ? ` · ${p.sub}` : ''), p.country, p.year ? String(p.year) : '', p.episode, minutes(between(p.start, p.stop))].filter(Boolean),
  );
</script>

<dialog use:dialog aria-labelledby="pd-title">
  <div class="body">
    {#if onAir && p.channelId != null}
      <LivePreview channelId={p.channelId} label={p.channelName} />
    {:else if p.icon}<img src={p.icon} alt="" class="icon" loading="lazy" referrerpolicy="no-referrer" />{/if}
    <div class="when">{day(p.start)} · {range(p.start, p.stop)} · {p.channelName || p.guideChannelName}</div>
    <h2 id="pd-title">{p.title}</h2>
    {#if p.subtitle}<div class="sub">{p.subtitle}</div>{/if}
    <div class="facts">
      {#each facts as f (f)}<span>{f}</span>{/each}
      {#if p.rating != null}<Badge tone="blue">★ {vote(p.rating)} {p.ratingSource}</Badge>{/if}
      {#if scheduled}<Badge tone="red">In programma</Badge>{/if}
    </div>
    {#if p.description}<p class="desc">{p.description}</p>{/if}
    {#if p.directors || p.actors}
      <p class="credits">{#if p.directors}Regia di {p.directors}{/if}{#if p.directors && p.actors} · {/if}{#if p.actors}Con {p.actors}{/if}</p>
    {/if}

    {#if error}<p class="error" role="alert">{error}</p>{/if}
    {#each warnings as w (w)}<p class="warn">Attenzione: {w}.</p>{/each}

    <div class="actions">
      {#if p.channelId != null && !ended}
        <RecordButton {scheduled} {busy} label={new Date(p.start).getTime() < Date.now() ? 'Registra da adesso' : 'Registra'} onclick={record} />
        {#if scheduled}<Button variant="ghost" onclick={() => goTo('/registrazioni')}>Vedi in Registrazioni</Button>{/if}
      {:else if p.channelId == null}
        <p class="muted">Questo canale non è in nessuna delle tue playlist: non si può registrare, ma con «Avvisami» nella scheda ti avviso quando passa.</p>
      {:else}
        <p class="muted">Il programma è finito.</p>
      {/if}
      {#if p.workId != null}
        <Button variant="soft" onclick={() => goTo(`/opera/${p.workId}`)}>Apri la scheda</Button>
      {/if}
      {#if p.workId != null}
        <Button variant="soft" onclick={() => goTo(`/opera/${p.workId}#avvisami`)}>Avvisami</Button>
      {/if}
      {#if inTonight !== null}
        <Button variant="soft" pressed={inTonight} onclick={toggleTonight}>{inTonight ? 'Nella mia serata ✓' : 'Aggiungi alla mia serata'}</Button>
      {/if}
      {#if p.workId != null}
        <Button variant="ghost" disabled={ignored} onclick={noMore}>{ignored ? 'Non lo propongo più' : 'Non proporre più'}</Button>
      {/if}
      <span class="grow"></span>
      <Button variant="secondary" onclick={onclose}>Chiudi</Button>
    </div>
    {#if p.channelId != null && !scheduled && !ended}
      <p class="muted small">Con le tue impostazioni: margini prima e dopo, formato e cartella predefiniti.</p>
    {/if}
  </div>
</dialog>

<style>
  dialog {
    width: min(640px, calc(100vw - 32px)); max-height: calc(100vh - 48px); padding: 0; border: 1px solid var(--line-strong);
    border-radius: var(--radius-l); background: var(--surface); color: var(--text); overflow: auto;
  }
  dialog::backdrop { background: rgba(5, 7, 10, 0.7); }
  .body { display: flex; flex-direction: column; gap: 12px; padding: 24px; }
  .icon { width: 100%; max-height: 220px; object-fit: cover; border-radius: var(--radius); background: var(--card); }
  .when { font-size: 14px; color: var(--muted); }
  h2 { font-size: 30px; line-height: 1.1; }
  .sub { font-size: 17px; color: var(--text-2); }
  .facts { display: flex; flex-wrap: wrap; align-items: center; gap: 6px 14px; font-size: 14px; color: var(--text-2); }
  .desc { margin: 0; font-size: 16px; line-height: 1.55; color: var(--text-2); }
  .credits { margin: 0; font-size: 14px; color: var(--muted); }
  .actions { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; margin-top: 6px; }
  .grow { flex: 1 1 0; }
  .muted { margin: 0; color: var(--muted); font-size: 14px; }
  .small { font-size: 13px; }
  .error { margin: 0; color: var(--rec-text); }
  .warn { margin: 0; color: var(--orange-text); font-size: 14px; }
</style>
