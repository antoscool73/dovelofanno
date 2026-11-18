<script lang="ts">
  // Libreria: liste personali e di famiglia, opere in casa per sezione (film, serie, documentari, programmi,
  // registrazioni) e file da sistemare. Le stelle sono il voto personale del profilo, accanto al voto IMDb.
  import Badge from '../lib/components/Badge.svelte';
  import PosterImg from '../lib/components/PosterImg.svelte';
  import AvailBadges from '../lib/components/AvailBadges.svelte';
  import Button from '../lib/components/Button.svelte';
  import Stars from '../lib/components/Stars.svelte';
  import AbbinaDialog from './AbbinaDialog.svelte';
  import {
    api, ApiError, type LibraryEntry, type LibraryItem, type LibrarySection, type LibrarySort, type ListDetail, type ListSummary,
  } from '../lib/api/client';
  import { clock, day, minutes, time, vote } from '../lib/format';
  import { router, navigate } from '../lib/router.svelte';
  import { session } from '../lib/session.svelte';

  const sections: { id: string; label: string; api?: LibrarySection }[] = [
    { id: 'liste', label: 'Liste' },
    { id: 'film', label: 'Film', api: 'films' },
    { id: 'serie', label: 'Serie', api: 'series' },
    { id: 'documentari', label: 'Documentari', api: 'documentaries' },
    { id: 'programmi', label: 'Programmi', api: 'programmes' },
    { id: 'registrazioni', label: 'Registrazioni', api: 'recordings' },
    { id: 'visti', label: 'Visti e votati', api: 'watched' },
    { id: 'da-sistemare', label: 'Da sistemare', api: 'unmatched' },
  ];
  const current = $derived(sections.find((s) => s.id === router.librarySection) ?? sections[0]!);
  const listId = $derived(router.libraryListId);

  let lists = $state<ListSummary[]>([]);
  let cont = $state<LibraryItem[]>([]);
  let entries = $state<LibraryEntry[] | null>(null);
  // sezione a cui appartiene l'elenco caricato: cambiando scheda non si mostrano mai i titoli della scheda prima
  // (i file da sistemare non hanno opera e nella griglia dei film davano chiavi doppie, che bloccavano la pagina)
  let entriesFor = $state<LibrarySection | null>(null);
  const shown = $derived(entriesFor === current.api ? entries : null);
  let detail = $state<ListDetail | null>(null);
  let sort = $state<LibrarySort>('added');
  let text = $state('');
  let hasTmdb = $state(false);
  let folders = $state(0);
  let unmatched = $state(0);
  let error = $state('');
  let message = $state('');
  let matching = $state<number | null>(null);

  const child = $derived(session.profile?.isChild ?? false);

  async function loadLists() {
    const prof = session.profile;
    if (!prof) return;
    try {
      [lists, cont] = await Promise.all([api.lists(prof.id), api.continueWatching(prof.id)]);
    } catch (e) { error = e instanceof ApiError ? e.message : 'Liste non lette'; }
  }

  async function loadEntries() {
    const s = current.api;
    if (!s) return;
    try {
      const res = await api.library(s, session.profile?.id, sort, text.trim());
      if (current.api !== s) return;   // nel frattempo si è passati a un'altra scheda
      entries = res;
      entriesFor = s;
      error = '';
    }
    catch (e) { error = e instanceof ApiError ? e.message : 'Libreria non letta'; }
  }

  async function loadDetail() {
    const prof = session.profile;
    if (!prof || !listId) { detail = null; return; }
    try { detail = await api.list(prof.id, listId, sort); }
    catch (e) { detail = null; error = e instanceof ApiError ? (e.status === 404 ? 'Lista non trovata.' : e.message) : 'Lista non letta'; }
  }

  async function loadStatus() {
    try {
      const s = await api.libraryStatus();
      hasTmdb = s.hasTmdbKey;
      folders = s.folders.length;
      unmatched = s.counts.unmatched;
    } catch { /* solo per i suggerimenti */ }
  }

  $effect(() => {
    void current.id; void listId; void sort;
    message = '';
    if (current.id === 'liste') { entries = null; loadLists(); loadDetail(); }
    else { detail = null; loadEntries(); }
  });
  loadStatus();

  let searchTimer: ReturnType<typeof setTimeout> | undefined;
  function onSearch() {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(loadEntries, 250);
  }

  async function act(f: () => Promise<unknown>, ok = '') {
    error = '';
    message = '';
    try {
      await f();
      message = ok;
      if (current.id === 'liste') { await loadLists(); await loadDetail(); }
      else await loadEntries();
    } catch (e) { error = e instanceof ApiError ? e.message : 'Operazione non riuscita'; }
  }

  // ---- liste
  let newName = $state('');
  let newFamily = $state<boolean | null>(null);
  function createList(e: SubmitEvent) {
    e.preventDefault();
    const prof = session.profile;
    if (!prof || newFamily === null || !newName.trim()) return;
    const family = newFamily;
    act(async () => {
      const l = await api.createList(prof.id, newName.trim(), family);
      newName = '';
      newFamily = null;
      router.go(`/libreria/liste/${l.id}`);
    }, 'Lista creata.');
  }

  let renaming = $state('');
  let isRenaming = $state(false);
  function rename(e: SubmitEvent) {
    e.preventDefault();
    const prof = session.profile;
    if (!prof || !detail || !renaming.trim()) return;
    const id = detail.list.id;
    act(() => api.updateList(prof.id, id, { name: renaming.trim() }).then(() => { isRenaming = false; }), 'Nome cambiato.');
  }
  function toFamily() {
    const prof = session.profile;
    if (!prof || !detail) return;
    if (!confirm(`Spostare «${detail.list.name}» nelle liste della famiglia? La vedranno e la cambieranno tutti gli adulti.`)) return;
    const id = detail.list.id;
    act(() => api.updateList(prof.id, id, { family: true }), 'Lista spostata in famiglia.');
  }
  function removeList() {
    const prof = session.profile;
    if (!prof || !detail) return;
    if (!confirm(`Cancellare la lista «${detail.list.name}»? I titoli restano nella libreria.`)) return;
    const id = detail.list.id;
    act(() => api.deleteList(prof.id, id).then(() => router.go('/libreria')), 'Lista cancellata.');
  }
  function removeFromList(workId: number) {
    const prof = session.profile;
    if (!prof || !detail) return;
    const id = detail.list.id;
    act(() => api.removeFromList(prof.id, id, workId));
  }

  async function setStars(workId: number, v: number | null) {
    const prof = session.profile;
    if (!prof) return;
    try {
      await api.setStars(prof.id, workId, v);
      if (detail) detail.items = detail.items.map((i) => (i.workId === workId ? { ...i, stars: v } : i));
      if (entries) entries = entries.map((i) => (i.workId === workId ? { ...i, stars: v } : i));
    } catch (e) { error = e instanceof ApiError ? e.message : 'Voto non salvato'; }
  }

  // ---- da sistemare
  function removeItem(e: LibraryEntry, deleteFile: boolean) {
    if (e.itemId == null) return;
    const q = deleteFile
      ? `Cancellare dal disco il file «${e.title}»? Non si può annullare.`
      : `Togliere «${e.title}» dalla libreria? Il file resta sul disco e torna alla prossima scansione se è in una cartella della libreria.`;
    if (!confirm(q)) return;
    const id = e.itemId;
    act(() => api.removeItem(id, deleteFile), deleteFile ? 'File cancellato.' : 'Tolto dalla libreria.');
  }

  const kindLabel: Record<string, { label: string; tone: 'blue' | 'orange' | 'green' | 'neutral' }> = {
    film: { label: 'Film', tone: 'blue' },
    series: { label: 'Serie', tone: 'orange' },
    episode: { label: 'Serie', tone: 'orange' },
    documentary: { label: 'Documentario', tone: 'green' },
    programme: { label: 'Programma', tone: 'neutral' },
  };
  const sorts: { id: LibrarySort; label: string }[] = [
    { id: 'added', label: 'Aggiunti di recente' },
    { id: 'title', label: 'Titolo' },
    { id: 'rating', label: 'Voto IMDb' },
    { id: 'stars', label: 'Le mie stelle' },
  ];
  const mine = $derived(lists.filter((l) => !l.family));
  const family = $derived(lists.filter((l) => l.family));
  const pct = (pos: number, dur: number) => (dur > 0 ? Math.min(100, Math.round((pos / dur) * 100)) : 0);
  const emptyText: Record<string, string> = {
    film: 'Nessun film in casa.', serie: 'Nessuna serie in casa.', documentari: 'Nessun documentario in casa.',
    programmi: 'Nessun programma in casa.', registrazioni: 'Nessuna registrazione finita.', 'da-sistemare': 'Niente da sistemare: tutti i file sono riconosciuti.',
    visti: 'Niente di visto o votato. Si segna dalla Scheda («Già visto», le stelle) o guardando un file fino alla fine.',
  };
