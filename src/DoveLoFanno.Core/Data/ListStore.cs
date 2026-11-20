using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Data;

/// <summary>Una lista vista da un profilo: Family = della famiglia; CanEdit = la può cambiare (le sue, o di famiglia se adulto).</summary>
public sealed record ListSummary(long Id, string Name, bool Family, bool IsTonight, int Count, IReadOnlyList<string> Posters, bool CanEdit, DateTimeOffset CreatedAt)
{
    /// <summary>Lista dinamica: la ricerca avanzata salvata (i titoli sono i suoi risultati). null = lista a mano.</summary>
    public SavedQuery? Query { get; init; }
    /// <summary>Il profilo la vuole nella pagina principale («La mia serata»).</summary>
    public bool OnHome { get; init; }
}

/// <summary>Un titolo di una lista, con lo stato del profilo e dove si vede.</summary>
public sealed record ListEntry(
    long WorkId, string Kind, string Title, int Year, string Poster, double? Rating, string RatingSource, int? Stars, bool Watched,
    DateTimeOffset AddedAt, long? PlayItemId, GuideProgramme? Next, string Country, string Credits)
{
    /// <summary>Piattaforme in streaming (abbonamento o gratis).</summary>
    public IReadOnlyList<string> Streaming { get; init; } = [];
    /// <summary>Piattaforme dove si noleggia o si compra.</summary>
    public IReadOnlyList<string> Paid { get; init; } = [];
    /// <summary>In casa: un file della libreria o una registrazione riuscita.</summary>
    public bool InHouse { get; init; }
    /// <summary>Primo passaggio su un canale a pagamento che il profilo non ha (Next è sempre uno che vede).</summary>
    public GuideProgramme? PaidNext { get; init; }
}

public sealed record ListDetail(ListSummary List, IReadOnlyList<ListEntry> Items);

/// <summary>In quali liste del profilo c'è un'opera (per la Scheda).</summary>
public sealed record WorkListRef(long Id, string Name, bool Family, bool IsTonight);

public enum ListResult { Ok, NotFound, Forbidden, Invalid, NoWork, Fixed, Dynamic }

/// <summary>
/// Liste: del profilo (profile_id) o della famiglia (profile_id NULL). Le liste di famiglia le vedono tutti e le
/// cambiano gli adulti. "La mia serata in TV" (is_tonight) è una per profilo e non si rinomina né si cancella.
/// Una lista dinamica (query non vuota, 19/11/2026) è una ricerca avanzata salvata: non ha titoli aggiunti a mano.
/// Ogni profilo sceglie quali liste vedere nella pagina principale (list_home), anche quelle di famiglia.
/// </summary>
public sealed class ListStore(string dbPath, GuideStore guide)
{
    /// <summary>Nome di una lista: al massimo 28 caratteri, perché stia su una riga con la pennina (Antonio, 20/11/2026).</summary>
    public const int MaxName = 28;

    SqliteConnection Open() => Database.Open(dbPath);

    static (bool Exists, bool Child) Profile(SqliteConnection c, long profileId)
    {
        var r = Db.Query(c, "SELECT is_child FROM profiles WHERE id = $p", x => x.GetInt64(0) == 1, ("$p", profileId));
        return r.Count == 0 ? (false, false) : (true, r[0]);
    }

    /// <summary>La lista se il profilo la vede: (profilo proprietario o null per famiglia, nome, serata).</summary>
    static (bool Found, long? Owner, string Name, bool Tonight, long Created, string Query) Get(SqliteConnection c, long listId, long profileId)
    {
        var r = Db.Query(c, "SELECT profile_id, name, is_tonight, created_at, query FROM lists WHERE id = $id AND (profile_id = $p OR profile_id IS NULL)",
            x => (Db.NLong(x, 0), x.GetString(1), x.GetInt64(2) == 1, x.GetInt64(3), x.GetString(4)), ("$id", listId), ("$p", profileId));
        return r.Count == 0 ? (false, null, "", false, 0, "") : (true, r[0].Item1, r[0].Item2, r[0].Item3, r[0].Item4, r[0].Item5);
    }

    static bool CanEdit(long? owner, bool child) => owner != null || !child;

