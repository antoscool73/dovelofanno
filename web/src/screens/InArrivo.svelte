<script lang="ts">
  // Segnaposto delle schermate della wave 5 non ancora scritte: arrivano dopo la prova in famiglia del prototipo.
  let { title, text }: { title: string; text: string } = $props();
</script>

<section>
  <h1>{title}</h1>
  <p>{text}</p>
  <p class="soon">In preparazione: questa schermata segue il prototipo approvato e arriva dopo la prova in famiglia.</p>
</section>

<style>
  section { padding: 28px var(--gutter); max-width: 900px; display: flex; flex-direction: column; gap: 12px; }
  h1 { font-size: 32px; }
  p { margin: 0; color: var(--text-2); }
  .soon { color: var(--muted); font-size: 14px; }
</style>
