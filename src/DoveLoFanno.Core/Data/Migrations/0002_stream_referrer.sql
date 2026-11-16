-- Wave 2 (16/11/2026): referrer dei flussi (attributo http-referrer o #EXTVLCOPT:http-referrer della playlist),
-- che RegistraTV passava a ffmpeg con -referer e che lo schema v1 non conservava.
ALTER TABLE streams ADD COLUMN referrer TEXT NOT NULL DEFAULT '';