    /// <summary>Le liste che il profilo vede: la serata, le sue, poi quelle di famiglia. null se il profilo non c'è.</summary>
    public List<ListSummary>? Lists(long profileId)
    {
        using var c = Open();
        var (exists, child) = Profile(c, profileId);
        if (!exists) return null;
        var rows = Db.Query(c, """
            SELECT l.id, l.name, l.profile_id, l.is_tonight, l.created_at, (SELECT count(*) FROM list_items li WHERE li.list_id = l.id), l.query,
                   EXISTS (SELECT 1 FROM list_home h WHERE h.profile_id = $p AND h.list_id = l.id)
            FROM lists l WHERE l.profile_id = $p OR l.profile_id IS NULL
            ORDER BY l.is_tonight DESC, l.profile_id IS NULL, l.created_at
            """,
            r => (Id: r.GetInt64(0), Name: r.GetString(1), Owner: Db.NLong(r, 2), Tonight: r.GetInt64(3) == 1, Created: r.GetInt64(4), Count: r.GetInt32(5),
                Query: r.GetString(6), Home: r.GetInt64(7) == 1),
            ("$p", profileId));
        return rows.Select(x => new ListSummary(x.Id, x.Name, x.Owner == null, x.Tonight, x.Count, Posters(c, x.Id), CanEdit(x.Owner, child), Db.FromUnix(x.Created))
        {
            Query = SavedQuery.FromJson(x.Query),
            OnHome = x.Home,
        }).ToList();
    }

    static List<string> Posters(SqliteConnection c, long listId) =>
        Db.Query(c, """
            SELECT w.poster FROM list_items li JOIN works w ON w.id = li.work_id WHERE li.list_id = $l AND w.poster <> ''
            ORDER BY li.added_at DESC LIMIT 3
            """, r => WorkStore.ImageUrl(r.GetString(0), "w185"), ("$l", listId));

    /// <summary>Nuova lista del profilo o della famiglia (solo gli adulti); con <paramref name="query"/> è dinamica.</summary>
    public (ListResult Result, long Id) Create(long profileId, string name, bool family, DateTimeOffset now, SavedQuery? query = null)
    {
        name = (name ?? "").Trim();
        if (name.Length is 0 or > MaxName) return (ListResult.Invalid, 0);
        if (query != null && query.Validate().Length > 0) return (ListResult.Invalid, 0);
        using var c = Open();
        var (exists, child) = Profile(c, profileId);
        if (!exists) return (ListResult.NotFound, 0);
        if (family && child) return (ListResult.Forbidden, 0);
        long id = Db.Insert(c, "INSERT INTO lists (profile_id, name, created_at, query) VALUES ($p, $n, $t, $q)",
            ("$p", family ? null : profileId), ("$n", name), ("$t", Db.Unix(now)), ("$q", query?.ToJson() ?? ""));
        return (ListResult.Ok, id);
    }

    /// <summary>
    /// Rinomina, sposta in famiglia (non si torna indietro: una lista di famiglia è di tutti), cambia la ricerca di una
    /// lista dinamica. <paramref name="onHome"/> (nella pagina principale) è una scelta del profilo: vale per ogni lista
    /// che vede, anche di famiglia e anche per i bambini.
    /// </summary>
    public ListResult Update(long profileId, long listId, string? name, bool? family, SavedQuery? query = null, bool? onHome = null, DateTimeOffset? now = null)
    {
        using var c = Open();
        var (exists, child) = Profile(c, profileId);
        if (!exists) return ListResult.NotFound;
        var l = Get(c, listId, profileId);
        if (!l.Found) return ListResult.NotFound;
        if (onHome is bool home)
        {
            if (home) Db.Exec(c, "INSERT OR IGNORE INTO list_home (profile_id, list_id, added_at) VALUES ($p, $l, $t)",
                ("$p", profileId), ("$l", listId), ("$t", Db.Unix(now ?? DateTimeOffset.UtcNow)));
            else Db.Exec(c, "DELETE FROM list_home WHERE profile_id = $p AND list_id = $l", ("$p", profileId), ("$l", listId));
            if (name == null && family == null && query == null) return ListResult.Ok;
        }
        if (l.Tonight) return ListResult.Fixed;
        if (!CanEdit(l.Owner, child) || (family == true && child)) return ListResult.Forbidden;
        if (family == false && l.Owner == null) return ListResult.Invalid;
        if (query != null && (l.Query.Length == 0 || query.Validate().Length > 0)) return ListResult.Invalid;
        if (query != null) Db.Exec(c, "UPDATE lists SET query = $q WHERE id = $id", ("$q", query.ToJson()), ("$id", listId));
        if (name != null)
        {
            name = name.Trim();
            if (name.Length is 0 or > MaxName) return ListResult.Invalid;
            Db.Exec(c, "UPDATE lists SET name = $n WHERE id = $id", ("$n", name), ("$id", listId));
        }
        if (family == true) Db.Exec(c, "UPDATE lists SET profile_id = NULL WHERE id = $id", ("$id", listId));
        return ListResult.Ok;
    }

