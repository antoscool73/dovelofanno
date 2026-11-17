using System.Text.Json;
using DoveLoFanno.Epg;
using DoveLoFanno.Library;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Data;

/// <summary>Una cartella del NAS nella libreria: kind = auto (decide il nome del file), films, series.</summary>
public sealed record LibraryFolderRow(long Id, string Path, string Kind, bool Enabled, DateTimeOffset? LastScannedAt, string LastError, int Files);

/// <summary>Una riga della libreria: un'opera (film, serie con i suoi episodi, programma) oppure un file da sistemare.</summary>
public sealed record LibraryEntry(
    long? WorkId, long? ItemId, string Kind, string Title, int Year, string Poster, double? Rating, string RatingSource,
    int? Stars, bool Watched, int Files, int Episodes, DateTimeOffset AddedAt, long? PlayItemId, int PositionS, int DurationS, bool Recorded);

/// <summary>Un file di un'opera, per la Scheda e per scegliere l'episodio.</summary>
public sealed record LibraryFile(
    long ItemId, string Name, string Origin, int? Season, int? Episode, string EpisodeTitle, int DurationS, int? Width, int? Height,
    bool Playable, string CopyState, int PositionS, bool Watched, DateTimeOffset AddedAt);

/// <summary>Sottotitoli che il player può mostrare: Id = "s0" (dentro il file) o "f0" (file .srt/.vtt accanto).</summary>
public sealed record SubtitleTrack(string Id, string Label, string Language);

/// <summary>Tutto quello che serve al player.</summary>
public sealed record LibraryItemDetail(
    long Id, long? WorkId, string Title, string Subtitle, string FileName, string Origin, int DurationS, int? Width, int? Height,
    string VideoCodec, string AudioCodec, bool Playable, string CopyState, int PositionS, bool Watched,
    IReadOnlyList<TrackInfo> AudioTracks, IReadOnlyList<SubtitleTrack> Subtitles, long? NextItemId, string MatchState,
    string TitleGuess, int YearGuess, int? Season, int? Episode, long SizeBytes);

/// <summary>Numeri della libreria per le Impostazioni.</summary>
public sealed record LibraryCounts(int Files, int Matched, int Unmatched, int Missing, int CopiesWaiting, int CopiesFailed);

/// <summary>Un file ancora da abbinare a un'opera.</summary>
public sealed record PendingItem(long Id, string Path, string FolderKind, string TitleGuess, int YearGuess, int? Season, int? Episode, string EpisodeTitle);

/// <summary>Esito di una posizione di visione salvata.</summary>
public enum ProgressResult { NotFound, Saved, Watched }

/// <summary>
/// Libreria: cartelle, file, abbinamento alle opere, posizione di ripresa e "visto" per profilo. I file delle
/// registrazioni entrano da soli (origin = recorded), quelli del NAS con la scansione (imported).
/// </summary>
public sealed class LibraryStore(string dbPath)
{
    SqliteConnection Open() => Database.Open(dbPath);

    // ---------------------------------------------------------------- cartelle

