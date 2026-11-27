#!/usr/bin/env python3
"""Novità della beta: da CHANGELOG.md aggiorna site/public/version.json.

Uso (dalla radice del repository):  python tools/changelog.py
Con --check non scrive niente e fallisce se version.json non è allineato a CHANGELOG.md (per la CI).
La pagina https://dovelofanno.it/beta/novita/ la genera il sito (site/src/pages/beta/novita.astro) leggendo
CHANGELOG.md con le stesse regole: niente da rigenerare qui (dal 27/11/2026).

version.json prende versione e data dalla prima sezione di CHANGELOG.md e, come note, i suoi punti uno per riga
(l'app li mostra così in Impostazioni › Aggiornamenti). Il server legge al massimo 500 caratteri: se i punti non ci
stanno si tengono i primi e si rimanda alla pagina delle novità. L'indirizzo («Scarica e istruzioni») resta com'è.
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / 'CHANGELOG.md'
VERSION = ROOT / 'site' / 'public' / 'version.json'
MAX_NOTES = 500
MORE = '…e altre novità su dovelofanno.it/beta/novita/'


def parse(text: str):
    """[(versione, 'gg/mm/aaaa', [punti])] nell'ordine del file."""
    out = []
    for m in re.finditer(r'^## (\S+) · (\d{2}/\d{2}/\d{4})\s*\n(.*?)(?=^## |\Z)', text, re.M | re.S):
        items = [re.sub(r'\s+', ' ', b).strip() for b in re.findall(r'^- (.+(?:\n  .+)*)', m.group(3), re.M)]
        if not items:
            raise SystemExit(f'CHANGELOG.md: la versione {m.group(1)} non ha punti')
        out.append((m.group(1), m.group(2), items))
    if not out:
        raise SystemExit('CHANGELOG.md: nessuna versione («## X.Y.Z · gg/mm/aaaa»)')
    return out


def notes_of(items):
    lines, size = [], 0
    for it in items:
        line = '• ' + it
        extra = len(line) + (1 if lines else 0)
        if size + extra > MAX_NOTES - len(MORE) - 1 and len(lines) < len(items):
            lines.append(MORE)
            return '\n'.join(lines)
        lines.append(line)
        size += extra
    return '\n'.join(lines)


def main():
    check = '--check' in sys.argv
    versions = parse(SRC.read_text(encoding='utf-8'))
    v, d, items = versions[0]
    dd, mm, yy = d.split('/')
    vj = json.loads(VERSION.read_text(encoding='utf-8'))
    vj.update({'version': v, 'date': f'{yy}-{mm}-{dd}', 'notes': notes_of(items)})
    want_json = json.dumps(vj, ensure_ascii=False, indent=2) + '\n'
    if check:
        if not VERSION.exists() or VERSION.read_text(encoding='utf-8') != want_json:
            raise SystemExit('Da rigenerare con python tools/changelog.py: ' + str(VERSION.relative_to(ROOT)))
        print('version.json allineato a CHANGELOG.md')
        return
    VERSION.write_text(want_json, encoding='utf-8', newline='\n')
    print(f'{v} del {d}: {len(items)} punti, note di {len(vj["notes"])} caratteri')


if __name__ == '__main__':
    main()
