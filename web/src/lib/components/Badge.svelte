<script lang="ts">
  import type { Snippet } from 'svelte';

  // tone: proposta (blu), ok (verde), avviso (arancio), rec/errore (rosso), neutro
  let { tone = 'neutral', children }: { tone?: 'blue' | 'green' | 'orange' | 'red' | 'neutral'; children: Snippet } = $props();
</script>

<span class="badge {tone}">{@render children()}</span>

<style>
  .badge { display: inline-flex; align-items: center; gap: 6px; font-size: 12px; font-weight: 700; letter-spacing: 0.02em; border-radius: 6px; padding: 3px 8px; text-transform: uppercase; }
  .blue { color: var(--link-hover); background: var(--blue-soft); }
  .green { color: var(--green-text); background: var(--green-soft); }
  .orange { color: var(--orange-text); background: var(--orange-soft); }
  .red { color: var(--rec-text); background: var(--rec-soft); }
  .neutral { color: var(--text-2); background: var(--raised); }
</style>
