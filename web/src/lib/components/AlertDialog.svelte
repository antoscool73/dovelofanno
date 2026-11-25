<script lang="ts">
  // «Avvisami» in un popup (Cerca, I miei avvisi, Scheda) (24/11/2026, Antonio: il pannello stretto e lungo nella colonna di destra era
  // «inguardabile e inutilizzabile»). Dentro c'è AlertPanel; Esc, «Chiudi» o il clic fuori chiudono.
  import AlertPanel from './AlertPanel.svelte';

  // onchange: quanti avvisi sono accesi (la Scheda lo mostra sul pulsante)
  let { workId, title, onchange, onclose }: { workId: number; title: string; onchange?: (active: number) => void; onclose: () => void } = $props();

  function dialog(node: HTMLDialogElement) {
    node.showModal();
    node.addEventListener('cancel', (e) => { e.preventDefault(); onclose(); });
    // clic sullo sfondo: il bersaglio è il dialog stesso, non il pannello
    node.addEventListener('click', (e) => { if (e.target === node) onclose(); });
  }
</script>

<dialog use:dialog aria-label="Avvisami per {title}">
  <AlertPanel {workId} {title} {onchange} {onclose} />
</dialog>

<style>
  dialog {
    width: min(600px, calc(100vw - 32px)); max-height: calc(100dvh - 48px); padding: 0; border: 0;
    border-radius: var(--radius-l); background: none; color: var(--text); overflow: auto;
  }
  dialog::backdrop { background: rgba(5, 7, 10, 0.7); }
</style>
