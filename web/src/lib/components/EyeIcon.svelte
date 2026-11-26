<script lang="ts">
  // L'occhio del «Visto» (26/11/2026): contorno = non visto, pieno = visto. Lo usano SeenButton, SeenMark e il player.
  let { filled = false, size = 16 }: { filled?: boolean; size?: number } = $props();
</script>

{#if filled}
  <svg width={size} height={size} viewBox="0 0 24 24" aria-hidden="true"><path d="M1.5 12S5.4 4.5 12 4.5 22.5 12 22.5 12 18.6 19.5 12 19.5 1.5 12 1.5 12z" fill="currentColor" /><circle cx="12" cy="12" r="3.6" fill="var(--eye-pupil, #34c77b)" /></svg>
{:else}
  <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z" /><circle cx="12" cy="12" r="3" /></svg>
{/if}
