using DoveLoFanno.Ratings;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Data;

/// <summary>Dove si vede un'opera (dati JustWatch tramite TMDB): kind = subscription, free, rent, buy.</summary>
public sealed record WorkAvailability(string Platform, string Kind, string Link, DateTimeOffset CheckedAt);

/// <summary>Un file dell'opera in casa (per ora: le registrazioni riuscite).</summary>
public sealed record WorkFile(long RecordingId, string File, DateTimeOffset Start, string ChannelName, string State, int LostMinutes);

/// <summary>Tutto quello che serve alla Scheda di un'opera, anche lo stato per il profilo che la guarda.</summary>
public sealed record WorkDetail(
    long Id, string Kind, string Title, string OriginalTitle, int Year, double? Rating, string RatingSource, int RatingVotes,
    long? TmdbId, string ImdbId, string Overview, string Poster, string Backdrop, int? RuntimeMin, string Trailer, DateTimeOffset? DetailsAt,
    IReadOnlyList<GuideProgramme> Airings, IReadOnlyList<WorkFile> InHouse, IReadOnlyList<WorkAvailability> Availability,
    bool InTonight, bool Watched, int? Stars, IReadOnlyList<LibraryFile> Files, IReadOnlyList<WorkListRef> Lists, long? ParentId, string ParentTitle);

/// <summary>Opere: scheda completa, dettagli da TMDB, voto personale a stelle.</summary>
public sealed class WorkStore(string dbPath, GuideStore guide)
{
    public const string ImageBase = "https://image.tmdb.org/t/p/";

    /// <summary>
    /// SQL: l'opera <paramref name="alias"/> è adatta a un profilo bambino: in guida tra i "Bambini", oppure con un genere
    /// TMDB di animazione (16), famiglia (10751) o per bambini (10762).
    /// </summary>
    public static string ChildSafe(string alias) =>
        $"(EXISTS (SELECT 1 FROM programmes cp WHERE cp.work_id = {alias}.id AND cp.group_name = 'Bambini')" +
        $" OR EXISTS (SELECT 1 FROM json_each({alias}.genres) cg WHERE cg.value IN (16, 10751, 10762)))";
    SqliteConnection Open() => Database.Open(dbPath);

    /// <summary>null se l'opera non esiste. <paramref name="profileId"/> facoltativo: senza, lo stato del profilo è vuoto.</summary>
    public WorkDetail? Detail(long workId, long? profileId, DateTimeOffset now)
    {
        using var c = Open();
        var w = Db.Query(c, """
            SELECT w.id, w.kind, w.title, w.original_title, w.year, w.rating, w.rating_source, w.rating_votes, w.tmdb_id, w.imdb_id, w.overview,
                   w.poster, w.backdrop, w.runtime_min, w.trailer, w.details_at, w.parent_id, coalesce(p.title, '')
            FROM works w LEFT JOIN works p ON p.id = w.parent_id WHERE w.id = $id
            """,
            r => (Id: r.GetInt64(0), Kind: r.GetString(1), Title: r.GetString(2), Orig: r.GetString(3), Year: r.GetInt32(4),
                Rating: r.IsDBNull(5) ? (double?)null : r.GetDouble(5), Src: r.GetString(6), Votes: r.GetInt32(7), Tmdb: Db.NLong(r, 8),
                Imdb: r.GetString(9), Overview: r.GetString(10), Poster: r.GetString(11), Backdrop: r.GetString(12), Runtime: Db.NInt(r, 13),
                Trailer: r.GetString(14), DetailsAt: Db.NTime(r, 15), Parent: Db.NLong(r, 16), ParentTitle: r.GetString(17)),
            ("$id", workId)).FirstOrDefault();
        if (w.Id == 0) return null;

        var progIds = Db.Query(c, "SELECT id FROM programmes WHERE work_id = $w AND stop > $now ORDER BY start LIMIT 50",
            r => r.GetInt64(0), ("$w", workId), ("$now", Db.Unix(now)));
        var files = Db.Query(c, """
            SELECT r.id, r.file, r.start, ch.name, r.state, r.lost_minutes FROM recordings r JOIN channels ch ON ch.id = r.channel_id
            WHERE r.work_id = $w AND r.state IN ('completed', 'incomplete', 'stopped') AND r.file <> '' ORDER BY r.start DESC
            """,
            r => new WorkFile(r.GetInt64(0), r.GetString(1), Db.FromUnix(r.GetInt64(2)), r.GetString(3), r.GetString(4), r.GetInt32(5)),
            ("$w", workId));
        var avail = Db.Query(c, "SELECT platform, kind, link, checked_at FROM availability WHERE work_id = $w ORDER BY kind, platform",
            r => new WorkAvailability(r.GetString(0), r.GetString(1), r.GetString(2), Db.FromUnix(r.GetInt64(3))), ("$w", workId));
        bool inTonight = false, watched = false;
        int? stars = null;
        if (profileId is long pid)
        {
            inTonight = (Db.ScalarLong(c, """
                SELECT count(*) FROM list_items li JOIN lists l ON l.id = li.list_id WHERE l.profile_id = $p AND l.is_tonight = 1 AND li.work_id = $w
                """, ("$p", pid), ("$w", workId)) ?? 0) > 0;
            var ws = Db.Query(c, "SELECT watched, stars FROM watch_states WHERE profile_id = $p AND work_id = $w AND item_id IS NULL",
                r => (r.GetInt64(0) == 1, Db.NInt(r, 1)), ("$p", pid), ("$w", workId));
            if (ws.Count > 0) (watched, stars) = ws[0];
        }
        c.Dispose();
        var airings = progIds.Select(guide.Programme).Where(p => p != null).Select(p => p!).ToList();
        var libraryFiles = new LibraryStore(dbPath).Files(workId, profileId);
        var lists = profileId is long lp ? new ListStore(dbPath, guide).ListsOf(lp, workId) : [];
        return new WorkDetail(w.Id, w.Kind, w.Title, w.Orig, w.Year, w.Rating, w.Src, w.Votes, w.Tmdb, w.Imdb, w.Overview,
            ImageUrl(w.Poster, "w500"), ImageUrl(w.Backdrop, "w1280"), w.Runtime, w.Trailer, w.DetailsAt, airings, files, avail, inTonight, watched, stars,
            libraryFiles, lists, w.Parent, w.ParentTitle);
    }

