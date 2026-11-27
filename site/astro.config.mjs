// Sito dovelofanno.it (docs/sito.md): home e area beta come pagine Astro scritte a mano, manuale con Starlight in
// /manuale/. Pubblicato con Cloudflare Pages (cartella principale site, comando npm ci && npm run build, uscita dist).
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import sitemap from '@astrojs/sitemap';

export default defineConfig({
  site: 'https://dovelofanno.it',
  trailingSlash: 'always',
  build: { format: 'directory' },
  // la compressione toglie gli a capo accanto ai tag in riga e attacca le parole («scrivi a <code>…» → «scrivi a…»)
  compressHTML: false,
  // Content-Security-Policy: niente security.csp di Astro, che improntava solo gli script elaborati da lui e non quelli
  // is:inline di Starlight (tema, menu), bloccandoli (prova nel browser del 27/11/2026). La calcola
  // tools/csp-headers.mjs dopo la compilazione, dalle pagine vere, e la scrive nell'intestazione HTTP di /manuale/*.
  integrations: [
    // la beta è riservata (Cloudflare Access) e non va nella sitemap
    sitemap({ filter: (page) => !page.includes('/beta/') }),
    starlight({
      title: 'DoveLoFanno',
      description: 'Manuale di DoveLoFanno: cosa guardare stasera tra guida TV, streaming e registrazioni.',
      // logo come nella home e nella beta: icona 36 px e «Dove<span>Lo</span>Fanno» (src/components/SiteTitle.astro)
      components: { SiteTitle: './src/components/SiteTitle.astro' },
      favicon: '/favicon.svg',
      defaultLocale: 'root',
      locales: { root: { label: 'Italiano', lang: 'it' } },
      customCss: ['./src/styles/dlf.css'],
      disable404Route: true, // la 404 è src/pages/404.astro, nello stile del sito
      lastUpdated: false,
      pagination: true,
      tableOfContents: { minHeadingLevel: 2, maxHeadingLevel: 3 },
      expressiveCode: { themes: ['github-dark', 'github-light'] },
      sidebar: [
        { label: 'Manuale', link: '/manuale/' },
        { label: 'Introduzione', items: [{ autogenerate: { directory: 'manuale/introduzione' } }] },
        { label: 'Primi passi', items: [{ autogenerate: { directory: 'manuale/primi-passi' } }] },
        { label: 'Area beta', link: '/beta/', attrs: { rel: 'nofollow' } },
      ],
    }),
  ],
});
