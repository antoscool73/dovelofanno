using DoveLoFanno.Ratings;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Data;

/// <summary>
/// Dove si vede un'opera: kind = subscription, free, rent, buy. Source = justwatch (JustWatch tramite TMDB) o release
/// (originale della piattaforma dedotto dall'uscita, finché JustWatch non lo ha in catalogo).
/// </summary>
public sealed record WorkAvailability(string Platform, string Kind, string Link, DateTimeOffset CheckedAt, string Source = "justwatch");

/// <summary>Un file dell'opera in casa (per ora: le registrazioni riuscite).</summary>
public sealed record WorkFile(long RecordingId, string File, DateTimeOffset Start, string ChannelName, string State, int LostMinutes);

/// <summary>Tutto quello che serve alla Scheda di un'opera, anche lo stato per il profilo che la guarda.</summary>
public sealed record WorkDetail(
    long Id, string Kind, string Title, string OriginalTitle, int Year, double? Rating, string RatingSource, int RatingVotes,
    long? TmdbId, string ImdbId, string Overview, string Poster, string Backdrop, int? RuntimeMin, string Trailer, DateTimeOffset? DetailsAt,
    IReadOnlyList<GuideProgramme> Airings, IReadOnlyList<WorkFile> InHouse, IReadOnlyList<WorkAvailability> Availability,
    bool InTonight, bool Watched, int? Stars, IReadOnlyList<LibraryFile> Files, IReadOnlyList<WorkListRef> Lists, long? ParentId, string ParentTitle);

