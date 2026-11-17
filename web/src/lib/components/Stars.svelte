<script lang="ts">
  // Voto personale da 1 a 5 stelle; un secondo clic sulla stessa stella lo toglie.
  let { value, label, size = 'm', onchange }: { value: number | null | undefined; label: string; size?: 's' | 'm'; onchange: (v: number | null) => void } = $props();
  const stars = [1, 2, 3, 4, 5];
</script>

<div class="stars {size}" role="group" aria-label={label}>
  {#each stars as n (n)}
    <button type="button" class:on={(value ?? 0) >= n} aria-label="{n} {n === 1 ? 'stella' : 'stelle'}" aria-pressed={value === n} onclick={() => onchange(value === n ? null : n)}>★</button>
  {/each}
</div>

<style>
  .stars { display: flex; align-items: center; }
  button { border: 0; background: none; padding: 0; line-height: 1; color: #4a5262; cursor: pointer; }
  .m button { width: 32px; height: 40px; font-size: 22px; }
  .s button { width: 26px; height: 36px; font-size: 18px; }
  button.on { color: var(--star); }
</style>