    /// <summary>Indirizzo completo di un'immagine TMDB (/abc.jpg) nella misura chiesta; gli indirizzi completi restano come sono.</summary>
    public static string ImageUrl(string path, string size) => path.Length == 0 || path.StartsWith("http", StringComparison.Ordinal) ? path : ImageBase + size + path;

    /// <summary>
    /// Film e serie con codice TMDB i cui dettagli mancano o hanno più di un giorno: codice e tipo (movie o tv), altrimenti null.
    /// </summary>
    public (long TmdbId, string Kind)? DetailsToRefresh(long workId, DateTimeOffset now)
    {
        using var c = Open();
        var r = Db.Query(c, """
            SELECT tmdb_id, coalesce(tmdb_kind, 'movie') FROM works WHERE id = $w AND tmdb_id IS NOT NULL
              AND (tmdb_kind IN ('movie', 'tv') OR (tmdb_kind IS NULL AND kind = 'film'))
              AND kind IN ('film', 'series', 'documentary') AND (details_at IS NULL OR details_at < $old)
            """,
            x => (x.GetInt64(0), x.GetString(1)), ("$w", workId), ("$old", Db.Unix(now.AddDays(-1))));
        return r.Count == 0 ? null : r[0];
    }

    public void SaveDetails(long workId, MovieDetails d, DateTimeOffset now)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        Db.Exec(c, """
            UPDATE works SET overview = CASE WHEN $o <> '' THEN $o ELSE overview END, poster = $p, backdrop = $b,
                   runtime_min = CASE WHEN $r > 0 THEN $r ELSE runtime_min END,
                   original_title = CASE WHEN original_title = '' THEN $ot ELSE original_title END,
                   trailer = $t, details_at = $now, genres = CASE WHEN $g <> '[]' THEN $g ELSE genres END WHERE id = $id
            """,
            ("$g", System.Text.Json.JsonSerializer.Serialize(d.GenreIds ?? [])), ("$o", d.Overview), ("$p", d.PosterPath), ("$b", d.BackdropPath), ("$r", d.Runtime), ("$ot", d.OriginalTitle),
            ("$t", d.Trailer), ("$now", Db.Unix(now)), ("$id", workId));
        Db.Exec(c, "DELETE FROM availability WHERE work_id = $w", ("$w", workId));
        foreach (var p in d.Providers)
            Db.Exec(c, "INSERT OR IGNORE INTO availability (work_id, platform, kind, link, checked_at) VALUES ($w, $n, $k, $l, $t)",
                ("$w", workId), ("$n", p.Name), ("$k", p.Kind), ("$l", d.WatchLink), ("$t", Db.Unix(now)));
        tx.Commit();
    }

    /// <summary>Voto personale da 1 a 5 (null lo toglie). false se profilo o opera non esistono o il voto non è valido.</summary>
    public bool SetStars(long profileId, long workId, int? stars, DateTimeOffset now)
    {
        if (stars is < 1 or > 5) return false;
        using var c = Open();
        if (Db.ScalarLong(c, "SELECT id FROM profiles WHERE id = $p", ("$p", profileId)) == null) return false;
        if (Db.ScalarLong(c, "SELECT id FROM works WHERE id = $w", ("$w", workId)) == null) return false;
        int n = Db.Exec(c, "UPDATE watch_states SET stars = $s, updated_at = $t WHERE profile_id = $p AND work_id = $w AND item_id IS NULL",
            ("$s", stars), ("$t", Db.Unix(now)), ("$p", profileId), ("$w", workId));
        if (n == 0)
            Db.Exec(c, "INSERT INTO watch_states (profile_id, work_id, stars, updated_at) VALUES ($p, $w, $s, $t)",
                ("$p", profileId), ("$w", workId), ("$s", stars), ("$t", Db.Unix(now)));
        return true;
    }
}
