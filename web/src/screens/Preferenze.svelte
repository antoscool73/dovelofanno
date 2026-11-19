<script lang="ts">
  // Preferenze del profilo: le liste nella pagina La mia serata (e gli esclusi dalle ricerche), i miei avvisi, i miei
  // abbonamenti, le notifiche, la registrazione predefinita. Le preferenze di prima («Cosa proporre») dal 19/11/2026 sono
  // liste dinamiche: qui si sceglie solo quali liste mostrare.
  import { ask } from '../lib/confirm.svelte';
  import Button from '../lib/components/Button.svelte';
  import Badge from '../lib/components/Badge.svelte';
  import ServicePicker from '../lib/components/ServicePicker.svelte';
  import ServiceIcon from '../lib/components/ServiceIcon.svelte';
  import {
    api, ApiError, type AlertRow, type GeneralSettings, type IgnoredWork, type ListSummary, type ProfileSubscriptions, type FamilySubscriptions,
  } from '../lib/api/client';
  import { navigate, router } from '../lib/router.svelte';
  import { session } from '../lib/session.svelte';
  import { day, time } from '../lib/format';

  type Tab = 'serata' | 'avvisi' | 'abbonamenti' | 'notifiche' | 'registrazione';
  const tabs: { id: Tab; label: string }[] = [
    { id: 'serata', label: 'La mia serata' },
    { id: 'avvisi', label: 'I miei avvisi' },
    { id: 'abbonamenti', label: 'I miei abbonamenti' },
    { id: 'notifiche', label: 'Notifiche' },
    { id: 'registrazione', label: 'Registrazione predefinita' },
  ];
  // senza scheda nell'indirizzo (o con #proposte, quello di prima) si apre «La mia serata»
  let tab = $state<Tab>((['serata', 'avvisi', 'abbonamenti', 'notifiche', 'registrazione'] as Tab[]).find((t) => location.hash === '#' + t) ?? 'serata');
  $effect(() => { history.replaceState(null, '', '#' + tab); });

  const prof = $derived(session.profile);
  const child = $derived(!!prof?.isChild);

  let lists = $state<ListSummary[]>([]);
  let ignored = $state<IgnoredWork[]>([]);
  let alerts = $state<AlertRow[]>([]);
  let mySubs = $state<ProfileSubscriptions | null>(null);
  let family = $state<FamilySubscriptions | null>(null);
  let general = $state<GeneralSettings | null>(null);
  let error = $state('');
  let message = $state('');
  let busy = $state(false);

  async function load() {
    const p = session.profile;
    if (!p) return;
    try {
      const [a, c, e, f, g, h] = await Promise.all([
        api.lists(p.id), api.ignored(p.id), api.alerts(p.id), api.profileSubscriptions(p.id), api.familySubscriptions(), api.generalSettings(),
      ]);
      lists = a.filter((l) => !l.isTonight); ignored = c; alerts = e; mySubs = f; family = g; general = h;
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

  // ---------------------------------------------------------------- liste nella pagina La mia serata
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

  // ---------------------------------------------------------------- notifiche del browser
  const canNotify = typeof window !== 'undefined' && 'Notification' in window;
  let permission = $state(canNotify ? Notification.permission : 'denied');
  async function askPermission() {
    if (!canNotify) return;
    permission = await Notification.requestPermission();
  }

  // ---------------------------------------------------------------- registrazione predefinita
  let gBefore = $state(''), gAfter = $state(''), gFormat = $state('mp4');
  $effect(() => { if (general) { gBefore = String(general.beforeMin); gAfter = String(general.afterMin); gFormat = general.format; } });
  function saveGeneral(e: SubmitEvent) {
    e.preventDefault();
    act(() => api.updateGeneralSettings({ beforeMin: Number(gBefore), afterMin: Number(gAfter), format: gFormat }), 'Salvato: vale per le prossime registrazioni.');
  }

  const kindLabel: Record<string, string> = { tv: 'In chiaro in TV', subscription: 'Nei miei abbonamenti', rent_buy: 'Noleggio o acquisto' };
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
      <h2>La mia serata</h2>
      <p class="f-muted">Scegli quali delle tue liste compaiono nella pagina La mia serata, sotto «Da vedere stasera». Le liste si creano in <a href="/libreria" onclick={navigate}>Libreria</a> (a mano) o dalla <a href="/cerca" onclick={navigate}>ricerca</a> con «Salva come lista» (dinamiche: i titoli li trova la ricerca, da sola). Dei titoli nuovi in TV nelle liste dinamiche scelte arriva una notifica.</p>
      {#if child}<p class="f-muted">Profilo bambino: le liste mostrano solo i titoli per bambini.</p>{/if}
      {#if lists.length === 0}
        <div class="empty"><p>Nessuna lista ancora. Apri la <a href="/cerca" onclick={navigate}>ricerca</a>, scegli i filtri (per esempio Film, Thriller, voto 7 e oltre, In TV) e tocca «Salva come lista».</p></div>
      {:else}
        <ul class="f-list">
          {#each lists as l (l.id)}
            <li>
              <label class="f-check"><input type="checkbox" checked={l.onHome} disabled={busy} aria-label="Mostra {l.name} in La mia serata"
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
        <p class="f-muted">Nessun titolo. Si aggiungono con «Non proporre più» da La mia serata o dalla scheda.</p>
      {:else}
        <ul class="f-list">
          {#each ignored as i (i.workKey)}
            <li><span class="grow">{i.title}</span><span class="f-muted">dal {day(i.addedAt).toLowerCase()}</span>
              <Button size="s" variant="ghost" onclick={() => act(() => api.unignore(prof!.id, i.workKey), 'Tornerà nelle liste e nelle ricerche.')}>Riammetti</Button></li>
          {/each}
        </ul>
      {/if}

    {:else if tab === 'avvisi'}
      <h2>I miei avvisi</h2>
      <p class="f-muted">Si creano con «Avvisami» dalla scheda di un titolo. Ognuno scatta una volta sola per passaggio o piattaforma.</p>
      {#if alerts.length === 0}
        <div class="empty"><p>Nessun avviso. Apri un film dalla <a href="/guida" onclick={navigate}>Guida</a> o dalla <a href="/libreria" onclick={navigate}>Libreria</a> e tocca «Avvisami».</p></div>
      {:else}
        <ul class="f-list">
          {#each alerts as a (a.id)}
            <li>
              <label class="f-check"><input type="checkbox" checked={a.active} disabled={busy} aria-label="Attiva l'avviso su {a.workTitle}"
                onchange={(e) => act(() => api.saveAlert(prof!.id, { workId: a.workId, kind: a.kind, active: e.currentTarget.checked, channels: null, autoRecord: null, moments: null, services: null, offers: null }))} /></label>
              <span class="atitle"><a href="/opera/{a.workId}" onclick={navigate}>{a.workTitle}</a>{#if a.workYear} ({a.workYear}){/if}</span>
              <Badge tone="blue">{kindLabel[a.kind] ?? a.kind}</Badge>
              {#if a.kind === 'tv' && a.autoRecord}<Badge tone="red">Registra da solo</Badge>{/if}
              <span class="f-muted grow">{a.status}{a.lastFiredAt ? ` · ultimo avviso ${day(a.lastFiredAt).toLowerCase()} ${time(a.lastFiredAt)}` : ''}</span>
              <Button size="s" onclick={() => router.go(`/opera/${a.workId}`)}>Modifica</Button>
              <Button size="s" variant="ghost" onclick={async () => { if (await ask({ title: `Togliere l'avviso su «${a.workTitle}»?`, text: 'Si perdono anche le sue impostazioni (quando avvisare, canali, registrazione automatica).', ok: 'Togli l\'avviso' })) act(() => api.deleteAlert(prof!.id, a.id), 'Avviso tolto.'); }}>Togli</Button>
            </li>
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

    {:else if tab === 'notifiche'}
      <h2>Notifiche</h2>
      <p class="f-muted">Le notifiche arrivano nella campanella in alto: registrazioni finite, avvisi, proposte nuove.</p>
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
      <h3>Telegram e orari di silenzio</h3>
      <p class="f-muted">Valgono per tutta la famiglia: <a href="/impostazioni#notifiche" onclick={navigate}>Impostazioni › Notifiche</a>.</p>

    {:else if tab === 'registrazione'}
      <h2>Registrazione predefinita</h2>
      <p class="f-muted">Quello che usa il pulsante «Registra» in un tocco. Vale per tutta la famiglia.</p>
      {#if general}
        <form class="card" onsubmit={saveGeneral}>
          <div class="f-row">
            <label class="f-field">Minuti prima<input bind:value={gBefore} inputmode="numeric" size="4" /></label>
            <label class="f-field">Minuti dopo<input bind:value={gAfter} inputmode="numeric" size="4" /></label>
            <label class="f-field">Formato
              <select bind:value={gFormat}><option value="mp4">Video (MP4)</option><option value="mp3">Solo audio (MP3)</option></select>
            </label>
          </div>
          <p class="f-muted">Cartella delle registrazioni sul server: {general.recordingsDir}{general.freeGb != null ? ` · ${general.freeGb} GB liberi` : ''}.</p>
          <div><Button variant="primary" type="submit" disabled={busy}>Salva</Button></div>
        </form>
      {/if}
    {/if}
  </div>
</div>

<style>
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
  .atitle { font-weight: 600; }
  .grow { flex: 1 1 180px; }
  .card { display: flex; flex-direction: column; gap: 14px; background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius-l); padding: 18px 20px; }
  .empty { background: var(--surface); border: 1px dashed var(--line-btn); border-radius: var(--radius-l); padding: 16px 20px; display: flex; flex-direction: column; gap: 10px; }
  .empty p { margin: 0; }
  @media (max-width: 720px) { aside { max-width: none; flex-basis: 100%; flex-direction: row; flex-wrap: wrap; } aside h1, aside p { width: 100%; } }
</style>
