<script lang="ts">
  import AppHeader from './lib/components/AppHeader.svelte';
  import Button from './lib/components/Button.svelte';
  import Profili from './screens/Profili.svelte';
  import NonTrovata from './screens/NonTrovata.svelte';
  import Preferenze from './screens/Preferenze.svelte';
  import Benvenuto from './screens/Benvenuto.svelte';
  import Registrazioni from './screens/Registrazioni.svelte';
  import Impostazioni from './screens/Impostazioni.svelte';
  import Guida from './screens/Guida.svelte';
  import Stasera from './screens/Stasera.svelte';
  import Scheda from './screens/Scheda.svelte';
  import Libreria from './screens/Libreria.svelte';
  import Player from './screens/Player.svelte';
  import Cerca from './screens/Cerca.svelte';
  import { api, ApiError, type Profile } from './lib/api/client';
  import { router } from './lib/router.svelte';
  import { session } from './lib/session.svelte';

  let profiles = $state<Profile[]>([]);
  let loading = $state(true);
  let error = $state('');
  // primo avvio: dopo il primo profilo, la procedura guidata (una volta sola per server)
  let setupDone = $state(true);

  async function load() {
    error = '';
    try {
      const [list, general, sources] = await Promise.all([api.profiles(), api.generalSettings(), api.sources()]);
      profiles = list;
      // un server già configurato prima di questa versione non rivede il primo avvio
      if (!general.setupDone && sources.length > 0) api.updateGeneralSettings({ setupDone: true }).catch(() => {});
      setupDone = general.setupDone || sources.length > 0;
      // il profilo ricordato su questo dispositivo, se c'è ancora
      const stored = profiles.find((p) => p.id === session.storedId);
      if (stored && !session.profile) session.profile = stored;
      else if (session.profile) session.profile = profiles.find((p) => p.id === session.profile?.id) ?? null;
    } catch (e) {
      error = e instanceof ApiError ? e.message : 'Errore imprevisto';
    } finally {
      loading = false;
    }
  }

  load();

  const titles: Record<string, string> = {
    stasera: 'La mia serata', cerca: 'Cerca', opera: 'Scheda', guida: 'Guida TV', libreria: 'Libreria', guarda: 'Player', registrazioni: 'Registrazioni',
    preferenze: 'Preferenze', impostazioni: 'Impostazioni', profili: 'Chi guarda?', 'non-trovata': 'Pagina non trovata',
  };

  $effect(() => {
    document.title = (titles[router.route] ?? '') + ' · DoveLoFanno';
  });

  const showProfiles = $derived(router.route === 'profili' || !session.profile);
</script>

{#if loading}
  <p class="center" role="status">Un momento…</p>
{:else if error}
  <div class="center" role="alert">
    <p>{error}</p>
    <Button variant="primary" onclick={load}>Riprova</Button>
  </div>
{:else if showProfiles}
  <Profili {profiles} onchange={load} />
{:else if !setupDone && !session.profile?.isChild}
  <Benvenuto ondone={() => (setupDone = true)} />
{:else if router.route === 'guarda'}
  <Player id={router.itemId} />
{:else}
  <AppHeader />
  <main>
    {#if router.route === 'stasera'}
      <Stasera />
    {:else if router.route === 'cerca'}
      {#key router.nav}<Cerca />{/key}
    {:else if router.route === 'opera'}
      <Scheda id={router.workId} />
    {:else if router.route === 'guida'}
      <Guida />
    {:else if router.route === 'libreria'}
      <Libreria />
    {:else if router.route === 'registrazioni'}
      <Registrazioni />
    {:else if router.route === 'preferenze'}
      <Preferenze />
    {:else if router.route === 'impostazioni'}
      <Impostazioni />
    {:else}
      <NonTrovata />
    {/if}
  </main>
{/if}

<style>
  .center { min-height: 100vh; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 16px; color: var(--text-2); }
</style>
