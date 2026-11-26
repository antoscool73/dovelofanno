<script lang="ts">
  // I miei avvisi (24/11/2026, Antonio): pagina propria nella barra in alto, dopo Registrazioni (prima era una scheda di
  // Preferenze). Righe a colonne fisse con locandina, matita (apre il popup «Avvisami» del titolo, 25/11/2026) e bidone con conferma.
  import { ask } from '../lib/confirm.svelte';
  import Badge from '../lib/components/Badge.svelte';
  import AlertDialog from '../lib/components/AlertDialog.svelte';
  import { api, ApiError, type AlertRow } from '../lib/api/client';
  import { navigate } from '../lib/router.svelte';
  import { session } from '../lib/session.svelte';
  import { alertedState } from '../lib/alerted.svelte';
  import { day, time } from '../lib/format';

  const prof = $derived(session.profile);
  let alerts = $state<AlertRow[]>([]);
  let loaded = $state(false);
  let error = $state('');
  let message = $state('');
  let busy = $state(false);
  // l'opera di cui si cambiano gli avvisi nel popup (matita)
  let editing = $state<{ workId: number; title: string } | null>(null);

  async function load() {
    const p = session.profile;
    if (!p) return;
    try {
      alerts = await api.alerts(p.id);
      error = '';
      // lo stato comune dei pulsanti «Avvisami» segue questa pagina (avviso tolto con il bidone)
      const on = new Set(alerts.filter((a) => a.active).map((a) => a.workId));
      for (const id of new Set(alerts.map((a) => a.workId).concat([...alertedState.ids]))) alertedState.set(id, on.has(id) ? 1 : 0);
    }
    catch (e) { error = e instanceof ApiError ? e.message : 'Avvisi non letti'; }
    finally { loaded = true; }
  }
  load();

  async function act(fn: () => Promise<unknown>, ok = '') {
    busy = true;
    message = '';
    try { await fn(); await load(); message = ok; error = ''; }
    catch (e) { error = e instanceof ApiError ? e.message : 'Operazione non riuscita'; }
    finally { busy = false; }
  }

  const kindLabel: Record<string, string> = { tv: 'In chiaro in TV', subscription: 'Nei miei abbonamenti', rent_buy: 'Noleggio o acquisto' };
</script>

