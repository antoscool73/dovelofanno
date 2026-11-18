-- 18/11/2026: da dove viene una disponibilità. justwatch = watch providers di TMDB (dati JustWatch); release = originale
-- di una piattaforma dedotto dall'uscita digitale o dalla rete d'origine, finché JustWatch non lo ha in catalogo
-- (es. "Madden", originale Prime Video, il giorno dell'uscita).
ALTER TABLE availability ADD COLUMN source TEXT NOT NULL DEFAULT 'justwatch' CHECK (source IN ('justwatch', 'release'));
