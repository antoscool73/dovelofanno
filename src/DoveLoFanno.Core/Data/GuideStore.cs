using System.Globalization;
using System.Text.Json;
using DoveLoFanno.Epg;
using DoveLoFanno.Sources;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Data;

public sealed record GuideSourceRow(long Id, string Url, bool Enabled, bool FromPlaylist, DateTimeOffset? LastLoadedAt, DateTimeOffset? LastStop, string LastError, int Channels, int Programmes);

public sealed record GuideStatus(int Channels, int LinkedChannels, int Programmes, DateTimeOffset? LastLoadedAt, DateTimeOffset? Until, IReadOnlyList<GuideSourceRow> Guides);

/// <summary>Un programma della guida, con il canale della playlist (se il canale si può registrare) e l'opera.</summary>
public sealed record GuideProgramme(
    long Id, long GuideChannelId, string GuideChannelName, long? ChannelId, string ChannelName,
    DateTimeOffset Start, DateTimeOffset Stop, string Title, string Subtitle, string Description,
    IReadOnlyList<string> Categories, string Group, string Sub, int Year, string Country, string Directors, string Actors,
    string Episode, string Icon, long? WorkId, string WorkKey, double? Rating, string RatingSource, bool Scheduled,
    int? ChannelNumber = null, int? ChannelOrder = null);

/// <summary>Filtri della guida: finestra di tempo obbligatoria, il resto facoltativo.</summary>
public sealed record GuideQuery(DateTimeOffset From, DateTimeOffset To)
{
    public long? ChannelId { get; init; }
    public string? Text { get; init; }
    public string? Group { get; init; }
    public string? Sub { get; init; }
    public double MinRating { get; init; }
    /// <summary>Solo i canali delle playlist (registrabili); altrimenti tutti i canali della guida.</summary>
    public bool OnlyRecordable { get; init; }
    public int Max { get; init; } = 2000;
}

/// <summary>
/// Guida TV nel database: fonti XMLTV, canali della guida, programmi, opere. Ogni caricamento sostituisce i programmi
/// della sua fonte in una transazione; i canali delle playlist si collegano ai canali della guida con le regole di
/// EpgGuide.Resolve (tvg-id, nome normalizzato, alias), e se un canale è in più guide vale quella che arriva più lontano.
/// </summary>
public sealed class GuideStore(string dbPath)
{
    SqliteConnection Open() => Database.Open(dbPath);

    /// <summary>Aggiunge come fonti le guide indicate dalle playlist (x-tvg-url) che non ci sono ancora.</summary>
    public void SyncFromPlaylists()
    {
        using var c = Open();
        Db.Exec(c, """
            INSERT OR IGNORE INTO guide_sources (url)
            SELECT DISTINCT xmltv_url FROM sources WHERE enabled = 1 AND xmltv_url <> ''
            """);
    }

    public long AddSource(string url)
    {
        using var c = Open();
        long? id = Db.ScalarLong(c, "SELECT id FROM guide_sources WHERE url = $u", ("$u", url));
        return id ?? Db.Insert(c, "INSERT INTO guide_sources (url) VALUES ($u)", ("$u", url));
    }

    public void SetEnabled(long id, bool enabled)
    {
        using var c = Open();
        Db.Exec(c, "UPDATE guide_sources SET enabled = $e WHERE id = $id", ("$e", enabled ? 1 : 0), ("$id", id));
    }

    public void DeleteSource(long id)
    {
        using var c = Open();
        Db.Exec(c, "DELETE FROM guide_sources WHERE id = $id", ("$id", id));
    }

    public List<GuideSourceRow> Sources()
    {
        using var c = Open();
        return Db.Query(c, """
            SELECT g.id, g.url, g.enabled, EXISTS (SELECT 1 FROM sources s WHERE s.xmltv_url = g.url),
                   g.last_loaded_at, g.last_stop, g.last_error,
                   (SELECT count(*) FROM guide_channels gc WHERE gc.guide_source_id = g.id),
                   (SELECT count(*) FROM programmes p JOIN guide_channels gc ON gc.id = p.guide_channel_id WHERE gc.guide_source_id = g.id)
            FROM guide_sources g ORDER BY g.id
            """,
            r => new GuideSourceRow(r.GetInt64(0), r.GetString(1), r.GetInt64(2) == 1, r.GetInt64(3) == 1, Db.NTime(r, 4), Db.NTime(r, 5), r.GetString(6), r.GetInt32(7), r.GetInt32(8)));
    }

