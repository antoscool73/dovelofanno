<script lang="ts">
  import Logo from './Logo.svelte';
  import Avatar from './Avatar.svelte';
  import NotificationBell from './NotificationBell.svelte';
  import { router, navigate, type Route } from '../router.svelte';
  import { session } from '../session.svelte';

  const items: { route: Route; path: string; label: string }[] = [
    { route: 'stasera', path: '/', label: 'La mia serata' },
    { route: 'guida', path: '/guida', label: 'Guida' },
    { route: 'libreria', path: '/libreria', label: 'Libreria' },
    { route: 'registrazioni', path: '/registrazioni', label: 'Registrazioni' },
    { route: 'preferenze', path: '/preferenze', label: 'Preferenze' },
  ];

  // l'altezza dell'intestazione cambia con la larghezza (le voci vanno a capo): la si pubblica in --header-h, così
  // le barre fisse delle pagine (Cerca) si fermano esattamente sotto
  function measure(node: HTMLElement) {
    const set = () => document.documentElement.style.setProperty('--header-h', node.offsetHeight + 'px');
    set();
    const ro = new ResizeObserver(set);
    ro.observe(node);
    window.addEventListener('resize', set);
    return { destroy: () => { ro.disconnect(); window.removeEventListener('resize', set); } };
  }
</script>

<header use:measure>
  <a href="/" class="home" onclick={navigate}><Logo size={26} /></a>
  <!-- la lente tra il logo e «La mia serata», senza riquadro (Antonio, 20/11/2026): cerca qualunque film o serie -->
  <a href="/cerca" onclick={navigate} class="icon search" aria-label="Cerca un film o una serie" title="Cerca" aria-current={router.route === 'cerca' ? 'page' : undefined}>
    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" aria-hidden="true"><circle cx="11" cy="11" r="7" /><path d="M20 20l-4-4" /></svg>
  </a>
  <nav aria-label="Principale">
    {#each items as it (it.route)}
      <a href={it.path} onclick={navigate} aria-current={router.route === it.route ? 'page' : undefined}>{it.label}</a>
    {/each}
  </nav>
  <div class="spacer"></div>
  <!-- Impostazioni: un piccolo ingranaggio al posto della scritta (Antonio, 20/11/2026) -->
  <a href="/impostazioni" onclick={navigate} class="icon settings" aria-label="Impostazioni" title="Impostazioni" aria-current={router.route === 'impostazioni' ? 'page' : undefined}>
    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="3" /><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z" /></svg>
  </a>
  {#if session.profile}
    <NotificationBell />
    <a href="/profili" onclick={navigate} class="profile" aria-label="Profilo attivo: {session.profile.name}. Cambia profilo">
      <Avatar name={session.profile.name} color={session.profile.avatar} size={28} />
      <span class="pname">{session.profile.name}</span>
      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M6 9l6 6 6-6" /></svg>
    </a>
  {/if}
</header>

<style>
  /* barra fissa in alto e sottile (feedback di Antonio, 18/11): resta visibile scorrendo */
  header {
    position: sticky; top: 0; z-index: 40;
    display: flex; flex-wrap: wrap; align-items: center; gap: 6px 16px;
    padding: 6px var(--gutter); border-bottom: 1px solid var(--line);
    background: var(--bg);
  }
  .home { text-decoration: none; display: flex; }
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
  /* profilo senza riquadro (Antonio, 20/11/2026): avatar, nome e freccia; sfondo solo al passaggio */
  .profile {
    display: flex; align-items: center; gap: 8px; min-height: 40px; padding: 4px 8px 4px 4px;
    border-radius: var(--radius-s); text-decoration: none; color: var(--text);
  }
  .profile:hover { background: var(--card); }
  .profile svg { color: var(--muted); }
  .pname { font-size: 14px; font-weight: 600; }
  @media (max-width: 1000px) { .pname { display: none; } }
  @media (max-width: 720px) {
    header { padding: 4px 16px; }
    nav { order: 3; width: 100%; overflow-x: auto; }
  }
</style>
