<script lang="ts">
  // Tendina delle regioni della famiglia (24/11/2026, Antonio: più regioni, nessuna o tutte). La stessa ovunque si
  // sceglie la regione: Guida (filtro dei canali) e Preferenze › Canali nella guida. value = regioni separate da virgole.
  import { regionList } from '../channels';

  let {
    value,
    regions,
    prefix = '',
    onchange,
  }: { value: string; regions: string[]; prefix?: string; onchange: (v: string) => void } = $props();

  let open = $state(false);
  let root = $state<HTMLElement | null>(null);
  const chosen = $derived(regionList(value));
  const all = $derived(regions.length > 0 && regions.every((r) => chosen.includes(r)));
  const summary = $derived(
    chosen.length === 0 ? 'nessuna (tutti i canali)' : all ? 'tutte' : chosen.length <= 2 ? chosen.join(', ') : `${chosen.slice(0, 2).join(', ')} e altre ${chosen.length - 2}`,
  );

  function set(list: string[]) {
    onchange(regions.filter((r) => list.includes(r)).join(','));
  }
  function toggle(r: string) {
    set(chosen.includes(r) ? chosen.filter((x) => x !== r) : [...chosen, r]);
  }

  // si chiude con un clic fuori o con Esc
  $effect(() => {
    if (!open) return;
    const away = (e: PointerEvent) => { if (root && !root.contains(e.target as Node)) open = false; };
    const esc = (e: KeyboardEvent) => { if (e.key === 'Escape') open = false; };
    document.addEventListener('pointerdown', away);
    document.addEventListener('keydown', esc);
    return () => { document.removeEventListener('pointerdown', away); document.removeEventListener('keydown', esc); };
  });
</script>

<div class="rp" bind:this={root}>
  <button type="button" class="rp-btn" aria-haspopup="true" aria-expanded={open} onclick={() => (open = !open)}>
    <span>{prefix}{summary}</span>
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M6 9l6 6 6-6" /></svg>
  </button>
  {#if open}
    <div class="rp-panel" role="group" aria-label="Regioni della famiglia">
      <div class="rp-quick">
        <button type="button" class="rp-q" class:on={chosen.length === 0} onclick={() => set([])}>Nessuna</button>
        <button type="button" class="rp-q" class:on={all} onclick={() => set(regions)}>Tutte</button>
      </div>
      <ul>
        {#each regions as r (r)}
          <li><label><input type="checkbox" checked={chosen.includes(r)} onchange={() => toggle(r)} /> {r}</label></li>
        {/each}
      </ul>
      <p class="rp-hint">Nessuna = la guida mostra tutti i canali, senza dividerli per regione.</p>
    </div>
  {/if}
</div>

<style>
  .rp { position: relative; display: inline-block; }
  .rp-btn { display: inline-flex; align-items: center; gap: 8px; min-height: 40px; max-width: 360px; padding: 0 12px; border-radius: var(--radius-s); border: 1px solid var(--line-btn);
    background: var(--surface); color: var(--text); font: inherit; font-size: 14px; cursor: pointer; text-align: left; }
  .rp-btn span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .rp-panel { position: absolute; z-index: 30; top: calc(100% + 6px); left: 0; width: 280px; max-height: 420px; overflow: auto; background: var(--raised);
    border: 1px solid var(--line-btn); border-radius: var(--radius-m, 10px); box-shadow: 0 12px 32px rgba(0, 0, 0, 0.5); padding: 10px; display: flex; flex-direction: column; gap: 8px; }
  .rp-quick { display: flex; gap: 8px; }
  .rp-q { flex: 1; min-height: 36px; border-radius: var(--radius-s); border: 1px solid var(--line-btn); background: none; color: var(--text); font: inherit; font-size: 14px; font-weight: 600; cursor: pointer; }
  .rp-q.on { background: var(--text); color: var(--bg, #0d1015); border-color: transparent; }
  ul { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; }
  label { display: flex; align-items: center; gap: 10px; min-height: 34px; padding: 0 6px; border-radius: 6px; font-size: 14px; cursor: pointer; }
  label:hover { background: rgba(255, 255, 255, 0.06); }
  input { width: 16px; height: 16px; accent-color: var(--blue); margin: 0; }
  .rp-hint { margin: 0; font-size: 12px; color: var(--muted); }
</style>
