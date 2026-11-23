<script lang="ts">
  // Preferenze › Canali nella guida (23/11/2026, richiesta di Antonio; prima in Impostazioni): la regione della famiglia per il filtro «Canali» della guida
  // e la scelta canale per canale: Automatico (decide il filtro), Sempre, Mai. Vale per tutta la famiglia e vince sul
  // filtro. Con i gruppi e la ricerca si sceglie anche in blocco (es. tutte le locali di un'altra regione: Mai).
  import Button from '../lib/components/Button.svelte';
  import { ask } from '../lib/confirm.svelte';
  import { api, ApiError, type Channel } from '../lib/api/client';
  import { KIND_LABEL, SCOPES, scopeOf, type Scope } from '../lib/channels';

  let channels = $state<Channel[]>([]);
  let regions = $state<string[]>([]);
  let region = $state('');
  let error = $state('');
  let message = $state('');
  let busy = $state(false);
  let q = $state('');
  let scope = $state<Scope | ''>('');
  let pick = $state<'' | 'auto' | 'show' | 'hide'>('');

  async function load() {
    try {
      const [l, g] = await Promise.all([api.channels(), api.generalSettings()]);
      channels = l.filter((c) => !c.excluded && c.streams > 0);
      regions = g.regions;
      region = g.region;
      error = '';
    } catch (e) {
      error = e instanceof ApiError ? e.message : 'Canali non letti';
    }
  }
  load();

  async function saveRegion(v: string) {
    try {
      const g = await api.updateGeneralSettings({ region: v });
      region = g.region;
      message = v ? `Regione: ${v}.` : 'Nessuna regione.';
    } catch (e) { error = e instanceof ApiError ? e.message : 'Regione non salvata'; }
  }

  const stateOf = (c: Channel): 'auto' | 'show' | 'hide' => (c.guideShow == null ? 'auto' : c.guideShow ? 'show' : 'hide');
  const plain = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
  const shown = $derived(channels.filter((c) =>
    (!q.trim() || plain(c.name).includes(plain(q.trim())))
    && (!scope || scopeOf(c, region) === scope)
    && (!pick || stateOf(c) === pick)));

  async function set(ids: number[], show: 'auto' | 'show' | 'hide') {
    if (ids.length === 0) return;
    // in blocco si chiede conferma: un clic sbagliato cambierebbe centinaia di canali
    const what = show === 'auto' ? 'Automatico' : show === 'show' ? 'Sempre nella guida' : 'Mai nella guida';
    if (ids.length > 1 && !(await ask({ title: `${what}: ${ids.length} canali?`, text: 'Vale per tutta la famiglia. Si può cambiare quando vuoi, anche canale per canale.', ok: `Sì, ${ids.length} canali` }))) return;
    busy = true;
    try {
      await api.setChannelsGuide(ids, show);
      const v = show === 'auto' ? null : show === 'show';
      channels = channels.map((c) => (ids.includes(c.id) ? { ...c, guideShow: v } : c));
      message = ids.length === 1 ? '' : `${ids.length} canali: ${show === 'auto' ? 'automatico' : show === 'show' ? 'sempre nella guida' : 'mai nella guida'}.`;
    } catch (e) { error = e instanceof ApiError ? e.message : 'Scelta non salvata'; }
    finally { busy = false; }
  }
  const scopeLabel = (c: Channel) => SCOPES.find((s) => s.id === scopeOf(c, region))?.label ?? '';
</script>

