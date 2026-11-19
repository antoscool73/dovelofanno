-- 19/11/2026: liste dinamiche. Una lista con query (la ricerca avanzata salvata, JSON) non ha titoli suoi: i titoli
-- sono i risultati della ricerca. list_home = le liste che il profilo vuole nella pagina principale («La mia serata»).
-- Le preferenze («Cosa proporre») diventano liste dinamiche: converted = già trasformata in lista.
ALTER TABLE lists ADD COLUMN query TEXT NOT NULL DEFAULT '';

CREATE TABLE list_home (
    profile_id  INTEGER NOT NULL REFERENCES profiles (id) ON DELETE CASCADE,
    list_id     INTEGER NOT NULL REFERENCES lists (id) ON DELETE CASCADE,
    added_at    INTEGER NOT NULL,
    PRIMARY KEY (profile_id, list_id)
) WITHOUT ROWID;

ALTER TABLE preferences ADD COLUMN converted INTEGER NOT NULL DEFAULT 0 CHECK (converted IN (0, 1));
