<script lang="ts">
  import Logo from '../lib/components/Logo.svelte';
  import Avatar from '../lib/components/Avatar.svelte';
  import Button from '../lib/components/Button.svelte';
  import IconPicker from '../lib/components/IconPicker.svelte';
  import { api, ApiError, type Profile } from '../lib/api/client';
  import { session } from '../lib/session.svelte';
  import { router } from '../lib/router.svelte';

  let { profiles, onchange }: { profiles: Profile[]; onchange: () => Promise<void> } = $props();

  const colors = ['#6E9BFF', '#34C77B', '#F5A25D', '#C79BFF', '#FF7A9C', '#4FC3D9'];

  let pinFor = $state<Profile | null>(null);
  let pin = $state('');
  let pinError = $state('');
  let adding = $state(false);
  let name = $state('');
  let child = $state(false);
  let color = $state(colors[0]!);
  let icon = $state('');
  let newPin = $state('');
  // «Modifica» dal menu del profilo (24/11/2026): /profili#modifica mostra i profili con la matita; un clic apre la
  // modifica di nome, colore, icona e tipo. PIN e abbonamenti restano in Impostazioni › Profili.
  let editing = $state<Profile | null>(null);
  const managing = $derived((router.nav, router.path === '/profili' && window.location.hash === '#modifica'));
  // dal menu del profilo, un profilo con PIN arriva qui come /profili#pin=ID: si chiede subito il PIN
  $effect(() => {
    void router.nav;
    const m = /^#pin=(\d+)$/.exec(window.location.hash);
    const p = m ? profiles.find((x) => x.id === Number(m[1])) : undefined;
    if (p) choose(p);
  });
  let formError = $state('');
  let busy = $state(false);

  const first = $derived(profiles.length === 0);

  // il campo da compilare prende subito il cursore (tastiera, telecomando)
  function focusOnMount(node: HTMLInputElement) {
    node.focus();
  }

  function enter(p: Profile) {
    session.enter(p);
    router.go('/');
  }

  function startEdit(p: Profile) {
    editing = p;
    name = p.name;
    child = p.isChild;
    color = p.avatar;
    icon = p.icon ?? '';
    formError = '';
  }

  async function saveEdit(e: SubmitEvent) {
    e.preventDefault();
    if (!editing) return;
    busy = true;
    formError = '';
    try {
      const p = await api.updateProfile(editing.id, { name, isChild: child, avatar: color, icon, pin: null, clearPin: null });
      if (session.profile?.id === p.id) session.profile = p;
      await onchange();
      editing = null;
    } catch (err) {
      formError = err instanceof ApiError ? err.message : 'Profilo non salvato';
    } finally {
      busy = false;
    }
  }

  // «Fatto»: si torna a «Chi guarda?»
  const doneManaging = () => router.go('/profili', true);

  function choose(p: Profile) {
    if (!p.hasPin) { enter(p); return; }
    pinFor = p;
    pin = '';
    pinError = '';
  }

  async function unlock(e: SubmitEvent) {
    e.preventDefault();
    if (!pinFor) return;
    busy = true;
    try {
      await api.unlockProfile(pinFor.id, pin);
      enter(pinFor);
    } catch (err) {
      pinError = err instanceof ApiError ? err.message : 'Non è stato possibile controllare il PIN';
      pin = '';
    } finally {
      busy = false;
    }
  }

  function startAdd() {
    adding = true;
    name = '';
    child = false;
    color = colors[profiles.length % colors.length]!;
    icon = '';
    newPin = '';
    formError = '';
  }

  async function add(e: SubmitEvent) {
    e.preventDefault();
    busy = true;
    formError = '';
    try {
      const p = await api.createProfile({ name, isChild: first ? false : child, avatar: color, icon, pin: !child && newPin ? newPin : null });
      await onchange();
      adding = false;
      if (first) enter(p);
    } catch (err) {
      formError = err instanceof ApiError ? err.message : 'Profilo non creato';
    } finally {
      busy = false;
    }
  }
