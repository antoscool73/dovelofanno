<script lang="ts">
  // Impostazioni › Limiti e Impostazioni › Notifiche (collegamento del bot Telegram): valgono per tutta la famiglia.
  // Gli orari di silenzio dal 24/11/2026 sono in Preferenze › Notifiche (Antonio).
  import { ask } from '../lib/confirm.svelte';
  import Button from '../lib/components/Button.svelte';
  import { api, ApiError, type GeneralSettings, type NotificationSettings } from '../lib/api/client';

  let { section }: { section: 'limiti' | 'notifiche' } = $props();

  let general = $state<GeneralSettings | null>(null);
  let notif = $state<NotificationSettings | null>(null);
  let error = $state('');
  let message = $state('');
  let busy = $state(false);

  let maxParallel = $state(''), minFree = $state('');
  let token = $state(''), chat = $state('');

  async function load() {
    try {
      [general, notif] = await Promise.all([api.generalSettings(), api.notificationSettings()]);
      maxParallel = String(general.maxParallel);
      minFree = String(general.minFreeGb);
      chat = notif.telegramChat;
      error = '';
    } catch (e) {
      error = e instanceof ApiError ? e.message : 'Impostazioni non lette';
    }
  }
  load();

  async function act(fn: () => Promise<unknown>, ok: string) {
    busy = true;
    message = '';
    try { await fn(); await load(); message = ok; error = ''; }
    catch (e) { error = e instanceof ApiError ? e.message : 'Non salvato'; }
    finally { busy = false; }
  }
</script>

{#if section === 'limiti'}
  <div class="head"><h2>Limiti</h2></div>
  <p class="f-muted">Prima di programmare, DoveLoFanno avvisa se si supera uno di questi limiti (si può procedere lo stesso). Le proposte scelgono il passaggio che li rispetta.</p>
  {#if error}<p class="f-error" role="alert">{error}</p>{/if}
  {#if message}<p class="f-ok" role="status">{message}</p>{/if}
  {#if general}
    <form class="card" onsubmit={(e) => { e.preventDefault(); act(() => api.updateGeneralSettings({ maxParallel: Number(maxParallel), minFreeGb: Number(minFree) }), 'Limiti salvati.'); }}>
      <div class="f-row">
        <label class="f-field">Registrazioni contemporanee al massimo (0 = nessun limite)<input bind:value={maxParallel} inputmode="numeric" size="4" /></label>
        <label class="f-field">Spazio libero da lasciare (GB)<input bind:value={minFree} inputmode="numeric" size="5" /></label>
      </div>
      <p class="f-muted">Spazio libero adesso: {general.freeGb != null ? `${general.freeGb} GB` : 'non leggibile'} nella cartella {general.recordingsDir}. Una registrazione occupa circa 2,5 GB all'ora.</p>
      <div><Button variant="primary" type="submit" disabled={busy}>Salva</Button></div>
    </form>
  {/if}
{:else}
  <div class="head"><h2>Notifiche</h2></div>
  <p class="f-muted">Ogni profilo vede le sue notifiche nella campanella in alto. Qui si collega il bot Telegram, che vale per tutta la famiglia; gli orari di silenzio sono in Preferenze › Notifiche.</p>
  {#if error}<p class="f-error" role="alert">{error}</p>{/if}
  {#if message}<p class="f-ok" role="status">{message}</p>{/if}
  {#if notif}
    <form class="card" onsubmit={(e) => { e.preventDefault(); act(() => api.updateNotificationSettings({ telegramToken: token || null, telegramChat: chat || null }), 'Telegram collegato: è arrivato un messaggio di prova.'); token = ''; }}>
      <h3>Telegram</h3>
      {#if notif.telegramConfigured}
        <p class="state on">● Collegato{notif.telegramBot ? ` al bot @${notif.telegramBot}` : ''}, chat {notif.telegramChat}</p>
      {:else}
        <p class="state">○ Non collegato</p>
      {/if}
      <p class="f-muted">Per ricevere avvisi e registrazioni sul telefono anche fuori casa: crea un bot con @BotFather, aprilo e premi Avvia (o scrivigli un messaggio), poi metti qui il token del bot e il numero della chat. Il numero si trova aprendo nel browser <code>https://api.telegram.org/bot&lt;token&gt;/getUpdates</code>: è quello dopo <code>"chat":{'{'}"id":</code>. Prima di salvare DoveLoFanno manda un messaggio di prova.</p>
      <div class="f-row">
        <label class="f-field w2">Token del bot{notif.telegramConfigured ? ' (vuoto = resta quello)' : ''}<input bind:value={token} autocomplete="off" placeholder="123456:ABC…" /></label>
        <label class="f-field">Chat<input bind:value={chat} autocomplete="off" placeholder="123456789" /></label>
      </div>
      <div class="f-row">
        <Button variant="primary" type="submit" disabled={busy || (!token && !notif.telegramConfigured) || !chat}>{notif.telegramConfigured ? 'Salva e prova' : 'Collega e prova'}</Button>
        {#if notif.telegramConfigured}<span class="trash" title="Scollega Telegram"><Button label="Scollega Telegram" disabled={busy} onclick={async () => { if (await ask({ title: 'Scollegare Telegram?', text: 'Gli avvisi non arriveranno più su Telegram; per ricollegarlo serviranno di nuovo il token del bot e la chat.', ok: 'Scollega' })) act(() => api.updateNotificationSettings({ clearTelegram: true }), 'Telegram scollegato.'); }}><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 6h18M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6M10 11v6M14 11v6" /></svg></Button></span>{/if}
      </div>
    </form>
  {/if}
{/if}

<style>
  .head { display: flex; align-items: baseline; justify-content: space-between; }
  h2 { font-size: 24px; }
  h3 { font-size: 17px; }
  .card { display: flex; flex-direction: column; gap: 14px; background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius-l); padding: 18px 20px; }
  .w2 { flex: 1 1 280px; }
  .trash { display: contents; }
  .trash :global(.btn) { padding-inline: 10px; color: var(--muted); }
  .trash :global(.btn:hover:not(:disabled)) { color: var(--orange-text); border-color: var(--orange); }
  .state { margin: 0; font-weight: 600; color: var(--muted); }
  .state.on { color: var(--green-text); }
  code { font-size: 13px; overflow-wrap: anywhere; }
</style>
