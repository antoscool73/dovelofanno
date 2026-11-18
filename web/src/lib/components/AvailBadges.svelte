<script lang="ts">
  // Quattro piccole icone in basso a destra della locandina: in TV (blu), in casa (bianca), in streaming nei miei
  // abbonamenti (verde del logo), a pagamento (gialla). Il contenitore della locandina deve essere position: relative.
  import type { Avail } from '../avail';
  import { platforms } from '../platforms.svelte';

  let { a, size = 22 }: { a: Avail; size?: number } = $props();
  const names = (l: string[]) => l.map((s) => platforms.of(s).short).join(', ');
  const items = $derived([
    a.tv ? { k: 'tv', label: `In TV: ${a.tv}` } : null,
    a.home ? { k: 'home', label: 'In casa: nella tua libreria o registrato' } : null,
    a.mine.length > 0 ? { k: 'mine', label: `In streaming nei tuoi abbonamenti: ${names(a.mine)}` } : null,
    a.pay.length > 0 ? { k: 'pay', label: `A pagamento: ${names(a.pay)}` } : null,
  ].filter((x): x is { k: string; label: string } => !!x));
</script>

{#if items.length > 0}
  <span class="badges" role="list" aria-label="Dove si vede">
    {#each items as it (it.k)}
      <span class="b {it.k}" role="listitem" title={it.label} aria-label={it.label} style:width="{size}px" style:height="{size}px">
        {#if it.k === 'tv'}
          <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="5" width="18" height="12" rx="2" /><path d="M9 21h6M12 17v4" /></svg>
        {:else if it.k === 'home'}
          <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 11l8-7 8 7" /><path d="M6 10v10h12V10" /><path d="M10 20v-5h4v5" /></svg>
        {:else if it.k === 'mine'}
          <svg viewBox="0 0 24 24" aria-hidden="true"><path class="fill" d="M9 7v10l8-5z" /></svg>
        {:else}
          <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M17 7.5A6 6 0 1 0 17 16.5" /><path d="M5 10.5h8M5 13.5h8" /></svg>
        {/if}
      </span>
    {/each}
  </span>
{/if}

<style>
  .badges { position: absolute; right: 6px; bottom: 6px; display: flex; gap: 4px; z-index: 1; pointer-events: auto; }
  .b {
    display: inline-flex; align-items: center; justify-content: center; border-radius: 50%;
    box-shadow: 0 0 0 2px rgba(13, 16, 21, 0.85), 0 2px 6px rgba(0, 0, 0, 0.45);
  }
  .b svg { width: 62%; height: 62%; fill: none; stroke: currentColor; stroke-width: 2.4; stroke-linecap: round; stroke-linejoin: round; }
  .b svg .fill { fill: currentColor; stroke: none; }
  .tv { background: #6e9bff; color: #0d1015; }
  .home { background: #f2f4f7; color: #0d1015; }
  .mine { background: #34c77b; color: #0d1015; }
  .pay { background: #f5b800; color: #0d1015; }
</style>
