<script lang="ts">
  // Scelta degli abbonamenti: riquadri con logo, dalla piattaforma più diffusa. I canali venduti dentro un'altra
  // piattaforma (es. "Cine Comico" dentro Prime Video) stanno in una sezione a parte sotto quella piattaforma.
  import ServiceIcon from './ServiceIcon.svelte';
  import { platforms } from '../platforms.svelte';

  let { known, selected, disabled = false, ontoggle }: {
    known: string[]; selected: string[]; disabled?: boolean; ontoggle: (name: string) => void;
  } = $props();

  platforms.load();

  const isOn = (s: string) => selected.some((x) => x.toLowerCase() === s.toLowerCase());
  // anche gli abbonamenti scelti che non sono più tra le piattaforme proposte restano visibili (si possono togliere)
  const names = $derived(platforms.sort([...new Set([...known, ...selected])]));
  const heads = $derived(names.filter((n) => platforms.of(n).group.toLowerCase() === n.toLowerCase()));
  const POPULAR = 20;
  const popular = $derived(heads.filter((n) => platforms.of(n).priority < POPULAR));
  const others = $derived(heads.filter((n) => platforms.of(n).priority >= POPULAR));
  const channelGroups = $derived.by(() => {
    const m = new Map<string, string[]>();
    for (const n of names) {
      const g = platforms.of(n).group;
      if (g.toLowerCase() !== n.toLowerCase()) m.set(g, [...(m.get(g) ?? []), n]);
    }
    return [...m.entries()].map(([group, list]) => ({ group, list }));
  });
  const shortGroup = (g: string) => g.replace(/^Amazon /, '');
  const active = $derived(platforms.sort(selected));
</script>

<div class="picker">
  <div class="active" aria-live="polite">
    {#if active.length > 0}
      <span class="f-muted">Attivi:</span>
      {#each active as s (s)}<span class="pill"><ServiceIcon name={s} size={20} label={false} />{platforms.of(s).short}</span>{/each}
    {:else}
      <span class="f-muted">Nessun abbonamento scelto: tocca le piattaforme che avete.</span>
    {/if}
  </div>

  {#snippet tiles(list: string[], small = false)}
    <div class="grid" class:small>
      {#each list as s (s)}
        <button type="button" class="tile" aria-pressed={isOn(s)} {disabled} onclick={() => ontoggle(s)} title={s}>
          <ServiceIcon name={s} size={small ? 28 : 36} label={false} />
          <span class="nm">{platforms.of(s).short}</span>
          <span class="tick" aria-hidden="true">{isOn(s) ? '✓' : ''}</span>
        </button>
      {/each}
    </div>
  {/snippet}

  {#if popular.length > 0}
    <h4>Le più diffuse</h4>
    {@render tiles(popular)}
  {/if}
  {#if others.length > 0}
    <h4>Altre piattaforme</h4>
    {@render tiles(others)}
  {/if}
  {#each channelGroups as g (g.group)}
    <h4>Canali dentro {shortGroup(g.group)} <span class="f-muted hint">abbonamenti aggiuntivi: si pagano a parte e si guardano nell'app di {shortGroup(g.group)}</span></h4>
    {@render tiles(g.list, true)}
  {/each}
</div>

<style>
  .picker { display: flex; flex-direction: column; gap: 10px; }
  .active { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; min-height: 32px; }
  .pill { display: inline-flex; align-items: center; gap: 6px; padding: 3px 10px 3px 4px; border-radius: 16px; background: var(--blue-soft); font-size: 13px; font-weight: 600; }
  h4 { margin: 10px 0 0; font-size: 15px; display: flex; flex-wrap: wrap; align-items: baseline; gap: 4px 10px; }
  .hint { font-size: 13px; font-weight: 400; }
  .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(180px, 1fr)); gap: 8px; }
  .grid.small { grid-template-columns: repeat(auto-fill, minmax(160px, 1fr)); }
  .tile {
    display: flex; align-items: center; gap: 10px; min-height: 52px; padding: 7px 10px; border-radius: 12px;
    border: 1px solid var(--line-btn); background: var(--card); color: var(--text-2); font-size: 14px; text-align: left; cursor: pointer;
  }
  .small .tile { min-height: 44px; font-size: 13px; }
  .tile:hover { border-color: var(--text-2); }
  .tile[aria-pressed='true'] { background: var(--blue-soft); border-color: var(--blue); color: var(--text); font-weight: 600; }
  .tile:disabled { opacity: 0.6; cursor: default; }
  .nm { flex: 1 1 auto; min-width: 0; overflow-wrap: anywhere; line-height: 1.2; }
  .tick { flex: none; width: 16px; color: var(--blue); font-weight: 700; }
</style>