<div class="page">
  <h1>I miei avvisi</h1>
  <p class="f-muted">Si creano con «Avvisami» dalla scheda di un titolo. Ognuno scatta una volta sola per passaggio o piattaforma.{prof ? ` Sono di ${prof.name}.` : ''}</p>
  {#if error}<p class="f-error" role="alert">{error}</p>{/if}
  {#if message}<p class="f-ok" role="status">{message}</p>{/if}

  {#if !loaded}
    <p class="f-muted" role="status">Un momento…</p>
  {:else if alerts.length === 0}
    <div class="empty"><p>Nessun avviso. Apri un film dalla <a href="/guida" onclick={navigate}>Guida</a>, dalla <a href="/libreria" onclick={navigate}>Libreria</a> o con la lente e tocca «Avvisami».</p></div>
  {:else}
    <!-- righe a colonne fisse con locandina, matita e bidone (Antonio, 24/11/2026: stessa misura per ogni riga, niente pulsanti a capo) -->
    <ul class="arows">
      {#each alerts as a (a.id)}
        <li class="arow">
          <label class="f-check"><input type="checkbox" checked={a.active} disabled={busy} aria-label="Attiva l'avviso su {a.workTitle}"
            onchange={(e) => act(() => api.saveAlert(prof!.id, { workId: a.workId, kind: a.kind, active: e.currentTarget.checked, channels: null, autoRecord: null, moments: null, services: null, offers: null }))} /></label>
          <a class="aposter" href="/opera/{a.workId}" onclick={navigate} aria-label="Apri la scheda di {a.workTitle}" title="Apri la scheda">
            {#if a.poster}<img src={a.poster} alt="" loading="lazy" />{:else}<span class="ph" aria-hidden="true">{a.workTitle.charAt(0)}</span>{/if}
          </a>
          <div class="awhat">
            <div class="aline"><a class="atitle" href="/opera/{a.workId}" onclick={navigate}>{a.workTitle}</a>{#if a.workYear}<span class="f-muted">{a.workYear}</span>{/if}</div>
            <div class="aline"><Badge tone="blue">{kindLabel[a.kind] ?? a.kind}</Badge>{#if a.kind === 'tv' && a.autoRecord}<Badge tone="red">Registra da solo</Badge>{/if}</div>
            <div class="f-muted ameta">{a.status}{a.lastFiredAt ? ` · ultimo avviso ${day(a.lastFiredAt).toLowerCase()} ${time(a.lastFiredAt)}` : ''}</div>
          </div>
          <div class="aacts">
            <button type="button" class="ico" onclick={() => (editing = { workId: a.workId, title: a.workTitle })} aria-label="Modifica l'avviso su «{a.workTitle}»" title="Modifica">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 20h9" /><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4z" /></svg>
            </button>
            <button type="button" class="ico del" disabled={busy} aria-label="Togli l'avviso su «{a.workTitle}»" title="Togli l'avviso"
              onclick={async () => { if (await ask({ title: `Togliere l'avviso su «${a.workTitle}»?`, text: 'Si perdono anche le sue impostazioni (quando avvisare, canali, registrazione automatica).', ok: 'Togli l\'avviso' })) act(() => api.deleteAlert(prof!.id, a.id), 'Avviso tolto.'); }}>
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 6h18M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6M10 11v6M14 11v6" /></svg>
            </button>
          </div>
        </li>
      {/each}
    </ul>
  {/if}
</div>

{#if editing}
  <!-- alla chiusura si rilegge: nel popup si accendono, spengono o cambiano gli avvisi dell'opera -->
  <AlertDialog workId={editing.workId} title={editing.title} onclose={() => { editing = null; load(); }} />
{/if}

<style>
  .page { padding: 28px var(--gutter) 64px; max-width: 1240px; display: flex; flex-direction: column; gap: 14px; }
  h1 { font-size: 30px; }
  .arows { list-style: none; margin: 0; padding: 0; background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius-l); }
  .arow { display: grid; grid-template-columns: auto 48px minmax(0, 1fr) auto; align-items: center; gap: 14px; padding: 12px 18px; }
  .arow + .arow { border-top: 1px solid var(--line); }
  .aposter { width: 48px; height: 72px; border-radius: 6px; overflow: hidden; background: var(--raised); display: flex; align-items: center; justify-content: center; text-decoration: none; }
  .aposter:hover { outline: 2px solid var(--blue); outline-offset: 2px; }
  .aposter img { width: 100%; height: 100%; object-fit: cover; display: block; }
  .ph { font-family: var(--font-display); font-size: 22px; font-weight: 700; color: var(--muted); }
  .awhat { display: flex; flex-direction: column; gap: 6px; min-width: 0; }
  .aline { display: flex; flex-wrap: wrap; align-items: baseline; gap: 8px; }
  .atitle { font-weight: 600; font-size: 16px; color: var(--text); }
  .ameta { font-size: 13px; overflow-wrap: anywhere; }
  .aacts { display: flex; gap: 8px; }
  .ico { display: inline-flex; align-items: center; justify-content: center; width: 40px; height: 40px; border-radius: var(--radius-s); border: 1px solid var(--line-btn); background: none; color: var(--text-2); cursor: pointer; }
  .ico:hover { color: var(--text); background: var(--raised); }
  .ico.del:hover:not(:disabled) { color: var(--orange-text); border-color: var(--orange); }
  .ico:disabled { opacity: 0.5; cursor: progress; }
  .empty { background: var(--surface); border: 1px dashed var(--line-btn); border-radius: var(--radius-l); padding: 16px 20px; display: flex; flex-direction: column; gap: 10px; }
  .empty p { margin: 0; }
</style>
