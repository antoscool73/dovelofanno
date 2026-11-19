<script lang="ts">
  // Impostazioni › Limiti e Impostazioni › Notifiche (Telegram, orari di silenzio): valgono per tutta la famiglia.
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
  let token = $state(''), chat = $state(''), quietFrom = $state(''), quietTo = $state('');

  async function load() {
    try {
      [general, notif] = await Promise.all([api.generalSettings(), api.notificationSettings()]);
      maxParallel = String(general.maxParallel);
      minFree = String(general.minFreeGb);
      chat = notif.telegramChat;
      quietFrom = notif.quietFrom;
      quietTo = notif.quietTo;
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
  <p class="f-muted">Ogni profilo vede le sue notifiche nella campanella in alto. Qui si collegano i canali che valgono per tutta la famiglia.</p>
  {#if error}<p class="f-error" role="alert">{error}</p>{/if}
  {#if message}<p class="f-ok" role="status">{message}</p>{/if}
  {#if notif}
    <form class="card" onsubmit={(e) => { e.preventDefault(); act(() => api.updateNotificationSettings({ telegramToken: token || null, telegramChat: chat || null }), 'Telegram collegato: è arrivato un messaggio di prova.'); token = ''; }}>
      <h3>Telegram {notif.telegramConfigured ? '· collegato' : ''}</h3>
      <p class="f-muted">Per ricevere avvisi e registrazioni sul telefono anche fuori casa: crea un bot con @BotFather, scrivigli un messaggio, poi metti qui il token del bot e il numero della chat (lo dà per esempio @userinfobot). Prima di salvare DoveLoFanno manda un messaggio di prova.</p>
      <div class="f-row">
        <label class="f-field w2">Token del bot{notif.telegramConfigured ? ' (vuoto = resta quello)' : ''}<input bind:value={token} autocomplete="off" placeholder="123456:ABC…" /></label>
        <label class="f-field">Chat<input bind:value={chat} autocomplete="off" placeholder="123456789" /></label>
      </div>
      <div class="f-row">
        <Button variant="primary" type="submit" disabled={busy || (!token && !notif.telegramConfigured) || !chat}>{notif.telegramConfigured ? 'Salva e prova' : 'Collega e prova'}</Button>
        {#if notif.telegramConfigured}<Button variant="ghost" disabled={busy} onclick={async () => { if (await ask({ title: 'Scollegare Telegram?', text: 'Gli avvisi non arriveranno più su Telegram; per ricollegarlo serviranno di nuovo il token del bot e la chat.', ok: 'Scollega' })) act(() => api.updateNotificationSettings({ clearTelegram: true }), 'Telegram scollegato.'); }}>Scollega</Button>{/if}
      </div>
    </form>
    <form class="card" onsubmit={(e) => { e.preventDefault(); act(() => api.updateNotificationSettings({ quietFrom, quietTo }), 'Orari di silenzio salvati.'); }}>
      <h3>Orari di silenzio</h3>
      <p class="f-muted">In questi orari non parte nulla su Telegram (la campanella si aggiorna lo stesso). Vuoti = mai silenzio.</p>
      <div class="f-row">
        <label class="f-field">Dalle<input type="time" bind:value={quietFrom} /></label>
        <label class="f-field">Alle<input type="time" bind:value={quietTo} /></label>
        <Button type="submit" disabled={busy}>Salva</Button>
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
</style>
