<script lang="ts">
  let { name, color, size = 36 }: { name: string; color: string; size?: number } = $props();
  const initial = $derived((name.trim()[0] ?? '?').toUpperCase());
</script>

<span class="avatar" aria-hidden="true" style:width="{size}px" style:height="{size}px" style:background={color} style:font-size="{Math.round(size * 0.44)}px" style:border-radius="{Math.round(size * 0.25)}px">{initial}</span>

<style>
  .avatar { display: inline-flex; align-items: center; justify-content: center; flex: 0 0 auto; color: var(--bg); font-weight: 700; }
</style>
