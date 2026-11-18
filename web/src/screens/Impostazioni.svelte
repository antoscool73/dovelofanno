<script lang="ts">
  import Badge from '../lib/components/Badge.svelte';
  import Button from '../lib/components/Button.svelte';
  import VotiFilm from './VotiFilm.svelte';
  import CartelleLibreria from './CartelleLibreria.svelte';
  import GestioneProfili from './GestioneProfili.svelte';
  import LimitiNotifiche from './LimitiNotifiche.svelte';
  import { api, ApiError, type BackupInfo, type Channel, type Source, type Stream } from '../lib/api/client';
  import { day, time } from '../lib/format';

  type Tab = 'sorgenti' | 'cartelle' | 'profili' | 'voti' | 'limiti' | 'notifiche' | 'backup';
  const tabs: { id: Tab; label: string }[] = [
    { id: 'sorgenti', label: 'Sorgenti' },
    { id: 'cartelle', label: 'Cartelle' },
    { id: 'profili', label: 'Profili e abbonamenti' },
    { id: 'voti', label: 'Voti dei film' },
    { id: 'limiti', label: 'Limiti' },
    { id: 'notifiche', label: 'Notifiche' },
    { id: 'backup', label: 'Backup' },
  ];
  // la scheda si ricorda nell'indirizzo (/impostazioni#profili), così i collegamenti dalle altre schermate ci arrivano
  let tab = $state<Tab>(tabs.find((t) => location.hash === '#' + t.id)?.id ?? 'sorgenti');
  $effect(() => { history.replaceState(null, '', '#' + tab); });

  let sources = $state<Source[]>([]);
  let channels = $state<Channel[]>([]);
  let streams = $state<Stream[]>([]);
  let backups = $state<BackupInfo[]>([]);
  let error = $state('');
  let message = $state('');
  let busy = $state<string | null>(null);

  async function load() {
    try {
      [sources, channels, streams, backups] = await Promise.all([api.sources(), api.channels(), api.allStreams(), api.backups()]);
      error = '';
    } catch (e) {
      error = e instanceof ApiError ? e.message : 'Impostazioni non lette';
    }
  }
  load();

  // stato di un canale: si registra se almeno un suo flusso ha superato il controllo
  const problemStates = ['unreachable', 'geo', 'limit'];
  // raggruppamento a mano: Map.groupBy manca sui browser meno recenti (TV)
  const byChannel = $derived.by(() => {
    const m = new Map<number, Stream[]>();
    for (const s of streams) {
      const l = m.get(s.channelId);
      if (l) l.push(s);
      else m.set(s.channelId, [s]);
    }
    return m;
  });
  const okChannels = $derived(channels.filter((c) => (byChannel.get(c.id) ?? []).some((s) => s.state === 'ok')).length);
  const drmChannels = $derived(channels.filter((c) => { const l = byChannel.get(c.id) ?? []; return l.length > 0 && l.every((s) => s.state === 'drm'); }).length);
  const problems = $derived(
    channels
      .map((c) => ({ c, list: byChannel.get(c.id) ?? [] }))
      .filter(({ list }) => list.length > 0 && !list.some((s) => s.state === 'ok') && list.some((s) => problemStates.includes(s.state)))
      .map(({ c, list }) => ({ c, s: list.find((s) => problemStates.includes(s.state))! })),
  );
  const unchecked = $derived(streams.filter((s) => s.state === 'unchecked').length);

  const explain: Record<string, { label: string; tone: 'orange' | 'red' | 'neutral'; text: string }> = {
    geo: { label: 'Bloccato per zona', tone: 'orange', text: 'Il server ha risposto 403 o 451: il flusso si vede solo da alcuni paesi o reti. Se il server esce da una VPN, va fatto uscire dalla linea di casa.' },
    unreachable: { label: 'Non raggiungibile', tone: 'red', text: 'Non risponde: forse l’indirizzo è cambiato. Rileggi la playlist o chiedi a chi la mantiene.' },
    limit: { label: 'Troppe connessioni', tone: 'neutral', text: 'La playlist accetta poche connessioni ed erano occupate durante il controllo: riprova quando è libera.' },
  };

  function stats(s: Source): string {
    const mine = streams.filter((x) => x.sourceId === s.id);
    const ch = new Set(mine.map((x) => x.channelId)).size;
    const ok = new Set(mine.filter((x) => x.state === 'ok').map((x) => x.channelId)).size;
    const drm = mine.filter((x) => x.state === 'drm').length;
    const bad = mine.filter((x) => problemStates.includes(x.state)).length;
    const todo = mine.filter((x) => x.state === 'unchecked').length;
    return [
      `${ch} ${ch === 1 ? 'canale' : 'canali'}`,
      `${ok} ${ok === 1 ? 'si registra' : 'si registrano'}`,
      bad ? `${bad} con problemi` : '',
      drm ? `${drm} DRM` : '',
      todo ? `${todo} da controllare` : '',
    ].filter(Boolean).join(' · ');
  }

  function checked(s: Source): string {
    const parts = [s.xmltvUrl ? 'guida indicata dalla playlist' : 'nessuna guida'];
    if (s.lastCheckedAt) parts.push(`riletta ${day(s.lastCheckedAt).toLowerCase()} alle ${time(s.lastCheckedAt)}`);
    parts.push(s.maxConnections ? `al massimo ${s.maxConnections} connession${s.maxConnections === 1 ? 'e' : 'i'}` : 'nessun limite di connessioni');
    if (!s.enabled) parts.push('disattivata');
    return parts.join(' · ');
  }

  async function act(key: string, f: () => Promise<unknown>, ok: string) {
    busy = key;
    message = '';
    error = '';
    try {
      await f();
      message = ok;
      await load();
    } catch (e) {
      error = e instanceof ApiError ? e.message : 'Operazione non riuscita';
    } finally {
      busy = null;
    }
  }

  // riordino: la prima playlist è quella preferita, le altre sono riserve
  function move(s: Source, dir: -1 | 1) {
    const list = [...sources];
    const i = list.findIndex((x) => x.id === s.id);
    const j = i + dir;
    if (j < 0 || j >= list.length) return;
    [list[i], list[j]] = [list[j]!, list[i]!];
    act('move', () => Promise.all(list.map((x, k) => api.updateSource(x.id, { priority: k, name: null, maxConnections: null, clearMaxConnections: null, enabled: null }))), 'Ordine aggiornato.');
  }

  let newName = $state('');
  let newUrl = $state('');
  function add(e: SubmitEvent) {
    e.preventDefault();
    act('add', async () => {
      const r = await api.addSource(newName, newUrl);
      newName = '';
      newUrl = '';
      return r;
    }, 'Playlist aggiunta: i canali si controllano in background, uno alla volta.');
  }

  let editing = $state<Source | null>(null);
  let editName = $state('');
  let editLimit = $state('');
  let editEnabled = $state(true);
  function startEdit(s: Source) {
    editing = s;
    editName = s.name;
    editLimit = s.maxConnections ? String(s.maxConnections) : '';
    editEnabled = s.enabled;
  }
  function saveEdit(e: SubmitEvent) {
    e.preventDefault();
    const s = editing;
    if (!s) return;
    const limit = editLimit.trim() === '' ? null : Number(editLimit);
    act('edit', async () => {
      await api.updateSource(s.id, { name: editName, maxConnections: limit, clearMaxConnections: limit === null, enabled: editEnabled, priority: null });
      editing = null;
    }, 'Playlist aggiornata.');
  }
  function remove(s: Source) {
    if (!confirm(`Togliere la playlist «${s.name}»? I canali restano, i suoi flussi no.`)) return;
    act('del', async () => { await api.deleteSource(s.id); editing = null; }, 'Playlist tolta.');
  }

  const kb = (b: number) => (b > 1_048_576 ? `${(b / 1_048_576).toFixed(1)} MB` : `${Math.max(1, Math.round(b / 1024))} KB`);
