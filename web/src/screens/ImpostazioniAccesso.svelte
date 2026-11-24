<script lang="ts">
  // Impostazioni › Accesso (24/11/2026): la password del server, una per tutta la famiglia. Senza password il server
  // risponde solo dalla rete di casa; con la password si può raggiungere anche da fuori (VPN, proxy, tunnel).
  import { ask } from '../lib/confirm.svelte';
  import Button from '../lib/components/Button.svelte';
  import { api, ApiError, type AuthStatus } from '../lib/api/client';

  let status = $state<AuthStatus | null>(null);
  let current = $state(''), next = $state(''), again = $state('');
  let error = $state('');
  let message = $state('');
  let busy = $state(false);

  async function load() {
    try { status = await api.authStatus(); }
    catch (e) { error = e instanceof ApiError ? e.message : 'Stato dell\'accesso non letto'; }
  }
  load();

  const min = $derived(status?.minLength ?? 8);
  const nextOk = $derived(next.trim().length >= min && next === again);

  async function act(fn: () => Promise<unknown>, ok: string) {
    busy = true;
    error = '';
    message = '';
    try { await fn(); await load(); message = ok; current = ''; next = ''; again = ''; }
    catch (e) { error = e instanceof ApiError ? e.message : 'Non riuscito'; }
    finally { busy = false; }
  }

  async function remove() {
    if (!current) { error = 'Scrivi la password attuale per toglierla'; return; }
    if (await ask({ title: 'Togliere la password del server?', text: 'Il server risponderà solo ai dispositivi della rete di casa, senza chiedere nulla. Da fuori casa non sarà più raggiungibile.', ok: 'Togli la password' }))
      act(() => api.removePassword(current), 'Password tolta: il server risponde solo dalla rete di casa.');
  }
</script>

<div class="head"><h2>Accesso</h2></div>
<p class="f-muted">Una password per tutta la famiglia, come in Jellyfin: ogni dispositivo la inserisce una volta. I profili con il PIN restano come sono.</p>
{#if error}<p class="f-error" role="alert">{error}</p>{/if}
{#if message}<p class="f-ok" role="status">{message}</p>{/if}

{#if status}
  {#if !status.passwordSet}
    <p class="state">○ Nessuna password: il server risponde solo dalla rete di casa{status.homeNetwork ? '' : ' (e questo dispositivo non lo è)'}.</p>
    <form class="card" onsubmit={(e) => { e.preventDefault(); act(() => api.setPassword(null, next.trim()), 'Password impostata: questo dispositivo resta dentro, gli altri la chiederanno.'); }}>
      <h3>Imposta la password</h3>
      <p class="f-muted">Serve per usare DoveLoFanno da fuori casa (VPN, proxy, tunnel) e per non lasciare il server aperto a chiunque sia nella rete. Almeno {min} caratteri: una frase di tre o quattro parole va benissimo.</p>
      <div class="f-row">
        <label class="f-field">Nuova password<input type="password" bind:value={next} autocomplete="new-password" /></label>
        <label class="f-field">Ripetila<input type="password" bind:value={again} autocomplete="new-password" /></label>
        <Button variant="primary" type="submit" disabled={busy || !nextOk}>Imposta</Button>
      </div>
      {#if again && next !== again}<p class="f-muted">Le due password non sono uguali.</p>{/if}
    </form>
  {:else}
    <p class="state on">● Protetto da password: ogni dispositivo deve fare l'accesso.</p>
    <form class="card" onsubmit={(e) => { e.preventDefault(); act(() => api.setPassword(current, next.trim()), 'Password cambiata: gli altri dispositivi devono rifare l\'accesso.'); }}>
      <h3>Cambia la password</h3>
      <p class="f-muted">Cambiandola, tutti gli altri dispositivi escono e la chiedono di nuovo.</p>
      <div class="f-row">
        <label class="f-field">Password attuale<input type="password" bind:value={current} autocomplete="current-password" /></label>
        <label class="f-field">Nuova password<input type="password" bind:value={next} autocomplete="new-password" /></label>
        <label class="f-field">Ripetila<input type="password" bind:value={again} autocomplete="new-password" /></label>
        <Button variant="primary" type="submit" disabled={busy || !current || !nextOk}>Cambia</Button>
        <span class="trash" title="Togli la password">
          <Button label="Togli la password del server" disabled={busy || !status.homeNetwork} onclick={remove}>
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 6h18M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6M10 11v6M14 11v6" /></svg>
          </Button>
        </span>
      </div>
      {#if again && next !== again}<p class="f-muted">Le due password non sono uguali.</p>{/if}
      <p class="f-muted small">Il bidone toglie la password (serve quella attuale, e solo dalla rete di casa).</p>
    </form>
    <div><Button disabled={busy} onclick={async () => { await api.logout().catch(() => {}); location.reload(); }}>Esci da questo dispositivo</Button></div>
  {/if}
{/if}

<style>
  .head { display: flex; align-items: baseline; justify-content: space-between; }
  h2 { font-size: 24px; }
  h3 { font-size: 17px; margin: 0; }
  .card { display: flex; flex-direction: column; gap: 14px; background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius-l); padding: 18px 20px; }
  .state { margin: 0; font-weight: 600; color: var(--muted); }
  .state.on { color: var(--green-text); }
  .small { font-size: 13px; margin: 0; }
  .trash { display: contents; }
  .trash :global(.btn) { padding-inline: 10px; color: var(--muted); }
  .trash :global(.btn:hover:not(:disabled)) { color: var(--orange-text); border-color: var(--orange); }
</style>
