#!/usr/bin/env python3
"""Pagine di installazione: file, dimensioni e impronte dell'ultima versione in site/src/data/release.json.

Uso (dalla radice del repository, dopo che release.yml ha finito):  python tools/site-release.py v0.5.0
Scarica con gh il release.json che release.yml allega al rilascio e lo copia nel sito; versione, nomi, dimensioni e
impronte di tutte le pagine di /installa/ vengono da lì (prima si cercava e sostituiva il numero in ogni pagina).
La pagina QNAP non lo usa finché non è provato che Container Station importa i .tar.gz del rilascio (docs/sito.md).
"""
import json
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEST = ROOT / 'site' / 'src' / 'data' / 'release.json'
KEYS = {'win-x64', 'docker-amd64', 'docker-arm64', 'src'}


def main():
    if len(sys.argv) != 2 or not sys.argv[1].startswith('v'):
        raise SystemExit('uso: python tools/site-release.py vX.Y.Z')
    tag = sys.argv[1]
    with tempfile.TemporaryDirectory() as tmp:
        subprocess.run(['gh', 'release', 'download', tag, '-p', 'release.json', '-D', tmp], check=True, cwd=ROOT)
        data = json.loads((Path(tmp) / 'release.json').read_text(encoding='utf-8'))
    if data.get('version') != tag[1:]:
        raise SystemExit(f'release.json del rilascio {tag} dice {data.get("version")}')
    if set(data.get('files', {})) != KEYS:
        raise SystemExit(f'release.json: file attesi {sorted(KEYS)}, trovati {sorted(data.get("files", {}))}')
    DEST.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8', newline='\n')
    sizes = ', '.join(f'{k} {v["size"] / 1e6:.1f} MB' for k, v in data['files'].items())
    print(f'{DEST.relative_to(ROOT)}: {data["version"]} ({sizes})')


if __name__ == '__main__':
    main()