</script>

<div class="wrap">
  <aside>
    <h1>Impostazioni</h1>
    <p class="muted small">Del server di casa: valgono per tutta la famiglia.</p>
    {#each tabs as t (t.id)}
      <button type="button" class="tab" class:on={tab === t.id} aria-pressed={tab === t.id} onclick={() => { tab = t.id; message = ''; }}>{t.label}</button>
    {/each}
  </aside>

  <div class="content">
    {#if error}<p class="error" role="alert">{error}</p>{/if}
    {#if message}<p class="ok" role="status">{message}</p>{/if}

    {#if tab === 'sorgenti'}
      <div class="head">
        <h2>Sorgenti</h2>
        <span class="muted small">Ogni flusso si controlla da solo ogni notte alle 4 e quando aggiungi una playlist.</span>
      </div>

      <div class="kpis">
        <div class="kpi"><strong>{channels.length}</strong><span>canali in tutto</span></div>
        <div class="kpi"><strong class="g">{okChannels}</strong><span>si registrano</span></div>
        <div class="kpi"><strong class="o">{problems.length}</strong><span>con problemi</span></div>
        <div class="kpi"><strong class="m">{drmChannels}</strong><span>protetti (DRM), esclusi</span></div>
      </div>
      {#if unchecked > 0}<p class="muted small">{unchecked === 1 ? '1 flusso ancora da controllare' : `${unchecked} flussi ancora da controllare`}: il controllo procede uno alla volta per non saturare le playlist.</p>{/if}

      {#if sources.length === 0}
        <div class="empty">Nessuna playlist. Senza, DoveLoFanno è guida, proposte e avvisi; con una playlist registra.</div>
      {:else}
        <ul class="sources">
          {#each sources as s, i (s.id)}
            <li>
              <div class="order">
                <button type="button" aria-label="Sposta su {s.name}" disabled={i === 0 || busy !== null} onclick={() => move(s, -1)}>▲</button>
                <button type="button" aria-label="Sposta giù {s.name}" disabled={i === sources.length - 1 || busy !== null} onclick={() => move(s, 1)}>▼</button>
              </div>
              <div class="sbody">
                <div class="line"><span class="sname">{s.name}</span><Badge tone="neutral">{i === 0 ? 'Prima' : 'Riserva'}</Badge>{#if !s.enabled}<Badge tone="orange">Disattivata</Badge>{/if}</div>
                <div class="url">{s.url}</div>
                <div class="stats">{stats(s)}</div>
                <div class="muted small">{checked(s)}</div>
                {#if s.lastError}<div class="error small">{s.lastError}</div>{/if}
              </div>
              <div class="acts">
                <Button size="s" disabled={busy !== null} onclick={() => act('r' + s.id, () => api.refreshSource(s.id), 'Playlist riletta.')}>Rileggi</Button>
                <Button size="s" disabled={busy !== null} onclick={() => act('c' + s.id, () => api.checkSource(s.id), 'Controllo avviato: i risultati arrivano man mano.')}>Controlla ora</Button>
                <Button size="s" disabled={busy !== null} onclick={() => startEdit(s)}>Modifica</Button>
              </div>
              {#if editing?.id === s.id}
                <form class="edit" onsubmit={saveEdit}>
                  <label class="field">Nome<input bind:value={editName} required /></label>
                  <label class="field">Connessioni al massimo<input bind:value={editLimit} inputmode="numeric" pattern="[0-9]*" placeholder="nessun limite" /></label>
                  <label class="check"><input type="checkbox" bind:checked={editEnabled} /> Attiva</label>
                  <div class="eacts">
                    <Button variant="ghost" onclick={() => remove(s)}>Togli la playlist</Button>
                    <span class="grow"></span>
                    <Button variant="ghost" onclick={() => (editing = null)}>Annulla</Button>
                    <Button variant="primary" type="submit" disabled={busy !== null}>Salva</Button>
                  </div>
                </form>
              {/if}
            </li>
          {/each}
        </ul>
        <p class="muted small">Se un canale è in più playlist si usa la prima in elenco; se quel flusso si ferma durante una registrazione, si passa da solo al successivo.</p>
      {/if}

      {#if problems.length > 0}
        <h3>Canali con problemi</h3>
        <ul class="problems">
          {#each problems as p (p.c.id)}
            {@const x = explain[p.s.state]}
            <li>
              <span class="pname">{p.c.name}</span>
              {#if x}<Badge tone={x.tone}>{x.label}</Badge><span class="ptext">{x.text}</span>{/if}
            </li>
          {/each}
        </ul>
      {/if}

      <form class="add" onsubmit={add}>
        <h3>Aggiungi una playlist</h3>
        <div class="row">
          <label class="field n">Nome (facoltativo)<input bind:value={newName} placeholder="Playlist di casa" /></label>
          <label class="field u">Indirizzo della playlist M3U o file sul server<input bind:value={newUrl} placeholder="https://…/playlist.m3u" required /></label>
        </div>
        <p class="muted small">DoveLoFanno non include canali né playlist e non li fornisce: aggiungi solo playlist che hai il diritto di usare. La guida TV, se la playlist la indica, si collega da sola.</p>
        <div><Button variant="primary" type="submit" disabled={busy !== null || newUrl.trim() === ''}>{busy === 'add' ? 'Verifica in corso…' : 'Verifica e aggiungi'}</Button></div>
      </form>
    {:else if tab === 'voti'}
      <VotiFilm />
    {:else if tab === 'cartelle'}
      <CartelleLibreria />
    {:else if tab === 'backup'}
      <div class="head"><h2>Backup</h2></div>
      <p class="muted">Una copia del database ogni notte alle 3; si tengono le ultime 7. Le registrazioni restano nella loro cartella.</p>
      <div><Button variant="primary" disabled={busy !== null} onclick={() => act('bak', () => api.createBackup(), 'Copia fatta.')}>Fai una copia ora</Button></div>
      {#if backups.length > 0}
        <ul class="problems">
          {#each backups as b (b.name)}
            <li><span class="pname">{day(b.createdAt)} alle {time(b.createdAt)}</span><span class="ptext">{b.name} · {kb(b.bytes)}</span></li>
          {/each}
        </ul>
      {/if}
      <p class="muted small">Ripristino: a server fermo, si rimette la copia al posto di dlf.db (istruzioni nella guida di installazione).</p>
    {:else if tab === 'profili'}
      <GestioneProfili />
    {:else if tab === 'limiti' || tab === 'notifiche'}
      <LimitiNotifiche section={tab} />
    {/if}
  </div>
</div>

<style>
  .wrap { display: flex; flex-wrap: wrap; gap: 32px; padding: 28px var(--gutter) 64px; max-width: 1340px; }
  aside { flex: 1 1 220px; max-width: 240px; display: flex; flex-direction: column; gap: 6px; }
  h1 { font-size: 30px; margin-bottom: 4px; }
  h2 { font-size: 24px; }
  h3 { font-size: 17px; margin: 8px 0 0; }
  .tab { text-align: left; min-height: 44px; padding: 0 14px; border-radius: var(--radius-s); border: 0; background: none; color: var(--muted); font-size: 15px; font-weight: 500; cursor: pointer; }
  .tab.on { background: var(--raised); color: var(--text); font-weight: 600; }
  .content { flex: 999 1 640px; min-width: 0; display: flex; flex-direction: column; gap: 18px; }
  .head { display: flex; flex-wrap: wrap; align-items: baseline; justify-content: space-between; gap: 12px; }
  .muted { color: var(--muted); margin: 0; }
  .small { font-size: 13px; }
  .error { color: var(--rec-text); margin: 0; }
  .ok { color: var(--green-text); margin: 0; }
  .kpis { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 12px; }
  .kpi { background: var(--card); border: 1px solid var(--line-strong); border-radius: 12px; padding: 14px 16px; display: flex; flex-direction: column; }
  .kpi strong { font-size: 28px; }
  .kpi span { font-size: 13px; color: var(--muted); }
  .g { color: var(--green-text); } .o { color: var(--orange-text); } .m { color: var(--muted); }
  .empty { background: var(--surface); border: 1px dashed var(--line-btn); border-radius: 12px; padding: 18px; color: var(--text-2); }
  .sources { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 10px; }
  .sources li { display: flex; flex-wrap: wrap; align-items: center; gap: 14px 20px; background: var(--card); border: 1px solid var(--line-strong); border-radius: 12px; padding: 16px 18px; }
  .order { display: flex; flex-direction: column; gap: 2px; }
  .order button { width: 34px; height: 28px; border: 1px solid var(--line-btn); border-radius: 6px; background: none; color: var(--text-2); cursor: pointer; }
  .order button:disabled { opacity: 0.35; cursor: default; }
  .sbody { flex: 1 1 320px; min-width: 0; display: flex; flex-direction: column; gap: 4px; }
  .line { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; }
  .sname { font-size: 17px; font-weight: 600; }
  .url { font-size: 13px; color: var(--muted); overflow-wrap: anywhere; }
  .stats { font-size: 14px; color: var(--text-2); }
  .acts { display: flex; flex-wrap: wrap; gap: 8px; }
  .edit { flex: 1 1 100%; display: flex; flex-wrap: wrap; gap: 12px; align-items: flex-end; border-top: 1px solid var(--line); padding-top: 14px; }
  .eacts { flex: 1 1 100%; display: flex; flex-wrap: wrap; gap: 8px; }
  .grow { flex: 1 1 0; }
  .field { display: flex; flex-direction: column; gap: 6px; font-size: 13px; color: var(--muted); }
  .field input { min-height: 42px; padding: 0 12px; border-radius: var(--radius-s); border: 1px solid var(--line-strong); background: var(--card); color: var(--text); font-size: 15px; }
  .check { display: flex; align-items: center; gap: 8px; min-height: 42px; font-size: 15px; }
  .problems { list-style: none; margin: 0; padding: 0; background: var(--surface); border: 1px solid var(--line); border-radius: 12px; overflow: hidden; }
  .problems li { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 16px; padding: 12px 16px; border-bottom: 1px solid var(--line-soft); }
  .problems li:last-child { border-bottom: 0; }
  .pname { flex: 0 0 160px; font-weight: 600; }
  .ptext { flex: 1 1 300px; font-size: 13px; color: var(--text-2); }
  .add { display: flex; flex-direction: column; gap: 12px; background: var(--surface); border: 1px solid var(--line-strong); border-radius: var(--radius-l); padding: 18px; }
  .row { display: flex; flex-wrap: wrap; gap: 12px; }
  .row .n { flex: 1 1 200px; } .row .u { flex: 3 1 360px; }
  @media (max-width: 720px) { .kpis { grid-template-columns: repeat(2, minmax(0, 1fr)); } aside { max-width: none; } }
</style>