    public ListResult Delete(long profileId, long listId)
    {
        using var c = Open();
        var (exists, child) = Profile(c, profileId);
        if (!exists) return ListResult.NotFound;
        var l = Get(c, listId, profileId);
        if (!l.Found) return ListResult.NotFound;
        if (l.Tonight) return ListResult.Fixed;
        if (!CanEdit(l.Owner, child)) return ListResult.Forbidden;
        Db.Exec(c, "DELETE FROM lists WHERE id = $id", ("$id", listId));
        return ListResult.Ok;
    }

    public ListResult Add(long profileId, long listId, long workId, DateTimeOffset now)
    {
        using var c = Open();
        var (exists, child) = Profile(c, profileId);
        if (!exists) return ListResult.NotFound;
        var l = Get(c, listId, profileId);
        if (!l.Found) return ListResult.NotFound;
        if (!CanEdit(l.Owner, child)) return ListResult.Forbidden;
        if (l.Query.Length > 0) return ListResult.Dynamic;
        if (Db.ScalarLong(c, "SELECT id FROM works WHERE id = $w", ("$w", workId)) == null) return ListResult.NoWork;
        long pos = Db.ScalarLong(c, "SELECT coalesce(max(position), 0) + 1 FROM list_items WHERE list_id = $l", ("$l", listId)) ?? 1;
        Db.Exec(c, "INSERT OR IGNORE INTO list_items (list_id, work_id, position, added_at) VALUES ($l, $w, $p, $t)",
            ("$l", listId), ("$w", workId), ("$p", pos), ("$t", Db.Unix(now)));
        return ListResult.Ok;
    }

    public ListResult Remove(long profileId, long listId, long workId)
    {
        using var c = Open();
        var (exists, child) = Profile(c, profileId);
        if (!exists) return ListResult.NotFound;
        var l = Get(c, listId, profileId);
        if (!l.Found) return ListResult.NotFound;
        if (!CanEdit(l.Owner, child)) return ListResult.Forbidden;
        Db.Exec(c, "DELETE FROM list_items WHERE list_id = $l AND work_id = $w", ("$l", listId), ("$w", workId));
        return ListResult.Ok;
    }

