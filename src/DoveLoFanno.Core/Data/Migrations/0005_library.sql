-- Wave 6 (17/11/2026): libreria. Cartelle del NAS da scansionare e, per ogni file, quello che si capisce dal nome
-- (titolo, anno, stagione, episodio) e le tracce lette da ffprobe; serve a mostrare la coda "da sistemare" e a
-- decidere se il browser lo riproduce così com'è o se serve una copia (browser_copy). La copia si fa solo quando
-- qualcuno vuole guardare il file (copy_state: '' = si guarda così, needed = serve, queued, working, done, failed):
-- farla per tutto il NAS raddoppierebbe lo spazio occupato.

CREATE TABLE library_folders (
    id               INTEGER PRIMARY KEY,
    path             TEXT    NOT NULL UNIQUE,
    kind             TEXT    NOT NULL DEFAULT 'auto' CHECK (kind IN ('auto', 'films', 'series')),
    enabled          INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0, 1)),
    last_scanned_at  INTEGER,
    last_error       TEXT    NOT NULL DEFAULT '',
    files            INTEGER NOT NULL DEFAULT 0
);

ALTER TABLE library_items ADD COLUMN folder_id INTEGER REFERENCES library_folders (id) ON DELETE CASCADE;
ALTER TABLE library_items ADD COLUMN title_guess TEXT NOT NULL DEFAULT '';
ALTER TABLE library_items ADD COLUMN year_guess INTEGER NOT NULL DEFAULT 0;
ALTER TABLE library_items ADD COLUMN season INTEGER;
ALTER TABLE library_items ADD COLUMN episode INTEGER;
ALTER TABLE library_items ADD COLUMN width INTEGER;
ALTER TABLE library_items ADD COLUMN height INTEGER;
ALTER TABLE library_items ADD COLUMN audio_tracks TEXT NOT NULL DEFAULT '[]';     -- JSON: [{"index","codec","language","title"}]
ALTER TABLE library_items ADD COLUMN subtitle_tracks TEXT NOT NULL DEFAULT '[]';  -- JSON: [{"index","codec","language","title"}]
ALTER TABLE library_items ADD COLUMN modified_at INTEGER;                          -- data del file: si riletta solo se cambia
ALTER TABLE library_items ADD COLUMN match_state TEXT NOT NULL DEFAULT 'pending'
    CHECK (match_state IN ('pending', 'matched', 'local', 'unmatched', 'manual'));
ALTER TABLE library_items ADD COLUMN copy_state TEXT NOT NULL DEFAULT ''
    CHECK (copy_state IN ('', 'needed', 'queued', 'working', 'done', 'failed'));
CREATE INDEX library_items_folder ON library_items (folder_id);
