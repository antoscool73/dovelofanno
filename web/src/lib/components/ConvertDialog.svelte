<script lang="ts">
  // «Converti...» (lib/convert.svelte.ts): formato MP4 o MP3, poi in coda sul server.
  import Button from './Button.svelte';
  import { convertState as c, formatsFor } from '../convert.svelte';
  import { router } from '../router.svelte';

  function dialog(node: HTMLDialogElement) {
    node.showModal();
    node.addEventListener('cancel', (e) => { e.preventDefault(); c.close(); });
    node.addEventListener('click', (e) => { if (e.target === node) c.close(); });
  }
</script>

{#if c.target}
  <dialog use:dialog aria-labelledby="cv-title">
    <div class="box">
      <h2 id="cv-title">Converti «{c.target.name}»</h2>
      {#if c.done}
        <p role="status">{c.done}</p>
        <div class="actions">
          <button type="button" class="ghost" onclick={() => { c.close(); router.go('/impostazioni#registrazioni'); }}>Vedi le conversioni</button>
          <Button variant="primary" onclick={() => c.close()}>Chiudi</Button>
        </div>
      {:else if formatsFor(c.target.name).length === 0}
        <p>È già un MP3, cioè solo audio: non c'è niente in cui convertirlo.</p>
        <div class="actions"><Button variant="primary" onclick={() => c.close()}>Chiudi</Button></div>
      {:else}
        <!-- solo i formati diversi da quello del file (23/11/2026): un MP4 si converte solo in MP3 -->
        {@const fmts = formatsFor(c.target.name)}
        <fieldset>
          <legend class="sr-only">Formato</legend>
          {#if fmts.includes('mp4')}
            <label class="opt"><input type="radio" name="cv-format" value="mp4" bind:group={c.format} />
              <span><strong>Video (MP4)</strong><br /><span class="muted">Si legge ovunque (TV, telefono, Jellyfin). Prima senza ricodificare; se non basta, ricodifica l'audio o tutto.</span></span></label>
          {/if}
          {#if fmts.includes('mp3')}
            <label class="opt"><input type="radio" name="cv-format" value="mp3" bind:group={c.format} />
              <span><strong>Solo audio (MP3)</strong><br /><span class="muted">La prima traccia audio, ~190 kbit/s: per musica e programmi da ascoltare.{fmts.length === 1 ? ' È già un MP4: si può solo estrarne l’audio.' : ''}</span></span></label>
          {/if}
        </fieldset>
        <p class="muted small">Il file nuovo va accanto all'originale, che resta (dal NAS in sola lettura va in «Convertiti», nella cartella delle registrazioni). Si fa sul server, una conversione alla volta.</p>
        {#if c.error}<p class="err" role="alert">{c.error}</p>{/if}
        <div class="actions">
          <button type="button" class="ghost" onclick={() => c.close()}>Annulla</button>
          <Button variant="primary" disabled={c.busy} onclick={() => c.start()}>{c.busy ? 'Un momento…' : 'Converti'}</Button>
        </div>
      {/if}
    </div>
  </dialog>
{/if}

<style>
  dialog { width: min(520px, calc(100vw - 32px)); padding: 0; border: 1px solid var(--line-strong); border-radius: var(--radius-l); background: var(--surface); color: var(--text); }
  dialog::backdrop { background: rgba(5, 7, 10, 0.7); }
  .box { display: flex; flex-direction: column; gap: 12px; padding: 22px 24px; }
  h2 { font-size: 20px; margin: 0; overflow-wrap: anywhere; }
  p { margin: 0; font-size: 15px; line-height: 1.5; color: var(--text-2); }
  fieldset { border: 0; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 8px; }
  .opt { display: flex; gap: 12px; align-items: flex-start; padding: 12px 14px; border: 1px solid var(--line); border-radius: var(--radius); background: var(--card); cursor: pointer; font-size: 15px; }
  .opt input { margin-top: 4px; width: 18px; height: 18px; }
  .muted { color: var(--muted); }
  .small { font-size: 13px; }
  .err { color: var(--rec); }
  .actions { display: flex; justify-content: flex-end; flex-wrap: wrap; gap: 10px; margin-top: 4px; }
  .ghost { min-height: 44px; padding: 0 18px; border-radius: var(--radius); border: 1px solid var(--line-btn); background: none; color: var(--text); font: inherit; font-size: 15px; font-weight: 600; cursor: pointer; }
  .ghost:hover { background: var(--raised); }
  .sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); }
</style>
