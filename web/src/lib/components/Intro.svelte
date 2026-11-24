<script lang="ts">
  // Intro a schermo intero (lib/intro.ts). Prova a partire col suono: dopo il clic sul profilo il browser lo permette;
  // se no parte muta e c'è il pulsante per sentirla. Un clic, Esc o la fine del video la chiudono.
  import { onMount } from 'svelte';
  import { introSeen } from '../intro';

  let { onclose }: { onclose: () => void } = $props();
  let video = $state<HTMLVideoElement | null>(null);
  let muted = $state(false);
  let leaving = $state(false);

  function close() {
    if (leaving) return;
    leaving = true;
    video?.pause();
    setTimeout(onclose, 250);
  }

  onMount(() => {
    introSeen();
    const v = video;
    if (!v) return;
    v.volume = 0.7;
    v.play().catch(() => {
      muted = true;
      v.muted = true;
      v.play().catch(() => close());
    });
  });

  function unmute(e: MouseEvent) {
    e.stopPropagation();
    if (!video) return;
    video.muted = false;
    muted = false;
    if (video.ended) video.play();
  }
</script>

<svelte:window onkeydown={(e) => { if (e.key === 'Escape' || e.key === 'Enter' || e.key === ' ') close(); }} />

<!-- svelte-ignore a11y_click_events_have_key_events -->
<div class="intro" class:leaving role="dialog" aria-label="Intro di DoveLoFanno" tabindex="-1" onclick={close}>
  <!-- solo musica, niente parlato: non ci sono sottotitoli da mettere -->
  <!-- svelte-ignore a11y_media_has_caption -->
  <video bind:this={video} src="/intro.mp4?v=3" poster="/intro.jpg" playsinline preload="auto" onended={close}></video>
  <div class="bar">
    {#if muted}<button type="button" class="b" onclick={unmute}>🔊 Con l'audio</button>{/if}
    <button type="button" class="b" onclick={(e) => { e.stopPropagation(); close(); }}>Salta ›</button>
  </div>
</div>

<style>
  .intro { position: fixed; inset: 0; z-index: 1000; background: #000; display: flex; align-items: center; justify-content: center; cursor: pointer; opacity: 1; transition: opacity 0.25s; }
  .intro.leaving { opacity: 0; }
  video { width: 100%; height: 100%; object-fit: contain; }
  .bar { position: absolute; right: 24px; bottom: 24px; display: flex; gap: 10px; }
  .b { min-height: 40px; padding: 0 16px; border-radius: 999px; border: 1px solid rgba(255, 255, 255, 0.25); background: rgba(13, 16, 21, 0.6); color: #f2f4f7; font: inherit; font-size: 14px; font-weight: 600; cursor: pointer; }
  .b:hover { background: rgba(13, 16, 21, 0.85); }
</style>