</script>

<main>
  <Logo size={44} />

  {#if first || adding || editing}
    <form class="panel" onsubmit={editing ? saveEdit : add}>
      <h1>{editing ? 'Modifica profilo' : first ? 'Benvenuto. Come ti chiami?' : 'Nuovo profilo'}</h1>
      {#if first}<p class="lead">Sarà il primo profilo, da adulto. Gli altri della famiglia li aggiungi dopo.</p>{/if}
      <label class="field">Nome<input bind:value={name} maxlength="40" required autocomplete="off" use:focusOnMount /></label>
      {#if !first}
        <label class="check"><input type="checkbox" bind:checked={child} /> Profilo bambino: solo programmi adatti, senza PIN</label>
      {/if}
      <fieldset>
        <legend>Colore</legend>
        <div class="colors">
          {#each colors as c (c)}
            <label class="swatch" style:background={c} class:sel={color === c}>
              <input type="radio" name="color" value={c} bind:group={color} class="sr-only" />
              <span class="sr-only">{c}</span>
            </label>
          {/each}
        </div>
      </fieldset>
      <IconPicker bind:value={icon} {color} {name} />
      {#if editing}
        <p class="hint">PIN e abbonamenti si cambiano in Impostazioni › Profili e abbonamenti.</p>
      {:else if !child}
        <label class="field">PIN (facoltativo, da 4 a 8 cifre)<input bind:value={newPin} inputmode="numeric" pattern="[0-9]{'{'}4,8{'}'}" maxlength="8" autocomplete="off" /></label>
      {/if}
      {#if formError}<p class="error" role="alert">{formError}</p>{/if}
      <div class="actions">
        {#if !first}<Button variant="ghost" onclick={() => { adding = false; editing = null; }}>Annulla</Button>{/if}
        <Button variant="primary" size="l" type="submit" disabled={busy || name.trim().length === 0}>{editing ? 'Salva' : first ? 'Inizia' : 'Crea profilo'}</Button>
      </div>
    </form>
  {:else if pinFor}
    <form class="panel narrow" onsubmit={unlock}>
      <Avatar name={pinFor.name} color={pinFor.avatar} icon={pinFor.icon} size={72} />
      <h1>{pinFor.name}</h1>
      <label class="field">PIN<input bind:value={pin} type="password" inputmode="numeric" maxlength="8" autocomplete="off" required use:focusOnMount /></label>
      {#if pinError}<p class="error" role="alert">{pinError}</p>{/if}
      <div class="actions">
        <Button variant="ghost" onclick={() => { pinFor = null; if (window.location.hash) router.go('/profili', true); }}>Indietro</Button>
        <Button variant="primary" size="l" type="submit" disabled={busy || pin.length < 4}>Entra</Button>
      </div>
    </form>
  {:else}
    <h1 class="who">{managing ? 'Modifica i profili' : 'Chi guarda?'}</h1>
    <ul class="grid">
      {#each profiles as p (p.id)}
        <li>
          {#if managing}
            <button type="button" class="tile" onclick={() => startEdit(p)} aria-label="Modifica {p.name}">
              <span class="edit-wrap">
                <Avatar name={p.name} color={p.avatar} icon={p.icon} size={120} />
                <span class="pen" aria-hidden="true"><svg width="34" height="34" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9" /><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4z" /></svg></span>
              </span>
              <span class="tname">{p.name}</span>
              <span class="tmeta">{p.isChild ? 'Bambino' : 'Adulto'}{p.hasPin ? ' · con PIN' : ''}</span>
            </button>
          {:else}
            <button type="button" class="tile" onclick={() => choose(p)} aria-label="{p.name}{p.hasPin ? ', con PIN' : ''}">
              <Avatar name={p.name} color={p.avatar} icon={p.icon} size={120} />
              <span class="tname">{p.name}</span>
              <span class="tmeta">{p.isChild ? 'Bambino' : 'Adulto'}{p.hasPin ? ' · con PIN' : ''}</span>
            </button>
          {/if}
        </li>
      {/each}
      <li>
        <button type="button" class="tile add" onclick={startAdd}>
          <span class="plus" aria-hidden="true">+</span>
          <span class="tname">Aggiungi profilo</span>
        </button>
      </li>
    </ul>
    {#if managing}
      <Button variant="primary" size="l" onclick={doneManaging}>Fatto</Button>
    {:else}
      <p class="note">Il profilo scelto resta in alto a destra in ogni schermata; un clic lo cambia.</p>
      <Button variant="secondary" onclick={() => router.go('/profili#modifica')}>
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 20h9" /><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4z" /></svg>Modifica i profili
      </Button>
    {/if}
  {/if}
</main>

<style>
  main { min-height: 100vh; display: flex; flex-direction: column; align-items: center; gap: 32px; padding: 56px var(--gutter) 64px; }
  h1 { font-size: 32px; text-align: center; }
  .who { font-size: 44px; margin-top: 24px; }
  .grid { list-style: none; margin: 0; padding: 0; display: flex; flex-wrap: wrap; justify-content: center; gap: 24px; }
  .tile {
    width: 180px; display: flex; flex-direction: column; align-items: center; gap: 10px; padding: 16px 8px;
    border-radius: var(--radius-l); border: 2px solid transparent; background: none; cursor: pointer; color: var(--text);
  }
  .tile:hover, .tile:focus-visible { border-color: var(--text); box-shadow: none; }
  .tname { font-size: 18px; font-weight: 600; }
  .tmeta { font-size: 13px; color: var(--muted); }
  .plus {
    width: 120px; height: 120px; border-radius: 30px; border: 2px dashed var(--line-btn);
    display: flex; align-items: center; justify-content: center; font-size: 48px; color: var(--muted);
  }
  .note { color: var(--muted); font-size: 14px; text-align: center; }
  .hint { margin: 0; color: var(--muted); font-size: 13px; }
  .edit-wrap { position: relative; display: inline-flex; }
  .pen {
    position: absolute; inset: 0; border-radius: 30px; display: flex; align-items: center; justify-content: center;
    background: rgba(13, 16, 21, 0.72); color: var(--text);
  }
  .panel {
    width: 100%; max-width: 560px; display: flex; flex-direction: column; gap: 18px;
    background: var(--surface); border: 1px solid var(--line-strong); border-radius: var(--radius-l); padding: 28px;
  }
  .panel.narrow { max-width: 380px; align-items: center; }
  .lead { margin: -6px 0 0; color: var(--text-2); text-align: center; }
  .field { display: flex; flex-direction: column; gap: 8px; font-size: 14px; color: var(--muted); width: 100%; }
  .field input {
    min-height: 48px; padding: 0 14px; border-radius: var(--radius); border: 1px solid var(--line-strong);
    background: var(--card); color: var(--text); font-size: 17px;
  }
  .check { display: flex; align-items: center; gap: 10px; font-size: 15px; color: var(--text-2); }
  .check input { width: 20px; height: 20px; }
  fieldset { border: 0; margin: 0; padding: 0; }
  legend { font-size: 14px; color: var(--muted); margin-bottom: 8px; }
  .colors { display: flex; gap: 10px; }
  .swatch { width: 40px; height: 40px; border-radius: 10px; cursor: pointer; box-shadow: inset 0 0 0 2px transparent; }
  .swatch.sel { box-shadow: 0 0 0 3px var(--bg), 0 0 0 5px var(--text); }
  .swatch:focus-within { box-shadow: var(--focus); }
  .actions { display: flex; justify-content: flex-end; gap: 10px; width: 100%; }
  .error { margin: 0; color: var(--rec-text); font-size: 14px; }
</style>
