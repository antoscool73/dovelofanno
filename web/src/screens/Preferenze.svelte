<script lang="ts">
  // Preferenze del profilo: cosa proporre, i miei avvisi, i miei abbonamenti, le notifiche, la registrazione predefinita.
  import Button from '../lib/components/Button.svelte';
  import Badge from '../lib/components/Badge.svelte';
  import ServicePicker from '../lib/components/ServicePicker.svelte';
  import ServiceIcon from '../lib/components/ServiceIcon.svelte';
  import {
    api, ApiError, type AlertRow, type GenreCount, type GeneralSettings, type IgnoredWork, type PreferenceRow, type ProfileSubscriptions, type FamilySubscriptions,
  } from '../lib/api/client';
  import { navigate, router } from '../lib/router.svelte';
  import { session } from '../lib/session.svelte';
  import { day, time } from '../lib/format';

  type Tab = 'proposte' | 'avvisi' | 'abbonamenti' | 'notifiche' | 'registrazione';
  const tabs: { id: Tab; label: string }[] = [
    { id: 'proposte', label: 'Proposte' },
    { id: 'avvisi', label: 'I miei avvisi' },
    { id: 'abbonamenti', label: 'I miei abbonamenti' },
    { id: 'notifiche', label: 'Notifiche' },
    { id: 'registrazione', label: 'Registrazione predefinita' },
  ];
  let tab = $state<Tab>((['proposte', 'avvisi', 'abbonamenti', 'notifiche', 'registrazione'] as Tab[]).find((t) => location.hash === '#' + t) ?? 'proposte');
  $effect(() => { history.replaceState(null, '', '#' + tab); });

  const groups = ['Film', 'Serie TV', 'Documentari', 'Notizie', 'Sport', 'Intrattenimento', 'Bambini', 'Altro'];
  const prof = $derived(session.profile);
  const child = $derived(!!prof?.isChild);

  let prefs = $state<PreferenceRow[]>([]);
  let excluded = $state<string[]>([]);
  let ignored = $state<IgnoredWork[]>([]);
  let genres = $state<GenreCount[]>([]);
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
      const [a, b, c, d, e, f, g, h] = await Promise.all([
        api.preferences(p.id), api.excludedGroups(p.id), api.ignored(p.id), api.genres(), api.alerts(p.id),
        api.profileSubscriptions(p.id), api.familySubscriptions(), api.generalSettings(),
      ]);
      prefs = a; excluded = b.groups; ignored = c; genres = d; alerts = e; mySubs = f; family = g; general = h;
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

  // ---------------------------------------------------------------- preferenze
  let editing = $state<number | 'new' | null>(null);
  let fName = $state(''), fText = $state(''), fGroup = $state(''), fSub = $state(''), fMin = $state('0');
  function edit(p: PreferenceRow | null) {
    editing = p ? p.id : 'new';
    fName = p?.name ?? ''; fText = p?.text ?? ''; fGroup = p?.group ?? ''; fSub = p?.sub ?? ''; fMin = String(p?.minRating ?? 0);
  }
  const subOptions = $derived(genres.filter((g) => !fGroup || g.group === fGroup).map((g) => g.sub).filter((s, i, a) => a.indexOf(s) === i));
  function savePref(e: SubmitEvent) {
    e.preventDefault();
    const p = session.profile;
    if (!p || editing === null) return;
    const body = { name: fName, text: fText, group: fGroup || null, sub: fSub || null, minRating: Number(fMin.replace(',', '.')) || 0, enabled: true };
    const id = editing;
    act(async () => {
      if (id === 'new') await api.addPreference(p.id, body);
      else await api.updatePreference(p.id, id, { ...body, enabled: prefs.find((x) => x.id === id)?.enabled ?? true });
      editing = null;
    }, 'Preferenza salvata: le proposte si aggiornano subito in Stasera.');
  }
  const describe = (p: PreferenceRow) =>
    [p.group ? (p.sub ? `${p.group}: ${p.sub}` : p.group) : '', p.text ? `«${p.text}»` : '', p.minRating > 0 ? `voto ≥ ${p.minRating}` : ''].filter(Boolean).join(' · ');
  const examples = [
    { name: 'Film belli', group: 'Film', minRating: 7, text: '' },
    { name: 'Documentari', group: 'Documentari', minRating: 0, text: '' },
    { name: 'Commedie', group: 'Film', sub: 'Commedia', minRating: 6, text: '' },
  ];

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

    {#if tab === 'proposte'}
      <h2>Cosa proporre</h2>
      <p class="f-muted">Ogni preferenza cerca nella guida dei prossimi giorni; quello che trova compare in Stasera tra le «Proposte per te», una volta per titolo, senza ciò che hai già visto, già in casa o già in programma. Le proposte non si registrano da sole: per quello c'è «Avvisami» con la registrazione automatica.</p>
      {#if child}<p class="f-muted">Profilo bambino: si propongono solo i programmi per bambini.</p>{/if}
      {#if prefs.length > 0}
        <ul class="f-list">
          {#each prefs as p (p.id)}
            <li>
              <label class="f-check"><input type="checkbox" checked={p.enabled} disabled={busy} aria-label="Attiva {p.name}"
                onchange={(e) => act(() => api.updatePreference(prof!.id, p.id, { name: p.name, text: p.text, group: p.group || null, sub: p.sub || null, minRating: p.minRating, enabled: e.currentTarget.checked }))} /></label>
              <span class="pname">{p.name}</span>
              <span class="f-muted grow">{describe(p)}</span>
              <Button size="s" onclick={() => edit(p)}>Modifica</Button>
              <Button size="s" variant="ghost" onclick={() => act(() => api.deletePreference(prof!.id, p.id), 'Preferenza tolta.')}>Togli</Button>
            </li>
          {/each}
        </ul>
      {:else if editing === null}
        <div class="empty">
          <p>Nessuna preferenza ancora. Qualche idea per cominciare:</p>
          <div class="f-chips">
            {#each examples as x (x.name)}
              <button type="button" class="f-chip" disabled={busy} onclick={() => act(() => api.addPreference(prof!.id, { name: x.name, text: x.text, group: x.group, sub: x.sub ?? null, minRating: x.minRating, enabled: true }), 'Preferenza aggiunta.')}>+ {x.name}</button>
            {/each}
          </div>
        </div>
      {/if}
      {#if editing === null}
        <div><Button variant="primary" onclick={() => edit(null)}>Nuova preferenza</Button></div>
      {:else}
        <form class="card" onsubmit={savePref}>
          <h3>{editing === 'new' ? 'Nuova preferenza' : 'Modifica preferenza'}</h3>
          <div class="f-row">
            <label class="f-field w2">Nome<input bind:value={fName} maxlength="60" placeholder="Film belli" required /></label>
            <label class="f-field w2">Titolo, regista o attore (facoltativo)<input bind:value={fText} placeholder="Verdone" /></label>
          </div>
          <div class="f-row">
            <label class="f-field">Genere
              <select bind:value={fGroup} onchange={() => (fSub = '')}>
                <option value="">Tutti</option>
                {#each groups as g (g)}<option value={g}>{g}</option>{/each}
              </select>
            </label>
            <label class="f-field">Sottogenere
              <select bind:value={fSub}>
                <option value="">Tutti</option>
                {#each subOptions as s (s)}<option value={s}>{s}</option>{/each}
              </select>
            </label>
            <label class="f-field">Voto minimo (0 = qualsiasi)<input bind:value={fMin} inputmode="decimal" size="4" /></label>
          </div>
          <div class="f-row">
            <Button variant="primary" type="submit" disabled={busy}>Salva</Button>
            <Button variant="ghost" onclick={() => (editing = null)}>Annulla</Button>
          </div>
        </form>
      {/if}

      <h3>Generi da non propormi mai</h3>
      <div class="f-chips">
        {#each groups as g (g)}
          <button type="button" class="f-chip" aria-pressed={excluded.includes(g)} disabled={busy}
            onclick={() => act(() => api.setExcludedGroups(prof!.id, excluded.includes(g) ? excluded.filter((x) => x !== g) : [...excluded, g]))}>{g}</button>
        {/each}
      </div>

      <h3>Da non proporre più</h3>
      {#if ignored.length === 0}
        <p class="f-muted">Nessun titolo. Si aggiungono con «Non proporre più» dalle proposte o dalla scheda.</p>
      {:else}
        <ul class="f-list">
          {#each ignored as i (i.workKey)}
            <li><span class="grow">{i.title}</span><span class="f-muted">dal {day(i.addedAt).toLowerCase()}</span>
              <Button size="s" variant="ghost" onclick={() => act(() => api.unignore(prof!.id, i.workKey), 'Tornerà tra le proposte.')}>Riproponi</Button></li>
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
              <Button size="s" variant="ghost" onclick={() => act(() => api.deleteAlert(prof!.id, a.id), 'Avviso tolto.')}>Togli</Button>
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
  .w2 { flex: 1 1 240px; }
  .empty { background: var(--surface); border: 1px dashed var(--line-btn); border-radius: var(--radius-l); padding: 16px 20px; display: flex; flex-direction: column; gap: 10px; }
  .empty p { margin: 0; }
  @media (max-width: 720px) { aside { max-width: none; flex-basis: 100%; flex-direction: row; flex-wrap: wrap; } aside h1, aside p { width: 100%; } }
</style>
