// Le versioni di CHANGELOG.md (nella radice del repository), con le stesse regole di tools/changelog.py:
// «## X.Y.Z · gg/mm/aaaa» e i punti «- …»; la sezione «Prossima versione», senza numero, non c'è.
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

export interface Release { version: string; date: string; items: string[] }

// la compilazione parte sempre da site/ (npm run build, anche su Cloudflare Pages)
const path = resolve(process.cwd(), '..', 'CHANGELOG.md');

export function releases(): Release[] {
  const text = readFileSync(path, 'utf-8').replace(/\r\n/g, '\n');
  const out: Release[] = [];
  const re = /^## (\S+) · (\d{2}\/\d{2}\/\d{4})\s*\n([\s\S]*?)(?=^## |(?![\s\S]))/gm;
  for (const m of text.matchAll(re)) {
    const items = [...m[3].matchAll(/^- (.+(?:\n {2}.+)*)/gm)].map((b) => b[1].replace(/\s+/g, ' ').trim());
    if (!items.length) throw new Error(`CHANGELOG.md: la versione ${m[1]} non ha punti`);
    out.push({ version: m[1], date: m[2], items });
  }
  if (!out.length) throw new Error('CHANGELOG.md: nessuna versione («## X.Y.Z · gg/mm/aaaa»)');
  return out;
}

/** Testo di un punto in HTML: tutto escapato, `codice` → <code>. */
export function inline(s: string): string {
  const esc = s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  return esc.replace(/`([^`]+)`/g, '<code>$1</code>');
}
