-- 20/11/2026: «Registra la serie». Una regola per titolo (title_key) di una serie o di un programma a puntate: le
-- puntate che la guida riporta si programmano da sole, una volta per puntata (episode_key: numero di episodio, titolo
-- della puntata o descrizione), su qualunque canale registrabile; le repliche si saltano. Fermata (active = 0), le
-- registrazioni ancora da fare si annullano.
CREATE TABLE series_rules (
    id          INTEGER PRIMARY KEY,
    profile_id  INTEGER REFERENCES profiles (id) ON DELETE SET NULL,
    title       TEXT    NOT NULL,
    title_key   TEXT    NOT NULL,
    active      INTEGER NOT NULL DEFAULT 1 CHECK (active IN (0, 1)),
    created_at  INTEGER NOT NULL,
    checked_at  INTEGER
);
CREATE UNIQUE INDEX series_rules_active ON series_rules (title_key) WHERE active = 1;

ALTER TABLE recordings ADD COLUMN episode_key TEXT NOT NULL DEFAULT '';
CREATE INDEX recordings_series ON recordings (series_rule_id);