    public List<LibraryFolderRow> Folders()
    {
        using var c = Open();
        return Db.Query(c, "SELECT id, path, kind, enabled, last_scanned_at, last_error, files FROM library_folders ORDER BY path",
            r => new LibraryFolderRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt64(3) == 1, Db.NTime(r, 4), r.GetString(5), r.GetInt32(6)));
    }

    public LibraryFolderRow? Folder(long id) => Folders().FirstOrDefault(f => f.Id == id);

    /// <summary>null se la cartella c'è già.</summary>
    public long? AddFolder(string path, string kind)
    {
        using var c = Open();
        if (Db.ScalarLong(c, "SELECT id FROM library_folders WHERE path = $p", ("$p", path)) != null) return null;
        return Db.Insert(c, "INSERT INTO library_folders (path, kind) VALUES ($p, $k)", ("$p", path), ("$k", kind));
    }

    public bool UpdateFolder(long id, string? kind, bool? enabled)
    {
        using var c = Open();
        int n = Db.Exec(c, "UPDATE library_folders SET kind = coalesce($k, kind), enabled = coalesce($e, enabled) WHERE id = $id",
            ("$k", kind), ("$e", enabled is bool b ? (b ? 1 : 0) : null), ("$id", id));
        // cambiando il tipo di cartella i file si riabbinano
        if (n > 0 && kind != null)
            Db.Exec(c, "UPDATE library_items SET match_state = 'pending' WHERE folder_id = $id AND match_state <> 'manual'", ("$id", id));
        return n > 0;
    }

    /// <summary>Toglie la cartella e i suoi file dalla libreria (i file sul disco restano).</summary>
    public bool DeleteFolder(long id)
    {
        using var c = Open();
        return Db.Exec(c, "DELETE FROM library_folders WHERE id = $id", ("$id", id)) > 0;
    }

    public void FolderScanned(long id, int files, string error, DateTimeOffset now)
    {
        using var c = Open();
        Db.Exec(c, "UPDATE library_folders SET last_scanned_at = $t, files = $f, last_error = $e WHERE id = $id",
            ("$t", Db.Unix(now)), ("$f", files), ("$e", error), ("$id", id));
    }

    // ---------------------------------------------------------------- scansione

    /// <summary>Un file trovato nella cartella: nuovo, cambiato (si rilegge e si riabbina) o uguale (solo "non manca più").</summary>
    public void Seen(long folderId, string path, long size, DateTimeOffset modified, NameGuess guess, DateTimeOffset now)
    {
        using var c = Open();
        var old = Db.Query(c, "SELECT id, size_bytes, modified_at, match_state FROM library_items WHERE path = $p",
            r => (Id: r.GetInt64(0), Size: Db.NLong(r, 1), Mod: Db.NLong(r, 2), State: r.GetString(3)), ("$p", path)).FirstOrDefault();
        long mod = Db.Unix(modified);
        if (old.Id == 0)
        {
            Db.Exec(c, """
                INSERT INTO library_items (path, origin, folder_id, size_bytes, modified_at, title_guess, year_guess, season, episode, added_at)
                VALUES ($p, 'imported', $f, $s, $m, $t, $y, $se, $ep, $now)
                """,
                ("$p", path), ("$f", folderId), ("$s", size), ("$m", mod), ("$t", guess.Title), ("$y", guess.Year), ("$se", guess.Season),
                ("$ep", guess.Episode), ("$now", Db.Unix(now)));
            return;
        }
        if (old.Size == size && old.Mod == mod)
        {
            Db.Exec(c, "UPDATE library_items SET missing_since = NULL, folder_id = coalesce(folder_id, $f) WHERE id = $id", ("$f", folderId), ("$id", old.Id));
            // regole del nome migliorate: se ora si capisce un altro titolo, il file si riabbina (non quelli decisi a mano)
            Db.Exec(c, """
                UPDATE library_items SET title_guess = $t, year_guess = $y, season = $se, episode = $ep, match_state = 'pending'
                WHERE id = $id AND origin = 'imported' AND match_state <> 'manual'
                  AND (title_guess <> $t OR year_guess <> $y OR coalesce(season, -1) <> coalesce($se, -1) OR coalesce(episode, -1) <> coalesce($ep, -1))
                """,
                ("$t", guess.Title), ("$y", guess.Year), ("$se", guess.Season), ("$ep", guess.Episode), ("$id", old.Id));
            return;
        }
        // file cambiato: si rilegge con ffprobe; l'abbinamento si rifà se non l'ha deciso l'utente
        Db.Exec(c, """
            UPDATE library_items SET size_bytes = $s, modified_at = $m, missing_since = NULL, duration_s = NULL, container = '',
                   copy_state = '', browser_copy = '', folder_id = coalesce(folder_id, $f),
                   title_guess = $t, year_guess = $y, season = $se, episode = $ep,
                   match_state = CASE WHEN match_state IN ('manual') OR origin = 'recorded' THEN match_state ELSE 'pending' END
            WHERE id = $id
            """,
            ("$s", size), ("$m", mod), ("$f", folderId), ("$t", guess.Title), ("$y", guess.Year), ("$se", guess.Season), ("$ep", guess.Episode), ("$id", old.Id));
    }

    /// <summary>
    /// I file della cartella non visti in questa scansione: risultano mancanti (si tengono, un disco può essere staccato).
    /// Quelli che ora si saltano (extra, "Bonus", campioni) invece escono dalla libreria. Restituisce i mancanti.
    /// </summary>
    public int MarkMissing(long folderId, string root, IReadOnlySet<string> seen, DateTimeOffset now)
    {
        using var c = Open();
        var paths = Db.Query(c, "SELECT id, path FROM library_items WHERE folder_id = $f AND missing_since IS NULL",
            r => (r.GetInt64(0), r.GetString(1)), ("$f", folderId));
        int n = 0;
        foreach (var (id, path) in paths)
        {
            if (seen.Contains(path)) continue;
            if (MediaName.Skipped(root, path)) Db.Exec(c, "DELETE FROM library_items WHERE id = $id", ("$id", id));
            else n += Db.Exec(c, "UPDATE library_items SET missing_since = $t WHERE id = $id", ("$t", Db.Unix(now)), ("$id", id));
        }
        return n;
    }

    /// <summary>
    /// Regole di abbinamento della libreria cambiate (versione nelle impostazioni): i file abbinati da TMDB o non
    /// trovati si riabbinano una volta, quelli decisi a mano restano. true se è successo.
    /// </summary>
    public bool ResetMatchesForRules(int version)
    {
        using var c = Open();
        int old = (int)(Db.ScalarLong(c, "SELECT CAST(value AS INTEGER) FROM settings WHERE key = 'LibraryMatchRules'") ?? 0);
        if (old >= version) return false;
        Db.Exec(c, "UPDATE library_items SET match_state = 'pending' WHERE origin = 'imported' AND match_state IN ('matched', 'unmatched')");
        Db.Exec(c, "INSERT INTO settings (key, value) VALUES ('LibraryMatchRules', $v) ON CONFLICT (key) DO UPDATE SET value = $v",
            ("$v", version.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return old > 0;
    }

    /// <summary>
    /// Le registrazioni finite entrano nella libreria, già abbinate alla loro opera. Quelle senza opera (registrazioni a mano)
    /// ricevono un'opera "programma" dal titolo. Restituisce quante ne sono entrate.
    /// </summary>
    public int SyncRecordings(DateTimeOffset now)
    {
        using var c = Open();
        var recs = Db.Query(c, """
            SELECT r.id, r.file, r.work_id, r.title, r.stop FROM recordings r
            WHERE r.state IN ('completed', 'incomplete', 'stopped') AND r.file <> ''
              AND NOT EXISTS (SELECT 1 FROM library_items li WHERE li.recording_id = r.id OR li.path = r.file)
            """,
            r => (Id: r.GetInt64(0), File: r.GetString(1), Work: Db.NLong(r, 2), Title: r.GetString(3), Stop: r.GetInt64(4)));
        int n = 0;
        foreach (var r in recs)
        {
            if (!File.Exists(r.File)) continue;
            long? work = r.Work ?? LocalWork(c, r.Title, 0, "programme", now);
            var fi = new FileInfo(r.File);
            Db.Exec(c, """
                INSERT INTO library_items (path, work_id, origin, recording_id, size_bytes, modified_at, title_guess, added_at, match_state)
                VALUES ($p, $w, 'recorded', $r, $s, $m, $t, $a, 'matched')
                """,
                ("$p", r.File), ("$w", work), ("$r", r.Id), ("$s", fi.Length), ("$m", Db.Unix(fi.LastWriteTimeUtc)), ("$t", r.Title),
                ("$a", Math.Min(r.Stop, Db.Unix(now))));
            n++;
        }
        // registrazioni cancellate dal disco (o da Esplora risorse)
        foreach (var (id, path) in Db.Query(c, "SELECT id, path FROM library_items WHERE origin = 'recorded' AND missing_since IS NULL", r => (r.GetInt64(0), r.GetString(1))))
            if (!File.Exists(path)) Db.Exec(c, "UPDATE library_items SET missing_since = $t WHERE id = $id", ("$t", Db.Unix(now)), ("$id", id));
        return n;
    }

    /// <summary>File presenti ancora da leggere con ffprobe.</summary>
    public List<(long Id, string Path)> ToProbe()
    {
        using var c = Open();
        return Db.Query(c, "SELECT id, path FROM library_items WHERE duration_s IS NULL AND missing_since IS NULL ORDER BY added_at DESC",
            r => (r.GetInt64(0), r.GetString(1)));
    }

    /// <summary>Salva la lettura di ffprobe (null = file illeggibile: durata 0, non si riprova finché il file non cambia).</summary>
    public void SetProbe(long id, string path, ProbeInfo? p)
    {
        using var c = Open();
        if (p == null)
        {
            Db.Exec(c, "UPDATE library_items SET duration_s = 0, container = '?', copy_state = 'failed' WHERE id = $id", ("$id", id));
            return;
        }
        string copy = MediaProbe.Decide(path, p) == Playback.Direct ? "" : "needed";
        Db.Exec(c, """
            UPDATE library_items SET duration_s = $d, container = $ct, video_codec = $v, audio_codec = $a, width = $w, height = $h,
                   audio_tracks = $at, subtitle_tracks = $st,
                   copy_state = CASE WHEN copy_state = 'done' AND browser_copy <> '' THEN 'done' ELSE $cs END
            WHERE id = $id
            """,
            ("$d", (int)Math.Round(p.DurationS)), ("$ct", p.Container), ("$v", p.VideoCodec), ("$a", p.AudioCodec), ("$w", p.Width), ("$h", p.Height),
            ("$at", JsonSerializer.Serialize(p.Audio)), ("$st", JsonSerializer.Serialize(p.Subtitles)), ("$cs", copy), ("$id", id));
    }

    // ---------------------------------------------------------------- abbinamento alle opere

    /// <summary>File da abbinare: nuovi o cambiati; con la chiave TMDB anche quelli abbinati solo dal nome.</summary>
    public List<PendingItem> Pending(bool withTmdb, int max = 500)
    {
        using var c = Open();
        return Db.Query(c, $"""
            SELECT li.id, li.path, coalesce(f.kind, 'auto'), li.title_guess, li.year_guess, li.season, li.episode
            FROM library_items li LEFT JOIN library_folders f ON f.id = li.folder_id
            WHERE li.origin = 'imported' AND li.missing_since IS NULL
              AND li.match_state IN ('pending'{(withTmdb ? ", 'local'" : "")})
            ORDER BY li.added_at DESC LIMIT $max
            """,
            r => new PendingItem(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4), Db.NInt(r, 5), Db.NInt(r, 6),
                MediaName.Parse(r.GetString(1)).EpisodeTitle),
            ("$max", max));
    }

    public void SetMatch(long itemId, long? workId, string state)
    {
        using var c = Open();
        Db.Exec(c, "UPDATE library_items SET work_id = $w, match_state = $s WHERE id = $id", ("$w", workId), ("$s", state), ("$id", itemId));
    }

    /// <summary>
    /// Opera di un film: prima quella con lo stesso codice TMDB (anche se viene dalla guida), poi quella con la stessa
    /// chiave titolo|anno, altrimenti una nuova. Il voto TMDB si mette solo se l'opera non ne ha già uno.
    /// </summary>
    public long FilmWork(string title, int year, int? tmdbId, string imdbId, string originalTitle, double vote, int votes, bool documentary, DateTimeOffset now,
        IReadOnlyList<int>? genres = null)
    {
        using var c = Open();
        if (tmdbId is int t && Db.ScalarLong(c, "SELECT id FROM works WHERE tmdb_kind = 'movie' AND tmdb_id = $t ORDER BY (kind = 'film') DESC, id LIMIT 1", ("$t", t)) is long byTmdb)
        {
            SetGenres(c, byTmdb, genres);
            return byTmdb;
        }
        string key = Titles.TitleKey(title) + "|" + year.ToString(System.Globalization.CultureInfo.InvariantCulture);
        long id = UpsertWork(c, documentary ? "documentary" : "film", key, title, year, tmdbId, tmdbId == null ? null : "movie", imdbId, originalTitle, vote, votes, null, null, null, now);
        SetGenres(c, id, genres);
        return id;
    }

    static void SetGenres(SqliteConnection c, long workId, IReadOnlyList<int>? genres)
    {
        if (genres is { Count: > 0 })
            Db.Exec(c, "UPDATE works SET genres = $g WHERE id = $id AND genres = '[]'", ("$g", JsonSerializer.Serialize(genres)), ("$id", workId));
    }

    /// <summary>Opera di una serie (o di un documentario a puntate): chiave tv:codice TMDB, o serie|titolo senza TMDB.</summary>
    public long SeriesWork(string title, int year, int? tmdbId, string imdbId, string originalTitle, double vote, int votes, bool documentary, DateTimeOffset now,
        IReadOnlyList<int>? genres = null)
    {
        using var c = Open();
        if (tmdbId is int t && Db.ScalarLong(c, "SELECT id FROM works WHERE tmdb_kind = 'tv' AND tmdb_id = $t AND parent_id IS NULL ORDER BY id LIMIT 1", ("$t", t)) is long byTmdb)
        {
            SetGenres(c, byTmdb, genres);
            return byTmdb;
        }
        string key = tmdbId is int tv ? "tv:" + tv.ToString(System.Globalization.CultureInfo.InvariantCulture) : "serie|" + Titles.TitleKey(title);
        long work = UpsertWork(c, documentary ? "documentary" : "series", key, title, year, tmdbId, tmdbId == null ? null : "tv", imdbId, originalTitle, vote, votes, null, null, null, now);
        SetGenres(c, work, genres);
        return work;
    }

    /// <summary>Opere della libreria (film e serie) con codice IMDb e senza il voto IMDb: per i voti dal file di IMDb.</summary>
    public List<(long WorkId, string ImdbId)> WorksWithoutImdbRating()
    {
        using var c = Open();
        return Db.Query(c, """
            SELECT DISTINCT t.id, t.imdb_id FROM library_items li JOIN works w ON w.id = li.work_id JOIN works t ON t.id = coalesce(w.parent_id, w.id)
            WHERE t.imdb_id <> '' AND t.rating_source <> 'IMDb' AND li.missing_since IS NULL
            """, r => (r.GetInt64(0), r.GetString(1)));
    }

    public void SetImdbRating(long workId, double rating, int votes)
    {
        using var c = Open();
        Db.Exec(c, "UPDATE works SET rating = $r, rating_source = 'IMDb', rating_votes = $v WHERE id = $id",
            ("$r", Math.Round(rating, 1)), ("$v", votes), ("$id", workId));
    }

    /// <summary>Titolo dell'episodio da TMDB, se finora era solo quello fatto da serie e numero ("Don Matteo 1x02").</summary>
    public void SetEpisodeTitle(long episodeWorkId, string title)
    {
        using var c = Open();
        Db.Exec(c, """
            UPDATE works SET title = $t, title_key = $tk WHERE id = $id AND parent_id IS NOT NULL
              AND title = (SELECT p.title FROM works p WHERE p.id = works.parent_id) || ' ' || works.season || 'x' || printf('%02d', works.episode)
            """, ("$t", title), ("$tk", Titles.TitleKey(title)), ("$id", episodeWorkId));
    }

    /// <summary>Opera di un episodio, figlia della serie.</summary>
    public long EpisodeWork(long seriesId, int season, int episode, string episodeTitle, DateTimeOffset now)
    {
        using var c = Open();
        var s = Db.Query(c, "SELECT work_key, title FROM works WHERE id = $id", r => (Key: r.GetString(0), Title: r.GetString(1)), ("$id", seriesId)).First();
        string key = $"{s.Key}|s{season:D2}e{episode:D2}";
        string title = episodeTitle.Length > 0 ? episodeTitle : $"{s.Title} {season}x{episode:D2}";
        return UpsertWork(c, "episode", key, title, 0, null, null, "", "", 0, 0, seriesId, season, episode, now);
    }

    /// <summary>Opera dal solo nome del file (senza chiave TMDB o con abbinamento a mano senza TMDB).</summary>
    public long LocalWork(string title, int year, string kind, DateTimeOffset now)
    {
        using var c = Open();
        return LocalWork(c, title, year, kind, now);
    }

    static long LocalWork(SqliteConnection c, string title, int year, string kind, DateTimeOffset now)
    {
        string tk = Titles.TitleKey(title);
        string key = kind switch
        {
            "series" => "serie|" + tk,
            "programme" => "programma|" + tk,
            _ => tk + "|" + year.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        return UpsertWork(c, kind, key, title, year, null, null, "", "", 0, 0, null, null, null, now);
    }

    static long UpsertWork(SqliteConnection c, string kind, string key, string title, int year, int? tmdbId, string? tmdbKind, string imdbId,
        string originalTitle, double vote, int votes, long? parentId, int? season, int? episode, DateTimeOffset now)
    {
        bool rated = votes >= 20 && vote > 0;
        return Db.Insert(c, """
            INSERT INTO works (kind, work_key, title, title_key, original_title, year, parent_id, season, episode, tmdb_id, tmdb_kind, imdb_id,
                               rating, rating_source, rating_votes, updated_at)
            VALUES ($k, $key, $t, $tk, $ot, $y, $par, $se, $ep, $tm, $tmk, $im, $r, $rs, $rv, $now)
            ON CONFLICT (work_key) DO UPDATE SET
                tmdb_id = coalesce(works.tmdb_id, excluded.tmdb_id), tmdb_kind = coalesce(works.tmdb_kind, excluded.tmdb_kind),
                imdb_id = CASE WHEN works.imdb_id = '' THEN excluded.imdb_id ELSE works.imdb_id END,
                original_title = CASE WHEN works.original_title = '' THEN excluded.original_title ELSE works.original_title END,
                rating = coalesce(works.rating, excluded.rating),
                rating_source = CASE WHEN works.rating IS NULL THEN excluded.rating_source ELSE works.rating_source END,
                rating_votes = CASE WHEN works.rating IS NULL THEN excluded.rating_votes ELSE works.rating_votes END,
                updated_at = excluded.updated_at
            """,
            ("$k", kind), ("$key", key), ("$t", title), ("$tk", Titles.TitleKey(title)), ("$ot", originalTitle), ("$y", year), ("$par", parentId),
            ("$se", season), ("$ep", episode), ("$tm", tmdbId), ("$tmk", tmdbKind), ("$im", imdbId),
            ("$r", rated ? Math.Round(vote, 1) : null), ("$rs", rated ? "TMDB" : ""), ("$rv", rated ? votes : 0), ("$now", Db.Unix(now)));
    }

    /// <summary>Opere della libreria con codice TMDB ma senza dettagli (locandina, trama): (opera, codice, movie|tv).</summary>
    public List<(long WorkId, int TmdbId, string Kind)> WorksWithoutDetails(int max = 200)
    {
        using var c = Open();
        return Db.Query(c, """
            SELECT DISTINCT t.id, t.tmdb_id, t.tmdb_kind FROM library_items li JOIN works w ON w.id = li.work_id
            JOIN works t ON t.id = coalesce(w.parent_id, w.id)
            WHERE t.tmdb_id IS NOT NULL AND t.tmdb_kind IS NOT NULL AND t.details_at IS NULL AND li.missing_since IS NULL LIMIT $max
            """,
            r => (r.GetInt64(0), r.GetInt32(1), r.GetString(2)), ("$max", max));
    }

    // ---------------------------------------------------------------- copia per il browser

    /// <summary>Mette in coda la copia per il browser. false se il file non esiste o non ne ha bisogno.</summary>
    public bool QueueCopy(long itemId)
    {
        using var c = Open();
        return Db.Exec(c, "UPDATE library_items SET copy_state = 'queued' WHERE id = $id AND copy_state IN ('needed', 'failed')", ("$id", itemId)) > 0
            || (Db.ScalarLong(c, "SELECT count(*) FROM library_items WHERE id = $id AND copy_state IN ('queued', 'working')", ("$id", itemId)) ?? 0) > 0;
    }

    public (long Id, string Path)? NextCopy()
    {
        using var c = Open();
        var r = Db.Query(c, "SELECT id, path FROM library_items WHERE copy_state = 'queued' AND missing_since IS NULL ORDER BY id LIMIT 1",
            x => (x.GetInt64(0), x.GetString(1)));
        return r.Count == 0 ? null : r[0];
    }

    /// <summary>Le copie rimaste a metà quando il server si è fermato tornano in coda.</summary>
    public void RequeueInterruptedCopies()
    {
        using var c = Open();
        Db.Exec(c, "UPDATE library_items SET copy_state = 'queued' WHERE copy_state = 'working'");
    }

    public void SetCopy(long itemId, string state, string browserCopy = "")
    {
        using var c = Open();
        Db.Exec(c, "UPDATE library_items SET copy_state = $s, browser_copy = $b WHERE id = $id", ("$s", state), ("$b", browserCopy), ("$id", itemId));
    }

    public ProbeInfo? StoredProbe(long itemId)
    {
        using var c = Open();
        return Db.Query(c, "SELECT duration_s, container, video_codec, audio_codec, width, height, audio_tracks, subtitle_tracks FROM library_items WHERE id = $id",
            r => new ProbeInfo(r.IsDBNull(0) ? 0 : r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), Db.NInt(r, 4), Db.NInt(r, 5),
                Tracks(r.GetString(6)), Tracks(r.GetString(7))), ("$id", itemId)).FirstOrDefault();
    }

    /// <summary>Il file da mandare al browser: l'originale, la copia pronta, oppure null (serve la copia, o il file non c'è).</summary>
    public (string Path, bool Ready, string CopyState)? StreamPath(long itemId)
    {
        using var c = Open();
        var r = Db.Query(c, "SELECT path, copy_state, browser_copy FROM library_items WHERE id = $id AND missing_since IS NULL",
            x => (Path: x.GetString(0), State: x.GetString(1), Copy: x.GetString(2)), ("$id", itemId)).FirstOrDefault();
        if (r.Path == null) return null;
        if (r.State == "") return (r.Path, true, "");
        if (r.State == "done" && r.Copy.Length > 0) return (r.Copy, true, "done");
        return (r.Path, false, r.State);
    }

    /// <summary>Toglie il file dalla libreria; restituisce i percorsi da cancellare dal disco (file e copia), null se non c'è.</summary>
    public (string Path, string Copy, long? RecordingId)? Remove(long itemId)
    {
        using var c = Open();
        var r = Db.Query(c, "SELECT path, browser_copy, recording_id FROM library_items WHERE id = $id",
            x => (x.GetString(0), x.GetString(1), Db.NLong(x, 2)), ("$id", itemId));
        if (r.Count == 0) return null;
        Db.Exec(c, "DELETE FROM library_items WHERE id = $id", ("$id", itemId));
        if (r[0].Item3 is long rec) Db.Exec(c, "UPDATE recordings SET file = '' WHERE id = $id", ("$id", rec));
        return r[0];
    }

    // ---------------------------------------------------------------- lettura

    public LibraryCounts Counts()
    {
        using var c = Open();
        int Q(string where) => (int)(Db.ScalarLong(c, "SELECT count(*) FROM library_items WHERE " + where) ?? 0);
        return new LibraryCounts(
            Q("missing_since IS NULL"),
            Q("missing_since IS NULL AND work_id IS NOT NULL"),
            Q("missing_since IS NULL AND work_id IS NULL AND match_state <> 'pending'"),
            Q("missing_since IS NOT NULL"),
            Q("copy_state IN ('queued', 'working')"),
            Q("copy_state = 'failed' AND missing_since IS NULL"));
    }

    /// <summary>
    /// Le opere di una sezione della libreria: all, films, series, documentaries, programmes, recordings, unmatched.
    /// sort = added (predefinito), title, rating, stars. Gli episodi stanno sotto la loro serie.
    /// </summary>
    public List<LibraryEntry> Entries(string section, long? profileId, string sort, string? text)
    {
        using var c = Open();
        string tk = Titles.TitleKey(text);
        bool child = profileId != null && Db.ScalarLong(c, "SELECT is_child FROM profiles WHERE id = $p", ("$p", profileId)) == 1;
        if (section == "watched") return Watched(c, profileId, sort, tk, child);
        if (section == "unmatched")
            return Db.Query(c, """
                SELECT li.id, li.title_guess, li.year_guess, li.added_at, coalesce(li.duration_s, 0), li.origin, li.path,
                       coalesce(ws.position_s, 0), coalesce(ws.watched, 0)
                FROM library_items li LEFT JOIN watch_states ws ON ws.profile_id = $p AND ws.item_id = li.id
                WHERE li.work_id IS NULL AND li.missing_since IS NULL AND li.match_state <> 'pending'
                ORDER BY li.added_at DESC
                """,
                r => new LibraryEntry(null, r.GetInt64(0), "unmatched", r.GetString(1).Length > 0 ? r.GetString(1) : Path.GetFileNameWithoutExtension(r.GetString(6)),
                    r.GetInt32(2), "", null, "", null, r.GetInt64(8) == 1, 1, 0, Db.FromUnix(r.GetInt64(3)), r.GetInt64(0), r.GetInt32(7), r.GetInt32(4),
                    r.GetString(5) == "recorded"),
                ("$p", profileId)).Where(e => !child && (tk.Length == 0 || Titles.TitleKey(e.Title).Contains(tk, StringComparison.Ordinal))).ToList();

        string filter = section switch
        {
            "films" => "t.kind = 'film'",
            "series" => "t.kind IN ('series', 'episode')",
            "documentaries" => "t.kind = 'documentary'",
            "programmes" => "t.kind = 'programme'",
            "recordings" => "it.origin = 'recorded'",
            _ => "1 = 1",
        };
        string order = sort switch
        {
            "title" => "t.title_key, t.year",
            "rating" => "t.rating IS NULL, t.rating DESC, t.title_key",
            "stars" => "ws.stars IS NULL, ws.stars DESC, t.title_key",
            _ => "max(it.added_at) DESC, t.title_key",
        };
        var rows = Db.Query(c, $"""
            WITH it AS (
                SELECT li.id, li.added_at, li.origin, li.season, li.episode, coalesce(li.duration_s, 0) AS duration_s, coalesce(w.parent_id, w.id) AS top
                FROM library_items li JOIN works w ON w.id = li.work_id WHERE li.missing_since IS NULL)
            SELECT t.id, t.kind, t.title, t.year, t.poster, t.rating, t.rating_source, ws.stars, coalesce(ws.watched, 0),
                   count(it.id), count(it.episode), max(it.added_at), max(it.origin = 'recorded'),
                   (SELECT x.id FROM it x LEFT JOIN watch_states xs ON xs.profile_id = $p AND xs.item_id = x.id
                    WHERE x.top = t.id ORDER BY coalesce(xs.watched, 0), (xs.position_s > 0) DESC, x.season, x.episode, x.added_at DESC LIMIT 1)
            FROM it JOIN works t ON t.id = it.top
            LEFT JOIN watch_states ws ON ws.profile_id = $p AND ws.work_id = t.id AND ws.item_id IS NULL
            WHERE {filter} AND ($tk = '' OR t.title_key LIKE '%' || $tk || '%') {(child ? "AND " + WorkStore.ChildSafe("t") : "")}
            GROUP BY t.id ORDER BY {order}
            """,
            r => (Work: r.GetInt64(0), Kind: r.GetString(1), Title: r.GetString(2), Year: r.GetInt32(3), Poster: r.GetString(4),
                Rating: r.IsDBNull(5) ? (double?)null : r.GetDouble(5), Src: r.GetString(6), Stars: Db.NInt(r, 7), Watched: r.GetInt64(8) == 1,
                Files: r.GetInt32(9), Episodes: r.GetInt32(10), Added: r.GetInt64(11), Recorded: r.GetInt64(12) == 1, Play: Db.NLong(r, 13)),
            ("$p", profileId), ("$tk", tk));

        var play = rows.Where(x => x.Play != null).Select(x => x.Play!.Value).ToList();
        var pos = new Dictionary<long, (int Pos, int Dur)>();
        if (play.Count > 0)
            foreach (var (id, p, d) in Db.Query(c, $"""
                SELECT li.id, coalesce(ws.position_s, 0), coalesce(li.duration_s, 0) FROM library_items li
                LEFT JOIN watch_states ws ON ws.profile_id = $p AND ws.item_id = li.id WHERE li.id IN ({string.Join(',', play)})
                """, r => (r.GetInt64(0), r.GetInt32(1), r.GetInt32(2)), ("$p", profileId)))
                pos[id] = (p, d);
        return rows.Select(x =>
        {
            var (p, d) = x.Play is long pid && pos.TryGetValue(pid, out var v) ? v : (0, 0);
            return new LibraryEntry(x.Work, null, x.Kind, x.Title, x.Year, WorkStore.ImageUrl(x.Poster, "w342"), x.Rating, x.Src, x.Stars, x.Watched,
                x.Files, x.Episodes, Db.FromUnix(x.Added), x.Play, p, d, x.Recorded);
        }).ToList();
    }

    /// <summary>
    /// "Visti e votati": le opere che il profilo ha visto o votato con le stelle, anche senza file in casa (viste in TV,
    /// segnate dalla Scheda). Prima le più recenti.
    /// </summary>
    static List<LibraryEntry> Watched(SqliteConnection c, long? profileId, string sort, string tk, bool child)
    {
        if (profileId == null) return [];
        string order = sort switch
        {
            "title" => "t.title_key, t.year",
            "rating" => "t.rating IS NULL, t.rating DESC, t.title_key",
            "stars" => "ws.stars IS NULL, ws.stars DESC, t.title_key",
            _ => "ws.updated_at DESC",
        };
        return Db.Query(c, $"""
            SELECT t.id, t.kind, t.title, t.year, t.poster, t.rating, t.rating_source, ws.stars, ws.watched, ws.updated_at,
                   (SELECT count(*) FROM library_items x JOIN works xw ON xw.id = x.work_id WHERE (xw.id = t.id OR xw.parent_id = t.id) AND x.missing_since IS NULL),
                   (SELECT x.id FROM library_items x JOIN works xw ON xw.id = x.work_id
                    WHERE (xw.id = t.id OR xw.parent_id = t.id) AND x.missing_since IS NULL ORDER BY x.season, x.episode, x.added_at DESC LIMIT 1)
            FROM watch_states ws JOIN works t ON t.id = ws.work_id
            WHERE ws.profile_id = $p AND ws.item_id IS NULL AND t.parent_id IS NULL AND (ws.watched = 1 OR ws.stars IS NOT NULL)
              AND ($tk = '' OR t.title_key LIKE '%' || $tk || '%') {(child ? "AND " + WorkStore.ChildSafe("t") : "")}
            ORDER BY {order}
            """,
            r => new LibraryEntry(r.GetInt64(0), null, r.GetString(1), r.GetString(2), r.GetInt32(3), WorkStore.ImageUrl(r.GetString(4), "w342"),
                r.IsDBNull(5) ? null : r.GetDouble(5), r.GetString(6), Db.NInt(r, 7), r.GetInt64(8) == 1, r.GetInt32(10), 0,
                Db.FromUnix(r.GetInt64(9)), Db.NLong(r, 11), 0, 0, false),
            ("$p", profileId), ("$tk", tk));
    }

    /// <summary>I file di un'opera (per una serie: tutti gli episodi in ordine), con la ripresa del profilo.</summary>
    public List<LibraryFile> Files(long workId, long? profileId)
    {
        using var c = Open();
        string series = Db.Query(c, "SELECT title FROM works WHERE id = $w", r => r.GetString(0), ("$w", workId)).FirstOrDefault() ?? "";
        return Db.Query(c, """
            SELECT li.id, li.path, li.origin, li.season, li.episode, CASE WHEN w.parent_id = $w THEN w.title ELSE '' END,
                   coalesce(li.duration_s, 0), li.width, li.height, li.copy_state, coalesce(ws.position_s, 0), coalesce(ws.watched, 0), li.added_at
            FROM library_items li JOIN works w ON w.id = li.work_id
            LEFT JOIN watch_states ws ON ws.profile_id = $p AND ws.item_id = li.id
            WHERE (w.id = $w OR w.parent_id = $w
                   OR (w.tmdb_id IS NOT NULL AND w.tmdb_kind = 'movie'
                       AND w.tmdb_id = (SELECT tmdb_id FROM works WHERE id = $w AND tmdb_kind = 'movie')))
              AND li.missing_since IS NULL
            ORDER BY li.season, li.episode, li.added_at DESC
            """,
            r => new LibraryFile(r.GetInt64(0), Path.GetFileName(r.GetString(1)), r.GetString(2), Db.NInt(r, 3), Db.NInt(r, 4),
                OwnEpisodeTitle(r.GetString(5), series, Db.NInt(r, 3), Db.NInt(r, 4)),
                r.GetInt32(6), Db.NInt(r, 7), Db.NInt(r, 8), r.GetString(9) is "" or "done", r.GetString(9), r.GetInt32(10), r.GetInt64(11) == 1,
                Db.FromUnix(r.GetInt64(12))),
            ("$w", workId), ("$p", profileId));
    }

    /// <summary>Il file per il player; null se non c'è.</summary>
    public LibraryItemDetail? Item(long itemId, long? profileId)
    {
        using var c = Open();
        var r = Db.Query(c, """
            SELECT li.id, li.work_id, li.path, li.origin, coalesce(li.duration_s, 0), li.width, li.height, li.video_codec, li.audio_codec,
                   li.copy_state, coalesce(ws.position_s, 0), coalesce(ws.watched, 0), li.audio_tracks, li.subtitle_tracks, li.match_state,
                   li.title_guess, li.year_guess, li.season, li.episode, coalesce(li.size_bytes, 0),
                   w.title, w.year, w.kind, p.title, w.parent_id, r.start, ch.name
            FROM library_items li LEFT JOIN works w ON w.id = li.work_id LEFT JOIN works p ON p.id = w.parent_id
            LEFT JOIN watch_states ws ON ws.profile_id = $p AND ws.item_id = li.id
            LEFT JOIN recordings r ON r.id = li.recording_id LEFT JOIN channels ch ON ch.id = r.channel_id
            WHERE li.id = $id AND li.missing_since IS NULL
            """,
            x => new
            {
                Id = x.GetInt64(0), Work = Db.NLong(x, 1), Path = x.GetString(2), Origin = x.GetString(3), Dur = x.GetInt32(4), W = Db.NInt(x, 5), H = Db.NInt(x, 6),
                V = x.GetString(7), A = x.GetString(8), Copy = x.GetString(9), Pos = x.GetInt32(10), Watched = x.GetInt64(11) == 1,
                Audio = Tracks(x.GetString(12)), Subs = Tracks(x.GetString(13)), Match = x.GetString(14), Guess = x.GetString(15), Year = x.GetInt32(16),
                Season = Db.NInt(x, 17), Episode = Db.NInt(x, 18), Size = x.GetInt64(19),
                WTitle = x.IsDBNull(20) ? "" : x.GetString(20), WYear = x.IsDBNull(21) ? 0 : x.GetInt32(21), WKind = x.IsDBNull(22) ? "" : x.GetString(22),
                Series = x.IsDBNull(23) ? "" : x.GetString(23), Parent = Db.NLong(x, 24), RecStart = Db.NTime(x, 25), Channel = x.IsDBNull(26) ? "" : x.GetString(26),
            },
            ("$id", itemId), ("$p", profileId)).FirstOrDefault();
        if (r == null) return null;

        string title = r.Series.Length > 0 ? r.Series : r.WTitle.Length > 0 ? r.WTitle : r.Guess.Length > 0 ? r.Guess : Path.GetFileNameWithoutExtension(r.Path);
        var sub = new List<string>();
        if (r.Season is int s && r.Episode is int e)
        {
            string own = OwnEpisodeTitle(r.WTitle, r.Series, s, e);
            sub.Add($"Stagione {s}, episodio {e}" + (r.Series.Length > 0 && own.Length > 0 ? " · " + own : ""));
        }
        if (r.Channel.Length > 0 && r.RecStart is DateTimeOffset rs) sub.Add($"{r.Channel} · registrato il {rs.ToLocalTime():d/M/yyyy}");
        else if (r.WYear > 0) sub.Add(r.WYear.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (r.W is int ww && r.H is int hh) sub.Add($"{ww}×{hh}");

        // episodio successivo della stessa serie
        long? next = null;
        if (r.Parent is long par && r.Season is int s0 && r.Episode is int e0)
            next = Db.ScalarLong(c, """
                SELECT li.id FROM library_items li JOIN works w ON w.id = li.work_id
                WHERE w.parent_id = $par AND li.missing_since IS NULL AND (li.season > $s OR (li.season = $s AND li.episode > $e))
                ORDER BY li.season, li.episode LIMIT 1
                """, ("$par", par), ("$s", s0), ("$e", e0));

        var subs = r.Subs.Where(t => MediaProbe.IsTextSubtitle(t.Codec))
            .Select(t => new SubtitleTrack("s" + t.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), TrackLabel(t, "Sottotitoli"), t.Language)).ToList();
        int k = 0;
        foreach (string f in SidecarSubtitles(r.Path))
        {
            string lang = Path.GetExtension(Path.GetFileNameWithoutExtension(f)).TrimStart('.');
            subs.Add(new SubtitleTrack("f" + k.ToString(System.Globalization.CultureInfo.InvariantCulture), lang.Length is 2 or 3 ? LanguageName(lang) : "Sottotitoli (file)", lang));
            k++;
        }
        var audio = r.Audio.Select(t => t with { Title = TrackLabel(t, "Audio " + (t.Index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)) }).ToList();
        return new LibraryItemDetail(r.Id, r.Work, title, string.Join(" · ", sub), Path.GetFileName(r.Path), r.Origin, r.Dur, r.W, r.H, r.V, r.A,
            r.Copy is "" or "done", r.Copy, r.Pos, r.Watched, audio, subs, next, r.Match, r.Guess, r.Year, r.Season, r.Episode, r.Size);
    }

    /// <summary>Il titolo dell'episodio, vuoto se è solo quello fatto da serie, stagione e numero ("Don Matteo 1x02").</summary>
    static string OwnEpisodeTitle(string title, string series, int? season, int? episode) =>
        season is int s && episode is int e && title == $"{series} {s}x{e:D2}" ? "" : title;

    /// <summary>Percorso del file, se presente.</summary>
    public string? ItemPath(long itemId)
    {
        using var c = Open();
        return Db.Query(c, "SELECT path FROM library_items WHERE id = $id AND missing_since IS NULL", r => r.GetString(0), ("$id", itemId)).FirstOrDefault();
    }

    /// <summary>File di sottotitoli accanto al video: "Film.srt", "Film.it.srt", "Film.ita.vtt".</summary>
    public static List<string> SidecarSubtitles(string videoPath)
    {
        string? dir = Path.GetDirectoryName(videoPath);
        if (dir == null || !Directory.Exists(dir)) return [];
        string stem = Path.GetFileNameWithoutExtension(videoPath);
        try
        {
            return Directory.EnumerateFiles(dir)
                .Where(f => Path.GetExtension(f) is ".srt" or ".vtt" or ".SRT" or ".VTT")
                .Where(f => Path.GetFileName(f).StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    static string TrackLabel(TrackInfo t, string fallback)
    {
        string lang = LanguageName(t.Language);
        if (t.Title.Length > 0 && lang.Length > 0 && !t.Title.Contains(lang, StringComparison.OrdinalIgnoreCase)) return $"{lang} ({t.Title})";
        return t.Title.Length > 0 ? t.Title : lang.Length > 0 ? lang : fallback;
    }

    static string LanguageName(string code) => code.ToLowerInvariant() switch
    {
        "ita" or "it" => "Italiano",
        "eng" or "en" => "Inglese",
        "fra" or "fre" or "fr" => "Francese",
        "deu" or "ger" or "de" => "Tedesco",
        "spa" or "es" => "Spagnolo",
        "por" or "pt" => "Portoghese",
        "jpn" or "ja" => "Giapponese",
        "und" or "" => "",
        var x => x.ToUpperInvariant(),
    };

    static List<TrackInfo> Tracks(string json)
    {
        try { return JsonSerializer.Deserialize<List<TrackInfo>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    // ---------------------------------------------------------------- visione per profilo

    /// <summary>
    /// Posizione di visione di un file per il profilo. Oltre il 90% il file e la sua opera diventano "visti"
    /// (e la posizione torna all'inizio); per un episodio, se sono visti tutti quelli in casa, anche la serie.
    /// Un titolo visto esce dalla mia serata.
    /// </summary>
    public ProgressResult Progress(long profileId, long itemId, int positionS, int durationS, DateTimeOffset now)
    {
        using var c = Open();
        if (Db.ScalarLong(c, "SELECT id FROM profiles WHERE id = $p", ("$p", profileId)) == null) return ProgressResult.NotFound;
        var it = Db.Query(c, "SELECT li.work_id, coalesce(li.duration_s, 0), w.parent_id FROM library_items li LEFT JOIN works w ON w.id = li.work_id WHERE li.id = $id",
            r => (Work: Db.NLong(r, 0), Dur: r.GetInt32(1), Parent: Db.NLong(r, 2)), ("$id", itemId));
        if (it.Count == 0) return ProgressResult.NotFound;
        int dur = it[0].Dur > 0 ? it[0].Dur : durationS;
        bool done = dur > 0 && positionS >= dur * 0.9;
        using var tx = c.BeginTransaction();
        UpsertState(c, profileId, null, itemId, done ? 0 : Math.Max(0, positionS), done ? true : null, now);
        if (done) WatchedWork(c, profileId, it[0].Work, it[0].Parent, now);
        tx.Commit();
        return done ? ProgressResult.Watched : ProgressResult.Saved;
    }

    /// <summary>"Segna come visto" dal player.</summary>
    public bool MarkWatched(long profileId, long itemId, DateTimeOffset now)
    {
        using var c = Open();
        if (Db.ScalarLong(c, "SELECT id FROM profiles WHERE id = $p", ("$p", profileId)) == null) return false;
        var it = Db.Query(c, "SELECT li.work_id, w.parent_id FROM library_items li LEFT JOIN works w ON w.id = li.work_id WHERE li.id = $id",
            r => (Work: Db.NLong(r, 0), Parent: Db.NLong(r, 1)), ("$id", itemId));
        if (it.Count == 0) return false;
        using var tx = c.BeginTransaction();
        UpsertState(c, profileId, null, itemId, 0, true, now);
        WatchedWork(c, profileId, it[0].Work, it[0].Parent, now);
        tx.Commit();
        return true;
    }

    static void WatchedWork(SqliteConnection c, long profileId, long? work, long? parent, DateTimeOffset now)
    {
        if (work is not long w) return;
        UpsertState(c, profileId, w, null, 0, true, now);
        RemoveFromTonight(c, profileId, w);
        if (parent is long par)
        {
            long left = Db.ScalarLong(c, """
                SELECT count(*) FROM library_items li JOIN works e ON e.id = li.work_id
                LEFT JOIN watch_states ws ON ws.profile_id = $p AND ws.item_id = li.id
                WHERE e.parent_id = $par AND li.missing_since IS NULL AND coalesce(ws.watched, 0) = 0
                """, ("$p", profileId), ("$par", par)) ?? 0;
            if (left == 0)
            {
                UpsertState(c, profileId, par, null, 0, true, now);
                RemoveFromTonight(c, profileId, par);
            }
        }
    }

    static void RemoveFromTonight(SqliteConnection c, long profileId, long workId) =>
        Db.Exec(c, "DELETE FROM list_items WHERE work_id = $w AND list_id IN (SELECT id FROM lists WHERE profile_id = $p AND is_tonight = 1)",
            ("$w", workId), ("$p", profileId));

    static void UpsertState(SqliteConnection c, long profileId, long? workId, long? itemId, int position, bool? watched, DateTimeOffset now)
    {
        string where = itemId != null ? "profile_id = $p AND item_id = $i" : "profile_id = $p AND work_id = $w AND item_id IS NULL";
        int n = Db.Exec(c, $"UPDATE watch_states SET position_s = $pos, watched = coalesce($wd, watched), updated_at = $t WHERE {where}",
            ("$pos", position), ("$wd", watched is bool b ? (b ? 1 : 0) : null), ("$t", Db.Unix(now)), ("$p", profileId), ("$i", itemId), ("$w", workId));
        if (n == 0)
            Db.Exec(c, "INSERT INTO watch_states (profile_id, work_id, item_id, watched, position_s, updated_at) VALUES ($p, $w, $i, $wd, $pos, $t)",
                ("$p", profileId), ("$w", itemId != null ? null : workId), ("$i", itemId), ("$wd", watched == true ? 1 : 0), ("$pos", position), ("$t", Db.Unix(now)));
    }

    /// <summary>"Continua a guardare": file iniziati e non finiti dal profilo, i più recenti prima.</summary>
    public List<(long ItemId, int PositionS, DateTimeOffset At)> InProgress(long profileId, int max = 20)
    {
        using var c = Open();
        return Db.Query(c, """
            SELECT ws.item_id, ws.position_s, ws.updated_at FROM watch_states ws JOIN library_items li ON li.id = ws.item_id
            WHERE ws.profile_id = $p AND ws.item_id IS NOT NULL AND ws.watched = 0 AND ws.position_s > 30 AND li.missing_since IS NULL
            ORDER BY ws.updated_at DESC LIMIT $max
            """,
            r => (r.GetInt64(0), r.GetInt32(1), Db.FromUnix(r.GetInt64(2))), ("$p", profileId), ("$max", max));
    }
}
