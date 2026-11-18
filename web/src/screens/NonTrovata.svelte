<script lang="ts">
  import { navigate } from '../lib/router.svelte';
</script>

<section>
  <h1>Pagina non trovata</h1>
  <p>L'indirizzo non corrisponde a nessuna schermata.</p>
  <p><a href="/" onclick={navigate}>Torna a Stasera</a></p>
</section>

<style>
  section { padding: 28px var(--gutter); max-width: 900px; display: flex; flex-direction: column; gap: 12px; }
  h1 { font-size: 32px; }
  p { margin: 0; color: var(--text-2); }
</style>
