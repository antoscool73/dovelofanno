<script lang="ts">
  // Preferenze del profilo: le liste nella pagina La mia lista (e gli esclusi dalle ricerche), i miei avvisi, i miei
  // abbonamenti, le notifiche (la registrazione predefinita dal 20/11/2026 è in Impostazioni › Registrazioni). Le preferenze di prima («Cosa proporre») dal 19/11/2026 sono
  // liste dinamiche: qui si sceglie solo quali liste mostrare.
  import { HOME_OPTIONAL } from '../lib/sections.svelte';
  import { ask } from '../lib/confirm.svelte';
  import Button from '../lib/components/Button.svelte';
  import ServicePicker from '../lib/components/ServicePicker.svelte';
  import ServiceIcon from '../lib/components/ServiceIcon.svelte';
  import {
    api, ApiError, type IgnoredWork, type ListSummary, type ProfileSubscriptions, type FamilySubscriptions,
    type NotificationSettings, type NotifyPref,
  } from '../lib/api/client';
  import { navigate, router } from '../lib/router.svelte';
  import { session } from '../lib/session.svelte';
  import { day } from '../lib/format';
  import PreferenzeCanali from './PreferenzeCanali.svelte';
  import { introOn, setIntroOn } from '../lib/intro';
  import Intro from '../lib/components/Intro.svelte';
  let introEnabled = $state(introOn());
  let introPreview = $state(false);

  type Tab = 'serata' | 'abbonamenti' | 'canali' | 'notifiche';
  const tabs: { id: Tab; label: string }[] = [
    { id: 'serata', label: 'La mia lista' },
    { id: 'abbonamenti', label: 'I miei abbonamenti' },
    // canali nella guida (23/11/2026: spostati da Impostazioni, «non è un'impostazione», Antonio)
    { id: 'canali', label: 'Canali nella guida' },
    { id: 'notifiche', label: 'Notifiche' },
  ];
  // senza scheda nell'indirizzo (o con #proposte, quello di prima) si apre «La mia serata»
  // #registrazione (la scheda di prima) porta a Impostazioni › Registrazioni
  if (location.hash === '#registrazione') router.go('/impostazioni#registrazioni');
  let tab = $state<Tab>((['serata', 'abbonamenti', 'canali', 'notifiche'] as Tab[]).find((t) => location.hash === '#' + t) ?? 'serata');
  $effect(() => { history.replaceState(history.state, '', '#' + tab); });

  const prof = $derived(session.profile);
  const child = $derived(!!prof?.isChild);

  let lists = $state<ListSummary[]>([]);
  let ignored = $state<IgnoredWork[]>([]);
  let mySubs = $state<ProfileSubscriptions | null>(null);
  let family = $state<FamilySubscriptions | null>(null);
  // sezioni predefinite di La mia lista che il profilo ha spento (20/11/2026)
  let homeHidden = $state<string[]>([]);
  let error = $state('');
  let message = $state('');
  let busy = $state(false);

  async function load() {
    const p = session.profile;
    if (!p) return;
    try {
      const [a, c, f, g, h] = await Promise.all([
        api.lists(p.id), api.ignored(p.id), api.profileSubscriptions(p.id), api.familySubscriptions(), api.homeSections(p.id),
      ]);
      lists = a.filter((l) => !l.isTonight); ignored = c; mySubs = f; family = g; homeHidden = h.hidden;
      error = '';
    } catch (e) {
      error = e instanceof ApiError ? e.message : 'Preferenze non lette';
    }
  }
  load();

  async function act(fn: () => Promise<unknown>, ok = '') {
    busy = true;
    message = '';
    try { await fn(); await load(); message = ok; error = ''; }
    catch (e) { error = e instanceof ApiError ? e.message : 'Operazione non riuscita'; }
    finally { busy = false; }
  }

  // ---------------------------------------------------------------- liste nella pagina La mia lista
  const listKind = (l: ListSummary) =>
    [l.query ? 'Dinamica' : `${l.count} ${l.count === 1 ? 'titolo' : 'titoli'}`, l.family ? 'della famiglia' : 'personale'].join(', ');
  const listHref = (l: ListSummary) => (l.query ? `/cerca#list=${l.id}` : `/libreria/liste/${l.id}`);

  // ---------------------------------------------------------------- abbonamenti
  function toggleSub(s: string) {
    const p = session.profile;
    if (!p || !mySubs) return;
    const cur = mySubs.inherits ? (family?.services ?? []) : mySubs.own;
    const next = cur.includes(s) ? cur.filter((x) => x !== s) : [...cur, s];
    act(() => api.setProfileSubscriptions(p.id, false, next));
  }

  // ---------------------------------------------------------------- Telegram e orari di silenzio (24/11/2026: gli orari
  // sono qui, il collegamento del bot resta in Impostazioni › Notifiche; valgono per tutta la famiglia)
  let notif = $state<NotificationSettings | null>(null);
  let quietFrom = $state(''), quietTo = $state('');
  async function loadNotif() {
    try { notif = await api.notificationSettings(); quietFrom = notif.quietFrom; quietTo = notif.quietTo; }
    catch (e) { error = e instanceof ApiError ? e.message : 'Notifiche non lette'; }
  }
  loadNotif();
  const quietChanged = $derived(!!notif && (quietFrom !== notif.quietFrom || quietTo !== notif.quietTo));
  const quietOk = $derived((quietFrom === '') === (quietTo === ''));
  function saveQuiet(from: string, to: string, ok: string) {
    act(async () => { notif = await api.updateNotificationSettings({ quietFrom: from, quietTo: to }); quietFrom = notif.quietFrom; quietTo = notif.quietTo; }, ok);
  }

  // ---------------------------------------------------------------- cosa arriva e dove (26/11/2026, richiesta di Antonio):
  // per ogni tipo di notifica, campanella (sito) e Telegram. Si salva a ogni spunta.
  const kindLabels: Record<string, { label: string; hint: string }> = {
    recording: { label: 'Registrazioni finite', hint: 'per tutta la famiglia' },
    recording_problem: { label: 'Registrazioni incomplete o non riuscite', hint: 'per tutta la famiglia' },
    series: { label: 'Serie: puntate nuove programmate', hint: 'da «Registra la serie»' },
    alert: { label: 'I tuoi avvisi', hint: 'in TV, nei tuoi abbonamenti, a noleggio' },
    proposals: { label: 'Proposte nuove', hint: 'con voto, anno, genere, paese, regia e cast' },
    library: { label: 'Pronti da guardare e conversioni', hint: 'copie per il browser e «Converti...»' },
    system: { label: 'Aggiornamenti di DoveLoFanno', hint: 'solo per gli adulti' },
  };
  let prefs = $state<NotifyPref[]>([]);
  async function loadPrefs() {
    const p = session.profile;
    if (!p) return;
    try { prefs = await api.notifyPrefs(p.id); } catch (e) { error = e instanceof ApiError ? e.message : 'Preferenze delle notifiche non lette'; }
  }
  loadPrefs();
  const shownPrefs = $derived(prefs.filter((x) => kindLabels[x.kind] && !(x.kind === 'system' && session.profile?.isChild)));
  async function setPref(kind: string, channel: 'web' | 'telegram', on: boolean) {
    const p = session.profile;
    if (!p) return;
    const next = prefs.map((x) => (x.kind === kind ? { ...x, [channel]: on } : x));
    busy = true;
    try { prefs = await api.setNotifyPrefs(p.id, next); message = 'Salvato.'; error = ''; }
    catch (e) { error = e instanceof ApiError ? e.message : 'Non salvato'; }
    finally { busy = false; }
  }

  // ---------------------------------------------------------------- notifiche del browser
  const canNotify = typeof window !== 'undefined' && 'Notification' in window;
  let permission = $state(canNotify ? Notification.permission : 'denied');
  async function askPermission() {
    if (!canNotify) return;
    permission = await Notification.requestPermission();
  }

