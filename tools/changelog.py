#!/usr/bin/env python3
"""Novità della beta: da CHANGELOG.md genera site/beta/novita/index.html e aggiorna site/version.json.

Uso (dalla radice del repository):  python tools/changelog.py
Con --check non scrive niente e fallisce se i due file non sono allineati a CHANGELOG.md (per la CI).

version.json prende versione e data dalla prima sezione di CHANGELOG.md e, come note, i suoi punti uno per riga
(l'app li mostra così in Impostazioni › Aggiornamenti). Il server legge al massimo 500 caratteri: se i punti non ci
stanno si tengono i primi e si rimanda alla pagina delle novità. L'indirizzo («Scarica e istruzioni») resta com'è.
"""
import html
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / 'CHANGELOG.md'
PAGE = ROOT / 'site' / 'beta' / 'novita' / 'index.html'
VERSION = ROOT / 'site' / 'version.json'
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


def inline(s: str) -> str:
    s = html.escape(s, quote=False)
    return re.sub(r'`([^`]+)`', r'<code>\1</code>', s)


def page(versions) -> str:
    latest = versions[0][0]
    secs = []
    for v, d, items in versions:
        lis = '\n'.join(f'      <li>{inline(i)}</li>' for i in items)
        secs.append(f'''  <section id="v{v}">
    <h2>{v} <span class="small">· {d}</span></h2>
    <ul>
{lis}
    </ul>
  </section>''')
    body = '\n\n'.join(secs)
    return f'''<!doctype html>
<html lang="it">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex, nofollow">
<title>Novità della beta di DoveLoFanno</title>
<link rel="icon" href="/favicon.svg" type="image/svg+xml">
<link rel="stylesheet" href="/style.css?v=7">
</head>
<body>
<!-- generata da tools/changelog.py a partire da CHANGELOG.md: modificare quello -->
<header class="bar">
  <div class="wrap bar-in">
    <a class="home" href="/" aria-label="DoveLoFanno, torna al sito"><img src="/favicon.svg" alt="" width="32" height="32"><b>Dove<span>Lo</span>Fanno</b></a>
    <a class="tag" href="/beta/" aria-label="Beta {latest}, pagina iniziale della beta">Beta {latest}</a>
  </div>
</header>

<main class="wrap">
  <h1>Novità della beta</h1>
  <p class="lead">Cosa è cambiato in ogni versione, dalla più recente. Per aggiornare segui le istruzioni della
    <a href="/beta/#aggiornare">pagina della beta</a>.</p>

{body}
</main>
</body>
</html>
'''


def main():
    check = '--check' in sys.argv
    versions = parse(SRC.read_text(encoding='utf-8'))
    v, d, items = versions[0]
    dd, mm, yy = d.split('/')
    vj = json.loads(VERSION.read_text(encoding='utf-8'))
    vj.update({'version': v, 'date': f'{yy}-{mm}-{dd}', 'notes': notes_of(items)})
    want_json = json.dumps(vj, ensure_ascii=False, indent=2) + '\n'
    want_page = page(versions)
    if check:
        bad = [p for p, w in ((VERSION, want_json), (PAGE, want_page))
               if not p.exists() or p.read_text(encoding='utf-8') != w]
        if bad:
            raise SystemExit('Da rigenerare con python tools/changelog.py: ' + ', '.join(str(p.relative_to(ROOT)) for p in bad))
        print('Novità allineate a CHANGELOG.md')
        return
    PAGE.parent.mkdir(parents=True, exist_ok=True)
    PAGE.write_text(want_page, encoding='utf-8', newline='\n')
    VERSION.write_text(want_json, encoding='utf-8', newline='\n')
    print(f'{v} del {d}: {len(items)} punti, note di {len(vj["notes"])} caratteri; {len(versions)} versioni nella pagina')


if __name__ == '__main__':
    main()
