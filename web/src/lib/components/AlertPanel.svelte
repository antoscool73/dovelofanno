<script lang="ts">
  // "Avvisami": tre avvisi indipendenti per un'opera (in chiaro in TV, nei miei abbonamenti, a noleggio o acquisto).
  // Ogni scelta si salva subito; spegnere un avviso ne tiene le opzioni.
  import { api, ApiError, type AlertKind, type AlertRow, type ProfileSubscriptions } from '../api/client';
  import { session } from '../session.svelte';

  // onchange: quanti avvisi sono accesi (la Scheda lo mostra sul pulsante); onclose: pannello che si chiude (Scheda)
  let { workId, onchange, onclose }: { workId: number; onchange?: (active: number) => void; onclose?: () => void } = $props();

  let alerts = $state<AlertRow[]>([]);
  let subs = $state<ProfileSubscriptions | null>(null);
  let error = $state('');
  let busy = $state(false);

  async function load() {
    const prof = session.profile;
    if (!prof) return;
    try {
      [alerts, subs] = await Promise.all([api.alerts(prof.id, workId), api.profileSubscriptions(prof.id)]);
      error = '';
    } catch (e) {
      error = e instanceof ApiError ? e.message : 'Avvisi non letti';
    }
  }
  $effect(() => { void workId; load(); });

  const of = (k: AlertKind) => alerts.find((a) => a.kind === k && a.active) ?? null;
  const any = (k: AlertKind) => alerts.find((a) => a.kind === k) ?? null;

  async function save(kind: AlertKind, patch: { active?: boolean; channels?: string; autoRecord?: boolean; moments?: string[]; services?: string[]; offers?: string[] }) {
    const prof = session.profile;
    if (!prof) return;
    busy = true;
    try {
      const row = await api.saveAlert(prof.id, {
        workId, kind, active: patch.active ?? null, channels: patch.channels ?? null, autoRecord: patch.autoRecord ?? null,
        moments: patch.moments ?? null, services: patch.services ?? null, offers: patch.offers ?? null,
      });
      alerts = [...alerts.filter((a) => a.id !== row.id), row];
      error = '';
    } catch (e) {
      error = e instanceof ApiError ? e.message : 'Avviso non salvato';
    } finally {
      busy = false;
    }
  }

  function toggleIn(list: string[], v: string) {
    return list.includes(v) ? list.filter((x) => x !== v) : [...list, v];
  }

  const moments: { id: string; label: string }[] = [
    { id: 'appear', label: 'Appena compare in guida' },
    { id: 'eve', label: 'La sera prima' },
    { id: 'soon', label: '30 minuti prima' },
  ];
  const mySubs = $derived(subs?.services ?? []);
  $effect(() => { onchange?.(alerts.filter((a) => a.active).length); });
</script>

