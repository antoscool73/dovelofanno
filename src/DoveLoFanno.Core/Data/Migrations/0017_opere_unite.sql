-- 24/11/2026: opere doppie dello stesso titolo TMDB unite in una (WorkStore.MergeDuplicates). Nascono dall'anno della guida,
-- che fa parte della chiave dell'opera: «Per qualche dollaro in più» del 1966 e lo stesso film dato 2019 da Rai Movie.
-- L'opera tolta resta come alias (merged_into = l'opera che resta): la guida la ricrea con la stessa chiave a ogni
-- caricamento e così i nuovi passaggi vanno subito sull'opera giusta. Gli alias non compaiono da nessuna parte.
ALTER TABLE works ADD COLUMN merged_into INTEGER REFERENCES works (id) ON DELETE SET NULL;
CREATE INDEX works_merged ON works (merged_into) WHERE merged_into IS NOT NULL;
