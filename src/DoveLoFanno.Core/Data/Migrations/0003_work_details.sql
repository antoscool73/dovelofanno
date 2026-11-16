-- Wave 5 (16/11/2026): dettagli dell'opera da TMDB per la Scheda: trailer (YouTube) e data dell'ultimo aggiornamento
-- dei dettagli (trama, immagini, durata, piattaforme), che si rinfrescano una volta al giorno.
ALTER TABLE works ADD COLUMN trailer TEXT NOT NULL DEFAULT '';
ALTER TABLE works ADD COLUMN details_at INTEGER;
