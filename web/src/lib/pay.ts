// Canali a pagamento della guida e abbonamenti: serve a dividere "Stasera in TV" tra quello che si vede con i propri
// abbonamenti (o in chiaro) e quello che non si vede. Un canale delle playlist o con il numero del digitale terrestre
// è in chiaro; tra gli altri si riconoscono quelli delle piattaforme a pagamento note, dal nome.
import type { GuideProgramme } from './api/client';

/** Piattaforma a pagamento del canale, o null se il canale è in chiaro (o non riconosciuto come a pagamento). */
export function payPlatform(p: GuideProgramme): string | null {
  if (p.channelId != null || p.channelNumber != null) return null;
  const n = (p.guideChannelName || p.channelName).toLowerCase();
  if (/\bsky\b/.test(n)) return 'Sky';
  if (/\bdazn\b/.test(n)) return 'DAZN';
  if (/eurosport/.test(n)) return 'Eurosport';
  if (/\b(premium|mgm|amc|cine ?sony)\b/.test(n)) return 'Altri canali a pagamento';
  return null;
}

// abbonamenti (nomi di JustWatch/TMDB) che danno accesso ai canali di ogni piattaforma
const access: Record<string, RegExp> = {
  Sky: /^(sky|sky go|now|now tv)$/i,
  DAZN: /^dazn$/i,
  Eurosport: /^(eurosport|discovery\+|dazn|amazon prime video)$/i,
};

/** Vero se il programma si vede: canale in chiaro o piattaforma tra gli abbonamenti indicati. */
export function inMySubscriptions(p: GuideProgramme, services: string[]): boolean {
  const platform = payPlatform(p);
  if (platform == null) return true;
  const re = access[platform];
  return !!re && services.some((s) => re.test(s.trim()));
}
