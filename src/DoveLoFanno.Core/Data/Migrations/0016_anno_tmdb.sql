-- 24/11/2026: anno di uscita secondo TMDB, per la Scheda. La guida a volte sbaglia l'anno (Rai Movie: «Per qualche dollaro
-- in più» del 1966 dato come 2019), e works.year non si tocca perché fa parte della chiave dell'opera.
-- NULL = dettagli non ancora riletti con l'anno (il servizio dei voti li rilegge una volta); 0 = TMDB non ha la data.
ALTER TABLE works ADD COLUMN tmdb_year INTEGER;
