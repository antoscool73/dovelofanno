<script lang="ts">
  import Logo from './Logo.svelte';
  import Avatar from './Avatar.svelte';
  import { router, navigate, type Route } from '../router.svelte';
  import { session } from '../session.svelte';

  const items: { route: Route; path: string; label: string }[] = [
    { route: 'stasera', path: '/', label: 'Stasera' },
    { route: 'guida', path: '/guida', label: 'Guida' },
    { route: 'libreria', path: '/libreria', label: 'Libreria' },
    { route: 'registrazioni', path: '/registrazioni', label: 'Registrazioni' },
    { route: 'preferenze', path: '/preferenze', label: 'Preferenze' },
  ];
</script>

<header>
  <a href="/" class="home" onclick={navigate}><Logo /></a>
  <nav aria-label="Principale">
    {#each items as it (it.route)}
      <a href={it.path} onclick={navigate} aria-current={router.route === it.route ? 'page' : undefined}>{it.label}</a>
    {/each}
  </nav>
  <div class="spacer"></div>
  <a href="/impostazioni" onclick={navigate} class="settings" aria-current={router.route === 'impostazioni' ? 'page' : undefined}>Impostazioni</a>
  {#if session.profile}
    <a href="/profili" onclick={navigate} class="profile" aria-label="Profilo attivo: {session.profile.name}. Cambia profilo">
      <Avatar name={session.profile.name} color={session.profile.avatar} />
      <span class="pname">{session.profile.name}</span>
      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M6 9l6 6 6-6" /></svg>
    </a>
  {/if}
</header>

<style>
  header {
    display: flex; flex-wrap: wrap; align-items: center; gap: 12px 20px;
    padding: 14px var(--gutter); border-bottom: 1px solid var(--line); background: var(--bg);
  }
  .home { text-decoration: none; }
  nav { display: flex; flex-wrap: wrap; gap: 4px; }
  nav a, .settings {
    padding: 10px 14px; border-radius: var(--radius-s); color: var(--muted);
    text-decoration: none; font-weight: 500; font-size: 15px;
  }
  nav a:hover, .settings:hover { color: var(--text); background: var(--card); }
  nav a[aria-current='page'], .settings[aria-current='page'] { background: var(--raised); color: var(--text); font-weight: 600; }
  .spacer { flex: 1 1 0; }
  .profile {
    display: flex; align-items: center; gap: 10px; min-height: 44px; padding: 4px 12px 4px 4px;
    border-radius: 12px; background: var(--card); border: 1px solid var(--line-strong);
    text-decoration: none; color: var(--text);
  }
  .profile svg { color: var(--muted); }
  .pname { font-size: 14px; font-weight: 600; }
  @media (max-width: 720px) {
    nav { order: 3; width: 100%; overflow-x: auto; flex-wrap: nowrap; }
    .settings { display: none; }
  }
</style>
