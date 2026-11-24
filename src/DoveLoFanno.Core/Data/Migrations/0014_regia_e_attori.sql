-- 24/11/2026: regia e attori principali da TMDB per la Scheda (prima solo dalla guida, che spesso non li ha o non c'è
-- per i titoli solo in streaming o in casa). I dettagli già scaricati si danno per vecchi: si riscaricano alla prima
-- apertura della Scheda o dal servizio dei voti.
ALTER TABLE works ADD COLUMN directors TEXT NOT NULL DEFAULT '';
ALTER TABLE works ADD COLUMN actors TEXT NOT NULL DEFAULT '';
UPDATE works SET details_at = 1 WHERE details_at IS NOT NULL;
