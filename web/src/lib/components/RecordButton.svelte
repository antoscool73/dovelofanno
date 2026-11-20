<script lang="ts">
  // Il pulsante Registra: un tocco programma con le impostazioni predefinite, un secondo tocco annulla.
  // Il rosso è riservato alla registrazione. La larghezza non cambia tra i due stati (i pulsanti accanto restano fermi).
  // Stesso contenuto nei due stati (pallino rosso + testo): «Registra» scuro con il bordo chiaro, «Programmato» chiaro
  // (scelta di Antonio, 20/11/2026: prima era il contrario e «In programma» aveva lo sfondo rosso).
  import StableLabel from './StableLabel.svelte';
  let {
    scheduled = false,
    busy = false,
    size = 'l',
    label = 'Registra',
    onclick,
  }: { scheduled?: boolean; busy?: boolean; size?: 'm' | 'l'; label?: string; onclick: () => void } = $props();
</script>

<button type="button" class="rec {size}" class:on={scheduled} aria-pressed={scheduled} disabled={busy} {onclick}>
  <span class="dot" aria-hidden="true"></span>
  <StableLabel text={busy ? 'Un momento…' : scheduled ? 'Programmato' : label} variants={['Un momento…', 'Programmato', label]} />
</button>

<style>
  .rec {
    display: inline-flex; align-items: center; gap: 10px; border: 0; border-radius: var(--radius);
    background: var(--bg); color: var(--text); box-shadow: inset 0 0 0 1.5px var(--text); font-weight: 700; cursor: pointer;
  }
  .m { min-height: 44px; padding: 0 16px; font-size: 15px; }
  .l { min-height: 48px; padding: 0 22px; font-size: 16px; }
  .on { background: var(--text); color: var(--bg); box-shadow: none; }
  .rec:disabled { cursor: progress; opacity: 0.8; }
  .dot { width: 12px; height: 12px; border-radius: 50%; background: var(--rec-dot); }
</style>
