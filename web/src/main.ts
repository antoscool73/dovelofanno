import '@fontsource-variable/inter';
import '@fontsource/space-grotesk/500.css';
import '@fontsource/space-grotesk/700.css';
import './lib/theme.css';
import { mount } from 'svelte';
import App from './App.svelte';

// menu a tendina fatti con <details data-dropdown> (filtro «Canali» della guida, generi della ricerca): si chiudono da
// soli con un clic fuori o con Esc (23/11/2026, richiesta di Antonio)
function closeDropdowns(except?: EventTarget | null) {
  for (const d of document.querySelectorAll<HTMLDetailsElement>('details[data-dropdown][open]')) {
    if (except instanceof Node && d.contains(except)) continue;
    d.open = false;
  }
}
document.addEventListener('pointerdown', (e) => closeDropdowns(e.target), true);
document.addEventListener('keydown', (e) => { if (e.key === 'Escape') closeDropdowns(); });

const target = document.getElementById('app');
if (!target) throw new Error('manca #app in index.html');

export default mount(App, { target });
