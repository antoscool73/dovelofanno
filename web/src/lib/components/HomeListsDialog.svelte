<script lang="ts">
  // Quali liste compaiono in La mia lista (20/11/2026, richiesta di Antonio): un popup con tutte le liste del profilo
  // (sue e della famiglia) e una casella per ciascuna. Ogni casella si salva subito; chiudendo, la pagina si ricarica.
  // È la stessa scelta di Preferenze › La mia lista.
  import Button from './Button.svelte';
  import { api, ApiError, type ListSummary } from '../api/client';
  import { session } from '../session.svelte';
  import { navigate } from '../router.svelte';

  let { onclose }: { onclose: () => void } = $props();

  let lists = $state<ListSummary[] | null>(null);
  let error = $state('');
  let busy = $state<number | null>(null);

  async function load() {
    const prof = session.profile;
    if (!prof) return;
    try { lists = (await api.lists(prof.id)).filter((l) => !l.isTonight); error = ''; }
    catch (e) { error = e instanceof ApiError ? e.message : 'Liste non lette'; }
  }
  load();

  async function toggle(l: ListSummary, on: boolean) {
    const prof = session.profile;
    if (!prof) return;
    busy = l.id;
    try { const u = await api.updateList(prof.id, l.id, { onHome: on }); l.onHome = u.onHome; error = ''; }
    catch (e) { error = e instanceof ApiError ? e.message : 'Non salvato'; }
    finally { busy = null; }
  }

  function dialog(node: HTMLDialogElement) {
    node.showModal();
    node.addEventListener('cancel', (e) => { e.preventDefault(); onclose(); });
    node.addEventListener('click', (e) => { if (e.target === node) onclose(); });
  }
  const kind = (l: ListSummary) =>
    [l.query ? 'Dinamica: i risultati della sua ricerca' : `${l.count} ${l.count === 1 ? 'titolo' : 'titoli'}`, l.family ? 'della famiglia' : 'personale'].join(' · ');
</script>

<dialog use:dialog aria-labelledby="hl-title">
  <div class="box">
    <h2 id="hl-title">Liste in La mia lista</h2>
    <p class="muted">Scegli quali liste vedere qui, sotto «I miei titoli». Si salva subito.</p>
    {#if error}<p class="err" role="alert">{error}</p>{/if}
    {#if lists === null}
      <p class="muted" role="status">Un momento…</p>
    {:else if lists.length === 0}
      <p class="muted">Nessuna lista ancora. Una lista dinamica si crea dalla ricerca: scegli i filtri e «Salva come lista».</p>
    {:else}
      <ul>
        {#each lists as l (l.id)}
          <li>
            <label>
              <input type="checkbox" checked={l.onHome} disabled={busy === l.id} onchange={(e) => toggle(l, e.currentTarget.checked)} />
              <span class="name">{l.name}<span class="muted small">{kind(l)}</span></span>
            </label>
          </li>
        {/each}
      </ul>
    {/if}
    <div class="actions">
      <a class="ghost" href="/cerca" onclick={(e) => { onclose(); navigate(e); }}>Nuova lista dalla ricerca</a>
      <Button variant="primary" onclick={onclose}>Fatto</Button>
    </div>
  </div>
</dialog>

<style>
  dialog { width: min(480px, calc(100vw - 32px)); padding: 0; border: 1px solid var(--line-strong); border-radius: var(--radius-l); background: var(--surface); color: var(--text); }
  dialog::backdrop { background: rgba(5, 7, 10, 0.7); }
  .box { display: flex; flex-direction: column; gap: 12px; padding: 22px 24px; max-height: calc(100vh - 64px); overflow: auto; }
  h2 { font-size: 20px; margin: 0; }
  p { margin: 0; font-size: 15px; line-height: 1.5; }
  .muted { color: var(--muted); }
  .small { font-size: 13px; }
  .err { color: var(--rec); }
  ul { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 6px; }
  label { display: flex; align-items: center; gap: 12px; min-height: 48px; padding: 8px 12px; border-radius: var(--radius-s); background: var(--card); cursor: pointer; }
  label input { width: 20px; height: 20px; flex: 0 0 auto; }
  .name { display: flex; flex-direction: column; font-weight: 600; min-width: 0; overflow-wrap: anywhere; }
  .name .small { font-weight: 400; }
  .actions { display: flex; justify-content: flex-end; flex-wrap: wrap; align-items: center; gap: 10px; margin-top: 4px; }
  .ghost { min-height: 44px; display: inline-flex; align-items: center; padding: 0 16px; border-radius: var(--radius); border: 1px solid var(--line-btn); color: var(--text); text-decoration: none; font-weight: 600; font-size: 15px; }
  .ghost:hover { background: var(--raised); }
</style>
