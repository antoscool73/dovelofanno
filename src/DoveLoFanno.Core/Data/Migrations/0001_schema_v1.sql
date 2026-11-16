-- Schema v1 di DoveLoFanno (wave 1, 16/11/2026).
-- Convenzioni: orari in secondi Unix UTC (INTEGER, nomi *_at, start, stop); booleani 0/1; testi liberi in TEXT;
-- opzioni variabili in JSON (TEXT). Le tabelle seguono "Modello dei dati" del documento di progetto.
-- Questo file non si modifica più dopo il rilascio: ogni cambiamento è una nuova migrazione.

-- ---------------------------------------------------------------- famiglia e profili

CREATE TABLE profiles (
    id                      INTEGER PRIMARY KEY,
    name                    TEXT    NOT NULL UNIQUE,
    is_child                INTEGER NOT NULL DEFAULT 0 CHECK (is_child IN (0, 1)),
    avatar                  TEXT    NOT NULL DEFAULT '',
    pin_hash                TEXT,                                   -- PIN facoltativo per gli adulti
    excluded_groups         TEXT    NOT NULL DEFAULT '[]',          -- JSON: gruppi di Genres esclusi
    inherits_subscriptions  INTEGER NOT NULL DEFAULT 1 CHECK (inherits_subscriptions IN (0, 1)),
    created_at              INTEGER NOT NULL
);

-- abbonamenti: profile_id NULL = della famiglia
CREATE TABLE subscriptions (
    id          INTEGER PRIMARY KEY,
    profile_id  INTEGER REFERENCES profiles (id) ON DELETE CASCADE,
    service     TEXT    NOT NULL                                    -- id del provider TMDB o nome
);
-- in SQLite i NULL di UNIQUE sono tutti diversi: due indici parziali, uno per la famiglia e uno per i profili
CREATE UNIQUE INDEX subscriptions_family ON subscriptions (service) WHERE profile_id IS NULL;
CREATE UNIQUE INDEX subscriptions_profile ON subscriptions (profile_id, service) WHERE profile_id IS NOT NULL;

-- impostazioni generali (margini, cartelle, MaxParallel, MinFreeGB, chiave TMDB...)
CREATE TABLE settings (
    key    TEXT PRIMARY KEY,
    value  TEXT NOT NULL
) WITHOUT ROWID;

-- ---------------------------------------------------------------- sorgenti, canali, flussi

-- playlist M3U dell'utente
CREATE TABLE sources (
    id               INTEGER PRIMARY KEY,
    name             TEXT    NOT NULL,
    url              TEXT    NOT NULL UNIQUE,                       -- http(s) o file locale
    xmltv_url        TEXT    NOT NULL DEFAULT '',                   -- guida indicata dalla playlist (x-tvg-url)
    max_connections  INTEGER CHECK (max_connections IS NULL OR max_connections > 0),
    user_agent       TEXT    NOT NULL DEFAULT '',
    enabled          INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0, 1)),
    priority         INTEGER NOT NULL DEFAULT 0,
    last_checked_at  INTEGER,
    last_error       TEXT    NOT NULL DEFAULT ''
);

-- guide XMLTV (anche più di una: per ogni canale vale quella che arriva più lontano)
CREATE TABLE guide_sources (
    id              INTEGER PRIMARY KEY,
    url             TEXT    NOT NULL UNIQUE,
    enabled         INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0, 1)),
    last_loaded_at  INTEGER,
    last_stop       INTEGER,                                        -- fine dell'ultimo programma letto
    last_error      TEXT    NOT NULL DEFAULT ''
);

-- canali della guida: anche quelli che non sono in nessuna playlist (avvisi senza registrazione)
CREATE TABLE guide_channels (
    id               INTEGER PRIMARY KEY,
    guide_source_id  INTEGER NOT NULL REFERENCES guide_sources (id) ON DELETE CASCADE,
    xmltv_id         TEXT    NOT NULL,
    name             TEXT    NOT NULL DEFAULT '',
    icon             TEXT    NOT NULL DEFAULT '',
    UNIQUE (guide_source_id, xmltv_id)
);

