<script lang="ts">
  // Avvertenze da accettare una volta per server, prima del primo avvio (24/11/2026). Il testo è in lib/terms.ts.
  import Logo from '../lib/components/Logo.svelte';
  import Button from '../lib/components/Button.svelte';
  import { api, ApiError } from '../lib/api/client';
  import { TERMS, TERMS_VERSION } from '../lib/terms';

  let { ondone }: { ondone: () => void } = $props();
  let ok = $state(false);
  let busy = $state(false);
  let error = $state('');

  async function accept() {
    busy = true;
    error = '';
    try {
      await api.updateGeneralSettings({ termsAccepted: TERMS_VERSION });
      ondone();
    } catch (e) {
      error = e instanceof ApiError ? e.message : 'Non salvato';
    } finally {
      busy = false;
    }
  }
</script>

<div class="wrap">
  <div class="card">
    <Logo />
    <h1>Prima di cominciare</h1>
    <p class="f-muted">Cinque cose da sapere su DoveLoFanno. Si accettano una volta per tutta la famiglia.</p>
    <ol>
      {#each TERMS as t (t.title)}
        <li><strong>{t.title}.</strong> {t.text}</li>
      {/each}
    </ol>
    {#if error}<p class="f-error" role="alert">{error}</p>{/if}
    <label class="f-check"><input type="checkbox" bind:checked={ok} /> Ho letto e accetto queste avvertenze</label>
    <div><Button variant="primary" disabled={busy || !ok} onclick={accept}>Accetto, continua</Button></div>
  </div>
</div>

<style>
  .wrap { min-height: 100vh; display: flex; align-items: center; justify-content: center; padding: 24px; }
  .card { width: min(680px, 100%); display: flex; flex-direction: column; gap: 16px; background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius-l); padding: 28px; }
  h1 { font-size: 26px; margin: 0; }
  p { margin: 0; }
  ol { margin: 0; padding-left: 22px; display: flex; flex-direction: column; gap: 12px; line-height: 1.5; color: var(--text-2); }
  strong { color: var(--text); }
</style>
