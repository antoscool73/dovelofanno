-- 20/11/2026: «Converti...» come in RegistraTV. Una registrazione o un file della libreria in MP4 o MP3, in coda e in
-- background (uno alla volta); il file nuovo va accanto all'originale, che resta (o in «Convertiti» nella cartella delle
-- registrazioni se la cartella dell'originale è in sola lettura, come il NAS). method = come è riuscita: copy (senza
-- ricodifica), audio (audio in AAC), video (H.264 + AAC), mp3.
CREATE TABLE conversions (
    id           INTEGER PRIMARY KEY,
    profile_id   INTEGER REFERENCES profiles (id) ON DELETE SET NULL,
    source       TEXT    NOT NULL,
    title        TEXT    NOT NULL,
    format       TEXT    NOT NULL CHECK (format IN ('mp4', 'mp3')),
    state        TEXT    NOT NULL DEFAULT 'queued' CHECK (state IN ('queued', 'running', 'done', 'failed', 'cancelled')),
    output       TEXT    NOT NULL DEFAULT '',
    method       TEXT    NOT NULL DEFAULT '',
    error        TEXT    NOT NULL DEFAULT '',
    log          TEXT    NOT NULL DEFAULT '',
    created_at   INTEGER NOT NULL,
    finished_at  INTEGER
);
CREATE INDEX conversions_state ON conversions (state, id);
