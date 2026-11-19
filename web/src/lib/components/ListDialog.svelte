<script lang="ts">
  // Gestione delle liste in un popup, la stessa per Libreria e Cerca (19/11/2026). Senza `query` crea una lista a mano
  // (Libreria «+ Nuova lista»); con `query` salva la ricerca: come lista dinamica nuova, oppure aggiornando una lista
  // dinamica che c'è già (la sua ricerca viene sostituita). In entrambi i casi si sceglie se mostrarla in La mia serata.
  import Button from './Button.svelte';
  import { api, ApiError, type ListSummary, type SavedQuery } from '../api/client';
  import { session } from '../session.svelte';

  let { query = null, family: startFamily = false, currentListId = null, suggestedName = '', onclose, ondone }: {
    query?: SavedQuery | null;
    family?: boolean;
    currentListId?: number | null;
    suggestedName?: string;
    onclose: () => void;
    ondone: (list: ListSummary, created: boolean) => void;
  } = $props();

  const child = $derived(!!session.profile?.isChild);
  // svelte-ignore state_referenced_locally
  let name = $state(suggestedName.slice(0, 60));
  // svelte-ignore state_referenced_locally
  let family = $state(startFamily);
  let onHome = $state(true);
  let lists = $state<ListSummary[]>([]);
  // svelte-ignore state_referenced_locally
  let mode = $state<'new' | 'update'>(query && currentListId ? 'update' : 'new');
  // svelte-ignore state_referenced_locally
  let target = $state<number | null>(currentListId);
  let busy = $state(false);
  let error = $state('');

  // le liste dinamiche che il profilo può cambiare: le sue e, se adulto, quelle di famiglia
  const dynamic = $derived(lists.filter((l) => l.query && l.canEdit));
  const chosen = $derived(dynamic.find((l) => l.id === target) ?? null);

  $effect(() => {
    const p = session.profile;
    if (!query || !p) return;
    api.lists(p.id).then((l) => {
      lists = l;
      if (target == null) target = dynamic[0]?.id ?? null;
      const t = l.find((x) => x.id === target);
      if (mode === 'update' && t) onHome = !!t.onHome;
    }).catch(() => {});
  });

  function dialog(node: HTMLDialogElement) {
    node.showModal();
    node.addEventListener('cancel', (e) => { e.preventDefault(); onclose(); });
  }

  async function submit(e: SubmitEvent) {
    e.preventDefault();
    const p = session.profile;
    if (!p || busy) return;
    busy = true;
    error = '';
    try {
      if (mode === 'update' && query) {
        if (!chosen) { error = 'Scegli la lista da aggiornare.'; return; }
        ondone(await api.updateList(p.id, chosen.id, { query, onHome }), false);
      } else {
        if (!name.trim()) { error = 'Dai un nome alla lista.'; return; }
        const l = await api.createList(p.id, name.trim(), family && !child, query);
        ondone(onHome ? await api.updateList(p.id, l.id, { onHome: true }) : l, true);
      }
    } catch (err) {
      error = err instanceof ApiError ? err.message : 'Lista non salvata';
    } finally {
      busy = false;
    }
  }
</script>

<dialog use:dialog aria-labelledby="ld-title">
  <form onsubmit={submit}>
    <h2 id="ld-title">{query ? 'Salva la ricerca come lista' : family ? 'Nuova lista di famiglia' : 'Nuova lista'}</h2>
    {#if query}
      <p class="hint">Una lista dinamica: i titoli li trova la ricerca, da sola, e cambiano con la guida e le piattaforme.</p>
      <div class="modes" role="radiogroup" aria-label="Dove salvare">
        <label class="radio"><input type="radio" bind:group={mode} value="new" /> Nuova lista</label>
        <label class="radio"><input type="radio" bind:group={mode} value="update" disabled={dynamic.length === 0} /> Aggiorna una lista che hai già</label>
      </div>
    {/if}

    {#if mode === 'update' && query}
      {#if dynamic.length === 0}
        <p class="hint">Non hai ancora liste dinamiche da aggiornare.</p>
      {:else}
        <label class="field">Lista da aggiornare
          <select bind:value={target} onchange={() => { if (chosen) onHome = !!chosen.onHome; }}>
            {#each dynamic as l (l.id)}<option value={l.id}>{l.name}{l.family ? ' (famiglia)' : ''}</option>{/each}
          </select>
        </label>
        <p class="hint">La sua ricerca viene sostituita da quella che stai guardando.</p>
      {/if}
    {:else}
      <label class="field">Nome della lista<input bind:value={name} maxlength="60" placeholder={family ? 'Natale in famiglia' : 'Da rivedere'} /></label>
      {#if !child}<label class="check"><input type="checkbox" bind:checked={family} /> Della famiglia: la vedono tutti i profili e la cambiano gli adulti</label>{/if}
    {/if}
    <label class="check"><input type="checkbox" bind:checked={onHome} /> Mostrala in La mia serata</label>

    {#if error}<p class="error" role="alert">{error}</p>{/if}
    <div class="actions">
      <Button variant="ghost" onclick={onclose}>Annulla</Button>
      <Button variant="primary" type="submit" disabled={busy || (mode === 'update' ? !chosen : !name.trim())}>{mode === 'update' ? 'Aggiorna la lista' : 'Crea la lista'}</Button>
    </div>
  </form>
</dialog>

<style>
  dialog {
    width: min(520px, calc(100vw - 32px)); padding: 0; border: 1px solid var(--line-strong);
    border-radius: var(--radius-l); background: var(--surface); color: var(--text);
  }
  dialog::backdrop { background: rgba(5, 7, 10, 0.7); }
  form { display: flex; flex-direction: column; gap: 14px; padding: 24px; }
  h2 { font-size: 22px; }
  .modes { display: flex; flex-wrap: wrap; gap: 6px 20px; }
  .radio, .check { display: flex; align-items: center; gap: 8px; font-size: 15px; min-height: 36px; cursor: pointer; }
  .radio input, .check input { width: 18px; height: 18px; accent-color: var(--blue); }
  .field { display: flex; flex-direction: column; gap: 6px; font-size: 13px; color: var(--muted); }
  .field input, .field select {
    min-height: 44px; padding: 0 12px; border-radius: var(--radius-s); border: 1px solid var(--line-strong);
    background: var(--card); color: var(--text); font-size: 15px;
  }
  .hint { margin: 0; font-size: 13px; color: var(--muted); }
  .error { margin: 0; font-size: 14px; color: var(--rec-text); }
  .actions { display: flex; justify-content: flex-end; gap: 10px; }
</style>