</script>

<section class="pad top">
  <h1>Libreria</h1>
  <nav class="pills" aria-label="Sezioni della libreria">
    {#each sections as s (s.id)}
      <a class="pill" class:on={current.id === s.id} aria-current={current.id === s.id ? 'page' : undefined} href={s.id === 'liste' ? '/libreria' : `/libreria/${s.id}`} onclick={navigate}>
        {s.label}{#if s.id === 'da-sistemare' && unmatched > 0}<span class="count">{unmatched}</span>{/if}
      </a>
    {/each}
  </nav>
  {#if error}<p class="error" role="alert">{error}</p>{/if}
  {#if message}<p class="ok" role="status">{message}</p>{/if}
</section>

{#if current.id === 'liste'}
  {#if detail}
    <section class="pad block">
      <div class="panel">
        <div class="phead">
          <div class="grow">
            <a class="back" href="/libreria" onclick={navigate}>← Tutte le liste</a>
            {#if isRenaming}
              <form class="rename" onsubmit={rename}>
                <label class="sr-only" for="nome-lista">Nome della lista</label>
                <input id="nome-lista" bind:value={renaming} maxlength="60" />
                <Button variant="primary" size="s" type="submit">Salva</Button>
                <Button variant="ghost" size="s" onclick={() => (isRenaming = false)}>Annulla</Button>
              </form>
            {:else}
              <h2>{detail.list.name}</h2>
            {/if}
            <div class="muted small">{detail.items.length} {detail.items.length === 1 ? 'titolo' : 'titoli'} · {detail.list.isTonight ? 'fissa: i titoli disponibili compaiono in Stasera' : detail.list.family ? 'lista della famiglia' : `lista personale di ${session.profile?.name}`}</div>
          </div>
          <label class="sel"><span class="sr-only">Ordina</span>
            <select bind:value={sort}>{#each sorts as s (s.id)}<option value={s.id}>Ordina: {s.label.toLowerCase()}</option>{/each}</select>
          </label>
          {#if detail.list.canEdit && !detail.list.isTonight}
            <Button size="s" onclick={() => { renaming = detail?.list.name ?? ''; isRenaming = true; }}>Rinomina</Button>
            {#if !detail.list.family && !child}<Button size="s" onclick={toFamily}>Sposta in famiglia</Button>{/if}
            <Button size="s" variant="ghost" onclick={removeList}>Cancella</Button>
          {/if}
        </div>
        {#if detail.items.length === 0}
          <p class="empty">La lista è vuota. Si aggiungono titoli dalla scheda di un'opera: «Aggiungi a una lista».</p>
        {/if}
        {#each detail.items as it (it.workId)}
          <div class="lrow">
            <a class="mini" href="/opera/{it.workId}" onclick={navigate} aria-label="Apri la scheda di {it.title}">
              <PosterImg src={it.poster} title={it.title} compact />
            </a>
            <div class="lbody">
              <div class="line">
                <a class="ltitle" href="/opera/{it.workId}" onclick={navigate}>{it.title}</a>
                {#if kindLabel[it.kind]}<Badge tone={kindLabel[it.kind]!.tone}>{kindLabel[it.kind]!.label}</Badge>{/if}
                {#if it.rating != null}<span class="muted small">{it.ratingSource} {vote(it.rating)}</span>{/if}
                {#if it.watched}<Badge tone="green">Visto</Badge>{/if}
              </div>
              <div class="muted small">{[it.country, it.year ? String(it.year) : '', it.credits].filter(Boolean).join(' · ')}</div>
              <div class="where small">
                {#if it.playItemId}In casa{#if it.next} · {/if}{/if}
                {#if it.next}In onda {day(it.next.start).toLowerCase()} · {time(it.next.start)} · {it.next.channelName || it.next.guideChannelName}{/if}
                {#if !it.playItemId && !it.next}Non in casa né in guida nei prossimi giorni{/if}
              </div>
            </div>
            {#if it.playItemId}<Button size="s" variant="primary" href="/guarda/{it.playItemId}" onclick={navigate}>▶ Guarda</Button>{/if}
            <Stars value={it.stars} label="Il tuo voto per {it.title}" onchange={(v) => setStars(it.workId, v)} />
            {#if detail.list.canEdit}
              <button type="button" class="x" aria-label="Togli {it.title} dalla lista" onclick={() => removeFromList(it.workId)}>×</button>
            {/if}
          </div>
        {/each}
      </div>
      <p class="muted small">Le stelle sono il tuo voto personale da 1 a 5, uguale per film, serie, documentari e programmi; il voto IMDb resta accanto al titolo.</p>
    </section>
  {:else}
    {#if cont.length > 0}
      <section class="pad block">
        <h2>Continua a guardare</h2>
        <div class="row-scroll">
          {#each cont as c (c.id)}
            <a class="cont" href="/guarda/{c.id}" onclick={navigate}>
              <span class="ctitle">{c.title}</span>
              {#if c.subtitle}<span class="muted small">{c.subtitle}</span>{/if}
              <span class="muted small">Mancano {minutes((c.durationS - c.positionS) / 60)}</span>
              <span class="prog" aria-hidden="true"><span style="width: {pct(c.positionS, c.durationS)}%"></span></span>
            </a>
          {/each}
        </div>
      </section>
    {/if}

    <section class="pad block">
      <div class="shead">
        <div><h2>Le mie liste</h2><p class="muted small">«La mia serata in TV» è fissa: i titoli che ci metti compaiono in Stasera appena sono disponibili e finché non li hai visti.</p></div>
        <Button variant="primary" onclick={() => (newFamily = false)}>+ Nuova lista</Button>
      </div>
      {#if newFamily === false}{@render newForm()}{/if}
      <div class="cards">
        {#each mine as l (l.id)}{@render listCard(l)}{/each}
      </div>
    </section>

    <section class="pad block">
      <div class="shead">
        <div><h2>Liste della famiglia</h2><p class="muted small">Le vede ogni profilo; gli adulti le modificano, i bambini vedono solo i titoli adatti.</p></div>
        {#if !child}<Button onclick={() => (newFamily = true)}>+ Nuova lista di famiglia</Button>{/if}
      </div>
      {#if newFamily === true}{@render newForm()}{/if}
      {#if family.length === 0}
        <p class="empty">Nessuna lista della famiglia. Esempi: «Natale in famiglia», «Serate con i bambini».</p>
      {:else}
        <div class="cards">{#each family as l (l.id)}{@render listCard(l)}{/each}</div>
      {/if}
    </section>
  {/if}
{:else}
  <section class="pad block">
    <div class="tools">
      <label class="search"><span class="sr-only">Cerca nella libreria</span>
        <input type="search" placeholder="Cerca un titolo" bind:value={text} oninput={onSearch} />
      </label>
      {#if current.id !== 'da-sistemare'}
        <label class="sel"><span class="sr-only">Ordina</span>
          <select bind:value={sort}>{#each sorts as s (s.id)}<option value={s.id}>Ordina: {s.label.toLowerCase()}</option>{/each}</select>
        </label>
      {/if}
    </div>

    {#if shown === null}
      <p class="muted" role="status">Un momento…</p>
    {:else if shown.length === 0}
      <div class="empty">
        <p>{text ? 'Nessun titolo con queste parole.' : emptyText[current.id]}</p>
        {#if folders === 0 && current.id !== 'registrazioni' && current.id !== 'visti'}
          <p class="muted small">Per i file già sul NAS aggiungi le cartelle (Film, Serie…) in <a href="/impostazioni" onclick={navigate}>Impostazioni › Cartelle</a>. Le registrazioni entrano da sole.</p>
        {/if}
      </div>
    {:else if current.id === 'da-sistemare'}
      <p class="muted small">File che non sono stati riconosciuti{hasTmdb ? ' su TMDB' : ''}: indica tu di che titolo si tratta.</p>
      <ul class="fix">
        {#each shown as e (e.itemId ?? e.title)}
          <li>
            <span class="lbody"><strong>{e.title}</strong>{#if e.year} <span class="muted">({e.year})</span>{/if}
              <span class="muted small">{e.durationS ? clock(e.durationS) : 'durata sconosciuta'}{e.recorded ? ' · registrazione' : ''} · aggiunto {day(e.addedAt).toLowerCase()}</span></span>
            <Button size="s" variant="primary" onclick={() => (matching = e.itemId)}>Abbina…</Button>
            {#if e.playItemId}<Button size="s" href="/guarda/{e.playItemId}" onclick={navigate}>▶ Guarda</Button>{/if}
            <Button size="s" variant="ghost" onclick={() => removeItem(e, false)}>Togli</Button>
            <Button size="s" variant="ghost" onclick={() => removeItem(e, true)}>Elimina file</Button>
          </li>
        {/each}
      </ul>
    {:else}
      <div class="grid">
        {#each shown as e (e.workId ?? `f${e.itemId}`)}
          <article class="card">
            <a class="poster" href="/opera/{e.workId}" onclick={navigate}>
              <PosterImg src={e.poster} title={e.title} />
              <AvailBadges a={{ tv: "", home: true, mine: [], pay: [] }} />
              {#if e.positionS > 0 && e.durationS > 0}<span class="prog over" aria-hidden="true"><span style="width: {pct(e.positionS, e.durationS)}%"></span></span>{/if}
            </a>
            <div class="cbody">
              <a class="ctitle" href="/opera/{e.workId}" onclick={navigate}>{e.title}</a>
              <div class="muted small">
                {[e.year ? String(e.year) : '', e.episodes > 0 ? `${e.episodes} ${e.episodes === 1 ? 'episodio' : 'episodi'}` : e.files > 1 ? `${e.files} file` : '', e.rating != null ? `${e.ratingSource} ${vote(e.rating)}` : ''].filter(Boolean).join(' · ')}
              </div>
              <div class="cfoot">
                {#if e.playItemId}
                  <a class="play" href="/guarda/{e.playItemId}" onclick={navigate} aria-label="{e.positionS > 0 ? 'Riprendi' : 'Guarda'} {e.title}">▶ {e.positionS > 0 ? 'Riprendi' : e.episodes > 0 ? 'Guarda' : 'Guarda'}</a>
                {/if}
                {#if e.watched}<Badge tone="green">Visto</Badge>{:else if current.id === 'visti'}<Badge tone="neutral">Votato</Badge>{/if}
                {#if e.recorded && current.id !== 'registrazioni'}<Badge tone="red">Registrato</Badge>{/if}
              </div>
              <Stars size="s" value={e.stars} label="Il tuo voto per {e.title}" onchange={(v) => e.workId != null && setStars(e.workId, v)} />
            </div>
          </article>
        {/each}
      </div>
    {/if}
  </section>
{/if}

{#if matching !== null}
  <AbbinaDialog itemId={matching} {hasTmdb} onclose={() => (matching = null)} ondone={() => { matching = null; message = 'Abbinato.'; loadEntries(); loadStatus(); }} />
{/if}

{#snippet listCard(l: ListSummary)}
  <a class="lcard" class:tonight={l.isTonight} href="/libreria/liste/{l.id}" onclick={navigate}>
    <span class="mosaic" aria-hidden="true">
      {#each [0, 1, 2] as i (i)}
        <span>{#if l.posters[i]}<img src={l.posters[i]} alt="" referrerpolicy="no-referrer" loading="lazy" />{/if}</span>
      {/each}
    </span>
    <span class="lname">{l.name}</span>
    <span class="muted small">{l.count} {l.count === 1 ? 'titolo' : 'titoli'} · {l.isTonight ? 'Fissa' : l.family ? 'Famiglia' : 'Personale'}</span>
  </a>
{/snippet}

{#snippet newForm()}
  <form class="newlist" onsubmit={createList}>
    <label class="field grow">{newFamily ? 'Nome della lista di famiglia' : 'Nome della lista'}
      <input bind:value={newName} maxlength="60" placeholder={newFamily ? 'Natale in famiglia' : 'Da rivedere'} />
    </label>
    <Button variant="primary" type="submit" disabled={!newName.trim()}>Crea</Button>
    <Button variant="ghost" onclick={() => { newFamily = null; newName = ''; }}>Annulla</Button>
  </form>
{/snippet}

<style>
  .pad { padding-left: var(--gutter); padding-right: var(--gutter); max-width: 1440px; }
  .top { padding-top: 28px; display: flex; flex-direction: column; gap: 16px; }
  .block { padding-top: 28px; display: flex; flex-direction: column; gap: 14px; }
  .block:last-of-type { padding-bottom: 64px; }
  h1 { font-size: 32px; }
  h2 { font-size: 22px; }
  .pills { display: flex; flex-wrap: wrap; gap: 6px; }
  .pill { display: inline-flex; align-items: center; gap: 8px; min-height: 40px; padding: 0 16px; border-radius: 999px; border: 1px solid var(--line-strong); color: var(--text-2); font-size: 14px; text-decoration: none; }
  .pill:hover { color: var(--text); background: var(--card); }
  .pill.on { background: var(--text); color: var(--bg); border-color: var(--text); font-weight: 600; }
  .count { background: var(--orange); color: var(--bg); border-radius: 999px; padding: 0 7px; font-size: 12px; font-weight: 700; }
  .muted { color: var(--muted); margin: 0; }
  .small { font-size: 13px; }
  .error { color: var(--rec-text); margin: 0; }
  .ok { color: var(--green-text); margin: 0; }
  .grow { flex: 1 1 0; min-width: 0; }
  .shead { display: flex; flex-wrap: wrap; align-items: flex-end; justify-content: space-between; gap: 12px; }
  .shead p { margin-top: 4px; }
  .empty { background: var(--surface); border: 1px dashed var(--line-btn); border-radius: 12px; padding: 18px; color: var(--text-2); margin: 0; display: flex; flex-direction: column; gap: 6px; }
  .empty p { margin: 0; }
  .cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 16px; }
  .lcard { display: flex; flex-direction: column; gap: 10px; text-decoration: none; color: var(--text); background: var(--card); border: 1px solid var(--line-strong); border-radius: 14px; padding: 14px; }
  .lcard:hover { border-color: var(--line-btn); color: var(--text); }
  .lcard.tonight { background: #1a2033; border: 2px solid #ffd36e; }
  .mosaic { display: flex; gap: 6px; height: 96px; }
  .mosaic span { flex: 1 1 0; border-radius: 6px; background: var(--raised); overflow: hidden; }
  .mosaic img { width: 100%; height: 100%; object-fit: cover; display: block; }
  .lname { font-size: 17px; font-weight: 600; }
  .newlist { display: flex; flex-wrap: wrap; align-items: flex-end; gap: 10px; background: var(--surface); border: 1px solid var(--line-strong); border-radius: 12px; padding: 14px; }
  .field { display: flex; flex-direction: column; gap: 6px; font-size: 13px; color: var(--muted); min-width: 220px; }
  .field input, .rename input, .search input { min-height: 42px; padding: 0 12px; border-radius: var(--radius-s); border: 1px solid var(--line-strong); background: var(--card); color: var(--text); font-size: 15px; }
  .row-scroll { display: flex; gap: 14px; overflow-x: auto; padding-bottom: 6px; }
  .cont { flex: 0 0 260px; display: flex; flex-direction: column; gap: 4px; text-decoration: none; color: var(--text); background: var(--card); border: 1px solid var(--line-strong); border-radius: 12px; padding: 14px; }
  .ctitle { font-weight: 600; color: var(--text); text-decoration: none; overflow-wrap: anywhere; }
  .prog { display: block; height: 4px; border-radius: 2px; background: rgba(255, 255, 255, 0.18); overflow: hidden; margin-top: 6px; }
  .prog span { display: block; height: 100%; background: var(--blue); }
  .prog.over { position: absolute; left: 8px; right: 38px; bottom: 15px; margin: 0; }  /* a destra c'è l'icona "in casa" */
  .panel { background: var(--surface); border: 1px solid var(--line-strong); border-radius: 16px; overflow: hidden; }
  .phead { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; padding: 18px 20px; border-bottom: 1px solid var(--line); }
  .phead h2 { font-size: 24px; }
  .back { font-size: 14px; text-decoration: none; }
  .rename { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; margin: 4px 0; }
  .sel select { min-height: 40px; padding: 0 12px; border-radius: var(--radius-s); border: 1px solid var(--line-btn); background: var(--card); color: var(--text); font-size: 14px; }
  .lrow { display: flex; flex-wrap: wrap; align-items: center; gap: 12px 18px; padding: 14px 20px; border-bottom: 1px solid var(--line-soft); }
  .lrow:last-child { border-bottom: 0; }
  .mini { flex: 0 0 56px; aspect-ratio: 2 / 3; border-radius: 6px; background: var(--raised); overflow: hidden; position: relative; }
  .mini img { width: 100%; height: 100%; object-fit: cover; display: block; }
  .lbody { flex: 1 1 300px; min-width: 0; display: flex; flex-direction: column; gap: 4px; }
  .line { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
  .ltitle { font-size: 16px; font-weight: 600; color: var(--text); text-decoration: none; }
  .where { color: var(--text-2); }
  .x { width: 40px; height: 40px; border-radius: 8px; border: 0; background: none; color: var(--muted); font-size: 22px; cursor: pointer; }
  .x:hover { color: var(--text); background: var(--raised); }
  .tools { display: flex; flex-wrap: wrap; gap: 10px; }
  .search { flex: 1 1 260px; max-width: 420px; display: flex; }
  .search input { flex: 1 1 0; }
  .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(170px, 1fr)); gap: 20px 16px; }
  .card { display: flex; flex-direction: column; gap: 8px; min-width: 0; }
  .poster { position: relative; aspect-ratio: 2 / 3; border-radius: 10px; background: #24304a; overflow: hidden; display: flex; align-items: flex-end; padding: 12px; text-decoration: none; }
  .cbody { display: flex; flex-direction: column; gap: 4px; }
  .cfoot { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
  .play { display: inline-flex; align-items: center; min-height: 36px; padding: 0 12px; border-radius: 8px; background: var(--green-soft); color: var(--green-text); font-weight: 600; font-size: 14px; text-decoration: none; }
  .play:hover { color: var(--text); }
  .fix { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 10px; }
  .fix li { display: flex; flex-wrap: wrap; align-items: center; gap: 10px 12px; background: var(--card); border: 1px solid var(--line-strong); border-radius: 12px; padding: 14px 16px; }
  .fix .lbody { overflow-wrap: anywhere; }
  @media (max-width: 720px) { .grid { grid-template-columns: repeat(2, minmax(0, 1fr)); } .cards { grid-template-columns: minmax(0, 1fr); } }
</style>
