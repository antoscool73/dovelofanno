<script lang="ts">
  // Riquadro colorato del profilo: l'icona scelta (24/11/2026) o, senza, l'iniziale del nome.
  import { profileIcon } from '../profileIcons';

  let { name, color, icon = '', size = 36 }: { name: string; color: string; icon?: string | null; size?: number } = $props();
  const initial = $derived((name.trim()[0] ?? '?').toUpperCase());
  const drawn = $derived(profileIcon(icon));
</script>

<span class="avatar" aria-hidden="true" style:width="{size}px" style:height="{size}px" style:background={color} style:font-size="{Math.round(size * 0.44)}px" style:border-radius="{Math.round(size * 0.25)}px">
  {#if drawn}
    <!-- tratto un po' più spesso nei riquadri piccoli, perché resti leggibile -->
    <svg width={Math.round(size * 0.62)} height={Math.round(size * 0.62)} viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width={size < 40 ? 2.4 : 2} stroke-linecap="round" stroke-linejoin="round">{@html drawn.svg}</svg>
  {:else}
    {initial}
  {/if}
</span>

<style>
  .avatar { display: inline-flex; align-items: center; justify-content: center; flex: 0 0 auto; color: var(--bg); font-weight: 700; }
</style>