-- canale con identità stabile (tvg-id o nome normalizzato, Titles.ChannelKey), indipendente dalle playlist
CREATE TABLE channels (
    id                INTEGER PRIMARY KEY,
    key               TEXT    NOT NULL UNIQUE,
    name              TEXT    NOT NULL,
    logo              TEXT    NOT NULL DEFAULT '',
    group_title       TEXT    NOT NULL DEFAULT '',
    excluded          INTEGER NOT NULL DEFAULT 0 CHECK (excluded IN (0, 1)),
    guide_channel_id  INTEGER REFERENCES guide_channels (id) ON DELETE SET NULL
);

-- un URL di un canale in una playlist; la registrazione sceglie il flusso all'ultimo momento
CREATE TABLE streams (
    id               INTEGER PRIMARY KEY,
    channel_id       INTEGER NOT NULL REFERENCES channels (id) ON DELETE CASCADE,
    source_id        INTEGER NOT NULL REFERENCES sources (id) ON DELETE CASCADE,
    url              TEXT    NOT NULL,
    kind             TEXT    NOT NULL DEFAULT 'unknown' CHECK (kind IN ('hls', 'dash', 'ts', 'unknown')),
    state            TEXT    NOT NULL DEFAULT 'unchecked'
                     CHECK (state IN ('unchecked', 'ok', 'drm', 'unreachable', 'geo', 'limit')),
    priority         INTEGER NOT NULL DEFAULT 0,                    -- scelta dall'utente, più bassa = prima
    width            INTEGER,
    height           INTEGER,
    video_codec      TEXT    NOT NULL DEFAULT '',
    audio_codec      TEXT    NOT NULL DEFAULT '',
    user_agent       TEXT    NOT NULL DEFAULT '',                   -- http-user-agent o #EXTVLCOPT
    last_checked_at  INTEGER,
    UNIQUE (source_id, channel_id, url)
);
CREATE INDEX streams_channel ON streams (channel_id, priority);

-- ---------------------------------------------------------------- opere e guida

-- l'opera (film, serie, episodio, documentario, programma): guida, registrazioni, libreria e
-- "dove lo vedo" puntano qui. work_key è il WorkKey di RegistraTV (titolo normalizzato|anno per i film).
CREATE TABLE works (
    id              INTEGER PRIMARY KEY,
    kind            TEXT    NOT NULL CHECK (kind IN ('film', 'series', 'episode', 'documentary', 'programme')),
    work_key        TEXT    NOT NULL UNIQUE,
    title           TEXT    NOT NULL,
    title_key       TEXT    NOT NULL,                               -- Titles.TitleKey
    original_title  TEXT    NOT NULL DEFAULT '',
    year            INTEGER NOT NULL DEFAULT 0,
    parent_id       INTEGER REFERENCES works (id) ON DELETE CASCADE, -- episodio -> serie
    season          INTEGER,
    episode         INTEGER,
    tmdb_id         INTEGER,
    tmdb_kind       TEXT    CHECK (tmdb_kind IS NULL OR tmdb_kind IN ('movie', 'tv')),
    imdb_id         TEXT    NOT NULL DEFAULT '',
    rating          REAL,                                           -- voto mostrato (IMDb, altrimenti TMDB)
    rating_source   TEXT    NOT NULL DEFAULT '' CHECK (rating_source IN ('', 'IMDb', 'TMDB')),
    rating_votes    INTEGER NOT NULL DEFAULT 0,
    overview        TEXT    NOT NULL DEFAULT '',
    poster          TEXT    NOT NULL DEFAULT '',
    backdrop        TEXT    NOT NULL DEFAULT '',
    runtime_min     INTEGER,
    updated_at      INTEGER NOT NULL
);
CREATE INDEX works_title_key ON works (title_key, year);
CREATE INDEX works_parent ON works (parent_id, season, episode);
CREATE INDEX works_tmdb ON works (tmdb_kind, tmdb_id);

