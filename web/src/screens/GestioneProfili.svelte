<script lang="ts">
  // Impostazioni › Profili e abbonamenti: nome, colore, bambino, PIN ed eliminazione dei profili; abbonamenti della famiglia.
  import { ask } from '../lib/confirm.svelte';
  import Avatar from '../lib/components/Avatar.svelte';
  import Badge from '../lib/components/Badge.svelte';
  import Button from '../lib/components/Button.svelte';
  import ServicePicker from '../lib/components/ServicePicker.svelte';
  import { api, ApiError, type FamilySubscriptions, type Profile } from '../lib/api/client';
  import { session } from '../lib/session.svelte';

  const colors = ['#6E9BFF', '#34C77B', '#F5A25D', '#C79BFF', '#FF7A9C', '#4FC3D9'];

  let profiles = $state<Profile[]>([]);
  let family = $state<FamilySubscriptions | null>(null);
  let error = $state('');
  let message = $state('');
  let busy = $state(false);

  async function load() {
    try {
      [profiles, family] = await Promise.all([api.profiles(), api.familySubscriptions()]);
      error = '';
    } catch (e) {
      error = e instanceof ApiError ? e.message : 'Profili non letti';
    }
  }
  load();

  async function act(fn: () => Promise<unknown>, ok = '') {
    busy = true;
    message = '';
    try {
      await fn();
      await load();
      message = ok;
      error = '';
      // il profilo attivo si aggiorna in alto a destra
      const me = profiles.find((p) => p.id === session.profile?.id);
      if (me) session.profile = me;
    } catch (e) {
      error = e instanceof ApiError ? e.message : 'Operazione non riuscita';
    } finally {
      busy = false;
    }
  }

  let editId = $state<number | 'new' | null>(null);
  let eName = $state(''), eColor = $state(colors[0]!), eChild = $state(false), ePin = $state(''), eClearPin = $state(false);
  // abbonamenti: ereditati dalla famiglia (predefinito) o una scelta propria, che parte da quelli della famiglia
  let eInherits = $state(true);
  function edit(p: Profile | null) {
    editId = p ? p.id : 'new';
    eName = p?.name ?? '';
    eColor = p?.avatar ?? colors[profiles.length % colors.length]!;
    eChild = p?.isChild ?? false;
    ePin = '';
    eClearPin = false;
    eInherits = p?.inheritsSubscriptions ?? true;
  }
  function save(e: SubmitEvent) {
    e.preventDefault();
    const id = editId;
    if (id === null) return;
    act(async () => {
      if (id === 'new') {
        const p = await api.createProfile({ name: eName, isChild: eChild, avatar: eColor, pin: !eChild && ePin ? ePin : null });
        if (!eInherits) await api.setProfileSubscriptions(p.id, false, family?.services ?? []);
      } else {
        await api.updateProfile(id, { name: eName, isChild: eChild, avatar: eColor, pin: !eChild && ePin ? ePin : null, clearPin: eClearPin });
        const before = profiles.find((p) => p.id === id)?.inheritsSubscriptions ?? true;
        // smettendo di ereditare, la scelta propria parte da quella della famiglia
        if (eInherits !== before) await api.setProfileSubscriptions(id, eInherits, eInherits ? null : family?.services ?? []);
      }
      editId = null;
    }, 'Profilo salvato.');
  }
  async function remove(p: Profile) {
    if (!(await ask({ title: `Eliminare il profilo «${p.name}»?`, text: 'Con le sue liste, preferenze e avvisi. Non si torna indietro.', ok: 'Elimina il profilo' }))) return;
    act(async () => {
      await api.deleteProfile(p.id);
      if (session.profile?.id === p.id) session.leave();
    }, 'Profilo eliminato.');
  }

  let custom = $state('');
  function toggleService(s: string) {
    if (!family) return;
    const next = family.services.includes(s) ? family.services.filter((x) => x !== s) : [...family.services, s];
    act(() => api.setFamilySubscriptions(next));
  }
  function addCustom(e: SubmitEvent) {
    e.preventDefault();
    const s = custom.trim();
    if (!s || !family) return;
    custom = '';
    act(() => api.setFamilySubscriptions([...family!.services, s]));
  }
</script>

