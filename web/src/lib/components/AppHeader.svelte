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
  <nav aria-label="Principale">
    {#each items as it (it.route)}
      <a href={it.path} onclick={navigate} aria-current={router.route === it.route ? 'page' : undefined}>{it.label}</a>
    {/each}
  </nav>
  <div class="spacer"></div>
  <!-- la lente: cerca qualunque film o serie, anche solo in streaming -->
  <a href="/cerca" onclick={navigate} class="search" aria-label="Cerca un film o una serie" title="Cerca" aria-current={router.route === 'cerca' ? 'page' : undefined}>
    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" aria-hidden="true"><circle cx="11" cy="11" r="7" /><path d="M20 20l-4-4" /></svg>
  </a>
  <a href="/impostazioni" onclick={navigate} class="settings" aria-current={router.route === 'impostazioni' ? 'page' : undefined}>Impostazioni</a>
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
  nav a, .settings {
    padding: 7px 12px; border-radius: var(--radius-s); color: var(--muted);
    text-decoration: none; font-weight: 500; font-size: 14px; white-space: nowrap;
  }
  nav a:hover, .settings:hover { color: var(--text); background: var(--card); }
  nav a[aria-current='page'], .settings[aria-current='page'] { background: var(--raised); color: var(--text); font-weight: 600; }
  .spacer { flex: 1 1 0; }
  .search {
    display: flex; align-items: center; justify-content: center; width: 40px; height: 40px; border-radius: 10px;
    border: 1px solid var(--line-strong); background: var(--card); color: var(--text-2);
  }
  .search:hover, .search[aria-current='page'] { color: var(--text); border-color: var(--text-2); }
  .profile {
    display: flex; align-items: center; gap: 8px; min-height: 40px; padding: 4px 10px 4px 4px;
    border-radius: 10px; background: var(--card); border: 1px solid var(--line-strong);
    text-decoration: none; color: var(--text);
  }
  .profile svg { color: var(--muted); }
  .pname { font-size: 14px; font-weight: 600; }
  @media (max-width: 1000px) { .pname { display: none; } }
  @media (max-width: 720px) {
    header { padding: 4px 16px; }
    nav { order: 3; width: 100%; overflow-x: auto; }
    .settings { display: none; }
  }
</style>
