<script lang="ts">
  // Logo quadrato di una piattaforma (Netflix, Prime Video...) dal catalogo TMDB; senza logo l'iniziale su fondo blu.
  import { platforms } from '../platforms.svelte';

  let { name, size = 24, label = true }: { name: string; size?: number; label?: boolean } = $props();
  const p = $derived(platforms.of(name));
  let broken = $state(false);
  $effect(() => { void name; broken = false; });
  platforms.load();
</script>

<span class="ic" style:width="{size}px" style:height="{size}px" style:border-radius="{Math.round(size / 4.5)}px" style:font-size="{Math.round(size * 0.5)}px"
  title={label ? name : undefined} role={label ? 'img' : undefined} aria-label={label ? name : undefined} aria-hidden={label ? undefined : 'true'}>
  {#if p.logo && !broken}
    <img src={p.logo} alt="" loading="lazy" referrerpolicy="no-referrer" onerror={() => (broken = true)} />
  {:else}
    {p.short.charAt(0).toUpperCase()}
  {/if}
</span>

<style>
  .ic {
    display: inline-flex; align-items: center; justify-content: center; flex: none; overflow: hidden; vertical-align: middle;
    background: var(--brand-navy); color: var(--text); font-weight: 700; line-height: 1; box-shadow: 0 0 0 1px rgba(255, 255, 255, 0.08);
  }
  img { width: 100%; height: 100%; object-fit: cover; display: block; }
</style>