<div class="head"><h2>Profili e abbonamenti</h2></div>
{#if error}<p class="f-error" role="alert">{error}</p>{/if}
{#if message}<p class="f-ok" role="status">{message}</p>{/if}

<ul class="f-list">
  {#each profiles as p (p.id)}
    <li>
      <Avatar name={p.name} color={p.avatar} size={40} />
      <span class="pname">{p.name}</span>
      {#if p.isChild}<Badge tone="green">Bambino</Badge>{/if}
      {#if p.hasPin}<Badge>PIN</Badge>{/if}
      {#if !p.inheritsSubscriptions}<Badge tone="blue">Abbonamenti suoi</Badge>{/if}
      <span class="grow"></span>
      <Button size="s" onclick={() => edit(p)}>Modifica</Button>
      <Button size="s" variant="ghost" disabled={busy} onclick={() => remove(p)}>Elimina</Button>
    </li>
  {/each}
</ul>

{#if editId === null}
  <div><Button variant="primary" onclick={() => edit(null)}>Aggiungi un profilo</Button></div>
{:else}
  <form class="card" onsubmit={save}>
    <h3>{editId === 'new' ? 'Nuovo profilo' : 'Modifica profilo'}</h3>
    <div class="f-row">
      <label class="f-field w2">Nome<input bind:value={eName} maxlength="40" required /></label>
      <div class="f-field">Colore
        <div class="colors">
          {#each colors as c (c)}
            <button type="button" class="color" style:background={c} aria-label="Colore {c}" aria-pressed={eColor === c} onclick={() => (eColor = c)}></button>
          {/each}
        </div>
      </div>
    </div>
    <label class="f-check"><input type="checkbox" bind:checked={eChild} /> Profilo bambino (vede solo i titoli per bambini, niente PIN)</label>
    <label class="f-check"><input type="checkbox" bind:checked={eInherits} /> Usa gli abbonamenti della famiglia
      {#if !eInherits}<span class="f-muted">(sceglie i suoi in Preferenze › I miei abbonamenti; si parte da quelli della famiglia)</span>{/if}</label>
    {#if !eChild}
      <div class="f-row">
        <label class="f-field">{editId !== 'new' && profiles.find((p) => p.id === editId)?.hasPin ? 'Nuovo PIN (vuoto = resta quello)' : 'PIN facoltativo (4-8 cifre)'}
          <input bind:value={ePin} inputmode="numeric" pattern="[0-9]{'{'}4,8{'}'}" maxlength="8" autocomplete="off" /></label>
        {#if editId !== 'new' && profiles.find((p) => p.id === editId)?.hasPin}
          <label class="f-check"><input type="checkbox" bind:checked={eClearPin} /> Togli il PIN</label>
        {/if}
      </div>
    {/if}
    <div class="f-row">
      <Button variant="primary" type="submit" disabled={busy}>Salva</Button>
      <Button variant="ghost" onclick={() => (editId = null)}>Annulla</Button>
    </div>
  </form>
{/if}

<h3>Abbonamenti della famiglia</h3>
<p class="f-muted">Le piattaforme che avete: «Dove Lo Fanno» le mette per prime e gli avvisi «nei miei abbonamenti» le controllano. Ogni profilo le eredita, a meno che non scelga le sue in Preferenze.</p>
{#if family}
  <ServicePicker known={family.known} selected={family.services} disabled={busy} ontoggle={toggleService} />
  <form class="f-row" onsubmit={addCustom}>
    <label class="f-field">Un'altra piattaforma<input bind:value={custom} maxlength="60" placeholder="Nome come su JustWatch" /></label>
    <Button type="submit" disabled={busy || !custom.trim()}>Aggiungi</Button>
  </form>
{/if}

<style>
  .head { display: flex; align-items: baseline; justify-content: space-between; }
  h2 { font-size: 24px; }
  h3 { font-size: 17px; margin-top: 10px; }
  .pname { font-weight: 600; }
  .grow { flex: 1 1 0; }
  .card { display: flex; flex-direction: column; gap: 14px; background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius-l); padding: 18px 20px; }
  .w2 { flex: 1 1 240px; }
  .colors { display: flex; gap: 10px; min-height: 42px; align-items: center; }
  .color { width: 32px; height: 32px; border-radius: 8px; border: 2px solid transparent; cursor: pointer; }
  .color[aria-pressed='true'] { border-color: var(--text); }
</style>
