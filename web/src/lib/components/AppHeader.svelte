<script lang="ts">
  import Logo from './Logo.svelte';
  import ProfileMenu from './ProfileMenu.svelte';
  import NotificationBell from './NotificationBell.svelte';
  import { router, navigate, type Route } from '../router.svelte';
  import { session } from '../session.svelte';
  import AboutDialog from './AboutDialog.svelte';

  let about = $state(false);

  const items: { route: Route; path: string; label: string }[] = [
    { route: 'stasera', path: '/', label: 'La mia lista' },
    { route: 'guida', path: '/guida', label: 'Guida tv' },
    { route: 'libreria', path: '/libreria', label: 'Libreria' },
    { route: 'registrazioni', path: '/registrazioni', label: 'Registrazioni' },
    // I miei avvisi: pagina propria dopo Registrazioni (24/11/2026, Antonio; prima in Preferenze)
    { route: 'avvisi', path: '/avvisi', label: 'I miei avvisi' },
    { route: 'preferenze', path: '/preferenze', label: 'Preferenze' },
  ];

  // l'altezza dell'intestazione cambia con la larghezza (le voci vanno a capo): la si pubblica in --header-h, così
  // le barre fisse delle pagine (Cerca) si fermano esattamente sotto
  function measure(node: HTMLElement) {
    // altezza vera arrotondata per eccesso: offsetHeight arrotonda e con lo zoom di Windows (125%) lasciava una fessura
    // di un pixel tra l'intestazione e le barre fisse, da cui si vedevano le locandine (24/11/2026)
    const set = () => document.documentElement.style.setProperty('--header-h', Math.ceil(node.getBoundingClientRect().height) + 'px');
    set();
    const ro = new ResizeObserver(set);
    ro.observe(node);
    window.addEventListener('resize', set);
    return { destroy: () => { ro.disconnect(); window.removeEventListener('resize', set); } };
  }
</script>

<header use:measure>
  <div class="bar">
    <!-- il profilo tutto a sinistra, a larghezza fissa, e il logo tutto a destra (Antonio, 26/11/2026) -->
    {#if session.profile}<ProfileMenu />{/if}
    <nav aria-label="Principale">
      {#each items as it (it.route)}
        {#if it.route === 'stasera'}
          <a href={it.path} onclick={navigate} class="house" aria-label={it.label} title={it.label} aria-current={router.route === it.route ? 'page' : undefined}>
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 10.5L12 3l9 7.5" /><path d="M5 9v11h5v-6h4v6h5V9" /></svg>
          </a>
        {:else}
          <a href={it.path} onclick={navigate} aria-current={router.route === it.route ? 'page' : undefined}>{it.label}</a>
        {/if}
      {/each}
    </nav>
    <!-- la lente dopo «Preferenze», senza riquadro (Antonio, 23/11/2026): cerca qualunque film o serie -->
    <a href="/cerca" onclick={navigate} class="icon search" aria-label="Cerca un film o una serie" title="Cerca" aria-current={router.route === 'cerca' ? 'page' : undefined}>
      <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" aria-hidden="true"><circle cx="11" cy="11" r="7" /><path d="M20 20l-4-4" /></svg>
    </a>
    <div class="spacer"></div>
    <!-- Impostazioni: un piccolo ingranaggio al posto della scritta (Antonio, 20/11/2026) -->
    <a href="/impostazioni" onclick={navigate} class="icon settings" aria-label="Impostazioni" title="Impostazioni" aria-current={router.route === 'impostazioni' ? 'page' : undefined}>
      <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="3" /><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z" /></svg>
    </a>
    {#if session.profile}<NotificationBell />{/if}
    <!-- il logo apre «Informazioni su DoveLoFanno»; a La mia lista si va con la casa (Antonio, 24/11/2026) -->
    <button type="button" class="home" onclick={() => (about = true)} aria-label="Informazioni su DoveLoFanno" title="Informazioni"><Logo size={26} /></button>
  </div>
  <!-- «Indietro» sempre fisso sotto la barra, in tutte le pagine (Antonio, 26/11/2026): fa parte dell'intestazione, così
       le barre fisse delle pagine (Cerca, Libreria, La mia lista) si fermano sotto da sole con --header-h. Compare solo
       se c'è una pagina dell'app a cui tornare -->
  {#if router.depth > 0}
    <div class="backrow">
      <button type="button" class="back" onclick={() => router.back()}>
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M15 18l-6-6 6-6" /></svg>
        Indietro
      </button>
    </div>
  {/if}
</header>
{#if about}<AboutDialog onclose={() => (about = false)} />{/if}

<style>
  /* barra fissa in alto e sottile (feedback di Antonio, 18/11): resta visibile scorrendo */
  header { position: sticky; top: 0; z-index: 40; border-bottom: 1px solid var(--line); background: var(--bg); }
  .bar { display: flex; flex-wrap: wrap; align-items: center; gap: 6px 16px; padding: 6px var(--gutter); }
  .backrow { padding: 0 var(--gutter) 8px; }
  .back {
    display: inline-flex; align-items: center; gap: 6px; min-height: 34px; padding: 0 14px 0 8px; border-radius: 18px;
    border: 1px solid var(--line-btn); background: none; color: var(--text); font: inherit; font-size: 14px; font-weight: 600; cursor: pointer;
  }
  .back:hover { background: var(--card); border-color: var(--text-2); }
  .back:focus-visible { box-shadow: var(--focus); outline: none; }
  .home { display: flex; padding: 4px 0 4px 6px; border: 0; border-radius: var(--radius-s); background: none; color: inherit; cursor: pointer; }
  .home:focus-visible { box-shadow: var(--focus); outline: none; }
  nav a.house { display: flex; align-items: center; padding: 7px 10px; }
  nav { display: flex; flex-wrap: nowrap; gap: 2px; }
  nav a {
    padding: 7px 12px; border-radius: var(--radius-s); color: var(--muted);
    text-decoration: none; font-weight: 500; font-size: 14px; white-space: nowrap;
  }
  nav a:hover { color: var(--text); background: var(--card); }
  nav a[aria-current='page'] { background: var(--raised); color: var(--text); font-weight: 600; }
  .spacer { flex: 1 1 0; }
  /* lente e ingranaggio: solo l'icona, senza riquadro; lo sfondo compare al passaggio e sulla pagina aperta */
  .icon {
    display: flex; align-items: center; justify-content: center; width: 40px; height: 40px; border-radius: var(--radius-s);
    color: var(--text-2);
  }
  .icon:hover { color: var(--text); background: var(--card); }
  .icon[aria-current='page'] { color: var(--text); background: var(--raised); }
  /* la lente a colori invertiti, come i pulsanti accesi (23/11/2026, Antonio: «più visibile») */
  .icon.search { color: var(--bg); background: var(--text); border-radius: 999px; }
  .icon.search:hover { color: var(--bg); background: var(--text-2); }
  .icon.search[aria-current='page'] { color: var(--bg); background: var(--text); box-shadow: 0 0 0 2px var(--bg), 0 0 0 4px var(--blue); }
  @media (max-width: 720px) {
    .bar { padding: 4px 16px; }
    .backrow { padding: 0 16px 6px; }
    nav { order: 3; width: 100%; overflow-x: auto; }
  }
</style>