    /// <summary>
    /// I titoli di una lista per il profilo. sort = added (predefinito), title, rating, stars. Il profilo bambino, nelle
    /// liste di famiglia, vede solo i titoli adatti (<see cref="WorkStore.ChildSafe"/>).
    /// </summary>
    public ListDetail? Detail(long profileId, long listId, string sort, DateTimeOffset now, IReadOnlyList<string>? subs = null)
    {
        List<(long Work, string Kind, string Title, int Year, string Poster, double? Rating, string Src, int? Stars, bool Watched, long Added, long? Play,
            string Streaming, string Paid, bool Recorded)> rows;
        Dictionary<long, WorkAirings> airings;
        ListSummary summary;
        using (var c = Open())
        {
            var (exists, child) = Profile(c, profileId);
            if (!exists) return null;
            var l = Get(c, listId, profileId);
            if (!l.Found) return null;
            string order = sort switch
            {
                "title" => "w.title_key, w.year",
                "rating" => "w.rating IS NULL, w.rating DESC, w.title_key",
                "stars" => "ws.stars IS NULL, ws.stars DESC, w.title_key",
                _ => "li.added_at DESC",
            };
            string childFilter = child && l.Owner == null ? "AND " + WorkStore.ChildSafe("w") : "";
            rows = Db.Query(c, $"""
                SELECT w.id, w.kind, w.title, w.year, w.poster, w.rating, w.rating_source, ws.stars, coalesce(ws.watched, 0), li.added_at,
                       (SELECT x.id FROM library_items x JOIN works xw ON xw.id = x.work_id
                        WHERE (xw.id = w.id OR xw.parent_id = w.id) AND x.missing_since IS NULL ORDER BY x.season, x.episode, x.added_at DESC LIMIT 1),
                       coalesce((SELECT group_concat(a.platform, '|') FROM availability a WHERE a.work_id = w.id AND a.kind IN ('subscription', 'free')), ''),
                       coalesce((SELECT group_concat(DISTINCT a.platform) FROM availability a WHERE a.work_id = w.id AND a.kind IN ('rent', 'buy')), ''),
                       EXISTS (SELECT 1 FROM recordings rr WHERE rr.work_id = w.id AND rr.state IN ('completed', 'incomplete') AND rr.file <> '')
                FROM list_items li JOIN works w ON w.id = li.work_id
                LEFT JOIN watch_states ws ON ws.profile_id = $p AND ws.work_id = w.id AND ws.item_id IS NULL
                WHERE li.list_id = $l {childFilter}
                ORDER BY {order}
                """,
                r => (r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetString(4), r.IsDBNull(5) ? (double?)null : r.GetDouble(5),
                    r.GetString(6), Db.NInt(r, 7), r.GetInt64(8) == 1, r.GetInt64(9), Db.NLong(r, 10),
                    r.GetString(11), r.GetString(12), r.GetInt64(13) == 1),
                ("$p", profileId), ("$l", listId));
            // prossimo passaggio che il profilo vede e primo a pagamento (come la mia serata)
            airings = TonightStore.ShowAirings(c, guide, rows.Select(x => x.Work), now, subs, TimeZoneInfo.Local);
            summary = new ListSummary(listId, l.Name, l.Owner == null, l.Tonight, rows.Count, Posters(c, listId), CanEdit(l.Owner, child), Db.FromUnix(l.Created))
            {
                Query = SavedQuery.FromJson(l.Query),
                OnHome = Db.ScalarLong(c, "SELECT count(*) FROM list_home WHERE profile_id = $p AND list_id = $l", ("$p", profileId), ("$l", listId)) > 0,
            };
        }
        var items = rows.Select(x =>
        {
            var a = airings.GetValueOrDefault(x.Work) ?? WorkAirings.None;
            var next = a.Visible;
            string credits = next == null ? "" : string.Join(" · ", new[] { next.Directors, next.Actors }.Where(s => s.Length > 0));
            return new ListEntry(x.Work, x.Kind, x.Title, x.Year, WorkStore.ImageUrl(x.Poster, "w185"), x.Rating, x.Src, x.Stars, x.Watched,
                Db.FromUnix(x.Added), x.Play, next, next?.Country ?? "", credits)
            {
                Streaming = x.Streaming.Split('|', StringSplitOptions.RemoveEmptyEntries).Distinct().ToList(),
                Paid = x.Paid.Split(',', StringSplitOptions.RemoveEmptyEntries),
                InHouse = x.Play != null || x.Recorded,
                PaidNext = a.Paid,
            };
        }).ToList();
        return new ListDetail(summary, items);
    }

    /// <summary>La ricerca di una lista dinamica che il profilo vede; null se la lista non c'è o non è dinamica.</summary>
    public SavedQuery? QueryOf(long profileId, long listId)
    {
        using var c = Open();
        var l = Get(c, listId, profileId);
        return l.Found ? SavedQuery.FromJson(l.Query) : null;
    }

    /// <summary>Le liste dinamiche che ogni profilo ha nella pagina principale (per le notifiche).</summary>
    public List<(long ProfileId, long ListId, string Name, SavedQuery Query)> HomeDynamic()
    {
        using var c = Open();
        var rows = Db.Query(c, """
            SELECT h.profile_id, l.id, l.name, l.query FROM list_home h JOIN lists l ON l.id = h.list_id
            WHERE l.query <> '' AND (l.profile_id = h.profile_id OR l.profile_id IS NULL) ORDER BY h.profile_id, h.added_at
            """, r => (P: r.GetInt64(0), L: r.GetInt64(1), N: r.GetString(2), Q: SavedQuery.FromJson(r.GetString(3))));
        return [.. rows.Where(x => x.Q != null).Select(x => (x.P, x.L, x.N, x.Q!))];
    }

    /// <summary>Le liste del profilo (e di famiglia) che contengono l'opera.</summary>
    public List<WorkListRef> ListsOf(long profileId, long workId)
    {
        using var c = Open();
        return Db.Query(c, """
            SELECT l.id, l.name, l.profile_id IS NULL, l.is_tonight FROM lists l JOIN list_items li ON li.list_id = l.id
            WHERE li.work_id = $w AND (l.profile_id = $p OR l.profile_id IS NULL) ORDER BY l.is_tonight DESC, l.profile_id IS NULL, l.name
            """,
            r => new WorkListRef(r.GetInt64(0), r.GetString(1), r.GetInt64(2) == 1, r.GetInt64(3) == 1), ("$w", workId), ("$p", profileId));
    }
}
