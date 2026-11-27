// Link ai servizi di streaming (26/11/2026, richiesta di Antonio: «i link ai provider devono aprire i provider, magari
// sul film, non TMDB»). TMDB dà per ogni film un solo link, alla sua pagina «dove guardarlo»: i link diretti al film
// sono di JustWatch, che non ha un'API pubblica. Si apre quindi il servizio cercando il titolo dove l'indirizzo di
// ricerca funziona (provati il 26/11), altrimenti la pagina principale del servizio; per i servizi sconosciuti resta
// il link di TMDB. I canali Amazon (gruppo Amazon Prime Video) cercano su Prime Video.

type Provider = { search?: string; home: string };

// chiave: nome del gruppo del catalogo delle piattaforme, in minuscolo; {q} = titolo
const providers: Record<string, Provider> = {
  'netflix': { search: 'https://www.netflix.com/search?q={q}', home: 'https://www.netflix.com/' },
  'amazon prime video': { search: 'https://www.primevideo.com/search/ref=atv_nb_sr?phrase={q}', home: 'https://www.primevideo.com/' },
  'amazon video': { search: 'https://www.primevideo.com/search/ref=atv_nb_sr?phrase={q}', home: 'https://www.primevideo.com/' },
  'apple tv': { search: 'https://tv.apple.com/it/search?term={q}', home: 'https://tv.apple.com/it' },
  'apple tv store': { search: 'https://tv.apple.com/it/search?term={q}', home: 'https://tv.apple.com/it' },
  'rai play': { search: 'https://www.raiplay.it/ricerca.html?q={q}', home: 'https://www.raiplay.it/' },
  'chili': { search: 'https://it.chili.com/search?q={q}', home: 'https://it.chili.com/' },
  'rakuten tv': { search: 'https://www.rakuten.tv/it/search?q={q}', home: 'https://www.rakuten.tv/it' },
  'paramount plus': { search: 'https://www.paramountplus.com/it/search/?q={q}', home: 'https://www.paramountplus.com/it/' },
  'timvision': { search: 'https://www.timvision.it/search?q={q}', home: 'https://www.timvision.it/' },
  'mubi': { search: 'https://mubi.com/it/search/films?query={q}', home: 'https://mubi.com/it' },
  'youtube premium': { search: 'https://www.youtube.com/results?search_query={q}', home: 'https://www.youtube.com/' },
  'pluto tv': { search: 'https://pluto.tv/it/search?query={q}', home: 'https://pluto.tv/it' },
  'hbo max': { search: 'https://play.max.com/search?q={q}', home: 'https://play.max.com/' },
  'disney plus': { home: 'https://www.disneyplus.com/it-it' },
  'mediaset infinity': { home: 'https://mediasetinfinity.mediaset.it/' },
  'infinity+': { home: 'https://mediasetinfinity.mediaset.it/' },
  'now tv': { home: 'https://www.nowtv.it/' },
  'sky go': { home: 'https://skygo.sky.it/' },
  'discovery+': { home: 'https://www.discoveryplus.com/it' },
  'dazn': { home: 'https://www.dazn.com/it-IT/home' },
  'crunchyroll': { home: 'https://www.crunchyroll.com/it' },
  'plex': { home: 'https://watch.plex.tv/' },
};

/** Indirizzo da aprire per un titolo su un servizio (gruppo del catalogo), o null se il servizio non si conosce. */
export function providerUrl(group: string, title: string): { url: string; search: boolean } | null {
  const p = providers[group.trim().toLowerCase()];
  if (!p) return null;
  if (p.search && title.trim()) return { url: p.search.replace('{q}', encodeURIComponent(title.trim())), search: true };
  return { url: p.home, search: false };
}