    public void SetError(long id, string error, DateTimeOffset now)
    {
        using var c = Open();
        Db.Exec(c, "UPDATE guide_sources SET last_error = $e, last_loaded_at = coalesce(last_loaded_at, $t) WHERE id = $id", ("$e", error), ("$t", Db.Unix(now)), ("$id", id));
    }

    static long Unix(DateTime local) => new DateTimeOffset(local).ToUnixTimeSeconds();

    static string Kind(EpgProgramme p) => p.Group switch
    {
        Genres.Film => "film",
        Genres.Series => p.Episode.Length > 0 ? "episode" : "series",
        Genres.Documentaries => "documentary",
        _ => "programme",
    };

    /// <summary>Salva la guida letta come contenuto della fonte e ricollega i canali delle playlist. Restituisce i programmi salvati.</summary>
    public int Save(long guideSourceId, EpgGuide guide, DateTimeOffset now)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        long t = Db.Unix(now);
        Db.Exec(c, "DELETE FROM programmes WHERE guide_channel_id IN (SELECT id FROM guide_channels WHERE guide_source_id = $s)", ("$s", guideSourceId));

        using var upChannel = Prepare(c, tx, """
            INSERT INTO guide_channels (guide_source_id, xmltv_id, name) VALUES ($s, $x, $n)
            ON CONFLICT (guide_source_id, xmltv_id) DO UPDATE SET name = $n RETURNING id
            """, "$s", "$x", "$n");
        using var upWork = Prepare(c, tx, """
            INSERT INTO works (kind, work_key, title, title_key, year, updated_at) VALUES ($k, $w, $t, $tk, $y, $u)
            ON CONFLICT (work_key) DO UPDATE SET updated_at = $u RETURNING id
            """, "$k", "$w", "$t", "$tk", "$y", "$u");
        using var insProg = Prepare(c, tx, """
            INSERT OR IGNORE INTO programmes (guide_channel_id, start, stop, title, subtitle, description, categories, group_name, sub_genre,
                group_guessed, year, country, directors, actors, episode, icon, work_id)
            VALUES ($c, $a, $b, $t, $st, $d, $cat, $g, $sg, $gg, $y, $co, $dir, $act, $ep, $ic, $w)
            """, "$c", "$a", "$b", "$t", "$st", "$d", "$cat", "$g", "$sg", "$gg", "$y", "$co", "$dir", "$act", "$ep", "$ic", "$w");

        var guideChannel = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        int count = 0;
        foreach (string xid in guide.ChannelIds)
        {
            Set(upChannel, guideSourceId, xid, guide.ChannelName(xid));
            long gcid = Convert.ToInt64(upChannel.ExecuteScalar(), CultureInfo.InvariantCulture);
            guideChannel[xid] = gcid;
            foreach (var p in guide.Programmes(xid))
            {
                if (p.Title.Length == 0) continue;
                Set(upWork, Kind(p), p.WorkKey, p.Title, p.TitleKey, p.Year, t);
                long wid = Convert.ToInt64(upWork.ExecuteScalar(), CultureInfo.InvariantCulture);
                Set(insProg, gcid, Unix(p.Start), Math.Max(Unix(p.Start), Unix(p.Stop)), p.Title, p.SubTitle, p.Desc,
                    JsonSerializer.Serialize(p.Categories), p.Group, p.Sub, p.GroupGuessed ? 1 : 0, p.Year, p.Country, p.Directors, p.Actors,
                    p.Episode, p.Icon, wid);
                count += insProg.ExecuteNonQuery();
            }
        }
        long lastStop = guide.LastStop == DateTime.MinValue ? t : Unix(guide.LastStop);
        Db.Exec(c, "UPDATE guide_sources SET last_loaded_at = $t, last_stop = $l, last_error = '' WHERE id = $id",
            ("$t", t), ("$l", lastStop), ("$id", guideSourceId));

