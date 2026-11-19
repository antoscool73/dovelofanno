<script lang="ts">
  // Impostazioni › Cartelle: cartelle del NAS nella libreria (Film, Serie...), scansione e copie per il browser.
  import { ask } from '../lib/confirm.svelte';
  import Badge from '../lib/components/Badge.svelte';
  import Button from '../lib/components/Button.svelte';
  import { api, ApiError, type LibraryFolder, type LibraryStatus } from '../lib/api/client';
  import { day, time } from '../lib/format';

  let s = $state<LibraryStatus | null>(null);
  let error = $state('');
  let message = $state('');
  let busy = $state(false);
  let path = $state('');
  let kind = $state('auto');

  async function load() {
    try { s = await api.libraryStatus(); error = ''; }
    catch (e) { error = e instanceof ApiError ? e.message : 'Stato della libreria non letto'; }
  }
  load();

  // durante la scansione si aggiorna da solo
  $effect(() => {
    if (!s?.scanning && !s?.copyStatus) return;
    const t = setInterval(load, 2000);
    return () => clearInterval(t);
  });

  async function act(f: () => Promise<unknown>, ok: string) {
    busy = true;
    error = '';
    message = '';
    try { await f(); message = ok; await load(); }
    catch (e) { error = e instanceof ApiError ? e.message : 'Operazione non riuscita'; }
    finally { busy = false; }
  }

  function add(e: SubmitEvent) {
    e.preventDefault();
    act(async () => { await api.addFolder(path.trim(), kind); path = ''; }, 'Cartella aggiunta: la scansione è partita.');
  }

  async function remove(f: LibraryFolder) {
    if (!(await ask({ title: `Togliere «${f.path}» dalla libreria?`, text: 'I file restano sul disco; le liste e i voti restano.', ok: 'Togli la cartella' }))) return;
    act(() => api.deleteFolder(f.id), 'Cartella tolta.');
  }

  const kinds: Record<string, string> = { auto: 'Decide il nome dei file', films: 'Film', series: 'Serie' };

  // cambiare tipo riabbina tutti i file della cartella: con conferma (20/11/2026, la cartella dei film era diventata
  // «Serie» senza che nessuno lo volesse, forse con la rotella del mouse sul menu, e 55 film erano da sistemare)
  async function changeKind(f: LibraryFolder, sel: HTMLSelectElement) {
    const k = sel.value;
    const ok = await ask({
      title: `Trattare «${f.path}» come «${kinds[k] ?? k}»?`,
      text: `Tutti i ${f.files} file della cartella si riabbinano da capo con TMDB; quelli abbinati a mano restano come sono.`,
      ok: 'Cambia tipo',
      danger: false,
    });
    if (!ok) { sel.value = f.kind; return; }
    act(() => api.updateFolder(f.id, { kind: k }), 'Tipo cambiato: i file si riabbinano.');
  }
</script>

<div class="head">
  <h2>Cartelle della libreria</h2>
  <Button variant="primary" disabled={busy || s?.scanning} onclick={() => act(() => api.scanLibrary(), 'Scansione avviata.')}>{s?.scanning ? 'Scansione in corso…' : 'Scansiona ora'}</Button>