</script>

<div class="wrap">
  <aside>
    <h1>Preferenze</h1>
    <p class="f-muted">Di {prof?.name}.</p>
    {#each tabs as t (t.id)}
      <button type="button" class="tab" class:on={tab === t.id} aria-pressed={tab === t.id} onclick={() => { tab = t.id; message = ''; }}>{t.label}</button>
    {/each}
  </aside>

  <div class="main">
    {#if error}<p class="f-error" role="alert">{error}</p>{/if}
    {#if message}<p class="f-ok" role="status">{message}</p>{/if}

    {#if tab === 'serata'}
      <h2>La mia lista</h2>
      <p class="f-muted">Scegli che cosa compare nella pagina La mia lista. «Da vedere stasera» e «I miei titoli» ci sono sempre; le altre sezioni e le tue liste si accendono e si spengono da qui.</p>
      <h3>Sezioni della pagina</h3>
      <ul class="f-list">
        <li><label class="f-check"><input type="checkbox" checked disabled aria-label="Da vedere stasera: sempre nella pagina" /></label><span class="pname">Da vedere stasera</span><span class="f-muted grow">Sempre nella pagina</span></li>
        <li><label class="f-check"><input type="checkbox" checked disabled aria-label="I miei titoli: sempre nella pagina" /></label><span class="pname">I miei titoli</span><span class="f-muted grow">Sempre nella pagina</span></li>
        {#each HOME_OPTIONAL as h (h.key)}
          <li>
            <label class="f-check"><input type="checkbox" checked={!homeHidden.includes(h.key)} disabled={busy} aria-label="Mostra {h.label} in La mia lista"
              onchange={(e) => { const on = e.currentTarget.checked; act(() => api.setHomeSections(prof!.id, on ? homeHidden.filter((k) => k !== h.key) : [...homeHidden, h.key])); }} /></label>
            <span class="pname">{h.label}</span>
            <span class="f-muted grow">Sezione predefinita</span>
          </li>
        {/each}
      </ul>
      <h3>Le mie liste</h3>
      <p class="f-muted">Le liste si creano in <a href="/libreria" onclick={navigate}>Libreria</a> (a mano) o dalla <a href="/cerca" onclick={navigate}>ricerca</a> con «Salva come lista» (dinamiche: i titoli li trova la ricerca, da sola). Dei titoli nuovi in TV nelle liste dinamiche scelte arriva una notifica.</p>
      {#if child}<p class="f-muted">Profilo bambino: le liste mostrano solo i titoli per bambini.</p>{/if}
      {#if lists.length === 0}
        <div class="empty"><p>Nessuna lista ancora. Apri la <a href="/cerca" onclick={navigate}>ricerca</a>, scegli i filtri (per esempio Film, Thriller, voto 7 e oltre, In TV) e tocca «Salva come lista».</p></div>
      {:else}
        <ul class="f-list">
          {#each lists as l (l.id)}
            <li>
              <label class="f-check"><input type="checkbox" checked={l.onHome} disabled={busy} aria-label="Mostra {l.name} in La mia lista"
                onchange={(e) => act(() => api.updateList(prof!.id, l.id, { onHome: e.currentTarget.checked }))} /></label>
              <span class="pname">{l.name}</span>
              <span class="f-muted grow">{listKind(l)}</span>
              <Button size="s" href={listHref(l)} onclick={navigate}>{l.query ? 'Cambia la ricerca' : 'Apri'}</Button>
            </li>
          {/each}
        </ul>
      {/if}

      <h3>Da non proporre più</h3>
      <p class="f-muted">Questi titoli non compaiono nelle liste dinamiche né nelle ricerche fatte con i soli filtri; cercati per nome ci sono, segnati «Escluso».</p>
      {#if ignored.length === 0}
        <p class="f-muted">Nessun titolo. Si aggiungono con «Non proporre più» da La mia lista o dalla scheda.</p>
      {:else}
        <ul class="f-list">
          {#each ignored as i (i.workKey)}
            <li><span class="grow">{i.title}</span><span class="f-muted">dal {day(i.addedAt).toLowerCase()}</span>
              <Button size="s" variant="ghost" onclick={() => act(() => api.unignore(prof!.id, i.workKey), 'Tornerà nelle liste e nelle ricerche.')}>Riammetti</Button></li>
          {/each}
        </ul>
      {/if}

    {:else if tab === 'abbonamenti'}
      <h2>I miei abbonamenti</h2>
      <p class="f-muted">Servono a «Dove Lo Fanno» e agli avvisi «nei miei abbonamenti». Quelli della famiglia si scelgono in Impostazioni › Profili e abbonamenti.</p>
      {#if mySubs && family}
        <label class="f-check"><input type="checkbox" checked={mySubs.inherits} disabled={busy}
          onchange={(e) => act(() => api.setProfileSubscriptions(prof!.id, e.currentTarget.checked, e.currentTarget.checked ? null : family!.services))} />
          <span>Uso gli abbonamenti della famiglia
            {#if family.services.length > 0}<span class="fam">{#each family.services as s (s)}<ServiceIcon name={s} size={20} />{/each}</span>{:else}(nessuno scelto){/if}</span></label>
        {#if !mySubs.inherits}
          <ServicePicker known={family.known} selected={mySubs.own} disabled={busy} ontoggle={toggleSub} />
        {/if}
      {/if}

    {:else if tab === 'canali'}
      <PreferenzeCanali />

    {:else if tab === 'notifiche'}
      <h2>Notifiche</h2>
      <p class="f-muted">Scegli cosa ti arriva e dove: nella campanella in alto (e nelle notifiche del browser, se le attivi) o su Telegram.</p>
      {#if shownPrefs.length > 0}
        <div class="nprefs" role="table" aria-label="Notifiche per tipo e canale">
          <div class="nrow nhead" role="row"><span role="columnheader">Tipo</span><span role="columnheader">Sito</span><span role="columnheader">Telegram</span></div>
          {#each shownPrefs as p (p.kind)}
            <div class="nrow" role="row">
              <span role="cell" class="nkind"><strong>{kindLabels[p.kind]!.label}</strong><span class="f-muted">{kindLabels[p.kind]!.hint}</span></span>
              <span role="cell">
                <input type="checkbox" checked={p.web} disabled={busy || p.webLocked} onchange={(e) => setPref(p.kind, 'web', e.currentTarget.checked)}
                  aria-label="{kindLabels[p.kind]!.label} nel sito" title={p.webLocked ? 'Sempre nella campanella' : ''} />
                {#if p.webLocked}<span class="f-muted lock">sempre</span>{/if}
              </span>
              <span role="cell">
                <input type="checkbox" checked={p.telegram && !!notif?.telegramConfigured} disabled={busy || !notif?.telegramConfigured} onchange={(e) => setPref(p.kind, 'telegram', e.currentTarget.checked)}
                  aria-label="{kindLabels[p.kind]!.label} su Telegram" title={notif?.telegramConfigured ? '' : 'Telegram non è collegato'} />
              </span>
            </div>
          {/each}
        </div>
        <p class="f-muted small">Avvisi e problemi delle registrazioni restano sempre nella campanella: per non ricevere più un avviso, toglilo in <a href="/avvisi" onclick={navigate}>I miei avvisi</a>. Telegram è una chat sola per la famiglia: un messaggio parte se lo vuole il profilo a cui è destinato; per le registrazioni, che sono di tutti, basta un profilo.{notif && !notif.telegramConfigured ? ' Telegram non è collegato: si collega in Impostazioni › Notifiche.' : ''}</p>
      {/if}
      <h3>Su questo dispositivo</h3>
      {#if !canNotify}
        <p class="f-muted">Questo browser non mostra notifiche di sistema: resta la campanella.</p>
      {:else if permission === 'granted'}
        <p class="f-ok">Attive: quando la pagina di DoveLoFanno è aperta in un'altra scheda le notifiche compaiono anche fuori dal browser.</p>
      {:else if !window.isSecureContext}
        <p class="f-muted">I browser permettono le notifiche di sistema solo su https o su questo stesso computer (localhost). Dalla rete di casa resta la campanella; per avere gli avvisi sul telefono collega Telegram in Impostazioni › Notifiche.</p>
      {:else if permission === 'denied'}
        <p class="f-muted">Il browser le ha bloccate: si riattivano dalle impostazioni del sito nel browser.</p>
      {:else}
        <div><Button variant="primary" onclick={askPermission}>Attiva le notifiche del browser</Button></div>
      {/if}
      <label class="introsw"><input type="checkbox" checked={introEnabled} onchange={(e) => { introEnabled = e.currentTarget.checked; setIntroOn(introEnabled); }} />
        <span><strong>Intro all'ingresso</strong> <span class="f-muted">il logo animato con il jingle, una volta ogni volta che apri DoveLoFanno; si salta con un clic</span></span></label>
      <div><Button variant="soft" size="s" onclick={() => (introPreview = true)}>Guarda l'intro</Button></div>
      {#if introPreview}<Intro onclose={() => (introPreview = false)} />{/if}
      <h3>Telegram</h3>
      {#if notif}
        {#if notif.telegramConfigured}
          <p class="tg on">● Collegato{notif.telegramBot ? ` al bot @${notif.telegramBot}` : ''}: avvisi e registrazioni arrivano anche lì, a tutta la famiglia.</p>
        {:else}
          <p class="tg">○ Non collegato. Il bot si collega in <a href="/impostazioni#notifiche" onclick={navigate}>Impostazioni › Notifiche</a>.</p>
        {/if}
        <form class="card" onsubmit={(e) => { e.preventDefault(); saveQuiet(quietFrom, quietTo, 'Orari di silenzio salvati.'); }}>
          <h3>Orari di silenzio</h3>
          <p class="f-muted">In questi orari i messaggi su Telegram restano in coda e partono tutti insieme, in un messaggio solo, quando il silenzio finisce (la campanella si aggiorna lo stesso). Valgono per tutta la famiglia.</p>
          {#if notif.quietFrom && notif.quietTo}
            <p class="tg on">In uso: silenzio dalle {notif.quietFrom} alle {notif.quietTo}.</p>
          {:else}
            <p class="tg">In uso: nessun silenzio, i messaggi partono a ogni ora.</p>
          {/if}
          {#if notif.telegramQueued > 0}
            <p class="f-muted">In coda: {notif.telegramQueued === 1 ? '1 messaggio' : `${notif.telegramQueued} messaggi`}{notif.quietTo ? `, partono alle ${notif.quietTo}` : ''}.</p>
          {/if}
          <div class="f-row">
            <label class="f-field">Dalle<input type="time" bind:value={quietFrom} /></label>
            <label class="f-field">Alle<input type="time" bind:value={quietTo} /></label>
            <Button variant="primary" type="submit" disabled={busy || !quietChanged || !quietOk}>{quietChanged ? 'Salva' : 'Salvato'}</Button>
            {#if notif.quietFrom || notif.quietTo}<span class="trash" title="Togli il silenzio"><Button label="Togli gli orari di silenzio" disabled={busy} onclick={async () => { if (await ask({ title: 'Togliere gli orari di silenzio?', text: 'I messaggi su Telegram partiranno a ogni ora, anche di notte.', ok: 'Togli il silenzio' })) saveQuiet('', '', 'Silenzio tolto: i messaggi partono a ogni ora.'); }}><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 6h18M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6M10 11v6M14 11v6" /></svg></Button></span>{/if}
          </div>
          {#if !quietOk}<p class="f-muted">Servono tutti e due gli orari, oppure nessuno.</p>{/if}
        </form>
      {/if}
    {/if}
  </div>
</div>

<style>
  .trash { display: contents; }
  .trash :global(.btn) { padding-inline: 10px; color: var(--muted); }
  .trash :global(.btn:hover:not(:disabled)) { color: var(--orange-text); border-color: var(--orange); }
  .tg { margin: 0; color: var(--muted); }
  /* tabella delle notifiche: colonne fisse, una riga per tipo (26/11/2026) */
  .nprefs { display: flex; flex-direction: column; background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius-l); overflow: hidden; max-width: 720px; }
  .nrow { display: grid; grid-template-columns: minmax(0, 1fr) 96px 96px; align-items: center; gap: 12px; padding: 12px 18px; border-bottom: 1px solid var(--line); }
  .nrow:last-child { border-bottom: 0; }
  .nhead { font-size: 13px; font-weight: 600; color: var(--muted); padding-top: 10px; padding-bottom: 10px; }
  .nrow > span:not(.nkind) { display: inline-flex; align-items: center; gap: 6px; }
  .nkind { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
  .nkind .f-muted { font-size: 13px; }
  .nrow input { width: 20px; height: 20px; margin: 0; accent-color: var(--blue); }
  .lock { font-size: 12px; }
  .small { font-size: 13px; max-width: 720px; }
  .tg.on { color: var(--green-text); }
  .card { display: flex; flex-direction: column; gap: 12px; background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius-l); padding: 18px 20px; }
  .card h3 { margin-top: 0; }
  .introsw { display: inline-flex; align-items: center; gap: 10px; font-size: 14px; cursor: pointer; }
  .introsw input { width: 18px; height: 18px; margin: 0; accent-color: var(--blue); flex: 0 0 auto; }
  .fam { display: inline-flex; flex-wrap: wrap; gap: 4px; margin-left: 6px; vertical-align: middle; }
  .wrap { display: flex; flex-wrap: wrap; gap: 32px; padding: 28px var(--gutter) 64px; max-width: 1340px; }
  aside { flex: 1 1 220px; max-width: 240px; display: flex; flex-direction: column; gap: 6px; }
  h1 { font-size: 30px; }
  h2 { font-size: 24px; }
  h3 { font-size: 17px; margin-top: 10px; }
  .tab { text-align: left; min-height: 44px; padding: 0 14px; border-radius: var(--radius-s); border: 0; background: none; color: var(--muted); font-size: 15px; font-weight: 500; cursor: pointer; }
  .tab.on { background: var(--raised); color: var(--text); font-weight: 600; }
  .main { flex: 999 1 520px; min-width: 0; display: flex; flex-direction: column; gap: 14px; }
  .pname { font-weight: 600; }
  .grow { flex: 1 1 180px; }
  .empty { background: var(--surface); border: 1px dashed var(--line-btn); border-radius: var(--radius-l); padding: 16px 20px; display: flex; flex-direction: column; gap: 10px; }
  .empty p { margin: 0; }
  @media (max-width: 720px) { aside { max-width: none; flex-basis: 100%; flex-direction: row; flex-wrap: wrap; } aside h1, aside p { width: 100%; } }
</style>
