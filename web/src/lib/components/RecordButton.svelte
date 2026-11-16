<script lang="ts">
  // Il pulsante Registra: un tocco programma con le impostazioni predefinite, un secondo tocco annulla.
  // Il rosso è riservato alla registrazione.
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
  {#if busy}Un momento…{:else if scheduled}In programma ✓{:else}{label}{/if}
</button>

<style>
  .rec {
    display: inline-flex; align-items: center; gap: 10px; border: 0; border-radius: var(--radius);
    background: var(--text); color: var(--bg); font-weight: 700; cursor: pointer;
  }
  .m { min-height: 44px; padding: 0 16px; font-size: 15px; }
  .l { min-height: 48px; padding: 0 22px; font-size: 16px; }
  .on { background: var(--rec-soft); color: var(--text); box-shadow: inset 0 0 0 1px var(--rec); }
  .rec:disabled { cursor: progress; opacity: 0.8; }
  .dot { width: 12px; height: 12px; border-radius: 50%; background: var(--rec-dot); }
</style>