CREATE TABLE programmes (
    id                INTEGER PRIMARY KEY,
    guide_channel_id  INTEGER NOT NULL REFERENCES guide_channels (id) ON DELETE CASCADE,
    start             INTEGER NOT NULL,
    stop              INTEGER NOT NULL CHECK (stop >= start),
    title             TEXT    NOT NULL,
    subtitle          TEXT    NOT NULL DEFAULT '',
    description       TEXT    NOT NULL DEFAULT '',
    categories        TEXT    NOT NULL DEFAULT '[]',                -- JSON, come nella guida
    group_name        TEXT    NOT NULL CHECK (group_name IN ('Film', 'Serie TV', 'Documentari', 'Notizie', 'Sport', 'Intrattenimento', 'Bambini', 'Altro')),
    sub_genre         TEXT    NOT NULL DEFAULT '',
    group_guessed     INTEGER NOT NULL DEFAULT 0 CHECK (group_guessed IN (0, 1)),
    year              INTEGER NOT NULL DEFAULT 0,
    country           TEXT    NOT NULL DEFAULT '',
    directors         TEXT    NOT NULL DEFAULT '',
    actors            TEXT    NOT NULL DEFAULT '',
    episode           TEXT    NOT NULL DEFAULT '',
    icon              TEXT    NOT NULL DEFAULT '',
    work_id           INTEGER REFERENCES works (id) ON DELETE SET NULL,
    UNIQUE (guide_channel_id, start, title)
);
CREATE INDEX programmes_time ON programmes (start, stop);
CREATE INDEX programmes_work ON programmes (work_id);

-- cache dei voti: stesse colonne di ratings\cache.tsv (RatingCache), chiave titolo normalizzato|anno della guida
CREATE TABLE rating_cache (
    key             TEXT    PRIMARY KEY,
    found           INTEGER NOT NULL CHECK (found IN (0, 1)),
    tmdb_id         INTEGER NOT NULL DEFAULT 0,
    imdb_id         TEXT    NOT NULL DEFAULT '',
    original_title  TEXT    NOT NULL DEFAULT '',
    year            INTEGER NOT NULL DEFAULT 0,
    tmdb_vote       REAL    NOT NULL DEFAULT 0,
    tmdb_count      INTEGER NOT NULL DEFAULT 0,
    imdb_vote       REAL    NOT NULL DEFAULT 0,
    imdb_votes      INTEGER NOT NULL DEFAULT 0,
    checked_at      INTEGER NOT NULL,
    imdb_checked_at INTEGER,
    match_version   INTEGER NOT NULL
) WITHOUT ROWID;

-- ---------------------------------------------------------------- registrazioni e libreria

CREATE TABLE recordings (
    id                INTEGER PRIMARY KEY,
    channel_id        INTEGER NOT NULL REFERENCES channels (id),    -- il canale, non l'URL
    work_id           INTEGER REFERENCES works (id) ON DELETE SET NULL,
    programme_id      INTEGER REFERENCES programmes (id) ON DELETE SET NULL,
    profile_id        INTEGER REFERENCES profiles (id) ON DELETE SET NULL, -- chi l'ha chiesta
    title             TEXT    NOT NULL,
    start             INTEGER NOT NULL,                             -- orari del programma
    stop              INTEGER NOT NULL CHECK (stop > start),
    before_min        INTEGER NOT NULL DEFAULT 0 CHECK (before_min >= 0),
    after_min         INTEGER NOT NULL DEFAULT 0 CHECK (after_min >= 0),
    format            TEXT    NOT NULL DEFAULT 'mp4' CHECK (format IN ('mp4', 'mp3')),
    state             TEXT    NOT NULL DEFAULT 'scheduled'
                      CHECK (state IN ('scheduled', 'recording', 'merging', 'completed', 'incomplete', 'stopped', 'failed', 'cancelled')),
    lost_minutes      INTEGER NOT NULL DEFAULT 0,
    lost_ranges       TEXT    NOT NULL DEFAULT '[]',                -- JSON: [[inizio, fine], ...]
    file              TEXT    NOT NULL DEFAULT '',
    log               TEXT    NOT NULL DEFAULT '',
    remind            INTEGER NOT NULL DEFAULT 0 CHECK (remind IN (0, 1)),
    reminded_eve      INTEGER NOT NULL DEFAULT 0 CHECK (reminded_eve IN (0, 1)),
    reminded_soon     INTEGER NOT NULL DEFAULT 0 CHECK (reminded_soon IN (0, 1)),
    series_rule_id    INTEGER,                                      -- "Registra la serie" (tabella futura)
    created_at        INTEGER NOT NULL
);
CREATE INDEX recordings_time ON recordings (state, start);

