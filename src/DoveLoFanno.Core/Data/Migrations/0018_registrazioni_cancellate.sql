-- 26/11/2026: cancellare una registrazione finita (richiesta di Antonio): si cancella il file e la riga sparisce da
-- Registrazioni, ma resta nel database con l'ora della cancellazione. Così «Registra la serie» sa ancora che quella
-- puntata è stata registrata e non la riprende alla replica. NULL = non cancellata.
ALTER TABLE recordings ADD COLUMN deleted_at INTEGER;
