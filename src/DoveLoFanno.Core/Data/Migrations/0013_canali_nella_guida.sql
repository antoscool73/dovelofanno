-- 23/11/2026: scelta a mano dei canali nella guida (Impostazioni › Canali): NULL = automatica (filtro «Canali» della
-- guida), 1 = sempre, 0 = mai. Vale per tutta la famiglia.
ALTER TABLE channels ADD COLUMN guide_show INTEGER;
