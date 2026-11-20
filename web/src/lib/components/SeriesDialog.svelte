<script lang="ts">
  // «Registra la serie» (lib/series.svelte.ts): prima la domanda «solo questa puntata o tutte?», poi l'esito: le
  // puntate trovate in guida (programmate, già in programma, repliche saltate...), quelle che mancano e la promessa
  // che le prossime si programmano da sole quando la guida le riporta.
  import Button from './Button.svelte';
  import { seriesState } from '../series.svelte';
  import { day, range } from '../format';
  import { router } from '../router.svelte';

  const a = $derived(seriesState.asking);
  const r = $derived(seriesState.result);
  const open = $derived(!!a || !!r || seriesState.busy || !!seriesState.error);

  const statusText: Record<string, string> = {
    scheduled: 'Programmata ora', pending: 'Già in programma', replica: 'Replica: saltata', done: 'Già registrata',
    not_recordable: 'Canale non nelle tue playlist', limit: 'Oltre il limite di registrazioni contemporanee',
  };

  function close() {
    if (a) seriesState.answer(null);
    else seriesState.close();
  }
  function dialog(node: HTMLDialogElement) {
    node.showModal();
    node.addEventListener('cancel', (e) => { e.preventDefault(); close(); });
    node.addEventListener('click', (e) => { if (e.target === node) close(); });
  }
  const channelOf = (p: { channelName: string; guideChannelName: string }) => p.channelName || p.guideChannelName;
  const shown = $derived(r ? r.episodes.filter((e) => e.status !== 'replica') : []);
  const replicas = $derived(r ? r.episodes.filter((e) => e.status === 'replica').length : 0);
</script>

{#if open}
  <dialog use:dialog aria-labelledby="sr-title">
    <div class="box">
      {#if a}
        <h2 id="sr-title">Registrare «{a.p.title}»</h2>
        <p>Solo questa puntata o tutte le puntate che andranno in onda? Con «Tutte» DoveLoFanno programma quelle che sono
          già in guida, una volta per puntata (le repliche si saltano), e le prossime da solo, man mano che la guida le riporta.</p>
        <div class="actions">
          <button type="button" class="ghost" onclick={() => seriesState.answer(null)}>Annulla</button>
          <Button variant="secondary" onclick={() => seriesState.answer('one')}>Solo questa puntata</Button>
          <Button variant="primary" onclick={() => seriesState.answer('all')}>Tutte le puntate</Button>
        </div>
      {:else if seriesState.busy}
        <h2 id="sr-title">Cerco le puntate in guida…</h2>
      {:else if seriesState.error}
        <h2 id="sr-title">Serie non programmata</h2>
        <p class="err" role="alert">{seriesState.error}</p>
        <div class="actions"><Button variant="primary" onclick={close}>Chiudi</Button></div>
      {:else if r}
        <h2 id="sr-title">«{r.rule.title}»: tutte le puntate</h2>
        <p>
          {r.newlyScheduled === 0 ? 'Nessuna puntata nuova da programmare adesso.' : r.newlyScheduled === 1 ? 'Programmata 1 puntata.' : `Programmate ${r.newlyScheduled} puntate.`}
          {replicas > 0 ? ` ${replicas === 1 ? 'Una replica saltata' : `${replicas} repliche saltate`}.` : ''}
        </p>
        {#if shown.length > 0}
          <ul class="eps">
            {#each shown as e (e.programme.id)}
              <li class:muted={e.status !== 'scheduled' && e.status !== 'pending'}>
                <span class="when">{day(e.programme.start)} {range(e.programme.start, e.programme.stop)}</span>
                <span class="what">{channelOf(e.programme)}{e.programme.episode ? ` · ${e.programme.episode}` : ''}{e.programme.subtitle ? ` · ${e.programme.subtitle}` : ''}</span>
                <span class="st" class:on={e.status === 'scheduled' || e.status === 'pending'}>{statusText[e.status] ?? e.status}</span>
              </li>
            {/each}
          </ul>
        {:else}
          <p>Per ora la guida non riporta puntate di questo titolo.</p>
        {/if}
        {#if r.missing.length > 0}
          <p class="warn">Nella guida mancano le puntate {r.missing.slice(0, 12).join(', ')}{r.missing.length > 12 ? ` e altre ${r.missing.length - 12}` : ''}.</p>
        {/if}
        <p class="note">Le registrazioni delle prossime puntate si attiveranno automaticamente sulla base delle programmazioni
          rilevate dalla guida (che arriva a 4-5 giorni). Arriva una notifica ogni volta. La serie si ferma da Impostazioni › Registrazioni.</p>
        <div class="actions">
          <button type="button" class="ghost" onclick={() => { seriesState.close(); router.go('/registrazioni'); }}>Vedi in Registrazioni</button>
          <Button variant="primary" onclick={close}>Chiudi</Button>
        </div>
      {/if}
    </div>
  </dialog>
{/if}

<style>
  dialog {
    width: min(620px, calc(100vw - 32px)); padding: 0; border: 1px solid var(--line-strong);
    border-radius: var(--radius-l); background: var(--surface); color: var(--text);
  }
  dialog::backdrop { background: rgba(5, 7, 10, 0.7); }
  .box { display: flex; flex-direction: column; gap: 12px; padding: 22px 24px; max-height: calc(100vh - 64px); overflow: auto; }
  h2 { font-size: 20px; margin: 0; }
  p { margin: 0; font-size: 15px; line-height: 1.5; color: var(--text-2); }
  .note { font-size: 14px; color: var(--muted); }
  .warn { color: #f5b800; }
  .err { color: var(--rec); }
  .actions { display: flex; justify-content: flex-end; flex-wrap: wrap; gap: 10px; margin-top: 6px; }
  .ghost { min-height: 44px; padding: 0 18px; border-radius: var(--radius); border: 1px solid var(--line-btn); background: none; color: var(--text); font: inherit; font-size: 15px; font-weight: 600; cursor: pointer; }
  .ghost:hover { background: var(--raised); }
  .eps { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 6px; }
  .eps li { display: flex; flex-wrap: wrap; gap: 4px 12px; align-items: baseline; padding: 8px 12px; border-radius: var(--radius-s); background: var(--card); font-size: 14px; }
  .eps li.muted { opacity: 0.65; }
  .when { font-weight: 600; min-width: 170px; }
  .what { flex: 1 1 200px; color: var(--text-2); }
  .st { font-size: 13px; color: var(--muted); }
  .st.on { color: var(--text); }
</style>
