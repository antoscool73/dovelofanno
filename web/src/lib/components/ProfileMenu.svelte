<script lang="ts">
  // Menu a tendina del profilo in alto a destra (Antonio, 24/11/2026): gli altri profili (un clic e si passa a quello,
  // restando sulla pagina; con PIN si chiede prima il PIN) e in fondo «Modifica», che porta a «Chi guarda?» in modifica.
  import Avatar from './Avatar.svelte';
  import { api, type Profile } from '../api/client';
  import { router } from '../router.svelte';
  import { session } from '../session.svelte';

  let open = $state(false);
  let others = $state<Profile[]>([]);
  let root = $state<HTMLDivElement | null>(null);
  let button = $state<HTMLButtonElement | null>(null);
  // sul telefono la barra va a capo e il profilo può finire a sinistra: il menu si apre verso il lato libero
  let alignLeft = $state(false);

  async function toggle() {
    open = !open;
    if (!open) return;
    if (button) alignLeft = button.getBoundingClientRect().left < window.innerWidth / 2;
    try {
      others = (await api.profiles()).filter((p) => p.id !== session.profile?.id);
    } catch {
      others = [];
    }
  }

  function close(focus = false) {
    open = false;
    if (focus) button?.focus();
  }

  function pick(p: Profile) {
    close();
    if (p.hasPin) router.go(`/profili#pin=${p.id}`);
    else session.enter(p);
  }

  function edit() {
    close();
    router.go('/profili#modifica');
  }

  // fuori dal menu o con Esc si chiude
  function onDoc(e: MouseEvent) {
    if (open && root && !root.contains(e.target as Node)) close();
  }
  function onKey(e: KeyboardEvent) {
    if (open && e.key === 'Escape') close(true);
  }
</script>

<svelte:document onclick={onDoc} onkeydown={onKey} />

{#if session.profile}
  <div class="wrap" bind:this={root}>
    <button type="button" class="profile" bind:this={button} onclick={toggle} aria-haspopup="menu" aria-expanded={open}
      aria-label="Profilo attivo: {session.profile.name}. Cambia profilo">
      <Avatar name={session.profile.name} color={session.profile.avatar} icon={session.profile.icon} size={28} />
      <span class="pname">{session.profile.name}</span>
      <svg class="chev" class:up={open} width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M6 9l6 6 6-6" /></svg>
    </button>
    {#if open}
      <div class="menu" class:left={alignLeft} role="menu" aria-label="Profili">
        {#each others as p (p.id)}
          <button type="button" role="menuitem" class="item" onclick={() => pick(p)}>
            <Avatar name={p.name} color={p.avatar} icon={p.icon} size={32} />
            <span class="name">{p.name}</span>
            {#if p.hasPin}
              <svg class="lock" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-label="con PIN"><rect x="5" y="11" width="14" height="10" rx="2" /><path d="M8 11V8a4 4 0 0 1 8 0v3" /></svg>
            {/if}
          </button>
        {/each}
        {#if others.length > 0}<div class="sep" role="separator"></div>{/if}
        <button type="button" role="menuitem" class="item edit" onclick={edit}>
          <span class="pen" aria-hidden="true"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9" /><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4z" /></svg></span>
          <span class="name">Modifica</span>
        </button>
      </div>
    {/if}
  </div>
{/if}

<style>
  .wrap { position: relative; }
  /* profilo senza riquadro (Antonio, 20/11/2026): avatar, nome e freccia; sfondo solo al passaggio */
  .profile {
    display: flex; align-items: center; gap: 8px; min-height: 40px; padding: 4px 8px 4px 4px; border: 0; background: none;
    border-radius: var(--radius-s); color: var(--text); cursor: pointer; font: inherit;
  }
  .profile:hover, .profile[aria-expanded='true'] { background: var(--card); }
  .chev { color: var(--muted); transition: transform 0.15s; }
  .chev.up { transform: rotate(180deg); }
  .pname { font-size: 14px; font-weight: 600; }
  @media (max-width: 1000px) { .pname { display: none; } }
  .menu {
    position: absolute; right: 0; top: calc(100% + 6px); z-index: 60; min-width: 220px; padding: 6px;
    display: flex; flex-direction: column; gap: 2px;
    background: var(--surface); border: 1px solid var(--line-strong); border-radius: var(--radius-l);
    box-shadow: 0 12px 32px rgba(0, 0, 0, 0.45);
  }
  .menu.left { right: auto; left: 0; }
  .item {
    display: flex; align-items: center; gap: 12px; min-height: 44px; padding: 6px 10px; border: 0; border-radius: var(--radius-s);
    background: none; color: var(--text); font: inherit; font-size: 15px; font-weight: 500; text-align: left; cursor: pointer;
  }
  .item:hover, .item:focus-visible { background: var(--raised); }
  .name { flex: 1 1 auto; }
  .lock { color: var(--muted); }
  .sep { height: 1px; margin: 4px 6px; background: var(--line); }
  .pen { width: 32px; height: 32px; display: flex; align-items: center; justify-content: center; color: var(--text-2); }
  .edit .name { color: var(--text-2); }
</style>
