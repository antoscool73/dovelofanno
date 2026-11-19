<script lang="ts">
  // Il popup delle conferme (lib/confirm.svelte.ts): titolo, una riga di spiegazione, «Annulla» e il verbo dell'azione.
  // Esc o un clic fuori valgono «Annulla»; il fuoco parte da «Annulla», così Invio per sbaglio non cancella niente.
  import Button from './Button.svelte';
  import { confirmState } from '../confirm.svelte';

  const c = $derived(confirmState.current);

  function dialog(node: HTMLDialogElement) {
    node.showModal();
    node.addEventListener('cancel', (e) => { e.preventDefault(); confirmState.answer(false); });
    node.addEventListener('click', (e) => { if (e.target === node) confirmState.answer(false); });
    queueMicrotask(() => node.querySelector<HTMLButtonElement>('button[data-cancel]')?.focus());
  }
</script>

{#if c}
  <dialog use:dialog aria-labelledby="cf-title" aria-describedby={c.text ? 'cf-text' : undefined}>
    <div class="box">
      <h2 id="cf-title">{c.title}</h2>
      {#if c.text}<p id="cf-text">{c.text}</p>{/if}
      <div class="actions">
        <button type="button" class="cancel" data-cancel onclick={() => confirmState.answer(false)}>{c.cancel ?? 'Annulla'}</button>
        {#if c.danger === false}
          <Button variant="primary" onclick={() => confirmState.answer(true)}>{c.ok}</Button>
        {:else}
          <button type="button" class="danger" onclick={() => confirmState.answer(true)}>{c.ok}</button>
        {/if}
      </div>
    </div>
  </dialog>
{/if}

<style>
  dialog {
    width: min(460px, calc(100vw - 32px)); padding: 0; border: 1px solid var(--line-strong);
    border-radius: var(--radius-l); background: var(--surface); color: var(--text);
  }
  dialog::backdrop { background: rgba(5, 7, 10, 0.7); }
  .box { display: flex; flex-direction: column; gap: 12px; padding: 22px 24px; }
  h2 { font-size: 20px; margin: 0; }
  p { margin: 0; font-size: 15px; line-height: 1.5; color: var(--text-2); }
  .actions { display: flex; justify-content: flex-end; flex-wrap: wrap; gap: 10px; margin-top: 6px; }
  .cancel, .danger {
    min-height: 44px; padding: 0 18px; border-radius: var(--radius); font: inherit; font-size: 15px; font-weight: 600; cursor: pointer;
  }
  .cancel { border: 1px solid var(--line-btn); background: none; color: var(--text); }
  .cancel:hover { background: var(--raised); }
  .danger { border: 0; background: #c62f38; color: #fff; }   /* rosso più scuro del tema: il bianco si legge (5:1) */
  .danger:hover { filter: brightness(1.08); }
</style>
