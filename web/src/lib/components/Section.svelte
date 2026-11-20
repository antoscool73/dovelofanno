<script lang="ts">
  // Sezione con titolo che si chiude e si riapre con un tocco; aperta di default. Lo stato si ricorda su questo
  // dispositivo (comodità, non dato importante: senza memoria del browser resta aperta).
  import type { Snippet } from 'svelte';
  import { untrack } from 'svelte';
  import { sectionsCommand } from '../sections.svelte';

  // after: subito dopo il titolo (la pennina di una lista); tools: controlli dopo il titolo (es. un interruttore); aside: testo a destra
  let { key, title, id = undefined, after = undefined, aside = undefined, tools = undefined, children }: {
    key: string; title: string; id?: string; after?: Snippet; aside?: Snippet; tools?: Snippet; children: Snippet;
  } = $props();

  const storeKey = $derived('dlf.sezione.' + key);
  function read(k: string): boolean {
    try { return localStorage.getItem(k) === 'chiusa'; } catch { return false; }
  }
  // svelte-ignore state_referenced_locally
  let closed = $state(read('dlf.sezione.' + key));
  function toggle() {
    closed = !closed;
    try { if (closed) localStorage.setItem(storeKey, 'chiusa'); else localStorage.removeItem(storeKey); } catch { /* resta solo per questa visita */ }
  }
  // «Apri tutte» / «Chiudi tutte» della pagina: solo quando il comando cambia, non all'avvio
  const startN = sectionsCommand.n;
  $effect(() => {
    const n = sectionsCommand.n;
    if (n === startN) return;
    const want = sectionsCommand.closed;
    untrack(() => {
      if (closed === want) return;
      closed = want;
      try { if (closed) localStorage.setItem(storeKey, 'chiusa'); else localStorage.removeItem(storeKey); } catch { /* solo per questa visita */ }
    });
  });
  const bodyId = $derived('sez-' + key);
</script>

<section {id}>
  <div class="head">
    <h2>
      <button type="button" aria-expanded={!closed} aria-controls={bodyId} onclick={toggle}>
        <svg class="chev" class:closed width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M6 9l6 6 6-6" /></svg>
        <span class="t">{title}</span>
      </button>
    </h2>
    {#if after}{@render after()}{/if}
    {#if tools && !closed}<div class="tools">{@render tools()}</div>{/if}
    {#if aside}<div class="aside">{@render aside()}</div>{/if}
  </div>
  <div id={bodyId} hidden={closed}>{@render children()}</div>
</section>

<style>
  .head { display: flex; flex-wrap: wrap; align-items: center; gap: 6px 16px; margin-bottom: 12px; }
  .tools { display: flex; align-items: center; }
  .aside { margin-left: auto; }
  h2 { font-size: 22px; margin: 0; }
  h2 button {
    display: inline-flex; align-items: center; gap: 8px; padding: 2px 6px 2px 0; border: 0; background: none; color: var(--text);
    font: inherit; font-family: var(--font-display); font-weight: 700; font-size: 22px; cursor: pointer;
  }
  /* il titolo resta su una riga: i nomi delle liste sono brevi (28 caratteri), i puntini sono solo una sicurezza */
  h2 { min-width: 0; max-width: 100%; }
  h2 button { max-width: 100%; }
  .t { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .chev { flex: 0 0 auto; color: var(--muted); transition: transform 0.15s; }
  .chev.closed { transform: rotate(-90deg); }
  .aside { font-size: 13px; color: var(--muted); }
</style>
