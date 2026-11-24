<script lang="ts">
  // «Informazioni su DoveLoFanno» (24/11/2026, Antonio): si apre con un clic sul logo. Versione, link al sito, alle novità
  // e alla mail della beta, crediti dei dati e dei componenti, licenza. Si chiude solo con «Chiudi» (già col fuoco, quindi
  // basta Invio) o con Esc; un clic fuori non la chiude.
  import Logo from './Logo.svelte';
  import { api } from '../api/client';
  import { navigate } from '../router.svelte';

  let { onclose }: { onclose: () => void } = $props();

  let version = $state('');
  api.updates().then((s) => { version = s.current; }).catch(() => { /* senza versione la finestra serve lo stesso */ });

  function dialog(node: HTMLDialogElement) {
    node.showModal();
    node.addEventListener('cancel', (e) => { e.preventDefault(); onclose(); });
    queueMicrotask(() => node.querySelector<HTMLButtonElement>('button[data-close]')?.focus());
  }

  function go(e: MouseEvent) {
    onclose();
    navigate(e);
  }
</script>

<dialog use:dialog aria-labelledby="about-title">
  <div class="box">
    <div class="brand">
      <Logo size={44} wordmark={false} />
      <div>
        <h2 id="about-title">Dove<span class="lo">Lo</span>Fanno</h2>
        <p class="ver">{version ? `Versione ${version} · beta` : 'Beta'}</p>
      </div>
    </div>
    <p class="lead">Dove vedere stasera film e serie: in TV, in casa o nei tuoi abbonamenti. E quello che passa in TV lo registra sul tuo server di casa, dove restano anche liste, profili e registrazioni.</p>

    <ul class="links">
      <li><a href="https://dovelofanno.it/" target="_blank" rel="noopener noreferrer">dovelofanno.it ↗</a></li>
      <li><a href="https://dovelofanno.it/beta/novita/" target="_blank" rel="noopener noreferrer">Novità di ogni versione ↗</a></li>
      <li><a href="/impostazioni#aggiornamenti" onclick={go}>Controlla gli aggiornamenti</a></li>
      <li><a href="mailto:beta@dovelofanno.it">Scrivici: beta@dovelofanno.it</a></li>
    </ul>

    <h3>Crediti</h3>
    <ul class="credits">
      <li>Dati di film e serie da <a href="https://www.themoviedb.org/" target="_blank" rel="noopener noreferrer">TMDB</a>. Questo prodotto usa l'API di TMDB ma non è approvato né certificato da TMDB.</li>
      <li>Disponibilità sulle piattaforme da JustWatch tramite TMDB; voti IMDb dal file ufficiale di IMDb, se attivati.</li>
      <li>Tipi dei canali italiani dal database di <a href="https://github.com/iptv-org/database" target="_blank" rel="noopener noreferrer">iptv-org</a>.</li>
      <li>Registrazione e anteprima con <a href="https://ffmpeg.org/" target="_blank" rel="noopener noreferrer">FFmpeg</a>, programma esterno.</li>
      <li>Fatto con .NET, SQLite e Svelte; caratteri Inter e Space Grotesk.</li>
    </ul>

    <p class="small">Software libero con licenza <a href="https://www.gnu.org/licenses/gpl-3.0.html" target="_blank" rel="noopener noreferrer">GPL-3.0</a>: niente abbonamento, niente pubblicità. Guida e canali arrivano dalle playlist e dalle guide che scegli tu.</p>

    <div class="actions">
      <button type="button" class="close" data-close onclick={onclose}>Chiudi</button>
    </div>
  </div>
</dialog>

<style>
  dialog {
    width: min(520px, calc(100vw - 32px)); max-height: calc(100vh - 32px); padding: 0; border: 1px solid var(--line-strong);
    border-radius: var(--radius-l); background: var(--surface); color: var(--text);
  }
  dialog::backdrop { background: rgba(5, 7, 10, 0.7); }
  .box { display: flex; flex-direction: column; gap: 14px; padding: 24px 26px 22px; }
  .brand { display: flex; align-items: center; gap: 14px; }
  h2 { font-family: var(--font-display); font-size: 24px; margin: 0; letter-spacing: -0.01em; }
  .lo { color: var(--blue); }
  .ver { margin: 2px 0 0; font-size: 13px; color: var(--muted); }
  .lead { margin: 0; font-size: 15px; line-height: 1.5; color: var(--text-2); }
  h3 { font-size: 15px; margin: 4px 0 0; }
  .links { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: 1fr 1fr; gap: 6px 16px; font-size: 14px; }
  .credits { margin: 0; padding-left: 18px; display: flex; flex-direction: column; gap: 6px; font-size: 13px; line-height: 1.45; color: var(--text-2); }
  .small { margin: 0; font-size: 13px; line-height: 1.45; color: var(--muted); }
  .actions { display: flex; justify-content: flex-end; margin-top: 4px; }
  .close { min-height: 44px; padding: 0 22px; border: 0; border-radius: var(--radius); background: var(--text); color: var(--bg); font: inherit; font-size: 15px; font-weight: 600; cursor: pointer; }
  .close:hover { background: var(--text-2); }
  .close:focus-visible { box-shadow: var(--focus); outline: none; }
  @media (max-width: 520px) { .links { grid-template-columns: 1fr; } }
</style>
