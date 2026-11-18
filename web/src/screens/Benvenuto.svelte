<script lang="ts">
  // Primo avvio, dopo il primo profilo: playlist facoltativa con conferma dei diritti, chiave TMDB facoltativa,
  // abbonamenti della famiglia, riepilogo. Ogni passo si può saltare; tutto si cambia poi in Impostazioni.
  import Button from '../lib/components/Button.svelte';
  import Logo from '../lib/components/Logo.svelte';
  import ServicePicker from '../lib/components/ServicePicker.svelte';
  import { api, ApiError, type FamilySubscriptions } from '../lib/api/client';

  let { ondone }: { ondone: () => void } = $props();

  let step = $state(1);
  let busy = $state(false);
  let error = $state('');
  let done = $state<string[]>([]);

  let url = $state('');
  let rights = $state(false);
  let key = $state('');
  let family = $state<FamilySubscriptions | null>(null);
  api.familySubscriptions().then((f) => (family = f)).catch(() => {});

  async function addPlaylist(e: SubmitEvent) {
    e.preventDefault();
    busy = true;
    error = '';
    try {
      const r = await api.addSource('', url.trim());
      done = [...done, `Playlist aggiunta: ${r.channels} canali${r.drm ? ` (${r.drm} protetti, esclusi)` : ''}${r.epgUrl ? ', con la guida TV' : ''}.`];
      step = 3;
    } catch (x) {
      error = x instanceof ApiError ? x.message : 'Playlist non aggiunta';
    } finally {
      busy = false;
    }
  }

  async function saveKey(e: SubmitEvent) {
    e.preventDefault();
    busy = true;
    error = '';
    try {
      await api.updateRatingSettings({ tmdbKey: key.trim(), clearTmdbKey: null, imdbEnabled: null });
      done = [...done, 'Chiave TMDB salvata: arrivano voti, locandine e «dove lo vedo».'];
      step = 4;
    } catch (x) {
      error = x instanceof ApiError ? x.message : 'Chiave non salvata';
    } finally {
      busy = false;
    }
  }

  async function toggle(s: string) {
    if (!family) return;
    const next = family.services.includes(s) ? family.services.filter((x) => x !== s) : [...family.services, s];
    try { family = await api.setFamilySubscriptions(next); } catch { /* si sceglie poi in Impostazioni */ }
  }

  async function finish() {
    busy = true;
    try { await api.updateGeneralSettings({ setupDone: true }); } catch { /* il riepilogo ricomparirebbe: pazienza */ }
    busy = false;
    ondone();
  }

  const steps = ['Playlist', 'Voti dei film', 'Abbonamenti', 'Pronto'];
</script>

<div class="wrap">
  <div class="card">
    <Logo />
    <ol class="steps" aria-label="Passi">
      {#each steps as s, i (s)}<li class:on={step === i + 2 || (step === 1 && i === 0)} class:past={step > i + 2}>{s}</li>{/each}
    </ol>
    {#if error}<p class="f-error" role="alert">{error}</p>{/if}

    {#if step === 1 || step === 2}
      <h1>Hai una playlist IPTV?</h1>
      <p class="f-muted">Con una playlist M3U DoveLoFanno registra i canali e mostra la guida a griglia con l'anteprima. Senza, resta una guida con proposte, «dove lo vedo», avvisi e la libreria dei file di casa.</p>
      <form onsubmit={addPlaylist} class="form">
        <label class="f-field">Indirizzo della playlist M3U (o file sul server)<input bind:value={url} placeholder="https://…/playlist.m3u" /></label>
        <label class="f-check"><input type="checkbox" bind:checked={rights} /> Ho il diritto di usare questa playlist. DoveLoFanno non include né fornisce canali.</label>
        <div class="f-row">
          <Button variant="primary" type="submit" disabled={busy || !url.trim() || !rights}>{busy ? 'Verifica in corso…' : 'Aggiungi'}</Button>
          <Button variant="ghost" onclick={() => (step = 3)}>Non ne ho, salta</Button>
        </div>
      </form>
    {:else if step === 3}
      <h1>Voti, locandine e dove lo vedo</h1>
      <p class="f-muted">Vengono da TMDB con una chiave personale gratuita: si chiede su themoviedb.org (Impostazioni › API) e si incolla qui. DoveLoFanno la prova prima di salvarla e non la mostra più.</p>
      <form onsubmit={saveKey} class="form">
        <label class="f-field">Chiave TMDB (v3 o token v4)<input bind:value={key} autocomplete="off" /></label>
        <div class="f-row">
          <Button variant="primary" type="submit" disabled={busy || !key.trim()}>{busy ? 'Prova in corso…' : 'Salva'}</Button>
          <Button variant="ghost" onclick={() => (step = 4)}>Più tardi</Button>
        </div>
      </form>
    {:else if step === 4}
      <h1>Che abbonamenti avete?</h1>
      <p class="f-muted">«Dove lo vedo» li mette per primi e gli avvisi ti dicono quando un titolo ci arriva. Ogni profilo può poi scegliere i suoi.</p>
      {#if family}
        <ServicePicker known={family.known} selected={family.services} ontoggle={toggle} />
      {/if}
      <div class="f-row"><Button variant="primary" onclick={() => (step = 5)}>Avanti</Button></div>
    {:else}
      <h1>Pronto</h1>
      <ul class="sum">
        {#each done as d (d)}<li>{d}</li>{/each}
        {#if family && family.services.length > 0}<li>Abbonamenti: {family.services.join(', ')}.</li>{/if}
        <li>Stasera ti propone cosa guardare; in Preferenze dici cosa ti piace e in Impostazioni aggiungi le cartelle dei film di casa.</li>
      </ul>
      <div class="f-row"><Button variant="primary" size="l" disabled={busy} onclick={finish}>Comincia</Button></div>
    {/if}
  </div>
</div>

<style>
  .wrap { min-height: 100vh; display: flex; align-items: center; justify-content: center; padding: 24px 16px; }
  .card { width: min(640px, 100%); display: flex; flex-direction: column; gap: 16px; background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius-l); padding: 28px; }
  h1 { font-size: 28px; }
  .steps { display: flex; flex-wrap: wrap; gap: 6px 16px; list-style: none; margin: 0; padding: 0; font-size: 13px; color: var(--muted); }
  .steps li.on { color: var(--text); font-weight: 700; }
  .steps li.past { color: var(--green-text); }
  .form { display: flex; flex-direction: column; gap: 14px; }
  .sum { margin: 0; padding-left: 20px; display: flex; flex-direction: column; gap: 6px; color: var(--text-2); }
</style>