<section class="panel" aria-labelledby="av-title">
  <div class="top">
    <h2 id="av-title">Avvisami</h2>
    {#if onclose}<button type="button" class="close" onclick={onclose}>Chiudi</button>{/if}
  </div>
  {#if error}<p class="f-error" role="alert">{error}</p>{/if}

  <div class="alert">
    <label class="f-check head"><input type="checkbox" checked={!!of('tv')} disabled={busy} onchange={(e) => save('tv', { active: e.currentTarget.checked })} /> Quando passa in chiaro in TV</label>
    {#if of('tv')}
      {@const a = of('tv')!}
      <div class="opts">
        <div class="line">
          <span class="lbl">Quando</span>
          <div class="f-chips" role="group" aria-label="Quando avvisare">
            {#each moments as m (m.id)}
              <button type="button" class="f-chip" aria-pressed={a.moments.includes(m.id)} disabled={busy} onclick={() => save('tv', { moments: toggleIn(a.moments, m.id) })}>{m.label}</button>
            {/each}
          </div>
        </div>
        <div class="line">
          <span class="lbl">Canali</span>
          <div class="f-chips" role="group" aria-label="Su quali canali">
            <button type="button" class="f-chip" aria-pressed={a.channels === 'all'} disabled={busy} onclick={() => save('tv', { channels: 'all' })}>Tutti i canali</button>
            <button type="button" class="f-chip" aria-pressed={a.channels === 'playlist'} disabled={busy} onclick={() => save('tv', { channels: 'playlist' })}>Solo quelli che posso registrare</button>
          </div>
        </div>
        <label class="f-check"><input type="checkbox" checked={a.autoRecord} disabled={busy} onchange={(e) => save('tv', { autoRecord: e.currentTarget.checked })} /> Registra da solo quando il canale si può registrare</label>
        <p class="status">{a.status}</p>
      </div>
    {/if}
  </div>

  <div class="alert">
    <label class="f-check head"><input type="checkbox" checked={!!of('subscription')} disabled={busy} onchange={(e) => save('subscription', { active: e.currentTarget.checked })} /> Quando arriva nei miei abbonamenti</label>
    {#if of('subscription')}
      {@const a = of('subscription')!}
      <div class="opts">
        {#if a.services.length === 0}
          <p class="f-muted">Controlla {mySubs.length > 0 ? mySubs.join(', ') : 'tutte le piattaforme in abbonamento (non hai ancora scelto i tuoi abbonamenti: Preferenze › I miei abbonamenti)'}.</p>
        {:else}
          <p class="f-muted">Controlla solo: {a.services.join(', ')}. <button type="button" class="link" onclick={() => save('subscription', { services: [] })}>Usa i miei abbonamenti</button></p>
        {/if}
        <p class="status">{a.status}</p>
      </div>
    {/if}
  </div>

  <div class="alert">
    <label class="f-check head"><input type="checkbox" checked={!!of('rent_buy')} disabled={busy} onchange={(e) => save('rent_buy', { active: e.currentTarget.checked })} /> Quando si può noleggiare o comprare</label>
    {#if of('rent_buy')}
      {@const a = of('rent_buy')!}
      <div class="opts">
        <div class="f-chips" role="group" aria-label="Quali offerte">
          <button type="button" class="f-chip" aria-pressed={a.offers.includes('rent')} disabled={busy} onclick={() => save('rent_buy', { offers: toggleIn(a.offers, 'rent') })}>Noleggio</button>
          <button type="button" class="f-chip" aria-pressed={a.offers.includes('buy')} disabled={busy} onclick={() => save('rent_buy', { offers: toggleIn(a.offers, 'buy') })}>Acquisto</button>
        </div>
        <p class="status">{a.status}</p>
      </div>
    {/if}
  </div>
  <p class="f-muted small foot">Gli avvisi arrivano nella campanella in alto (e su Telegram, se l'hai collegato in Impostazioni).{#if any('subscription') || any('rent_buy')}{' '}Piattaforme da JustWatch tramite TMDB, ricontrollate una volta al giorno.{/if}</p>
</section>

<style>
  .panel { display: flex; flex-direction: column; gap: 6px; background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius-l); padding: 14px 20px 16px; }
  .top { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
  h2 { font-size: 20px; }
  .close { border: 0; background: none; padding: 6px 4px; color: var(--link); font: inherit; font-size: 15px; font-weight: 600; cursor: pointer; }
  .alert { border-top: 1px solid var(--line-soft); padding-top: 4px; }
  .head { font-weight: 600; }
  .opts { display: flex; flex-direction: column; gap: 8px; padding: 0 0 8px 26px; }
  .line { display: flex; flex-wrap: wrap; align-items: center; gap: 6px 12px; }
  .lbl { flex: 0 0 56px; font-size: 13px; color: var(--muted); }
  .foot { margin-top: 4px; line-height: 1.45; }
  .status { margin: 0; font-size: 14px; color: var(--link-hover); }
  .small { font-size: 13px; }
  .link { border: 0; background: none; padding: 0; color: var(--link); font: inherit; cursor: pointer; }
</style>