</div>
<p class="muted">I film e le serie che hai già sul NAS: indica le cartelle come le vede il server (per esempio <code>/volume1/video/Film</code> o <code>D:\Film</code>). I nomi dei file vengono riconosciuti come fanno Jellyfin e Kodi: «Titolo (Anno)» per i film, «Serie/Stagione 01/Serie - S01E02» per gli episodi. Le registrazioni entrano da sole.</p>
{#if error}<p class="error" role="alert">{error}</p>{/if}
{#if message}<p class="ok" role="status">{message}</p>{/if}

{#if s}
  <div class="kpis">
    <div class="kpi"><strong>{s.counts.files}</strong><span>file in libreria</span></div>
    <div class="kpi"><strong class="g">{s.counts.matched}</strong><span>riconosciuti</span></div>
    <div class="kpi"><strong class:o={s.counts.unmatched > 0}>{s.counts.unmatched}</strong><span>da sistemare</span></div>
    <div class="kpi"><strong class="m">{s.counts.missing}</strong><span>mancanti dal disco</span></div>
  </div>
  {#if s.scanning}<p class="muted small" role="status">{s.status || 'Scansione in corso…'}</p>{/if}
  {#if s.lastScanAt && s.lastReport}
    <p class="muted small">Ultima scansione {day(s.lastScanAt).toLowerCase()} alle {time(s.lastScanAt)}: {s.lastReport.new} file nuovi, {s.lastReport.missing} mancanti, {s.lastReport.matched} riconosciuti{s.lastReport.error ? ` · ${s.lastReport.error}` : ''}. Si ripete ogni notte alle 5.</p>
  {/if}
  {#if !s.hasTmdbKey}<p class="warn small">Senza la chiave TMDB (Voti dei film) i titoli si riconoscono solo dal nome del file: niente locandine, trame e voti.</p>{/if}
  {#if s.copyStatus}<p class="muted small" role="status">Copia per il browser in corso: {s.copyStatus}.</p>{/if}
  {#if s.counts.copiesWaiting > 0}<p class="muted small">{s.counts.copiesWaiting} file in coda per la copia per il browser.</p>{/if}

  {#if s.folders.length === 0}
    <p class="empty">Nessuna cartella. Aggiungi la prima qui sotto.</p>
  {:else}
    <ul class="folders">
      {#each s.folders as f (f.id)}
        <li>
          <span class="body">
            <span class="fpath">{f.path}</span>
            <span class="muted small">{f.files} file · {kinds[f.kind] ?? f.kind}{f.lastScannedAt ? ` · letta ${day(f.lastScannedAt).toLowerCase()} alle ${time(f.lastScannedAt)}` : ' · non ancora letta'}</span>
            {#if f.lastError}<span class="err small">{f.lastError}</span>{/if}
          </span>
          {#if !f.enabled}<Badge tone="neutral">Sospesa</Badge>{/if}
          <label class="sel"><span class="sr-only">Tipo di cartella</span>
            <select value={f.kind} onchange={(e) => changeKind(f, e.currentTarget as HTMLSelectElement)}>
              <option value="auto">Decide il nome</option><option value="films">Film</option><option value="series">Serie</option>
            </select>
          </label>
          <Button size="s" onclick={() => act(() => api.updateFolder(f.id, { enabled: !f.enabled }), f.enabled ? 'Cartella sospesa.' : 'Cartella riattivata.')}>{f.enabled ? 'Sospendi' : 'Riattiva'}</Button>
          <Button size="s" variant="ghost" onclick={() => remove(f)}>Togli</Button>
        </li>
      {/each}
    </ul>
  {/if}
{/if}

<form class="add" onsubmit={add}>
  <h3>Aggiungi una cartella</h3>
  <div class="row">
    <label class="field u">Percorso sul server<input bind:value={path} placeholder="/volume1/video/Film" required /></label>
    <label class="field n">Contenuto
      <select bind:value={kind}><option value="auto">Misto: decide il nome dei file</option><option value="films">Film</option><option value="series">Serie</option></select>
    </label>
  </div>
  <p class="muted small">Con Docker la cartella va prima montata nel container (per esempio <code>-v /volume1/video:/media:ro</code>) e qui si scrive il percorso interno, <code>/media/Film</code>. La libreria legge soltanto: i file non vengono spostati né rinominati.</p>
  <div><Button variant="primary" type="submit" disabled={busy || path.trim() === ''}>Aggiungi e scansiona</Button></div>
</form>

<style>
  .head { display: flex; flex-wrap: wrap; align-items: baseline; justify-content: space-between; gap: 12px; }
  h2 { font-size: 24px; }
  h3 { font-size: 17px; margin: 0; }
  .muted { color: var(--muted); margin: 0; }
  .small { font-size: 13px; }
  .error, .err { color: var(--rec-text); margin: 0; }
  .ok { color: var(--green-text); margin: 0; }
  .warn { color: var(--orange-text); margin: 0; }
  code { font-size: 13px; background: var(--raised); border-radius: 4px; padding: 1px 5px; }
  .kpis { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 12px; }
  .kpi { background: var(--card); border: 1px solid var(--line-strong); border-radius: 12px; padding: 14px 16px; display: flex; flex-direction: column; }
  .kpi strong { font-size: 28px; }
  .kpi span { font-size: 13px; color: var(--muted); }
  .g { color: var(--green-text); } .o { color: var(--orange-text); } .m { color: var(--muted); }
  .empty { background: var(--surface); border: 1px dashed var(--line-btn); border-radius: 12px; padding: 18px; color: var(--text-2); margin: 0; }
  .folders { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 10px; }
  .folders li { display: flex; flex-wrap: wrap; align-items: center; gap: 10px 14px; background: var(--card); border: 1px solid var(--line-strong); border-radius: 12px; padding: 14px 16px; }
  .body { flex: 1 1 320px; min-width: 0; display: flex; flex-direction: column; gap: 3px; }
  .fpath { font-weight: 600; overflow-wrap: anywhere; }
  .sel select, .field select { min-height: 40px; padding: 0 12px; border-radius: var(--radius-s); border: 1px solid var(--line-btn); background: var(--card); color: var(--text); font-size: 14px; }
  .add { display: flex; flex-direction: column; gap: 12px; background: var(--surface); border: 1px solid var(--line-strong); border-radius: var(--radius-l); padding: 18px; }
  .row { display: flex; flex-wrap: wrap; gap: 12px; }
  .field { display: flex; flex-direction: column; gap: 6px; font-size: 13px; color: var(--muted); }
  .field input { min-height: 42px; padding: 0 12px; border-radius: var(--radius-s); border: 1px solid var(--line-strong); background: var(--card); color: var(--text); font-size: 15px; }
  .u { flex: 3 1 360px; } .n { flex: 1 1 200px; }
  @media (max-width: 720px) { .kpis { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
</style>
