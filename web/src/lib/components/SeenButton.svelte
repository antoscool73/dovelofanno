<script lang="ts">
  // Il pulsante «Visto» (23/11/2026): uguale ovunque compaia un'opera. Spento = occhio con il contorno; acceso = verde
  // con la spunta. Un tocco segna o toglie il visto per il profilo attivo (lib/seen.svelte.ts). Senza opera o senza
  // profilo non compare. `label` mostra anche la scritta (nei punti dove c'è spazio: popup della guida, righe).
  import { seenState } from '../seen.svelte';
  import { session } from '../session.svelte';

  let {
    workId,
    title = '',
    label = false,
    onchange,
  }: { workId: number | null | undefined; title?: string; label?: boolean; onchange?: (watched: boolean) => void } = $props();

  $effect(() => { void session.profile; seenState.sync(); });
  const on = $derived(seenState.has(workId));
  const busy = $derived(workId != null && seenState.busy.has(workId));

  async function click(e: MouseEvent) {
    e.stopPropagation();
    e.preventDefault();
    if (workId == null) return;
    if (await seenState.toggle(workId)) onchange?.(seenState.has(workId));
  }
</script>

{#if workId != null && session.profile}
  <button type="button" class="seen" class:on class:label disabled={busy} aria-pressed={on} onclick={click}
    aria-label={on ? `Visto${title ? `: ${title}` : ''}. Tocca per togliere` : `Segna come visto${title ? `: ${title}` : ''}`}
    title={on ? 'Visto (tocca per togliere)' : 'Segna come visto'}>
    {#if on}
      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M5 12.5l4.5 4.5L19 7.5" /></svg>
    {:else}
      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z" /><circle cx="12" cy="12" r="3" /></svg>
    {/if}
    {#if label}<span>{on ? 'Visto' : 'Visto?'}</span>{/if}
  </button>
{/if}

<style>
  .seen {
    display: inline-flex; align-items: center; justify-content: center; gap: 6px; flex: 0 0 auto;
    min-width: 34px; height: 34px; padding: 0 8px; border-radius: 999px;
    border: 1px solid var(--line-btn); background: rgba(13, 16, 21, 0.6); color: var(--text-2);
    font: inherit; font-size: 13px; font-weight: 600; cursor: pointer;
  }
  .seen:hover { color: var(--text); border-color: var(--text-2); }
  .seen.on { background: var(--green, #34c77b); border-color: transparent; color: #0d1015; }
  .seen.label { height: 38px; padding: 0 14px; }
  .seen:disabled { opacity: 0.6; cursor: progress; }
</style>
