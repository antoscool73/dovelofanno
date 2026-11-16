-- Wave 5 (16/11/2026): posizione del flusso nella sua playlist (0 = primo), per mostrare i canali nell'ordine della
-- playlist come RegistraTV (le playlist italiane seguono la numerazione del digitale terrestre). Si scrive a ogni
-- importazione; i flussi già presenti restano a 0 fino alla prossima rilettura della playlist.
ALTER TABLE streams ADD COLUMN position INTEGER NOT NULL DEFAULT 0;
