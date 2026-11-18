-- 18/11/2026: notifiche dentro l'app (registrazioni finite, avvisi scattati, proposte nuove), per profilo o per tutta
-- la famiglia (profile_id NULL). Ogni profilo ricorda fin dove ha letto.
CREATE TABLE notifications (
    id          INTEGER PRIMARY KEY,
    profile_id  INTEGER REFERENCES profiles (id) ON DELETE CASCADE,
    kind        TEXT    NOT NULL CHECK (kind IN ('recording', 'alert', 'proposals', 'system')),
    title       TEXT    NOT NULL,
    text        TEXT    NOT NULL DEFAULT '',
    link        TEXT    NOT NULL DEFAULT '',
    created_at  INTEGER NOT NULL
);
CREATE INDEX notifications_profile ON notifications (profile_id, id);

ALTER TABLE profiles ADD COLUMN notifications_read INTEGER NOT NULL DEFAULT 0;
