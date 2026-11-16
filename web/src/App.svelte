<script lang="ts">
  import AppHeader from './lib/components/AppHeader.svelte';
  import Button from './lib/components/Button.svelte';
  import Profili from './screens/Profili.svelte';
  import InArrivo from './screens/InArrivo.svelte';
  import { api, ApiError, type Profile } from './lib/api/client';
  import { router } from './lib/router.svelte';
  import { session } from './lib/session.svelte';

  let profiles = $state<Profile[]>([]);
  let loading = $state(true);
  let error = $state('');

  async function load() {
    error = '';
    try {
      profiles = await api.profiles();
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
    stasera: 'Stasera', guida: 'Guida TV', libreria: 'Libreria', registrazioni: 'Registrazioni',
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
{:else}
  <AppHeader />
  <main>
    {#if router.route === 'stasera'}
      <InArrivo title="Stasera" text="La mia serata in TV, le proposte per oggi e domani, le registrazioni in corso." />
    {:else if router.route === 'guida'}
      <InArrivo title="Guida TV" text="Griglia per canale e ora con anteprima muta; senza IPTV, elenco su tutti i giorni." />
    {:else if router.route === 'libreria'}
      <InArrivo title="Libreria" text="Liste personali e di famiglia, film, serie e registrazioni, con il voto a stelle." />
    {:else if router.route === 'registrazioni'}
      <InArrivo title="Registrazioni" text="In corso, in programma, completate, incomplete con i minuti persi." />
    {:else if router.route === 'preferenze'}
      <InArrivo title="Preferenze" text="Proposte, avvisi, abbonamenti, notifiche e registrazione predefinita." />
    {:else if router.route === 'impostazioni'}
      <InArrivo title="Impostazioni" text="Sorgenti e stato dei canali, cartelle, profili, voti dei film, limiti, backup." />
    {:else}
      <InArrivo title="Pagina non trovata" text="L'indirizzo non corrisponde a nessuna schermata." />
    {/if}
  </main>
{/if}

<style>
  .center { min-height: 100vh; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 16px; color: var(--text-2); }
</style>
