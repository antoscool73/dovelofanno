<script lang="ts">
  // Schermata della password del server (24/11/2026): compare quando il server ha una password e questo dispositivo non
  // ha ancora fatto l'accesso. Dopo l'accesso il dispositivo resta dentro finché non esce o la password non cambia.
  import Logo from '../lib/components/Logo.svelte';
  import Button from '../lib/components/Button.svelte';
  import { api, ApiError } from '../lib/api/client';

  let { ondone }: { ondone: () => void } = $props();
  let password = $state('');
  let error = $state('');
  let busy = $state(false);

  async function submit(e: SubmitEvent) {
    e.preventDefault();
    busy = true;
    error = '';
    try {
      await api.login(password);
      ondone();
    } catch (err) {
      error = err instanceof ApiError ? err.message : 'Accesso non riuscito';
      password = '';
    } finally {
      busy = false;
    }
  }
</script>

<div class="wrap">
  <form class="card" onsubmit={submit}>
    <Logo size={44} />
    <h1>La password del server</h1>
    <p class="f-muted">Questo server di DoveLoFanno è protetto. Scrivi la password della famiglia: su questo dispositivo te la chiederà una volta sola.</p>
    <label class="f-field">Password
      <!-- svelte-ignore a11y_autofocus -->
      <input type="password" bind:value={password} autocomplete="current-password" required autofocus />
    </label>
    {#if error}<p class="f-error" role="alert">{error}</p>{/if}
    <Button variant="primary" type="submit" disabled={busy || !password}>Entra</Button>
    <p class="f-muted small">Non la ricordi? Chi gestisce il server la può togliere riavviandolo con l'opzione di ripristino della password (è nella guida di installazione), poi impostarne una nuova in Impostazioni › Accesso.</p>
  </form>
</div>

<style>
  .wrap { min-height: 100vh; display: flex; align-items: center; justify-content: center; padding: 24px; }
  .card { width: min(420px, 100%); display: flex; flex-direction: column; gap: 16px; background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius-l); padding: 28px; }
  h1 { font-size: 24px; margin: 0; }
  p { margin: 0; }
  .small { font-size: 13px; }
</style>
