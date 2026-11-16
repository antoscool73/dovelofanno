using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Data;

/// <summary>Un titolo della lista "La mia serata in TV" con il suo prossimo passaggio in guida.</summary>
public sealed record TonightItem(
    long WorkId, string Kind, string Title, int Year, double? Rating, string RatingSource, DateTimeOffset AddedAt, GuideProgramme? Next);

public enum TonightResult { Ok, NoProfile, NoWork }

/// <summary>
/// "La mia serata in TV": la lista fissa di ogni profilo (creata con il profilo). Ci si mettono i titoli che si vogliono
/// vedere; "Visto" li toglie e li segna come visti per quel profilo.
/// </summary>
public sealed class TonightStore(string dbPath, GuideStore guide)
{
    SqliteConnection Open() => Database.Open(dbPath);

    static long? ListId(SqliteConnection c, long profileId) =>
        Db.ScalarLong(c, "SELECT id FROM lists WHERE profile_id = $p AND is_tonight = 1", ("$p", profileId));

    /// <summary>null se il profilo non esiste.</summary>
    public List<TonightItem>? Items(long profileId, DateTimeOffset now)
    {
        List<(long Work, string Kind, string Title, int Year, double? Rating, string Src, long Added, long? Prog)> rows;
        using (var c = Open())
        {
            long? list = ListId(c, profileId);
            if (list == null) return null;
            rows = Db.Query(c, """
                SELECT w.id, w.kind, w.title, w.year, w.rating, w.rating_source, li.added_at,
                       (SELECT p.id FROM programmes p WHERE p.work_id = w.id AND p.stop > $now ORDER BY p.start LIMIT 1)
                FROM list_items li JOIN works w ON w.id = li.work_id
                WHERE li.list_id = $l ORDER BY li.position, li.added_at
                """,
                r => (r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.IsDBNull(4) ? (double?)null : r.GetDouble(4), r.GetString(5), r.GetInt64(6), Db.NLong(r, 7)),
                ("$l", list), ("$now", Db.Unix(now)));
        }
        return rows.Select(x => new TonightItem(x.Work, x.Kind, x.Title, x.Year, x.Rating, x.Src, Db.FromUnix(x.Added),
            x.Prog is long pid ? guide.Programme(pid) : null)).ToList();
    }

    public TonightResult Add(long profileId, long workId, DateTimeOffset now)
    {
        using var c = Open();
        long? list = ListId(c, profileId);
        if (list == null) return TonightResult.NoProfile;
        if (Db.ScalarLong(c, "SELECT id FROM works WHERE id = $w", ("$w", workId)) == null) return TonightResult.NoWork;
        long pos = Db.ScalarLong(c, "SELECT coalesce(max(position), 0) + 1 FROM list_items WHERE list_id = $l", ("$l", list)) ?? 1;
        Db.Exec(c, "INSERT OR IGNORE INTO list_items (list_id, work_id, position, added_at) VALUES ($l, $w, $p, $t)",
            ("$l", list), ("$w", workId), ("$p", pos), ("$t", Db.Unix(now)));
        return TonightResult.Ok;
    }

    public TonightResult Remove(long profileId, long workId)
    {
        using var c = Open();
        long? list = ListId(c, profileId);
        if (list == null) return TonightResult.NoProfile;
        Db.Exec(c, "DELETE FROM list_items WHERE list_id = $l AND work_id = $w", ("$l", list), ("$w", workId));
        return TonightResult.Ok;
    }

    /// <summary>Segna l'opera come vista dal profilo e la toglie dalla serata.</summary>
    public TonightResult Watched(long profileId, long workId, DateTimeOffset now)
    {
        using var c = Open();
        long? list = ListId(c, profileId);
        if (list == null) return TonightResult.NoProfile;
        if (Db.ScalarLong(c, "SELECT id FROM works WHERE id = $w", ("$w", workId)) == null) return TonightResult.NoWork;
        using var tx = c.BeginTransaction();
        int n = Db.Exec(c, "UPDATE watch_states SET watched = 1, updated_at = $t WHERE profile_id = $p AND work_id = $w AND item_id IS NULL",
            ("$t", Db.Unix(now)), ("$p", profileId), ("$w", workId));
        if (n == 0)
            Db.Exec(c, "INSERT INTO watch_states (profile_id, work_id, watched, updated_at) VALUES ($p, $w, 1, $t)",
                ("$p", profileId), ("$w", workId), ("$t", Db.Unix(now)));
        Db.Exec(c, "DELETE FROM list_items WHERE list_id = $l AND work_id = $w", ("$l", list), ("$w", workId));
        tx.Commit();
        return TonightResult.Ok;
    }
}
