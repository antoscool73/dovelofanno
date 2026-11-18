<script lang="ts">
  // Testo di un pulsante che cambia con lo stato ("Aggiungi alla mia serata" / "Nella mia serata ✓") senza cambiare
  // larghezza: tutte le varianti occupano la stessa cella e se ne vede una, così i pulsanti accanto non si spostano.
  let { text, variants }: { text: string; variants: string[] } = $props();
  const all = $derived([...new Set([text, ...variants])]);
</script>

<span class="stable">
  {#each all as v (v)}<span class="v" class:on={v === text} aria-hidden={v === text ? undefined : 'true'}>{v}</span>{/each}
</span>

<style>
  .stable { display: inline-grid; justify-items: center; }
  .v { grid-area: 1 / 1; visibility: hidden; white-space: nowrap; }
  .v.on { visibility: visible; }
</style>
