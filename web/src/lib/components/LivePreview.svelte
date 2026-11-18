<script lang="ts">
  // Anteprima live muta: un fotogramma del canale preso dal server ogni pochi secondi, solo mentre la pagina è visibile.
  // Il fotogramma nuovo si carica a parte e sostituisce il vecchio solo se arriva: niente immagini rotte.
  import { api } from '../api/client';

  let { channelId, label = '' }: { channelId: number; label?: string } = $props();

  let src = $state('');
  let phase = $state<'wait' | 'ok' | 'fail'>('wait');

  $effect(() => {
    const ch = channelId;
    src = '';
    phase = 'wait';
    let alive = true;
    const next = () => {
      if (document.visibilityState !== 'visible') return;
      const url = api.previewUrl(ch);
      const img = new Image();
      img.onload = () => { if (alive) { src = url; phase = 'ok'; } };
      img.onerror = () => { if (alive && phase !== 'ok') phase = 'fail'; };
      img.src = url;
    };
    next();
    const t = setInterval(next, 6000);
    return () => { alive = false; clearInterval(t); };
  });
</script>

<figure class="live">
  {#if src}<img {src} alt={label ? `Anteprima di ${label}` : 'Anteprima del canale'} />{/if}
  {#if phase === 'wait'}<span class="note">Anteprima in arrivo…</span>{/if}
  {#if phase === 'fail'}<span class="note">Anteprima non disponibile: il canale non risponde o la playlist è occupata.</span>{/if}
  {#if phase === 'ok'}<span class="tag">● LIVE · muto</span>{/if}
</figure>

<style>
  .live { position: relative; margin: 0; aspect-ratio: 16 / 9; border-radius: var(--radius); background: #0a0c10; overflow: hidden; display: flex; align-items: center; justify-content: center; }
  img { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; }
  .note { position: absolute; padding: 12px; text-align: center; font-size: 14px; color: var(--muted); }
  .tag { position: absolute; top: 8px; left: 8px; background: rgba(13, 16, 21, 0.8); color: var(--rec-text); font-size: 12px; font-weight: 700; border-radius: 6px; padding: 2px 8px; }
</style>
