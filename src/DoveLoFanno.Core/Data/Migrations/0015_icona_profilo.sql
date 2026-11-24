-- 24/11/2026: icona del profilo (una di una ventina disegnate nell'app) al posto dell'iniziale; vuota = l'iniziale come prima.
ALTER TABLE profiles ADD COLUMN icon TEXT NOT NULL DEFAULT '';
