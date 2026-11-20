using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Data;

/// <summary>
/// Un titolo della lista "La mia serata in TV" con il suo prossimo passaggio in guida, le piattaforme dove si vede in
/// streaming (abbonamento o gratis), se c'è in casa (file o registrazione riuscita) e l'ultimo passaggio già trasmesso
/// negli ultimi 3 giorni (Last). Evening è il primo passaggio di sera (dalle 20 alle 2), che può venire dopo Next quando
/// Next è di giorno: in "Da vedere stasera" contano solo i passaggi di sera (scelta di Antonio, 20/11/2026).
/// Next ed Evening sono passaggi che il profilo vede (canale in chiaro o di un suo abbonamento); PaidNext è il primo
/// su un canale a pagamento che non ha (Sky senza Sky), anche se ce ne sono di visibili: TV ed € insieme.
/// </summary>
public sealed record TonightItem(
    long WorkId, string Kind, string Title, int Year, double? Rating, string RatingSource, DateTimeOffset AddedAt, GuideProgramme? Next,
    IReadOnlyList<string>? Streaming = null, bool InHouse = false, string Poster = "", IReadOnlyList<string>? Paid = null,
    GuideProgramme? Last = null, GuideProgramme? Evening = null, GuideProgramme? PaidNext = null);

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

    /// <summary>
    /// Opere i cui passaggi valgono per l'opera <paramref name="w"/> (alias SQL) in una lista: l'opera stessa e, per
    /// programmi e documentari, ogni puntata con lo stesso titolo. Una puntata senza episodio è un'opera a sé
    /// ("geo|puntata del 18 11 2026"), ma chi mette «Geo» nella serata vuole la trasmissione (scelta di Antonio, 20/11/2026).
    /// </summary>
    internal static string SameShow(string w) => $"""
        (SELECT {w}.id UNION
         SELECT s.id FROM works s WHERE {w}.kind IN ('programme', 'documentary') AND {w}.title_key <> ''
                AND s.title_key = {w}.title_key AND s.kind IN ('programme', 'documentary'))
        """;

    /// <summary>
    /// Passaggi in arrivo di ogni opera, con le puntate dello stesso titolo per programmi e documentari (<see cref="SameShow"/>),
    /// divisi tra quelli che il profilo vede e quelli a pagamento (<see cref="WorkAirings"/>). Una sola lettura della guida.
    /// </summary>
    internal static Dictionary<long, WorkAirings> ShowAirings(SqliteConnection c, GuideStore guide, IEnumerable<long> workIds, DateTimeOffset now,
        IReadOnlyList<string>? subs, TimeZoneInfo zone)
    {
        var shows = workIds.Distinct().ToDictionary(w => w, w => Db.Query(c, $"SELECT s.id FROM works w, works s WHERE w.id = $w AND s.id IN {SameShow("w")}",
            r => r.GetInt64(0), ("$w", w)));
        var upcoming = WorkAirings.Upcoming(guide, shows.Values.SelectMany(x => x).Distinct().ToList(), now);
        return shows.ToDictionary(x => x.Key, x => WorkAirings.Pick(
            x.Value.SelectMany(id => upcoming.GetValueOrDefault(id) ?? []).OrderBy(p => p.Start), subs, zone));
    }

    /// <summary>
    /// null se il profilo non esiste. <paramref name="zone"/> decide che cosa è "di sera" (predefinito: ora locale);
    /// <paramref name="subs"/> sono gli abbonamenti del profilo (null = si vede tutto): Next ed Evening sono passaggi che
    /// il profilo vede, PaidNext il primo su un canale a pagamento che non ha.
    /// </summary>
    public List<TonightItem>? Items(long profileId, DateTimeOffset now, TimeZoneInfo? zone = null, IReadOnlyList<string>? subs = null)
    {
        Dictionary<long, WorkAirings> airings;
        List<(long Work, string Kind, string Title, int Year, double? Rating, string Src, long Added, string Streaming, bool Home, string Poster, string Paid, long? Last)> rows;
        using (var c = Open())
        {
            long? list = ListId(c, profileId);
            if (list == null) return null;
            rows = Db.Query(c, $"""
                SELECT w.id, w.kind, w.title, w.year, w.rating, w.rating_source, li.added_at,
                       coalesce((SELECT group_concat(a.platform, '|') FROM availability a WHERE a.work_id = w.id AND a.kind IN ('subscription', 'free')), ''),
                       EXISTS (SELECT 1 FROM library_items i JOIN works e ON e.id = i.work_id
                               WHERE (e.id = w.id OR e.parent_id = w.id) AND i.missing_since IS NULL)
                       OR EXISTS (SELECT 1 FROM recordings r WHERE r.work_id = w.id AND r.state IN ('completed', 'incomplete') AND r.file <> ''),
                       w.poster,
                       coalesce((SELECT group_concat(DISTINCT a.platform) FROM availability a WHERE a.work_id = w.id AND a.kind IN ('rent', 'buy')), ''),
                       (SELECT p.id FROM programmes p WHERE p.work_id IN {SameShow("w")} AND p.stop <= $now AND p.stop > $past ORDER BY p.start DESC LIMIT 1)
                FROM list_items li JOIN works w ON w.id = li.work_id
                WHERE li.list_id = $l ORDER BY li.position, li.added_at
                """,
                r => (r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.IsDBNull(4) ? (double?)null : r.GetDouble(4), r.GetString(5), r.GetInt64(6),
                    r.GetString(7), r.GetInt64(8) == 1, r.GetString(9), r.GetString(10), Db.NLong(r, 11)),
                ("$l", list), ("$now", Db.Unix(now)), ("$past", Db.Unix(now.AddDays(-3))));
            airings = ShowAirings(c, guide, rows.Select(x => x.Work), now, subs, zone ?? TimeZoneInfo.Local);
        }
        return rows.Select(x =>
        {
            var a = airings.GetValueOrDefault(x.Work) ?? WorkAirings.None;
            return new TonightItem(x.Work, x.Kind, x.Title, x.Year, x.Rating, x.Src, Db.FromUnix(x.Added), a.Visible,
                x.Streaming.Split('|', StringSplitOptions.RemoveEmptyEntries).Distinct().ToList(), x.Home, WorkStore.ImageUrl(x.Poster, "w342"),
                x.Paid.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                // ultimo passaggio già trasmesso negli ultimi 3 giorni ("programmazione passata" in Stasera)
                x.Last is long lid ? guide.Programme(lid) : null,
                a.Evening, a.Paid);
        }).ToList();
    }

    /// <summary>Un passaggio è "di sera" se comincia dalle 20 alle 2 di notte, ora locale.</summary>
    public static bool IsEvening(DateTimeOffset start, TimeZoneInfo zone)
    {
        int h = TimeZoneInfo.ConvertTime(start, zone).Hour;
        return h >= 20 || h < 2;
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

    /// <summary>
    /// Segna l'opera come vista dal profilo (e la toglie dalla serata) o, con <paramref name="watched"/> falso, come non vista.
    /// Vale anche per i file in casa dell'opera e, per una serie, per i suoi episodi e i loro file: così "Guarda",
    /// "Continua a guardare" e la scheda dicono la stessa cosa.
    /// </summary>
    public TonightResult Watched(long profileId, long workId, DateTimeOffset now, bool watched = true)
    {
        using var c = Open();
        long? list = ListId(c, profileId);
        if (list == null) return TonightResult.NoProfile;
        if (Db.ScalarLong(c, "SELECT id FROM works WHERE id = $w", ("$w", workId)) == null) return TonightResult.NoWork;
        int v = watched ? 1 : 0;
        using var tx = c.BeginTransaction();
        var works = Db.Query(c, "SELECT id FROM works WHERE id = $w OR parent_id = $w", r => r.GetInt64(0), ("$w", workId));
        foreach (long w in works)
        {
            int n = Db.Exec(c, "UPDATE watch_states SET watched = $v, updated_at = $t WHERE profile_id = $p AND work_id = $w AND item_id IS NULL",
                ("$v", v), ("$t", Db.Unix(now)), ("$p", profileId), ("$w", w));
            // un episodio senza stato non si crea solo per dire "non visto"
            if (n == 0 && (watched || w == workId))
                Db.Exec(c, "INSERT INTO watch_states (profile_id, work_id, watched, updated_at) VALUES ($p, $w, $v, $t)",
                    ("$p", profileId), ("$w", w), ("$v", v), ("$t", Db.Unix(now)));
            foreach (long item in Db.Query(c, "SELECT id FROM library_items WHERE work_id = $w", r => r.GetInt64(0), ("$w", w)))
            {
                // visto: si riparte dall'inizio la prossima volta; non visto: il punto di ripresa resta
                int m = Db.Exec(c, $"UPDATE watch_states SET watched = $v, {(watched ? "position_s = 0, " : "")}updated_at = $t WHERE profile_id = $p AND item_id = $i",
                    ("$v", v), ("$t", Db.Unix(now)), ("$p", profileId), ("$i", item));
                if (m == 0 && watched)
                    Db.Exec(c, "INSERT INTO watch_states (profile_id, item_id, watched, position_s, updated_at) VALUES ($p, $i, 1, 0, $t)",
                        ("$p", profileId), ("$i", item), ("$t", Db.Unix(now)));
            }
        }
        if (watched) Db.Exec(c, "DELETE FROM list_items WHERE list_id = $l AND work_id = $w", ("$l", list), ("$w", workId));
        tx.Commit();
        return TonightResult.Ok;
    }
}