        // collegamento dei canali delle playlist: questa guida vince su una che arriva meno lontano
        var current = Db.Query(c, """
            SELECT ch.id, ch.key, ch.name, coalesce(gs.last_stop, 0), coalesce(gs.id, 0)
            FROM channels ch LEFT JOIN guide_channels gc ON gc.id = ch.guide_channel_id LEFT JOIN guide_sources gs ON gs.id = gc.guide_source_id
            """, r => (Id: r.GetInt64(0), Key: r.GetString(1), Name: r.GetString(2), Stop: r.GetInt64(3), Source: r.GetInt64(4)));
        foreach (var ch in current)
        {
            string? tvg = ch.Key.StartsWith("id:", StringComparison.Ordinal) ? ch.Key[3..] : null;
            string? xid = guide.Resolve(tvg, ch.Name);
            if (xid == null || !guideChannel.TryGetValue(xid, out long gcid)) continue;
            if (ch.Source != 0 && ch.Source != guideSourceId && ch.Stop > lastStop) continue;
            Db.Exec(c, "UPDATE channels SET guide_channel_id = $g WHERE id = $id", ("$g", gcid), ("$id", ch.Id));
        }
        tx.Commit();
        return count;
    }

    static SqliteCommand Prepare(SqliteConnection c, SqliteTransaction tx, string sql, params string[] names)
    {
        var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (string n in names) cmd.Parameters.Add(new SqliteParameter(n, null));
        cmd.Prepare();
        return cmd;
    }

    static void Set(SqliteCommand cmd, params object?[] values)
    {
        for (int i = 0; i < values.Length; i++) cmd.Parameters[i].Value = values[i] ?? DBNull.Value;
    }

    public GuideStatus Status()
    {
        var guides = Sources();
        using var c = Open();
        int channels = (int)(Db.ScalarLong(c, "SELECT count(*) FROM guide_channels") ?? 0);
        int linked = (int)(Db.ScalarLong(c, "SELECT count(*) FROM channels WHERE guide_channel_id IS NOT NULL") ?? 0);
        int progs = (int)(Db.ScalarLong(c, "SELECT count(*) FROM programmes") ?? 0);
        var last = guides.Where(g => g.LastLoadedAt != null).Select(g => g.LastLoadedAt).DefaultIfEmpty(null).Max();
        var until = guides.Where(g => g.LastStop != null).Select(g => g.LastStop).DefaultIfEmpty(null).Max();
        return new GuideStatus(channels, linked, progs, last, until, guides);
    }

    /// <summary>Programmi che si sovrappongono a [From, To), ordinati per inizio e canale.</summary>
    public List<GuideProgramme> Query(GuideQuery q)
    {
        using var c = Open();
        var pending = Db.Query(c, """
            SELECT channel_id, start - before_min * 60, stop + after_min * 60 FROM recordings
            WHERE state IN ('scheduled', 'recording', 'merging') AND stop + after_min * 60 > $from AND start - before_min * 60 < $to
            """, r => (Ch: r.GetInt64(0), A: r.GetInt64(1), B: r.GetInt64(2)), ("$from", Db.Unix(q.From)), ("$to", Db.Unix(q.To)));

        var rows = Db.Query(c, """
            SELECT p.id, gc.id, gc.name, ch.id, coalesce(ch.name, ''), p.start, p.stop, p.title, p.subtitle, p.description, p.categories,
                   p.group_name, p.sub_genre, p.year, p.country, p.directors, p.actors, p.episode, p.icon, p.work_id, coalesce(w.work_key, ''),
                   w.rating, coalesce(w.rating_source, ''), coalesce(ch.key, ''), gc.xmltv_id
            FROM programmes p
            JOIN guide_channels gc ON gc.id = p.guide_channel_id
            JOIN guide_sources gs ON gs.id = gc.guide_source_id AND gs.enabled = 1
            LEFT JOIN channels ch ON ch.id = (SELECT min(x.id) FROM channels x WHERE x.guide_channel_id = gc.id AND x.excluded = 0)
            LEFT JOIN works w ON w.id = p.work_id
            WHERE p.stop > $from AND p.start < $to
              AND ($ch IS NULL OR ch.id = $ch)
              AND ($g IS NULL OR p.group_name = $g)
              AND ($sg IS NULL OR p.sub_genre = $sg COLLATE NOCASE)
              AND ($min <= 0 OR coalesce(w.rating, 0) >= $min)
              AND ($rec = 0 OR ch.id IS NOT NULL)
            ORDER BY p.start, coalesce(ch.name, gc.name), p.id
            """,
            r =>
            {
                long? chId = Db.NLong(r, 3);
                long a = r.GetInt64(5), b = r.GetInt64(6);
                // in programma: una registrazione dello stesso canale ne copre almeno metà (come RegistraTV)
                bool scheduled = chId is long id && pending.Any(x => x.Ch == id && Math.Min(b, x.B) - Math.Max(a, x.A) >= (b - a) / 2.0);
                string[] cats;
                try { cats = JsonSerializer.Deserialize<string[]>(r.GetString(10)) ?? []; } catch (JsonException) { cats = []; }
                return new GuideProgramme(r.GetInt64(0), r.GetInt64(1), r.GetString(2), chId, r.GetString(4), Db.FromUnix(a), Db.FromUnix(b),
                    r.GetString(7), r.GetString(8), r.GetString(9), cats, r.GetString(11), r.GetString(12), r.GetInt32(13), r.GetString(14),
                    r.GetString(15), r.GetString(16), r.GetString(17), r.GetString(18), Db.NLong(r, 19), r.GetString(20),
                    r.IsDBNull(21) ? null : r.GetDouble(21), r.GetString(22), scheduled,
                    chId != null ? ChannelNumbers.For(r.GetString(23), r.GetString(4)) : ChannelNumbers.For(r.GetString(24), r.GetString(2)));
            },
            ("$from", Db.Unix(q.From)), ("$to", Db.Unix(q.To)), ("$ch", q.ChannelId), ("$g", string.IsNullOrEmpty(q.Group) ? null : q.Group),
            ("$sg", string.IsNullOrEmpty(q.Sub) ? null : q.Sub), ("$min", q.MinRating), ("$rec", q.OnlyRecordable ? 1 : 0));

        // a parità di inizio, i canali nell'ordine della playlist (ChannelOrder, come RegistraTV); quelli della sola
        // guida dopo, per numero del digitale terrestre e nome
        var order = SourceStore.QueryChannels(c).ToDictionary(x => x.Id, x => x.Order);
        rows = [.. rows.Select(p => p.ChannelId is long id && order.TryGetValue(id, out int o) ? p with { ChannelOrder = o } : p)
            .OrderBy(p => p.Start).ThenBy(p => p, Comparer<GuideProgramme>.Create(CompareChannels)).ThenBy(p => p.Id)];

        if (!string.IsNullOrWhiteSpace(q.Text))
        {
            string t = q.Text.Trim();
            rows = rows.Where(p => Has(p.Title, t) || Has(p.Subtitle, t) || Has(p.Directors, t) || Has(p.Actors, t)).ToList();
        }
        if (q.Max > 0 && rows.Count > q.Max) rows.RemoveRange(q.Max, rows.Count - q.Max);
        return rows;
    }

    static int CompareChannels(GuideProgramme x, GuideProgramme y)
    {
        if (x.ChannelOrder != y.ChannelOrder)
        {
            if (x.ChannelOrder == null) return 1;
            if (y.ChannelOrder == null) return -1;
            return x.ChannelOrder.Value.CompareTo(y.ChannelOrder.Value);
        }
        return ChannelNumbers.Compare(x.ChannelNumber, x.ChannelId != null ? x.ChannelName : x.GuideChannelName,
                                      y.ChannelNumber, y.ChannelId != null ? y.ChannelName : y.GuideChannelName);
    }

    public GuideProgramme? Programme(long id)
    {
        using var c = Open();
        var p = Db.Query(c, "SELECT start, stop FROM programmes WHERE id = $id", r => (A: r.GetInt64(0), B: r.GetInt64(1)), ("$id", id));
        if (p.Count == 0) return null;
        return Query(new GuideQuery(Db.FromUnix(p[0].A), Db.FromUnix(p[0].B + 1)) { Max = 0 }).FirstOrDefault(x => x.Id == id);
    }

    /// <summary>Opere film con almeno un passaggio non ancora finito: quelle di cui cercare il voto.</summary>
    public List<(long WorkId, string Title, int Year)> FilmWorks(DateTimeOffset now)
    {
        using var c = Open();
        return Db.Query(c, """
            SELECT w.id, w.title, w.year FROM works w
            WHERE w.kind = 'film' AND EXISTS (SELECT 1 FROM programmes p WHERE p.work_id = w.id AND p.stop > $now)
            ORDER BY w.id
            """, r => (r.GetInt64(0), r.GetString(1), r.GetInt32(2)), ("$now", Db.Unix(now)));
    }

    /// <summary>Scrive (o toglie, con null) il voto di un'opera e i codici TMDB e IMDb.</summary>
    public void SetRating(long workId, Ratings.RatingInfo? r, DateTimeOffset now)
    {
        using var c = Open();
        if (r == null)
            Db.Exec(c, "UPDATE works SET rating = NULL, rating_source = '', rating_votes = 0, updated_at = $t WHERE id = $id", ("$t", Db.Unix(now)), ("$id", workId));
        else
            Db.Exec(c, """
                UPDATE works SET rating = $v, rating_source = $s, rating_votes = $n, tmdb_id = $tm, tmdb_kind = 'movie',
                       imdb_id = $im, original_title = $ot, updated_at = $t WHERE id = $id
                """,
                ("$v", r.Value), ("$s", r.Source), ("$n", r.Votes), ("$tm", r.TmdbId == 0 ? null : r.TmdbId), ("$im", r.ImdbId),
                ("$ot", r.OriginalTitle), ("$t", Db.Unix(now)), ("$id", workId));
    }

    static bool Has(string s, string t) =>
        CultureInfo.InvariantCulture.CompareInfo.IndexOf(s, t, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
}
