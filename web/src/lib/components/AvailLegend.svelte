<script lang="ts">
  // Legenda delle icone sulle locandine (AvailBadges), una riga piccola.
  import AvailBadges from './AvailBadges.svelte';
  const one = (k: 'tv' | 'home' | 'mine' | 'pay') => ({ tv: k === 'tv' ? 'x' : '', home: k === 'home', mine: k === 'mine' ? ['x'] : [], pay: k === 'pay' ? ['x'] : [] });
  const items = [
    { k: 'tv', label: 'In TV' },
    { k: 'home', label: 'In casa (libreria o registrato)' },
    { k: 'mine', label: 'In streaming nei tuoi abbonamenti' },
    { k: 'pay', label: 'A pagamento' },
  ] as const;
</script>

<p class="legend" aria-label="Legenda delle icone sulle locandine">
  {#each items as it (it.k)}
    <span class="it"><span class="ic" aria-hidden="true"><AvailBadges a={one(it.k)} size={18} /></span>{it.label}</span>
  {/each}
</p>

<style>
  .legend { margin: 0; display: flex; flex-wrap: wrap; gap: 6px 18px; font-size: 13px; color: var(--muted); }
  .it { display: inline-flex; align-items: center; gap: 6px; }
  /* l'icona della legenda non sta su una locandina: la si rimette nel flusso del testo */
  .ic { position: relative; display: inline-block; width: 18px; height: 18px; }
  .ic :global(.badges) { right: 0; bottom: 0; }
</style>
