<script lang="ts">
  // Libreria (24/11/2026, Antonio): solo quello che c'è sul server e quello che ho visto o votato, a sezioni che si
  // chiudono come in La mia lista: Continua a guardare, Film, Serie, Documentari, Programmi, Registrazioni, Visti o
  // votati, Da sistemare. Ogni sezione è una fila di locandine. Niente liste qui (si gestiscono da La mia lista e da
  // Preferenze); resta solo il dettaglio di una lista a mano (/libreria/liste/N), dove porta la pennina della home.
  // Le stelle sono il voto personale del profilo, accanto al voto IMDb.
  import { ask } from '../lib/confirm.svelte';
  import Badge from '../lib/components/Badge.svelte';
  import SeenMark from '../lib/components/SeenMark.svelte';
  import PosterImg from '../lib/components/PosterImg.svelte';
  import AvailBadges from '../lib/components/AvailBadges.svelte';
  import Section from '../lib/components/Section.svelte';
  import { sectionsCommand } from '../lib/sections.svelte';
  import { dragScroll } from '../lib/dragScroll';
  import { availOf } from '../lib/avail';
  import { platforms } from '../lib/platforms.svelte';
  import Button from '../lib/components/Button.svelte';
  import Stars from '../lib/components/Stars.svelte';
  import AbbinaDialog from './AbbinaDialog.svelte';
  import {
    api, ApiError, type LibraryEntry, type LibraryItem, type LibrarySection, type LibrarySort, type ListDetail, type ListEntry,
  } from '../lib/api/client';
  import { clock, day, minutes, time, vote } from '../lib/format';
  import { router, navigate } from '../lib/router.svelte';
  import { session } from '../lib/session.svelte';

  const sections: { id: string; label: string; api: LibrarySection }[] = [
    { id: 'film', label: 'Film', api: 'films' },
    { id: 'serie', label: 'Serie', api: 'series' },
    { id: 'documentari', label: 'Documentari', api: 'documentaries' },
    { id: 'programmi', label: 'Programmi', api: 'programmes' },
    { id: 'registrazioni', label: 'Registrazioni', api: 'recordings' },
    // «o», non «e»: basta averlo visto oppure votato (26/11/2026, Antonio)
    { id: 'visti', label: 'Visti o votati', api: 'watched' },
    { id: 'da-sistemare', label: 'Da sistemare', api: 'unmatched' },
  ];
  const listId = $derived(router.libraryListId);

  let cont = $state<LibraryItem[]>([]);
  // i titoli di ogni sezione; null finché non sono arrivati
  let bySection = $state<Record<string, LibraryEntry[]> | null>(null);
  let detail = $state<ListDetail | null>(null);
  let sort = $state<LibrarySort>('added');
  let text = $state('');
  let hasTmdb = $state(false);
  let folders = $state(0);
  let error = $state('');
  let message = $state('');
  let matching = $state<number | null>(null);

  const child = $derived(session.profile?.isChild ?? false);

  // icone di disponibilità nelle righe delle liste: servono gli abbonamenti del profilo
  let mySubs = $state<string[]>([]);
  $effect(() => {
    const p = session.profile;
    if (p) api.profileSubscriptions(p.id).then((s) => (mySubs = s.services)).catch(() => {});
  });
  const listAvail = (it: ListEntry) =>
    availOf(it, mySubs, it.next, it.next ? `${day(it.next.start).toLowerCase()} ${time(it.next.start)}, ${it.next.channelName || it.next.guideChannelName}` : '');

  // liste a mano (personali e della famiglia, mai le dinamiche: 24/11/2026, Antonio), ognuna una sezione
  let lists = $state<ListDetail[]>([]);

  async function loadCont() {
    const prof = session.profile;
    if (!prof) return;
    try { cont = await api.continueWatching(prof.id); } catch { /* la sezione resta vuota */ }
  }

  async function loadLists() {
    const prof = session.profile;
    if (!prof) { lists = []; return; }
    try {
      const all = (await api.lists(prof.id)).filter((l) => !l.query);
      lists = await Promise.all(all.map((l) => api.list(prof.id, l.id, sort)));
    } catch (e) { error = e instanceof ApiError ? e.message : 'Liste non lette'; }
  }

  // tutte le sezioni insieme; una ricerca più vecchia che arriva tardi non sovrascrive la più nuova
  let loadN = 0;
  async function loadEntries() {
    const n = ++loadN;
    try {
      const res = await Promise.all(sections.map((s) => api.library(s.api, session.profile?.id, sort, text.trim())));
      if (n !== loadN) return;
      bySection = Object.fromEntries(sections.map((s, i) => [s.id, res[i]!]));
      error = '';
    }
    catch (e) { error = e instanceof ApiError ? e.message : 'Libreria non letta'; }
  }

  async function loadDetail() {
    const prof = session.profile;
    if (!prof || !listId) { detail = null; return; }
    try {
      const d = await api.list(prof.id, listId, sort);
      if (d.list.query) { router.go(`/cerca#list=${d.list.id}`); return; }   // lista dinamica: la sua pagina è la ricerca
      detail = d;
    }
    catch (e) { detail = null; error = e instanceof ApiError ? (e.status === 404 ? 'Lista non trovata.' : e.message) : 'Lista non letta'; }
  }

  async function loadStatus() {
    try {
      const s = await api.libraryStatus();
      hasTmdb = s.hasTmdbKey;
      folders = s.folders.length;
    } catch { /* solo per i suggerimenti */ }
  }

  $effect(() => {
    void listId; void sort;
    message = '';
    if (listId) loadDetail();
    else { detail = null; loadCont(); loadEntries(); loadLists(); }
  });
  loadStatus();

  // /libreria/film, /libreria/da-sistemare...: si apre la pagina sulla sezione (i vecchi indirizzi delle schede)
  let scrolledTo = '';
  $effect(() => {
    const s = router.librarySection;
    if (!bySection || listId || s === 'liste' || s === scrolledTo) return;
    scrolledTo = s;
    setTimeout(() => document.getElementById('lib-' + s)?.scrollIntoView({ behavior: 'smooth', block: 'start' }), 50);
  });

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
      if (listId) await loadDetail();
      else await loadEntries();
    } catch (e) { error = e instanceof ApiError ? e.message : 'Operazione non riuscita'; }
  }

  // ---- dettaglio di una lista
  let renaming = $state('');
  let isRenaming = $state(false);
  function rename(e: SubmitEvent) {
    e.preventDefault();
    const prof = session.profile;
    if (!prof || !detail || !renaming.trim()) return;
    const id = detail.list.id;
    act(() => api.updateList(prof.id, id, { name: renaming.trim() }).then(() => { isRenaming = false; }), 'Nome cambiato.');
  }
  async function toFamily() {
    const prof = session.profile;
    if (!prof || !detail) return;
    if (!(await ask({ title: `Spostare «${detail.list.name}» nelle liste della famiglia?`, text: 'La vedranno e la cambieranno tutti gli adulti. Non si torna indietro.', ok: 'Sposta in famiglia' }))) return;
    const id = detail.list.id;
    act(() => api.updateList(prof.id, id, { family: true }), 'Lista spostata in famiglia.');
  }
  async function removeList() {
    const prof = session.profile;
    if (!prof || !detail) return;
    if (!(await ask({ title: `Cancellare la lista «${detail.list.name}»?`, text: 'I titoli restano nella libreria.', ok: 'Cancella la lista' }))) return;
    const id = detail.list.id;
    act(() => api.deleteList(prof.id, id).then(() => router.go('/libreria')), 'Lista cancellata.');
  }
  // togliere un titolo da una lista chiede conferma, con il bidone (26/11/2026, Antonio)
  async function removeFromList(workId: number, title: string) {
    const prof = session.profile;
    if (!prof || !detail) return;
    if (!(await ask({ title: `Togliere «${title}» da «${detail.list.name}»?`, text: 'Il titolo resta nella libreria e nelle altre liste.', ok: 'Togli dalla lista' }))) return;
    const id = detail.list.id;
    act(() => api.removeFromList(prof.id, id, workId));
  }

  async function setStars(workId: number, v: number | null) {
    const prof = session.profile;
    if (!prof) return;
    try {
      await api.setStars(prof.id, workId, v);
      if (detail) detail.items = detail.items.map((i) => (i.workId === workId ? { ...i, stars: v } : i));
      if (bySection) bySection = Object.fromEntries(Object.entries(bySection).map(([k, l]) => [k, l.map((i) => (i.workId === workId ? { ...i, stars: v } : i))]));
      lists = lists.map((d) => ({ ...d, items: d.items.map((i) => (i.workId === workId ? { ...i, stars: v } : i)) }));
    } catch (e) { error = e instanceof ApiError ? e.message : 'Voto non salvato'; }
  }

  // ---- da sistemare
  async function removeItem(e: LibraryEntry, deleteFile: boolean) {
    if (e.itemId == null) return;
    const q = deleteFile
      ? 'Il file viene cancellato dal disco. Non si può annullare.'
      : 'Il file resta sul disco e torna alla prossima scansione se è in una cartella della libreria.';
    if (!(await ask({ title: deleteFile ? `Cancellare il file «${e.title}»?` : `Togliere «${e.title}» dalla libreria?`, text: q, ok: deleteFile ? 'Cancella il file' : 'Togli' }))) return;
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
  const pct = (pos: number, dur: number) => (dur > 0 ? Math.min(100, Math.round((pos / dur) * 100)) : 0);
  const asides: Record<string, string> = {
    film: 'sul server', serie: 'sul server', documentari: 'sul server', programmi: 'sul server', registrazioni: 'registrate da DoveLoFanno',
    visti: 'anche visti in TV o segnati dalla scheda', 'da-sistemare': 'file non riconosciuti: indica tu che titolo sono',
  };
  const count = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;
  // la ricerca filtra anche le liste (sul server la filtrano già le sezioni)
  const tkey = (s: string) => s.toLocaleLowerCase('it').normalize('NFD').replace(/[̀-ͯ]/g, '');
  const listItems = (d: ListDetail) => (text.trim().length === 0 ? d.items : d.items.filter((i) => tkey(i.title).includes(tkey(text.trim()))));
  const nothing = $derived(!!bySection && cont.length === 0 && lists.every((d) => d.items.length === 0) && sections.every((s) => (bySection![s.id] ?? []).length === 0));
</script>


<section class="pad top">
  <h1>Libreria</h1>
  {#if error}<p class="error" role="alert">{error}</p>{/if}
  {#if message}<p class="ok" role="status">{message}</p>{/if}
</section>

{#if listId}
  <section class="pad block">
    {#if detail}
      <div class="panel">
        <div class="phead">
          <div class="grow">
            <a class="back" href="/libreria" onclick={navigate}>← Libreria</a>
            {#if isRenaming}
              <form class="rename" onsubmit={rename}>
                <label class="sr-only" for="nome-lista">Nome della lista</label>
                <input id="nome-lista" bind:value={renaming} maxlength="28" />
                <Button variant="primary" size="s" type="submit">Salva</Button>
                <Button variant="ghost" size="s" onclick={() => (isRenaming = false)}>Annulla</Button>
              </form>
            {:else}
              <h2>{detail.list.name}</h2>
            {/if}
            <div class="muted small">{detail.items.length} {detail.items.length === 1 ? 'titolo' : 'titoli'} · {detail.list.isTonight ? 'fissa: i titoli disponibili compaiono in La mia lista' : detail.list.family ? 'lista della famiglia' : `lista personale di ${session.profile?.name}`}</div>
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
          {@const av = listAvail(it)}
          <div class="lrow">
            <a class="mini" href="/opera/{it.workId}" onclick={navigate} aria-label="Apri la scheda di {it.title}">
              <PosterImg src={it.poster || it.next?.poster || it.next?.icon || ''} title={it.title} compact />
            </a>
            <div class="lbody">
              <div class="line">
                <a class="ltitle" href="/opera/{it.workId}" onclick={navigate}>{it.title}</a>
                {#if kindLabel[it.kind]}<Badge tone={kindLabel[it.kind]!.tone}>{kindLabel[it.kind]!.label}</Badge>{/if}
                {#if it.rating != null}<span class="muted small">{it.ratingSource} {vote(it.rating)}</span>{:else if it.kind !== 'programme'}<span class="muted small" title="Voto non ancora disponibile">N/A</span>{/if}
                {#if it.watched}<SeenMark />{/if}
              </div>
              <div class="muted small">{[it.country, it.year ? String(it.year) : '', it.credits].filter(Boolean).join(' · ')}</div>
              <div class="where small">
                <AvailBadges a={av} size={20} inline />
                <span>{[
                  av.home ? 'In casa' : '',
                  av.tv ? `In TV ${av.tv}` : '',
                  av.mine.length > 0 ? `Su ${av.mine.map((s) => platforms.of(s).short).join(', ')}` : '',
                  av.pay.length > 0 ? `A pagamento: ${av.pay.map((s) => platforms.of(s).short).join(', ')}` : '',
                ].filter(Boolean).join(' · ') || 'Per ora non si vede: né in casa, né in TV nei prossimi giorni, né in streaming'}</span>
              </div>
            </div>
            {#if it.playItemId}<Button size="s" variant="primary" href="/guarda/{it.playItemId}" onclick={navigate}>▶ Guarda</Button>{/if}
            <Stars value={it.stars} label="Il tuo voto per {it.title}" onchange={(v) => setStars(it.workId, v)} />
            {#if detail.list.canEdit}
              <button type="button" class="x" aria-label="Togli {it.title} dalla lista" title="Togli dalla lista" onclick={() => removeFromList(it.workId, it.title)}><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 6h18M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6M10 11v6M14 11v6" /></svg></button>
            {/if}
          </div>
        {/each}
      </div>
      <p class="muted small">Le stelle sono il tuo voto personale da 1 a 5, uguale per film, serie, documentari e programmi; il voto IMDb resta accanto al titolo.</p>
    {:else if !error}
      <p class="muted" role="status">Un momento…</p>
    {/if}
  </section>
{:else}
  <!-- cerca, ordina, apri e chiudi tutte: ferma sotto l'intestazione come in La mia lista -->
  <div class="pad toprow">
    <div class="tools">
      <label class="search"><span class="sr-only">Cerca nella libreria</span>
        <input type="search" placeholder="Cerca un titolo" bind:value={text} oninput={onSearch} />
      </label>
      <label class="sel"><span class="sr-only">Ordina</span>
        <select bind:value={sort}>{#each sorts as s (s.id)}<option value={s.id}>Ordina: {s.label.toLowerCase()}</option>{/each}</select>
      </label>
    </div>
    <div class="allbtns">
      <button type="button" class="allbtn" onclick={() => sectionsCommand.all(false)} title="Apri tutte le sezioni">
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M7 15l5 5 5-5" /><path d="M7 9l5-5 5 5" /></svg>Apri tutte</button>
      <button type="button" class="allbtn" onclick={() => sectionsCommand.all(true)} title="Chiudi tutte le sezioni">
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M7 20l5-5 5 5" /><path d="M7 4l5 5 5-5" /></svg>Chiudi tutte</button>
    </div>
  </div>

  <div class="pad sections">
    {#if bySection === null}
      <p class="muted" role="status">Un momento…</p>
    {:else if nothing}
      <div class="empty">
        <p>{text ? 'Nessun titolo con queste parole.' : 'La libreria è vuota.'}</p>
        {#if folders === 0 && !text}
          <p class="muted small">Per i file già sul NAS aggiungi le cartelle (Film, Serie…) in <a href="/impostazioni" onclick={navigate}>Impostazioni › Cartelle</a>. Le registrazioni entrano da sole.</p>
        {/if}
      </div>
    {/if}

    {#if cont.length > 0 && !text}
      <Section key="lib-continua" title="Continua a guardare">
        <ul class="row" use:dragScroll>
          {#each cont as c (c.id)}<li>{@render contCard(c)}</li>{/each}
        </ul>
      </Section>
    {/if}

    {#if bySection}
      {#each sections as s (s.id)}
        {@const list = bySection[s.id] ?? []}
        {#if list.length > 0}
          <Section key={'lib-' + s.id} id={'lib-' + s.id} title={s.label}>
            {#snippet aside()}{s.id === 'visti' ? count(list.length, 'titolo', 'titoli') : s.id === 'da-sistemare' ? count(list.length, 'file', 'file') : count(list.length, 'titolo', 'titoli')} · {asides[s.id]}{/snippet}
            {#if s.id === 'da-sistemare'}
              <ul class="fix">
                {#each list as e (e.itemId ?? e.title)}
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
              <ul class="row" use:dragScroll>
                {#each list as e (e.workId ?? `f${e.itemId}`)}<li>{@render entryCard(e, s.id)}</li>{/each}
              </ul>
            {/if}
          </Section>
        {/if}
      {/each}
    {/if}

    <!-- le liste a mano, personali e della famiglia (24/11/2026, Antonio: «le mie liste, ma sempre statiche») -->
    {#each lists as d (d.list.id)}
      {@const items = listItems(d)}
      {#if items.length > 0}
        <Section key={'lib-lista-' + d.list.id} title={d.list.name}>
          {#snippet after()}<a class="edit" href="/libreria/liste/{d.list.id}" onclick={navigate} aria-label="Modifica la lista {d.list.name}" title="Modifica la lista"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 20h9" /><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4z" /></svg></a>{/snippet}
          {#snippet aside()}{count(items.length, 'titolo', 'titoli')} · {d.list.isTonight ? 'la lista fissa' : d.list.family ? 'lista della famiglia' : 'lista personale'}{/snippet}
          <ul class="row" use:dragScroll>
            {#each items as it (it.workId)}<li>{@render listCard(it)}</li>{/each}
          </ul>
        </Section>
      {/if}
    {/each}
  </div>
{/if}

{#if matching !== null}
  <AbbinaDialog itemId={matching} {hasTmdb} onclose={() => (matching = null)} ondone={() => { matching = null; message = 'Abbinato.'; loadEntries(); loadStatus(); }} />
{/if}

<!-- una locandina della libreria: righe fisse (titolo su una riga con «…», dati, stelle, pulsante, segni), così le
     schede affiancate restano allineate -->
{#snippet entryCard(e: LibraryEntry, sec: string)}
  {@const meta = [e.year ? String(e.year) : '', e.episodes > 0 ? `${e.episodes} ${e.episodes === 1 ? 'episodio' : 'episodi'}` : e.files > 1 ? `${e.files} file` : '', e.rating != null ? `${e.ratingSource} ${vote(e.rating)}` : e.kind !== 'programme' ? 'N/A' : ''].filter(Boolean).join(' · ')}
  <article class="card">
    <a class="poster" href="/opera/{e.workId}" onclick={navigate}>
      <PosterImg src={e.poster} title={e.title} />
      {#if e.playItemId}<AvailBadges a={{ tv: '', home: true, mine: [], pay: [] }} />{/if}
      {#if e.positionS > 0 && e.durationS > 0}<span class="prog over" aria-hidden="true"><span style="width: {pct(e.positionS, e.durationS)}%"></span></span>{/if}
    </a>
    <div class="cbody">
      <a class="ctitle one" href="/opera/{e.workId}" onclick={navigate} title={e.title}>{e.title}</a>
      <div class="muted small one" title={meta}>{meta || ' '}</div>
      <Stars size="s" value={e.stars} label="Il tuo voto per {e.title}" onchange={(v) => e.workId != null && setStars(e.workId, v)} />
      <div class="cacts">
        {#if e.playItemId}
          <a class="play" href="/guarda/{e.playItemId}" onclick={navigate} aria-label="{e.positionS > 0 ? 'Riprendi' : 'Guarda'} {e.title}">▶ {e.positionS > 0 ? 'Riprendi' : 'Guarda'}</a>
        {/if}
      </div>
      <div class="cfoot">
        <!-- copia per il browser (24/11/2026, Antonio: «come lo riconosco?»): senza segno si guarda subito -->
        {#if e.playItemId && e.copyState === 'needed'}<span title="Il browser non lo legge così: «Guarda» propone di prepararlo"><Badge tone="neutral">Da preparare</Badge></span>
        {:else if e.playItemId && (e.copyState === 'queued' || e.copyState === 'working')}<span title="Il server sta preparando la copia per il browser"><Badge tone="blue">In preparazione</Badge></span>
        {:else if e.playItemId && e.copyState === 'failed'}<span title="La copia per il browser non è riuscita"><Badge tone="orange">Non preparato</Badge></span>{/if}
        {#if e.watched}<SeenMark />{:else if sec === 'visti'}<Badge tone="neutral">Votato</Badge>{/if}
        {#if e.recorded && sec !== 'registrazioni'}<Badge tone="red">Registrato</Badge>{/if}
      </div>
    </div>
  </article>
{/snippet}

<!-- un file iniziato (24/11/2026): la locandina dell'opera o della serie con la barra di quanto è visto; riprende -->
{#snippet contCard(c: LibraryItem)}
  <article class="card">
    <a class="poster" href="/guarda/{c.id}" onclick={navigate} aria-label="Riprendi {c.title}">
      <PosterImg src={c.poster ?? ''} title={c.title} />
      <span class="prog over wide" role="img" aria-label="Visto al {pct(c.positionS, c.durationS)}%"><span style="width: {pct(c.positionS, c.durationS)}%"></span></span>
    </a>
    <div class="cbody">
      <a class="ctitle one" href="/guarda/{c.id}" onclick={navigate} title={c.title}>{c.title}</a>
      <div class="muted small one" title={c.subtitle}>{c.subtitle || ' '}</div>
      <div class="muted small one">Mancano {minutes(Math.max(0, c.durationS - c.positionS) / 60)}</div>
      <div class="cacts"><a class="play" href="/guarda/{c.id}" onclick={navigate} aria-label="Riprendi {c.title}">▶ Riprendi</a></div>
    </div>
  </article>
{/snippet}

<!-- un titolo di una lista a mano: stessa scheda, con dove si vede (in casa, in TV, negli abbonamenti) -->
{#snippet listCard(it: ListEntry)}
  {@const av = listAvail(it)}
  {@const meta = [it.year ? String(it.year) : '', it.rating != null ? `${it.ratingSource} ${vote(it.rating)}` : it.kind !== 'programme' ? 'N/A' : ''].filter(Boolean).join(' · ')}
  <article class="card">
    <a class="poster" href="/opera/{it.workId}" onclick={navigate}>
      <PosterImg src={it.poster || it.next?.poster || it.next?.icon || ''} title={it.title} />
      <AvailBadges a={av} />
    </a>
    <div class="cbody">
      <a class="ctitle one" href="/opera/{it.workId}" onclick={navigate} title={it.title}>{it.title}</a>
      <div class="muted small one" title={meta}>{meta || ' '}</div>
      <Stars size="s" value={it.stars} label="Il tuo voto per {it.title}" onchange={(v) => setStars(it.workId, v)} />
      <div class="cacts">
        {#if it.playItemId}
          <a class="play" href="/guarda/{it.playItemId}" onclick={navigate} aria-label="Guarda {it.title}">▶ Guarda</a>
        {:else if av.tv}
          <span class="muted small one" title="In TV {av.tv}">In TV {av.tv}</span>
        {:else if av.mine.length > 0}
          <span class="muted small one">Su {av.mine.map((s) => platforms.of(s).short).join(', ')}</span>
        {/if}
      </div>
      <div class="cfoot">{#if it.watched}<SeenMark />{/if}</div>
    </div>
  </article>
{/snippet}

<style>
  .pad { padding-left: var(--gutter); padding-right: var(--gutter); max-width: 1440px; }
  .top { padding-top: 28px; display: flex; flex-direction: column; gap: 12px; }
  .block { padding-top: 28px; padding-bottom: 64px; display: flex; flex-direction: column; gap: 14px; }
  h1 { font-size: 32px; }
  h2 { font-size: 22px; }
  .muted { color: var(--muted); margin: 0; }
  .small { font-size: 13px; }
  .error { color: var(--rec-text); margin: 0; }
  .ok { color: var(--green-text); margin: 0; }
  .grow { flex: 1 1 0; min-width: 0; }
  .empty { background: var(--surface); border: 1px dashed var(--line-btn); border-radius: 12px; padding: 18px; color: var(--text-2); margin: 0; display: flex; flex-direction: column; gap: 6px; }
  .empty p { margin: 0; }
  /* barra ferma sotto l'intestazione, come in La mia lista (1px sotto il suo bordo: nessuna fessura) */
  .toprow { position: sticky; top: calc(var(--header-h, 53px) - 1px); z-index: 20; display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between;
    gap: 8px 16px; margin-top: 12px; background: var(--bg); border-bottom: 1px solid var(--line); }
  .pad.toprow { padding-top: 10px; padding-bottom: 10px; }
  .allbtns { display: flex; flex-wrap: wrap; align-items: center; gap: 4px; }
  .allbtn { display: inline-flex; align-items: center; gap: 6px; min-height: 36px; padding: 0 10px; border: 0; border-radius: var(--radius-s); background: none; color: var(--muted); font: inherit; font-size: 13px; font-weight: 600; cursor: pointer; }
  .allbtn:hover { color: var(--text); background: var(--card); }
  .sections { display: flex; flex-direction: column; gap: 32px; padding-top: 28px; padding-bottom: 64px; }
  .edit { display: inline-flex; align-items: center; justify-content: center; width: 36px; height: 36px; border-radius: var(--radius-s); color: var(--muted); }
  .edit:hover { color: var(--text); background: var(--card); }
  .rename input, .search input { min-height: 42px; padding: 0 12px; border-radius: var(--radius-s); border: 1px solid var(--line-strong); background: var(--card); color: var(--text); font-size: 15px; }
  /* file di locandine che scorre, come le righe di La mia lista */
  .row { list-style: none; margin: 0; padding: 0 0 8px; display: grid; align-items: start; grid-auto-flow: column; grid-auto-columns: 180px; gap: 16px; overflow-x: auto; scroll-snap-type: x proximity; }
  .row li { scroll-snap-align: start; min-width: 0; }
  :global(.row.dragging), :global(.row-scroll.dragging) { cursor: grabbing; scroll-snap-type: none; user-select: none; }
  .ctitle { font-weight: 600; color: var(--text); text-decoration: none; overflow-wrap: anywhere; }
  .prog { display: block; height: 4px; border-radius: 2px; background: rgba(255, 255, 255, 0.18); overflow: hidden; margin-top: 6px; }
  .prog span { display: block; height: 100%; background: var(--blue); }
  .prog.over { position: absolute; left: 8px; right: 38px; bottom: 15px; margin: 0; }  /* a destra c'è l'icona "in casa" */
  .prog.over.wide { right: 8px; }
  .panel { background: var(--surface); border: 1px solid var(--line-strong); border-radius: 16px; overflow: hidden; }
  .phead { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; padding: 18px 20px; border-bottom: 1px solid var(--line); }
  .phead h2 { font-size: 24px; }
  .back { font-size: 14px; text-decoration: none; }
  .rename { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; margin: 4px 0; }
  .sel select { min-height: 40px; padding: 0 12px; border-radius: var(--radius-s); border: 1px solid var(--line-btn); background: var(--card); color: var(--text); font-size: 14px; }
  .lrow { display: flex; flex-wrap: wrap; align-items: center; gap: 12px 18px; padding: 14px 20px; border-bottom: 1px solid var(--line-soft); }
  .lrow:last-child { border-bottom: 0; }
  .mini { flex: 0 0 56px; aspect-ratio: 2 / 3; border-radius: 6px; background: var(--raised); overflow: hidden; position: relative; }
  .lbody { flex: 1 1 300px; min-width: 0; display: flex; flex-direction: column; gap: 4px; }
  .line { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
  .ltitle { font-size: 16px; font-weight: 600; color: var(--text); text-decoration: none; }
  .where { color: var(--text-2); display: flex; flex-wrap: wrap; align-items: center; gap: 4px 8px; }
  .x { width: 40px; height: 40px; border-radius: 8px; border: 0; background: none; color: var(--muted); font-size: 22px; cursor: pointer; }
  .x { display: inline-flex; align-items: center; justify-content: center; }
  .x:hover { color: var(--orange-text); background: var(--raised); }
  .tools { display: flex; flex-wrap: wrap; gap: 10px; flex: 1 1 320px; }
  .search { flex: 1 1 260px; max-width: 420px; display: flex; }
  .search input { flex: 1 1 0; }
  .card { display: flex; flex-direction: column; gap: 8px; min-width: 0; }
  .poster { position: relative; aspect-ratio: 2 / 3; border-radius: 10px; background: #24304a; overflow: hidden; display: flex; align-items: flex-end; padding: 12px; text-decoration: none; }
  .card:hover .poster { box-shadow: 0 0 0 2px var(--text); }
  .cbody { display: flex; flex-direction: column; gap: 4px; min-width: 0; }
  /* una riga sola: un titolo lungo non allunga la scheda */
  .one { display: block; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; overflow-wrap: normal; }
  /* spazio fisso anche quando non c'è il pulsante o un segno */
  .cacts { min-height: 36px; margin-top: 6px; display: flex; align-items: center; min-width: 0; }
  .cfoot { min-height: 22px; display: flex; flex-wrap: nowrap; align-items: center; gap: 6px; overflow: hidden; }
  .cfoot > :global(*) { flex: 0 0 auto; }
  .play { display: inline-flex; align-items: center; min-height: 36px; padding: 0 12px; border-radius: 8px; background: var(--green-soft); color: var(--green-text); font-weight: 600; font-size: 14px; text-decoration: none; }
  .play:hover { color: var(--text); }
  .fix { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 10px; }
  .fix li { display: flex; flex-wrap: wrap; align-items: center; gap: 10px 12px; background: var(--card); border: 1px solid var(--line-strong); border-radius: 12px; padding: 14px 16px; }
  .fix .lbody { overflow-wrap: anywhere; }
  @media (max-width: 720px) { .row { grid-auto-columns: 140px; } }
</style>
