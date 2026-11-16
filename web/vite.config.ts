import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';

// La build finisce in wwwroot del server: un solo processo serve API e interfaccia (porta 8765).
// In sviluppo (npm run dev) le chiamate /api vanno al server già avviato.
export default defineConfig({
  plugins: [svelte()],
  build: {
    outDir: '../src/DoveLoFanno.Server/wwwroot',
    emptyOutDir: true,
    target: 'es2022',
  },
  server: {
    port: 5173,
    proxy: { '/api': 'http://127.0.0.1:8765' },
  },
});
