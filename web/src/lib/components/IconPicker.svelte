<script lang="ts">
  // Scelta dell'icona del profilo (creazione e modifica): la prima casella è l'iniziale del nome, poi le venti icone,
  // ciascuna nel colore scelto. Gruppo di pulsanti radio veri, così si usa anche da tastiera.
  import Avatar from './Avatar.svelte';
  import { profileIcons } from '../profileIcons';

  let { value = $bindable(''), color, name, size = 44 }: { value?: string; color: string; name: string; size?: number } = $props();
  const choices = [{ id: '', label: 'Iniziale del nome' }, ...profileIcons];
</script>

<fieldset class="picker">
  <legend>Icona</legend>
  <div class="grid">
    {#each choices as c (c.id)}
      <label class="opt" class:sel={value === c.id} title={c.label}>
        <input type="radio" name="profile-icon" value={c.id} bind:group={value} class="sr-only" />
        <Avatar name={name || '?'} {color} icon={c.id} {size} />
        <span class="sr-only">{c.label}</span>
      </label>
    {/each}
  </div>
</fieldset>

<style>
  .picker { border: 0; margin: 0; padding: 0; min-width: 0; }
  legend { font-size: 14px; color: var(--muted); margin-bottom: 8px; padding: 0; }
  .grid { display: flex; flex-wrap: wrap; gap: 8px; }
  .opt { display: inline-flex; border-radius: 12px; cursor: pointer; padding: 3px; box-shadow: inset 0 0 0 2px transparent; opacity: 0.72; }
  .opt:hover { opacity: 1; }
  .opt.sel { opacity: 1; box-shadow: inset 0 0 0 2px var(--text); }
  .opt:focus-within { box-shadow: var(--focus); opacity: 1; }
</style>