/// <summary>Un'opera già nota trovata dalla ricerca: prossimo passaggio in TV e se c'è in casa.</summary>
public sealed record WorkSearchHit(long Id, string Kind, string Title, int Year, string Poster, double? Rating, string RatingSource,
    long? TmdbId, string TmdbKind, DateTimeOffset? NextAiring, bool InHouse, IReadOnlyList<string>? Streaming = null, IReadOnlyList<string>? Paid = null)
{
    /// <summary>Generi TMDB (codici), se l'opera ha i dettagli.</summary>
    public IReadOnlyList<int> GenreIds { get; init; } = [];
    /// <summary>Dalla guida: genere scritto (sottogenere o gruppo), paese, regia, attori.</summary>
    public string GuideGenre { get; init; } = "";
    public string Country { get; init; } = "";
    public string Directors { get; init; } = "";
    public string Actors { get; init; } = "";
    public string Overview { get; init; } = "";
}

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

        // per programmi e documentari i passaggi di tutte le puntate con lo stesso titolo, come nella serata (20/11/2026:
        // la Scheda di «L'Eredità» del 18/11 non mostrava i passaggi di oggi)
        var progIds = Db.Query(c, $"""
            SELECT p.id FROM programmes p, works w
            WHERE w.id = $w AND p.work_id IN {TonightStore.SameShow("w")} AND p.stop > $now ORDER BY p.start LIMIT 50
            """,
            r => r.GetInt64(0), ("$w", workId), ("$now", Db.Unix(now)));
        var files = Db.Query(c, """
            SELECT r.id, r.file, r.start, ch.name, r.state, r.lost_minutes FROM recordings r JOIN channels ch ON ch.id = r.channel_id
            WHERE r.work_id = $w AND r.state IN ('completed', 'incomplete', 'stopped') AND r.file <> '' ORDER BY r.start DESC
            """,
            r => new WorkFile(r.GetInt64(0), r.GetString(1), Db.FromUnix(r.GetInt64(2)), r.GetString(3), r.GetString(4), r.GetInt32(5)),
            ("$w", workId));
        var avail = Db.Query(c, "SELECT platform, kind, link, checked_at, source FROM availability WHERE work_id = $w ORDER BY kind, platform",
            r => new WorkAvailability(r.GetString(0), r.GetString(1), r.GetString(2), Db.FromUnix(r.GetInt64(3)), r.GetString(4)), ("$w", workId));
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

    /// <summary>
    /// Opere già note (dalla guida, dai file di casa o aperte da una ricerca) il cui titolo o titolo originale contiene il
    /// testo, senza badare ad accenti e maiuscole; prima quelle in TV o in casa. Gli episodi no: c'è la serie.
    /// </summary>
    public List<WorkSearchHit> Search(string text, DateTimeOffset now, bool childOnly = false, int max = 20) =>
        Epg.Titles.TitleKey(text).Length < 2 ? [] : Find(text, now, childOnly, max);

    /// <summary>
    /// Opere note per la ricerca avanzata: con il testo, quelle il cui titolo lo contiene (come <see cref="Search"/>);
    /// senza testo, quelle che si possono vedere o che interessano (in TV nei prossimi giorni, in casa, in una lista, su
    /// una piattaforma), programmi esclusi. <paramref name="ids"/>: solo queste opere (per i titoli trovati su TMDB).
    /// I filtri si applicano poi in memoria: qui ci sono anche generi, paese, regia e attori dalla guida.
    /// </summary>
    public List<WorkSearchHit> Find(string text, DateTimeOffset now, bool childOnly = false, int max = 400, IReadOnlyCollection<long>? ids = null)
    {
        string k = Epg.Titles.TitleKey(text);
        using var c = Open();
        string child = childOnly ? " AND " + ChildSafe("w") : "";
        string which = ids != null
            ? " AND w.id IN (SELECT value FROM json_each($ids))"
            : k.Length > 0
                ? " AND (w.title_key LIKE '%' || $k || '%' OR (w.original_title <> '' AND lower(w.original_title) LIKE '%' || lower($t) || '%'))"
                : """
                   AND w.kind <> 'programme' AND (EXISTS (SELECT 1 FROM programmes p WHERE p.work_id = w.id AND p.stop > $now)
                     OR EXISTS (SELECT 1 FROM library_items li WHERE li.work_id = w.id AND li.missing_since IS NULL)
                     OR EXISTS (SELECT 1 FROM list_items x WHERE x.work_id = w.id)
                     OR EXISTS (SELECT 1 FROM availability a WHERE a.work_id = w.id))
                  """;
        return Db.Query(c, """
            SELECT w.id, w.kind, w.title, w.year, w.poster, w.rating, w.rating_source, w.tmdb_id, coalesce(w.tmdb_kind, ''),
                   (SELECT min(p.start) FROM programmes p WHERE p.work_id = w.id AND p.stop > $now) AS next,
                   EXISTS (SELECT 1 FROM library_items li JOIN works e ON e.id = li.work_id
                           WHERE (e.id = w.id OR e.parent_id = w.id) AND li.missing_since IS NULL)
                   OR EXISTS (SELECT 1 FROM recordings r WHERE r.work_id = w.id AND r.state IN ('completed', 'incomplete') AND r.file <> '') AS home,
                   (SELECT p.icon FROM programmes p WHERE p.work_id = w.id AND p.icon <> '' LIMIT 1),
                   coalesce((SELECT group_concat(a.platform, '|') FROM availability a WHERE a.work_id = w.id AND a.kind IN ('subscription', 'free')), ''),
                   coalesce((SELECT group_concat(DISTINCT a.platform) FROM availability a WHERE a.work_id = w.id AND a.kind IN ('rent', 'buy')), ''),
                   w.genres, w.overview,
                   coalesce((SELECT CASE WHEN p.sub_genre <> '' THEN p.sub_genre ELSE p.group_name END || char(31) || p.country || char(31)
                                    || p.directors || char(31) || p.actors
                             FROM programmes p WHERE p.work_id = w.id ORDER BY p.start DESC LIMIT 1), '')
            FROM works w
            WHERE w.parent_id IS NULL AND w.kind <> 'episode'
            """ + which + child + """

            ORDER BY (next IS NOT NULL OR home) DESC, (w.title_key = $k) DESC, w.rating IS NULL, w.rating DESC, w.rating_votes DESC, w.id
            LIMIT $max
            """,
            r =>
            {
                string[] g = r.GetString(16).Split('\u001f');
                List<int> genres;
                try { genres = System.Text.Json.JsonSerializer.Deserialize<List<int>>(r.GetString(14)) ?? []; } catch (System.Text.Json.JsonException) { genres = []; }
                return new WorkSearchHit(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt32(3),
                    r.GetString(4).Length > 0 ? ImageUrl(r.GetString(4), "w342") : (r.IsDBNull(11) ? "" : r.GetString(11)),
                    r.IsDBNull(5) ? null : r.GetDouble(5), r.GetString(6), Db.NLong(r, 7), r.GetString(8), Db.NTime(r, 9), r.GetInt64(10) == 1,
                    r.GetString(12).Split('|', StringSplitOptions.RemoveEmptyEntries).Distinct().ToList(), r.GetString(13).Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    GenreIds = genres, Overview = r.GetString(15),
                    GuideGenre = g.Length > 0 ? g[0] : "", Country = g.Length > 1 ? g[1] : "", Directors = g.Length > 2 ? g[2] : "", Actors = g.Length > 3 ? g[3] : "",
                };
            },
            ("$k", k), ("$t", text.Trim()), ("$now", Db.Unix(now)), ("$max", max),
            ("$ids", ids == null ? "[]" : System.Text.Json.JsonSerializer.Serialize(ids)));
    }

    /// <summary>Codice IMDb di un'opera, se non l'aveva.</summary>
    public void SetImdbId(long workId, string imdbId)
    {
        if (imdbId.Length == 0) return;
        using var c = Open();
        Db.Exec(c, "UPDATE works SET imdb_id = $i WHERE id = $w AND imdb_id = ''", ("$i", imdbId), ("$w", workId));
    }

    /// <summary>Opere delle liste (di qualunque profilo, la mia serata compresa) con codice TMDB e senza voto IMDb.</summary>
    public List<(long WorkId, long TmdbId, string Kind, string ImdbId)> ListWorksWithoutImdbRating(int max = 200)
    {
        using var c = Open();
        return Db.Query(c, """
            SELECT DISTINCT w.id, w.tmdb_id, coalesce(w.tmdb_kind, 'movie'), w.imdb_id
            FROM list_items li JOIN works w ON w.id = li.work_id
            WHERE w.tmdb_id IS NOT NULL AND w.rating_source <> 'IMDb' AND w.kind IN ('film', 'series', 'documentary')
            ORDER BY w.id LIMIT $max
            """,
            r => (r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3)), ("$max", max));
    }

    /// <summary>Voto IMDb (vince su quello TMDB).</summary>
    public void SetImdbRating(long workId, double rating, int votes)
    {
        using var c = Open();
        Db.Exec(c, "UPDATE works SET rating = $r, rating_source = 'IMDb', rating_votes = $v WHERE id = $id",
            ("$r", Math.Round(rating, 1)), ("$v", votes), ("$id", workId));
    }

    /// <summary>Voto TMDB, se l'opera non ha già quello IMDb. false se non è cambiato niente.</summary>
    public bool SetTmdbRating(long workId, double rating, int votes)
    {
        using var c = Open();
        return Db.Exec(c, """
            UPDATE works SET rating = $r, rating_source = 'TMDB', rating_votes = $v
            WHERE id = $id AND rating_source <> 'IMDb' AND (rating IS NULL OR rating <> $r OR rating_votes <> $v)
            """, ("$r", Math.Round(rating, 1)), ("$v", votes), ("$id", workId)) > 0;
    }

    /// <summary>Opere con questi codici TMDB già note: (movie|tv, codice) → opera.</summary>
    public Dictionary<(string Kind, long TmdbId), long> ByTmdb(IEnumerable<(string Kind, long TmdbId)> ids)
    {
        var res = new Dictionary<(string, long), long>();
        using var c = Open();
        foreach (var (kind, id) in ids.Distinct())
            if (Db.ScalarLong(c, "SELECT id FROM works WHERE tmdb_kind = $k AND tmdb_id = $t AND parent_id IS NULL ORDER BY id LIMIT 1", ("$k", kind), ("$t", id)) is long w)
                res[(kind, id)] = w;
        return res;
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

    /// <summary>
    /// Film e serie con codice TMDB che passano in guida nei prossimi giorni e non hanno ancora i dettagli (locandina,
    /// trama, piattaforme), o li hanno da più di una settimana: si scaricano in background dopo i voti.
    /// </summary>
    public List<(long WorkId, long TmdbId, string Kind)> WorksNeedingDetails(DateTimeOffset now, int days, int max)
    {
        using var c = Open();
        return Db.Query(c, """
            SELECT w.id, w.tmdb_id, coalesce(w.tmdb_kind, 'movie') FROM works w
            WHERE w.tmdb_id IS NOT NULL AND w.kind IN ('film', 'series', 'documentary') AND (w.details_at IS NULL OR w.details_at < $old)
              AND EXISTS (SELECT 1 FROM programmes p WHERE p.work_id = w.id AND p.stop > $now AND p.start < $to)
            ORDER BY (SELECT min(p.start) FROM programmes p WHERE p.work_id = w.id AND p.stop > $now) LIMIT $max
            """,
            r => (r.GetInt64(0), r.GetInt64(1), r.GetString(2)),
            ("$old", Db.Unix(now.AddDays(-7))), ("$now", Db.Unix(now)), ("$to", Db.Unix(now.AddDays(days))), ("$max", max));
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
            Db.Exec(c, "INSERT OR IGNORE INTO availability (work_id, platform, kind, link, checked_at, source) VALUES ($w, $n, $k, $l, $t, $s)",
                ("$w", workId), ("$n", p.Name), ("$k", p.Kind), ("$l", p.Source == "release" ? "" : d.WatchLink), ("$t", Db.Unix(now)), ("$s", p.Source));
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
