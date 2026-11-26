<script lang="ts">
  // «Visto» da leggere, non da toccare (26/11/2026): il tondo verde con l'occhio pieno al posto del cartellino «Visto»,
  // per i file in casa e le righe dove il visto è del file e non dell'opera.
  import EyeIcon from './EyeIcon.svelte';
  let { text = 'Visto' }: { text?: string } = $props();
</script>

<span class="mark" role="img" aria-label={text} title={text}><EyeIcon filled size={15} /></span>

<style>
  .mark {
    display: inline-flex; align-items: center; justify-content: center; flex: 0 0 auto;
    width: 26px; height: 26px; border-radius: 999px; background: var(--green, #34c77b); color: #0d1015;
  }
</style>
