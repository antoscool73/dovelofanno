// Ultima versione pubblicata: src/data/release.json, scritto da tools/site-release.py a partire dal release.json che
// release.yml allega al rilascio su GitHub. Prende il posto del «cerca e sostituisci la versione» nelle pagine beta.
import data from '../data/release.json';

export type FileKey = keyof typeof data.files;

export const version = data.version;

export function file(key: FileKey) {
  const f = data.files[key];
  return { ...f, url: data.base + f.name, sizeText: sizeText(f.size) };
}

/** Megabyte decimali, come li mostrano Windows Esplora file e i browser: «5,0 MB», «246 MB». */
export function sizeText(bytes: number): string {
  const mb = bytes / 1_000_000;
  return (mb < 10 ? mb.toFixed(1).replace('.', ',') : Math.round(mb).toString()) + ' MB';
}
