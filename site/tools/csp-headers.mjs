// Dopo astro build: la Content-Security-Policy del manuale (docs/sito.md), calcolata dalle pagine compilate e scritta
// nell'intestazione HTTP di /manuale/* (dist/_headers, riga «Content-Security-Policy: @MANUALE@»).
// - script in linea di Starlight (tema chiaro/scuro, menu…): ognuno passa solo con la sua impronta SHA-256;
// - blocchi <style> in linea: idem;
// - attributi style="" (Starlight, blocchi di codice): non si possono improntare, quindi 'unsafe-inline' ma solo in
//   style-src-attr;
// - 'wasm-unsafe-eval': la ricerca Pagefind usa WebAssembly.
// Controlla anche che home, installazione e 404 non abbiano niente in linea, perché lì vale la CSP rigida di /*.
import { readFileSync, writeFileSync, readdirSync, statSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { join, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const dist = fileURLToPath(new URL('../dist/', import.meta.url));
const files = (dir) => readdirSync(dir).flatMap((n) => {
  const p = join(dir, n);
  return statSync(p).isDirectory() ? files(p) : p.endsWith('.html') ? [p] : [];
});
const sha = (s) => `'sha256-${createHash('sha256').update(s, 'utf8').digest('base64')}'`;
// script eseguibili: senza src e senza un type di soli dati (JSON-LD, JSON)
const inlineScripts = (html) => [...html.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi)]
  .filter((m) => !/\bsrc\s*=/i.test(m[1]) && !/\btype\s*=\s*"application\/(ld\+)?json"/i.test(m[1]))
  .map((m) => m[2]);
const inlineStyles = (html) => [...html.matchAll(/<style\b[^>]*>([\s\S]*?)<\/style>/gi)].map((m) => m[1]);

const scripts = new Set();
const styles = new Set();
const errors = [];
let manuale = 0;
for (const f of files(dist)) {
  const url = '/' + relative(dist, f).split(sep).join('/');
  const html = readFileSync(f, 'utf-8');
  if (/<meta http-equiv="content-security-policy"/i.test(html)) errors.push(`${url}: <meta> CSP (va solo nell'intestazione)`);
  if (url.startsWith('/manuale/')) {
    manuale++;
    inlineScripts(html).forEach((s) => scripts.add(sha(s)));
    inlineStyles(html).forEach((s) => styles.add(sha(s)));
    if (/\son[a-z]+\s*=\s*"/i.test(html)) errors.push(`${url}: attributo on…= (non si può permettere)`);
  } else {
    if (inlineScripts(html).some((s) => s.trim())) errors.push(`${url}: script in linea`);
    if (inlineStyles(html).length) errors.push(`${url}: <style> in linea`);
    if (/\sstyle\s*=\s*"/i.test(html)) errors.push(`${url}: attributo style=`);
    if (/\son[a-z]+\s*=\s*"/i.test(html)) errors.push(`${url}: attributo on…=`);
  }
}
if (!manuale) errors.push('nessuna pagina in /manuale/');

const csp = [
  "default-src 'self'",
  "img-src 'self' data:",
  ["script-src 'self' 'wasm-unsafe-eval'", ...scripts].join(' '),
  ["style-src-elem 'self'", ...styles].join(' '),
  "style-src-attr 'unsafe-inline'",
  "base-uri 'self'",
  "object-src 'none'",
  "form-action 'self'",
  "frame-ancestors 'none'",
].join('; ');

const hp = join(dist, '_headers');
const headers = readFileSync(hp, 'utf-8');
const line = /^([ \t]*Content-Security-Policy: )@MANUALE@(?=\r?$)/m; // anche con gli a capo di Windows
if (!line.test(headers)) errors.push('dist/_headers: manca la riga «Content-Security-Policy: @MANUALE@»');
if (errors.length) { console.error('csp-headers:\n  ' + errors.join('\n  ')); process.exit(1); }
const out = headers.replace(line, (_, pre) => pre + csp);
if (out.includes('@MANUALE@')) { console.error('csp-headers: segnaposto rimasto in dist/_headers'); process.exit(1); }
writeFileSync(hp, out);
console.log(`csp-headers: ${manuale} pagine del manuale, ${scripts.size} script e ${styles.size} stili in linea improntati, intestazione di ${csp.length} caratteri`);
