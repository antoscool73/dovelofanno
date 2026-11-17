-- Wave 6 (17/11/2026): generi TMDB dell'opera (JSON di numeri, come genre_ids), per mostrare al profilo bambino solo
-- i titoli adatti (16 animazione, 10751 famiglia, 10762 bambini) anche quando non passano in guida tra i "Bambini".
ALTER TABLE works ADD COLUMN genres TEXT NOT NULL DEFAULT '[]';
