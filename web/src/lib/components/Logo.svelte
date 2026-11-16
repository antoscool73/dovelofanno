<script lang="ts">
  // Marchio: punto di domanda bianco con il triangolo Play verde al posto del puntino, su quadrato blu notte.
  let { size = 32, wordmark = true }: { size?: number; wordmark?: boolean } = $props();
</script>

<span class="logo">
  <svg width={size} height={size} viewBox="0 0 64 64" aria-hidden="true">
    <rect width="64" height="64" rx="14" fill="#1B2440" />
    <path d="M20 21a12 12 0 1 1 18 10.4C35 33.4 32 35.5 32 39" fill="none" stroke="#F2F4F7" stroke-width="9" stroke-linecap="round" stroke-linejoin="round" />
    <path d="M25 45v15l14-7.5z" fill="#34C77B" stroke="#34C77B" stroke-width="3" stroke-linejoin="round" />
  </svg>
  {#if wordmark}
    <span class="word" style:font-size="{Math.round(size * 0.62)}px">Dove<span class="lo">Lo</span>Fanno</span>
  {:else}
    <span class="sr-only">DoveLoFanno</span>
  {/if}
</span>

<style>
  .logo { display: inline-flex; align-items: center; gap: 10px; }
  .word { font-family: var(--font-display); font-weight: 700; letter-spacing: -0.01em; color: var(--text); }
  .lo { color: var(--blue); }
</style>