-- spezzoni TS: ognuno ricorda da quale flusso viene
CREATE TABLE recording_parts (
    id            INTEGER PRIMARY KEY,
    recording_id  INTEGER NOT NULL REFERENCES recordings (id) ON DELETE CASCADE,
    part_no       INTEGER NOT NULL,
    stream_id     INTEGER REFERENCES streams (id) ON DELETE SET NULL,
    url           TEXT    NOT NULL DEFAULT '',                      -- URL risolto (dopo i reindirizzamenti)
    file          TEXT    NOT NULL,
    started_at    INTEGER NOT NULL,
    ended_at      INTEGER,
    exit_reason   TEXT    NOT NULL DEFAULT '',
    UNIQUE (recording_id, part_no)
);

CREATE TABLE library_items (
    id            INTEGER PRIMARY KEY,
    path          TEXT    NOT NULL UNIQUE,
    work_id       INTEGER REFERENCES works (id) ON DELETE SET NULL, -- NULL = da sistemare
    origin        TEXT    NOT NULL CHECK (origin IN ('recorded', 'imported')),
    recording_id  INTEGER REFERENCES recordings (id) ON DELETE SET NULL,
    duration_s    INTEGER,
    size_bytes    INTEGER,
    container     TEXT    NOT NULL DEFAULT '',
    video_codec   TEXT    NOT NULL DEFAULT '',
    audio_codec   TEXT    NOT NULL DEFAULT '',
    browser_copy  TEXT    NOT NULL DEFAULT '',                      -- copia per il browser, se serve ricodificare
    added_at      INTEGER NOT NULL,
    missing_since INTEGER                                           -- file sparito dal disco
);
CREATE INDEX library_items_work ON library_items (work_id);

-- ---------------------------------------------------------------- per profilo

-- visto, punto di ripresa e voto personale: per opera (anche episodio) o per file
CREATE TABLE watch_states (
    id          INTEGER PRIMARY KEY,
    profile_id  INTEGER NOT NULL REFERENCES profiles (id) ON DELETE CASCADE,
    work_id     INTEGER REFERENCES works (id) ON DELETE CASCADE,
    item_id     INTEGER REFERENCES library_items (id) ON DELETE CASCADE,
    watched     INTEGER NOT NULL DEFAULT 0 CHECK (watched IN (0, 1)),
    position_s  INTEGER NOT NULL DEFAULT 0,
    stars       INTEGER CHECK (stars IS NULL OR stars BETWEEN 1 AND 5),
    updated_at  INTEGER NOT NULL,
    CHECK (work_id IS NOT NULL OR item_id IS NOT NULL)
);
CREATE UNIQUE INDEX watch_states_work ON watch_states (profile_id, work_id) WHERE item_id IS NULL;
CREATE UNIQUE INDEX watch_states_item ON watch_states (profile_id, item_id) WHERE item_id IS NOT NULL;

-- liste: profile_id NULL = della famiglia; is_tonight = "La mia serata in TV", una per profilo, non cancellabile
CREATE TABLE lists (
    id          INTEGER PRIMARY KEY,
    profile_id  INTEGER REFERENCES profiles (id) ON DELETE CASCADE,
    name        TEXT    NOT NULL,
    is_tonight  INTEGER NOT NULL DEFAULT 0 CHECK (is_tonight IN (0, 1)),
    created_at  INTEGER NOT NULL,
    CHECK (is_tonight = 0 OR profile_id IS NOT NULL)
);
CREATE UNIQUE INDEX lists_tonight ON lists (profile_id) WHERE is_tonight = 1;

