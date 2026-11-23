<script lang="ts">
  // Campanella delle notifiche: registrazioni finite, avvisi scattati, proposte nuove. Si aggiorna ogni 30 secondi; se il
  // browser lo permette (Preferenze › Notifiche) le nuove compaiono anche come notifiche del sistema.
  import { api, type NotificationList, type NotificationRow } from '../api/client';
  import { router } from '../router.svelte';
  import { session } from '../session.svelte';
  import { day, time } from '../format';

  let data = $state<NotificationList | null>(null);
  let open = $state(false);
  let bell = $state<HTMLDivElement | null>(null);
  let lastId = 0;

  async function load() {
    const prof = session.profile;
    if (!prof) return;
    try {
      const d = await api.notifications(prof.id);
      const fresh = lastId > 0 ? d.items.filter((n) => n.id > lastId && !n.read) : [];
      lastId = Math.max(lastId, d.items[0]?.id ?? 0, 1);
      data = d;
      if ('Notification' in window && Notification.permission === 'granted' && document.visibilityState !== 'visible') {
        for (const n of fresh.slice(0, 3)) {
          const sys = new Notification(n.title, { body: n.text, tag: `dlf-${n.id}`, icon: '/favicon.svg' });
          sys.onclick = () => { window.focus(); go(n); };
        }
      }
    } catch { /* la campanella non deve disturbare: si riprova al prossimo giro */ }
  }

  $effect(() => {
    void session.profile?.id;
    lastId = 0;
    load();
    const t = setInterval(load, 30_000);
    return () => clearInterval(t);
  });

  async function toggle() {
    open = !open;
    const prof = session.profile;
    const top = data?.items[0];
    if (open && prof && data && top && data.unread > 0) {
      try { await api.markRead(prof.id, top.id); data.unread = 0; } catch { /* resta da leggere */ }
    }
  }

  function go(n: NotificationRow) {
    open = false;
    if (!n.link) return;
    const [path, hash] = n.link.split('#');
    router.go(path || '/');
    if (hash) setTimeout(() => document.getElementById(hash)?.scrollIntoView({ behavior: 'smooth' }), 300);
  }

  const icons: Record<string, string> = { recording: '●', alert: '🔔', proposals: '★', system: 'i' };
</script>

<!-- si chiude con Esc o con un clic fuori dalla campanella e dal suo riquadro -->
<svelte:window onkeydown={(e) => { if (e.key === 'Escape') open = false; }}
  onpointerdown={(e) => { if (open && bell && !bell.contains(e.target as Node)) open = false; }} />

<div class="bell" bind:this={bell}>
  <button type="button" class="btn" aria-expanded={open} aria-label={data && data.unread > 0 ? `Notifiche, ${data.unread} da leggere` : 'Notifiche'} onclick={toggle}>
    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9" /><path d="M10.3 21a1.94 1.94 0 0 0 3.4 0" /></svg>
    {#if data && data.unread > 0}<span class="count">{data.unread > 9 ? '9+' : data.unread}</span>{/if}
  </button>
  {#if open}
    <div class="panel" role="dialog" aria-label="Notifiche">
      <div class="phead"><strong>Notifiche</strong><button type="button" class="x" onclick={() => (open = false)} aria-label="Chiudi">✕</button></div>
      {#if !data || data.items.length === 0}
        <p class="empty">Nessuna notifica. Qui arrivano le registrazioni finite, i tuoi avvisi e le proposte nuove.</p>
      {:else}
        <ul>
          {#each data.items as n (n.id)}
            <li>
              <button type="button" class="item" class:unread={!n.read} onclick={() => go(n)}>
                <span class="ic {n.kind}" aria-hidden="true">{icons[n.kind] ?? '•'}</span>
                <span class="txt"><span class="t">{n.title}</span>{#if n.text}<span class="d">{n.text}</span>{/if}<span class="w">{day(n.createdAt)} {time(n.createdAt)}</span></span>
              </button>
            </li>
          {/each}
        </ul>
      {/if}
    </div>
  {/if}
</div>

<style>
  .bell { position: relative; }
  /* senza riquadro, come lente e ingranaggio (Antonio, 20/11/2026): lo sfondo solo al passaggio e a pannello aperto */
  .btn { position: relative; display: flex; align-items: center; justify-content: center; width: 40px; height: 40px; border-radius: var(--radius-s); border: 0; background: none; color: var(--text-2); cursor: pointer; }
  .btn:hover, .btn[aria-expanded='true'] { color: var(--text); background: var(--card); }
  .count { position: absolute; top: -4px; right: -4px; min-width: 20px; height: 20px; padding: 0 5px; border-radius: 10px; background: var(--rec); color: #fff; font-size: 12px; font-weight: 700; display: flex; align-items: center; justify-content: center; }
  .panel { position: absolute; right: 0; top: 48px; z-index: 50; width: min(380px, calc(100vw - 32px)); max-height: 70vh; overflow: auto; background: var(--surface); border: 1px solid var(--line-strong); border-radius: var(--radius-l); box-shadow: 0 12px 40px rgba(0, 0, 0, 0.5); }
  .phead { display: flex; align-items: center; justify-content: space-between; padding: 12px 16px; border-bottom: 1px solid var(--line); }
  .x { border: 0; background: none; color: var(--muted); font-size: 16px; cursor: pointer; min-width: 32px; min-height: 32px; }
  ul { list-style: none; margin: 0; padding: 0; }
  .item { display: flex; gap: 12px; width: 100%; padding: 12px 16px; border: 0; border-bottom: 1px solid var(--line-soft); background: none; color: var(--text); text-align: left; cursor: pointer; }
  .item:hover { background: var(--card); }
  .item.unread { background: var(--blue-soft); }
  .ic { flex: 0 0 24px; height: 24px; border-radius: 12px; display: flex; align-items: center; justify-content: center; font-size: 12px; background: var(--raised); }
  .ic.recording { color: var(--rec); }
  .ic.proposals { color: var(--star); }
  .txt { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
  .t { font-weight: 600; font-size: 14px; }
  .d { font-size: 13px; color: var(--text-2); }
  .w { font-size: 12px; color: var(--muted); }
  .empty { margin: 0; padding: 16px; color: var(--muted); font-size: 14px; }
  @media (max-width: 720px) { .panel { position: fixed; left: 16px; right: 16px; width: auto; top: 56px; } }
</style>
