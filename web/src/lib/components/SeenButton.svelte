<script lang="ts">
  // Il pulsante «Visto» (23/11/2026): uguale ovunque compaia un'opera, anche nella Scheda (26/11/2026). Sempre l'occhio
  // (Antonio, 26/11/2026: niente spunta né scritta «Segna come visto»): spento = occhio con il contorno; acceso = tondo
  // verde con l'occhio pieno. Un tocco segna o toglie il visto per il profilo attivo (lib/seen.svelte.ts). Senza opera o senza
  // profilo non compare. `label` mostra anche la scritta (nei punti dove c'è spazio: popup della guida, righe).
  import EyeIcon from './EyeIcon.svelte';
  import { seenState } from '../seen.svelte';
  import { session } from '../session.svelte';

  let {
    workId,
    title = '',
    label = false,
    size = 'm',
    onchange,
  }: { workId: number | null | undefined; title?: string; label?: boolean; size?: 'm' | 'l'; onchange?: (watched: boolean) => void } = $props();

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
  <button type="button" class="seen {size}" class:on class:label disabled={busy} aria-pressed={on} onclick={click}
    aria-label={on ? `Visto${title ? `: ${title}` : ''}. Tocca per togliere` : `Segna come visto${title ? `: ${title}` : ''}`}
    title={on ? 'Visto (tocca per togliere)' : 'Segna come visto'}>
    <EyeIcon filled={on} size={size === 'l' ? 22 : 16} />
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
  /* nella Scheda, alto come i pulsanti grandi accanto */
  .seen.l { min-width: 48px; height: 48px; padding: 0 12px; }
  .seen:disabled { opacity: 0.6; cursor: progress; }
</style>
