<script lang="ts">
  // Locandina di un titolo. Senza immagine (o se non si carica) una grafica generica con il marchio di DoveLoFanno,
  // il titolo e "Locandina non disponibile". Riempie il contenitore, che decide forma e misure.
  let { src = '', title, compact = false }: { src?: string; title: string; compact?: boolean } = $props();
  let broken = $state(false);
  $effect(() => { void src; broken = false; });
</script>

{#if src && !broken}
  <img class="img" {src} alt="" loading="lazy" referrerpolicy="no-referrer" onerror={() => (broken = true)} />
{:else}
  <span class="none" class:compact>
    <svg class="mark" viewBox="0 0 64 64" aria-hidden="true">
      <path d="M20 21a12 12 0 1 1 18 10.4C35 33.4 32 35.5 32 39" fill="none" stroke="#F2F4F7" stroke-width="9" stroke-linecap="round" stroke-linejoin="round" />
      <path d="M25 45v15l14-7.5z" fill="#34C77B" stroke="#34C77B" stroke-width="3" stroke-linejoin="round" />
    </svg>
    {#if !compact}
      <span class="t">{title}</span>
      <span class="n">Locandina non disponibile</span>
    {/if}
  </span>
{/if}

<style>
  .img { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; }
  .none {
    position: absolute; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 8px;
    padding: 12px; text-align: center; background: var(--brand-navy);
  }
  .mark { width: 34%; max-width: 64px; opacity: 0.9; }
  .compact .mark { width: 60%; }
  .t { font-family: var(--font-display); font-weight: 700; font-size: 15px; line-height: 1.15; color: var(--text); overflow-wrap: anywhere;
    display: -webkit-box; -webkit-line-clamp: 3; line-clamp: 3; -webkit-box-orient: vertical; overflow: hidden; }
  .n { font-size: 11px; color: var(--muted); letter-spacing: 0.02em; }
</style>
