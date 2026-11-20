// Canali a pagamento della guida e abbonamenti: serve a dividere "Stasera in TV" tra quello che si vede con i propri
// abbonamenti (o in chiaro) e quello che non si vede. Quale canale è a pagamento lo dice il server (payPlatform dei
// programmi, PayChannels.cs): un canale delle playlist o con il numero del digitale terrestre è in chiaro.
import type { GuideProgramme } from './api/client';

/** Piattaforma a pagamento del canale, o null se il canale è in chiaro (o non riconosciuto come a pagamento). */
export function payPlatform(p: GuideProgramme): string | null {
  return p.payPlatform || null;
}

// abbonamenti (nomi di JustWatch/TMDB) che danno accesso ai canali di ogni piattaforma; la stessa tabella è in
// PayChannels.cs, che la usa per ricerca, liste e la mia serata
const access: Record<string, RegExp> = {
  Sky: /^(sky|sky go|now|now tv)$/i,
  DAZN: /^dazn$/i,
  Eurosport: /^(eurosport|discovery\+|dazn|amazon prime video)$/i,
};

/**
 * Piattaforme dei miei abbonamenti dove il film si vede: il canale a pagamento (se ce l'ho) o lo streaming (Netflix,
 * Prime Video...). Vuoto se si vede solo in chiaro o non nei miei abbonamenti.
 */
export function mySubscriptionsFor(p: GuideProgramme, services: string[]): string[] {
  const out: string[] = [];
  const platform = payPlatform(p);
  const re = platform ? access[platform] : undefined;
  if (platform && re && services.some((s) => re.test(s.trim()))) out.push(platform);
  for (const s of streamingFor(p, services)) if (!out.includes(s)) out.push(s);
  return out;
}

/** Piattaforme in streaming dei miei abbonamenti dove il film si vede quando voglio (on demand). */
export function streamingFor(p: { streaming?: string[] | null }, services: string[]): string[] {
  const mine = services.map((s) => s.trim().toLowerCase()).filter(Boolean);
  // "Netflix basic with Ads" vale come "Netflix"
  const hits = (p.streaming ?? []).filter((s) => {
    const n = s.toLowerCase();
    return mine.some((m) => n === m || n.startsWith(m + ' '));
  });
  // le varianti dello stesso servizio ("Amazon Prime Video with Ads") compaiono una volta sola
  return hits.filter((s) => !hits.some((o) => o !== s && s.toLowerCase().startsWith(o.toLowerCase() + ' ')));
}

/** Vero se il programma si vede: canale in chiaro o piattaforma tra gli abbonamenti indicati. */
export function inMySubscriptions(p: GuideProgramme, services: string[]): boolean {
  const platform = payPlatform(p);
  if (platform == null) return true;
  const re = access[platform];
  return !!re && services.some((s) => re.test(s.trim()));
}