CREATE TABLE list_items (
    list_id   INTEGER NOT NULL REFERENCES lists (id) ON DELETE CASCADE,
    work_id   INTEGER NOT NULL REFERENCES works (id) ON DELETE CASCADE,
    position  INTEGER NOT NULL DEFAULT 0,
    added_at  INTEGER NOT NULL,
    PRIMARY KEY (list_id, work_id)
) WITHOUT ROWID;

-- preferenze che generano le proposte (Rules di RegistraTV), per profilo
CREATE TABLE preferences (
    id          INTEGER PRIMARY KEY,
    profile_id  INTEGER NOT NULL REFERENCES profiles (id) ON DELETE CASCADE,
    name        TEXT    NOT NULL,
    text        TEXT    NOT NULL DEFAULT '',
    group_name  TEXT    NOT NULL DEFAULT '',
    sub_genre   TEXT    NOT NULL DEFAULT '',
    min_rating  REAL    NOT NULL DEFAULT 0,
    enabled     INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0, 1))
);

-- "Non proporre più" (ignored.txt) e proposte già notificate (proposals-seen.txt), per profilo
CREATE TABLE ignored_works (
    profile_id  INTEGER NOT NULL REFERENCES profiles (id) ON DELETE CASCADE,
    work_key    TEXT    NOT NULL,
    added_at    INTEGER NOT NULL,
    PRIMARY KEY (profile_id, work_key)
) WITHOUT ROWID;

CREATE TABLE proposals_seen (
    profile_id  INTEGER NOT NULL REFERENCES profiles (id) ON DELETE CASCADE,
    key         TEXT    NOT NULL,                                   -- opera + passaggio
    seen_at     INTEGER NOT NULL,
    PRIMARY KEY (profile_id, key)
) WITHOUT ROWID;

-- ---------------------------------------------------------------- dove lo vedo e avvisi

CREATE TABLE availability (
    id          INTEGER PRIMARY KEY,
    work_id     INTEGER NOT NULL REFERENCES works (id) ON DELETE CASCADE,
    platform    TEXT    NOT NULL,
    kind        TEXT    NOT NULL CHECK (kind IN ('subscription', 'free', 'rent', 'buy')),
    link        TEXT    NOT NULL DEFAULT '',
    price       REAL,
    leaving_at  INTEGER,                                            -- esce dal catalogo
    checked_at  INTEGER NOT NULL,
    UNIQUE (work_id, platform, kind)
);

-- tre avvisi indipendenti per opera: in chiaro in TV, nei miei abbonamenti, a noleggio o acquisto
CREATE TABLE alerts (
    id             INTEGER PRIMARY KEY,
    profile_id     INTEGER NOT NULL REFERENCES profiles (id) ON DELETE CASCADE,
    work_id        INTEGER NOT NULL REFERENCES works (id) ON DELETE CASCADE,
    kind           TEXT    NOT NULL CHECK (kind IN ('tv', 'subscription', 'rent_buy')),
    active         INTEGER NOT NULL DEFAULT 1 CHECK (active IN (0, 1)),
    options        TEXT    NOT NULL DEFAULT '{}',                   -- JSON: canali, registrazione automatica, momenti; servizi; prezzo massimo
    notify         TEXT    NOT NULL DEFAULT '["app"]',              -- JSON: app, windows, telegram, email
    last_fired_at  INTEGER,
    UNIQUE (profile_id, work_id, kind)
);

-- ogni scatto di un avviso, per farlo scattare una volta sola
CREATE TABLE alert_events (
    alert_id  INTEGER NOT NULL REFERENCES alerts (id) ON DELETE CASCADE,
    key       TEXT    NOT NULL,                                     -- passaggio o disponibilità che l'ha fatto scattare
    fired_at  INTEGER NOT NULL,
    PRIMARY KEY (alert_id, key)
) WITHOUT ROWID;
