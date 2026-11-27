// Dopo astro build: collegamenti interni rotti, ancore mancanti, icone di uno sprite SVG che non esistono e indirizzi
// email fuori da <!--email_off-->. Fallisce la compilazione (e quindi la pubblicazione su Cloudflare Pages).
// I collegamenti esterni non si controllano: i download stanno dietro Cloudflare Access.
import { readFileSync, readdirSync, statSync, existsSync } from 'node:fs';
import { join, relative, sep, posix } from 'node:path';
import { fileURLToPath } from 'node:url';

const dist = fileURLToPath(new URL('../dist/', import.meta.url));
const walk = (dir) => readdirSync(dir).flatMap((n) => {
  const p = join(dir, n);
  return statSync(p).isDirectory() ? walk(p) : [p];
});
const all = walk(dist);
const urlOf = (f) => '/' + relative(dist, f).split(sep).join('/');

const pages = new Map(); // url della pagina → { html, ids }
for (const f of all.filter((f) => f.endsWith('.html'))) {
  const u = urlOf(f);
  const html = readFileSync(f, 'utf-8');
  const ids = new Set([...html.matchAll(/\sid="([^"]+)"/g)].map((m) => m[1]));
  pages.set(u.endsWith('/index.html') ? u.slice(0, -'index.html'.length) : u, { html, ids });
}
const svgIds = new Map(all.filter((f) => f.endsWith('.svg')).map((f) => [urlOf(f), new Set([...readFileSync(f, 'utf-8').matchAll(/\sid="([^"]+)"/g)].map((m) => m[1]))]));

const errors = [];
for (const [url, { html: raw }] of pages) {
  if (url === '/404.html') continue;
  const html = raw.replace(/<!--(?!\/?email_off)[\s\S]*?-->/g, ''); // i commenti (tranne email_off) non contano
  for (const m of html.matchAll(/\s(?:href|src|poster)="([^"]+)"/g)) {
    const v = m[1].replace(/&amp;/g, '&');
    if (/^(https?:|mailto:|tel:|data:|\/\/)/.test(v)) continue;
    const [pathq, frag = ''] = v.split('#');
    const path = pathq.split('?')[0];
    const target = path === '' ? url : path.startsWith('/') ? path : posix.normalize(posix.join(url.endsWith('/') ? url : posix.dirname(url) + '/', path));
    if (target.endsWith('.svg') && frag) {
      if (!svgIds.get(target)?.has(frag)) errors.push(`${url}: icona mancante ${v}`);
      continue;
    }
    const page = pages.get(target) ?? pages.get(target + '/');
    if (page) {
      if (frag && !page.ids.has(decodeURIComponent(frag))) errors.push(`${url}: ancora mancante ${v}`);
      if (!page && !target.endsWith('/')) errors.push(`${url}: manca la barra finale ${v}`);
    } else if (!existsSync(join(dist, ...target.split('/').filter(Boolean)))) {
      errors.push(`${url}: non esiste ${v}`);
    }
  }
  const outside = html.replace(/<!--email_off-->[\s\S]*?<!--\/email_off-->/g, '');
  for (const m of outside.matchAll(/[\w.+-]+@dovelofanno\.it/g)) errors.push(`${url}: email fuori da email_off ${m[0]}`);
}
if (errors.length) { console.error('check-links:\n  ' + errors.join('\n  ')); process.exit(1); }
console.log(`check-links: ${pages.size} pagine, nessun collegamento rotto`);