<section class="box">
  <h2>Canali nella guida</h2>
  <p class="muted">Nella guida il filtro «Canali» decide quali canali vedere: nazionali, della tua regione, di altre regioni, altri, radio. Qui scegli la regione della famiglia (si cambia anche dal filtro della guida) e, se serve, i singoli canali: «Sempre» e «Mai» valgono per tutti e vincono sul filtro.</p>

  <label class="field">La nostra regione
    <select value={region} onchange={(e) => saveRegion(e.currentTarget.value)}>
      <option value="">Nessuna: tutti i canali</option>
      {#each regions as r (r)}<option value={r}>{r}</option>{/each}
    </select>
  </label>
  <p class="muted small">La regione di un canale si ricava dal nome (città, provincia, regione): «Bergamo Tv» è in Lombardia. Con «Nessuna» la guida mostra tutti i canali, senza dividerli per gruppi. Il tipo (notizie, sport, televendite…) viene dal database pubblico di iptv-org.</p>

  {#if error}<p class="err" role="alert">{error}</p>{/if}
  {#if message}<p class="ok" role="status">{message}</p>{/if}

  <div class="tools">
    <input type="search" placeholder="Cerca un canale" bind:value={q} aria-label="Cerca un canale" />
    <select bind:value={scope} aria-label="Gruppo">
      <option value="">Tutti i gruppi</option>
      {#each SCOPES as s (s.id)}<option value={s.id}>{s.label}</option>{/each}
    </select>
    <select bind:value={pick} aria-label="Scelta">
      <option value="">Tutte le scelte</option>
      <option value="auto">Automatico</option>
      <option value="show">Sempre</option>
      <option value="hide">Mai</option>
    </select>
    <span class="muted small">{shown.length} canali</span>
  </div>
  {#if shown.length > 1}
    <div class="bulk">
      <span class="muted small">Ai {shown.length} canali qui sotto:</span>
      <Button size="s" variant="soft" disabled={busy} onclick={() => set(shown.map((c) => c.id), 'show')}>Sempre</Button>
      <Button size="s" variant="soft" disabled={busy} onclick={() => set(shown.map((c) => c.id), 'hide')}>Mai</Button>
      <Button size="s" variant="ghost" disabled={busy} onclick={() => set(shown.map((c) => c.id), 'auto')}>Automatico</Button>
    </div>
  {/if}

  <ul class="list">
    {#each shown.slice(0, 400) as c (c.id)}
      {@const st = stateOf(c)}
      <li>
        <span class="logo">{#if c.logo}<img src={c.logo} alt="" loading="lazy" referrerpolicy="no-referrer" />{/if}</span>
        <span class="main">
          <strong>{c.number != null ? `${c.number} · ` : ''}{c.name}</strong>
          <span class="muted small">{[scopeLabel(c), c.region && scopeOf(c, region) !== 'nazionali' ? c.region : '', KIND_LABEL[c.kind] ?? '', c.streamsOk > 0 ? '' : 'flusso non funzionante', c.closed ? 'chiuso' : ''].filter(Boolean).join(' · ')}</span>
        </span>
        <span class="seg" role="radiogroup" aria-label="Nella guida: {c.name}">
          {#each [['auto', 'Automatico'], ['show', 'Sempre'], ['hide', 'Mai']] as [v, l] (v)}
            <button type="button" role="radio" aria-checked={st === v} class:on={st === v} disabled={busy} onclick={() => set([c.id], v as 'auto' | 'show' | 'hide')}>{l}</button>
          {/each}
        </span>
      </li>
    {/each}
  </ul>
  {#if shown.length > 400}<p class="muted small">I primi 400: cerca per nome o scegli un gruppo per vedere gli altri.</p>{/if}
</section>

<style>
  .box { display: flex; flex-direction: column; gap: 14px; }
  h2 { font-size: 22px; margin: 0; }
  p { margin: 0; }
  .muted { color: var(--muted); }
  .small { font-size: 13px; }
  .err { color: var(--rec-text); }
  .ok { color: var(--green-text, #8fe3b5); }
  .field { display: flex; flex-direction: column; gap: 6px; font-size: 13px; color: var(--muted); max-width: 320px; }
  select, input[type='search'] { min-height: 40px; padding: 0 12px; border-radius: var(--radius-s); border: 1px solid var(--line-strong); background: var(--card); color: var(--text); font: inherit; font-size: 14px; }
  .tools, .bulk { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; }
  .tools input { flex: 1 1 220px; }
  .list { list-style: none; margin: 0; padding: 0; border: 1px solid var(--line); border-radius: var(--radius-l); background: var(--surface); }
  .list li { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 14px; padding: 10px 14px; border-bottom: 1px solid var(--line-soft); }
  .list li:last-child { border-bottom: 0; }
  .logo { flex: 0 0 36px; height: 36px; display: flex; align-items: center; justify-content: center; border-radius: 8px; background: #e9edf3; }
  .logo img { max-width: 32px; max-height: 32px; object-fit: contain; }
  .main { flex: 1 1 240px; min-width: 0; display: flex; flex-direction: column; gap: 2px; }
  .seg { display: inline-flex; border: 1px solid var(--line-strong); border-radius: var(--radius-s); overflow: hidden; }
  .seg button { min-height: 34px; padding: 0 12px; border: 0; background: none; color: var(--muted); font: inherit; font-size: 13px; cursor: pointer; }
  .seg button.on { background: var(--raised); color: var(--text); font-weight: 600; }
  .seg button:disabled { cursor: progress; }
</style>
