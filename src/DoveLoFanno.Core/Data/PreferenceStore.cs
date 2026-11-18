using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Data;

/// <summary>Una preferenza del profilo (le Rules di RegistraTV): cosa cercare nella guida per le proposte.</summary>
public sealed record PreferenceRow(long Id, string Name, string Text, string Group, string Sub, double MinRating, bool Enabled);

/// <summary>Un programma proposto: il passaggio scelto dell'opera, la preferenza che l'ha trovato, gli altri passaggi.</summary>
/// <param name="Conflict">Tutti i passaggi registrabili superano il limite di registrazioni contemporanee.</param>
public sealed record Proposal(GuideProgramme Programme, string Preference, bool Conflict, int OtherAirings);

/// <summary>Un sottogenere della guida con il numero di programmi futuri.</summary>
public sealed record GenreCount(string Group, string Sub, int Count);

/// <summary>Un'opera da non proporre più.</summary>
public sealed record IgnoredWork(string WorkKey, string Title, DateTimeOffset AddedAt);

/// <summary>
/// Preferenze e proposte per profilo, come "Proposte per te" di RegistraTV: per ogni preferenza attiva si cercano i
/// programmi futuri, si raggruppano per opera, si saltano quelli già in programma, già visti, già in casa o da non
/// proporre, e si propone il primo passaggio che non supera il limite di registrazioni contemporanee (altrimenti il primo,
/// segnato in conflitto). Il profilo bambino vede solo i programmi per bambini; i gruppi esclusi dal profilo non compaiono.
/// </summary>
public sealed class PreferenceStore(string dbPath, GuideStore guide)
{
    public static readonly string[] Groups = ["Film", "Serie TV", "Documentari", "Notizie", "Sport", "Intrattenimento", "Bambini", "Altro"];

    SqliteConnection Open() => Database.Open(dbPath);

    static bool ProfileExists(SqliteConnection c, long profileId) =>
        Db.ScalarLong(c, "SELECT id FROM profiles WHERE id = $p", ("$p", profileId)) != null;

    /// <summary>null se il profilo non esiste.</summary>
    public List<PreferenceRow>? List(long profileId)
    {
        using var c = Open();
        if (!ProfileExists(c, profileId)) return null;
        return Db.Query(c, "SELECT id, name, text, group_name, sub_genre, min_rating, enabled FROM preferences WHERE profile_id = $p ORDER BY id",
            r => new PreferenceRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetDouble(5), r.GetInt64(6) == 1),
            ("$p", profileId));
    }

    /// <summary>Controlla i campi: messaggio d'errore in italiano, o vuoto se vanno bene.</summary>
    public static string Validate(string name, string text, string group, double minRating)
    {
        if (name.Trim().Length is 0 or > 60) return "nome mancante o più lungo di 60 caratteri";
        if (group.Length > 0 && !Groups.Contains(group)) return "genere sconosciuto";
        if (minRating is < 0 or > 10) return "voto minimo da 0 a 10";
        if (text.Trim().Length == 0 && group.Length == 0 && minRating <= 0) return "indica almeno un testo, un genere o un voto minimo";
        return "";
    }

    /// <summary>Id della preferenza nuova, null se il profilo non esiste.</summary>
    public long? Add(long profileId, string name, string text, string group, string sub, double minRating, bool enabled)
    {
        using var c = Open();
        if (!ProfileExists(c, profileId)) return null;
        return Db.Insert(c, "INSERT INTO preferences (profile_id, name, text, group_name, sub_genre, min_rating, enabled) VALUES ($p, $n, $t, $g, $s, $m, $e)",
            ("$p", profileId), ("$n", name.Trim()), ("$t", text.Trim()), ("$g", group), ("$s", sub.Trim()), ("$m", minRating), ("$e", enabled ? 1 : 0));
    }

    public bool Update(long profileId, long id, string name, string text, string group, string sub, double minRating, bool enabled)
    {
        using var c = Open();
        return Db.Exec(c, """
            UPDATE preferences SET name = $n, text = $t, group_name = $g, sub_genre = $s, min_rating = $m, enabled = $e
            WHERE id = $id AND profile_id = $p
            """,
            ("$n", name.Trim()), ("$t", text.Trim()), ("$g", group), ("$s", sub.Trim()), ("$m", minRating), ("$e", enabled ? 1 : 0), ("$id", id), ("$p", profileId)) > 0;
    }

    public bool Delete(long profileId, long id)
    {
        using var c = Open();
        return Db.Exec(c, "DELETE FROM preferences WHERE id = $id AND profile_id = $p", ("$id", id), ("$p", profileId)) > 0;
    }

    /// <summary>Gruppi esclusi dal profilo (mai proposti); null se il profilo non esiste.</summary>
    public List<string>? ExcludedGroups(long profileId)
    {
        using var c = Open();
        var j = Db.Query(c, "SELECT excluded_groups FROM profiles WHERE id = $p", r => r.GetString(0), ("$p", profileId));
        if (j.Count == 0) return null;
        try { return JsonSerializer.Deserialize<List<string>>(j[0]) ?? []; } catch (JsonException) { return []; }
    }

    public bool SetExcludedGroups(long profileId, IEnumerable<string> groups)
    {
        var g = groups.Where(Groups.Contains).Distinct().ToList();
        using var c = Open();
        return Db.Exec(c, "UPDATE profiles SET excluded_groups = $g WHERE id = $p", ("$g", JsonSerializer.Serialize(g)), ("$p", profileId)) > 0;
    }

    /// <summary>Sottogeneri presenti nella guida da adesso in poi (almeno 3 programmi), per scegliere nelle preferenze.</summary>
    public List<GenreCount> SubGenres(DateTimeOffset now)
    {
        using var c = Open();
        return Db.Query(c, """
            SELECT group_name, sub_genre, count(*) FROM programmes WHERE stop > $now AND sub_genre <> ''
            GROUP BY group_name, sub_genre COLLATE NOCASE HAVING count(*) >= 3 ORDER BY group_name, count(*) DESC
            """, r => new GenreCount(r.GetString(0), r.GetString(1), r.GetInt32(2)), ("$now", Db.Unix(now)));
    }

    // ---------------------------------------------------------------- non proporre più

    public List<IgnoredWork>? Ignored(long profileId)
    {
        using var c = Open();
        if (!ProfileExists(c, profileId)) return null;
        return Db.Query(c, """
            SELECT i.work_key, coalesce(w.title, i.work_key), i.added_at FROM ignored_works i LEFT JOIN works w ON w.work_key = i.work_key
            WHERE i.profile_id = $p ORDER BY i.added_at DESC
            """,
            r => new IgnoredWork(r.GetString(0), r.GetString(1), Db.FromUnix(r.GetInt64(2))), ("$p", profileId));
    }

    /// <summary>Non proporre più l'opera. false se profilo o opera non esistono.</summary>
    public bool Ignore(long profileId, long workId, DateTimeOffset now)
    {
        using var c = Open();
        if (!ProfileExists(c, profileId)) return false;
        var key = Db.Query(c, "SELECT work_key FROM works WHERE id = $w", r => r.GetString(0), ("$w", workId));
        if (key.Count == 0) return false;
        Db.Exec(c, "INSERT OR IGNORE INTO ignored_works (profile_id, work_key, added_at) VALUES ($p, $k, $t)", ("$p", profileId), ("$k", key[0]), ("$t", Db.Unix(now)));
        return true;
    }

    public bool Unignore(long profileId, string workKey)
    {
        using var c = Open();
        return Db.Exec(c, "DELETE FROM ignored_works WHERE profile_id = $p AND work_key = $k", ("$p", profileId), ("$k", workKey)) > 0;
    }

    public bool IsIgnored(long profileId, long workId)
    {
        using var c = Open();
        return (Db.ScalarLong(c, """
            SELECT count(*) FROM ignored_works i JOIN works w ON w.work_key = i.work_key WHERE i.profile_id = $p AND w.id = $w
            """, ("$p", profileId), ("$w", workId)) ?? 0) > 0;
    }

    // ---------------------------------------------------------------- proposte

    /// <summary>
    /// Proposte del profilo per i prossimi <paramref name="days"/> giorni, in ordine di orario; null se il profilo non esiste.
    /// <paramref name="maxParallel"/> e i margini servono per i conflitti con le registrazioni già in programma.
    /// </summary>
    public List<Proposal>? Proposals(long profileId, DateTimeOffset now, int days, int maxParallel, int beforeMin, int afterMin)
    {
        var prefs = List(profileId);
        if (prefs == null) return null;
        var excluded = ExcludedGroups(profileId) ?? [];
        bool child;
        HashSet<string> ignored, done;
        List<(long From, long To)> pending;
        using (var c = Open())
        {
            child = Db.ScalarLong(c, "SELECT is_child FROM profiles WHERE id = $p", ("$p", profileId)) == 1;
            ignored = Db.Query(c, "SELECT work_key FROM ignored_works WHERE profile_id = $p", r => r.GetString(0), ("$p", profileId)).ToHashSet();
            // già visti dal profilo, già registrati o già nella libreria: non si ripropongono (l'archivio di RegistraTV)
            done = Db.Query(c, """
                SELECT w.work_key FROM watch_states s JOIN works w ON w.id = s.work_id WHERE s.profile_id = $p AND s.watched = 1 AND s.item_id IS NULL
                UNION SELECT w.work_key FROM recordings r JOIN works w ON w.id = r.work_id WHERE r.state IN ('completed', 'stopped')
                UNION SELECT w.work_key FROM library_items i JOIN works w ON w.id = i.work_id WHERE i.missing_since IS NULL
                """, r => r.GetString(0), ("$p", profileId)).ToHashSet();
            pending = Db.Query(c, "SELECT start - before_min * 60, stop + after_min * 60 FROM recordings WHERE state IN ('scheduled', 'recording', 'merging')",
                r => (r.GetInt64(0), r.GetInt64(1)));
        }

        var to = now.AddDays(Math.Clamp(days, 1, 14));
        var chosen = new Dictionary<string, Proposal>();
        foreach (var pref in prefs.Where(p => p.Enabled))
        {
            var found = guide.Query(new GuideQuery(now, to)
            {
                Text = pref.Text.Length > 0 ? pref.Text : null,
                Group = pref.Group.Length > 0 ? pref.Group : null,
                Sub = pref.Sub.Length > 0 ? pref.Sub : null,
                MinRating = pref.MinRating,
                Max = 0,
            });
            foreach (var g in found
                .Where(p => p.Start > now && p.WorkKey.Length > 0)
                .Where(p => !excluded.Contains(p.Group) && (!child || p.Group == "Bambini"))
                .GroupBy(p => p.WorkKey))
            {
                if (chosen.ContainsKey(g.Key) || ignored.Contains(g.Key) || done.Contains(g.Key) || IgnoredFilm(g.Key, ignored)) continue;
                var airings = g.OrderBy(p => p.Start).ToList();
                if (airings.Any(p => p.Scheduled)) continue;
                var recordable = airings.Where(p => p.ChannelId != null).ToList();
                GuideProgramme pick;
                bool conflict = false;
                if (recordable.Count == 0) pick = airings[0];
                else
                {
                    var free = recordable.FirstOrDefault(p => maxParallel <= 0 || Overlaps(pending, p, beforeMin, afterMin) + 1 <= maxParallel);
                    conflict = free == null;
                    pick = free ?? recordable[0];
                }
                chosen[g.Key] = new Proposal(pick, pref.Name, conflict, airings.Count - 1);
            }
        }
        return chosen.Values.OrderBy(p => p.Programme.Start).ThenBy(p => p.Programme.Title).ToList();
    }

    // un film ignorato con l'anno 0 (Mediaset non lo dà) vale per tutti gli anni, e viceversa: Test-SameWork di RegistraTV
    static bool IgnoredFilm(string key, HashSet<string> ignored)
    {
        int bar = key.LastIndexOf('|');
        if (bar < 0 || !int.TryParse(key[(bar + 1)..], out int year)) return false;
        string title = key[..bar];
        if (year == 0) return ignored.Any(k => k.StartsWith(title + "|", StringComparison.Ordinal) && int.TryParse(k[(title.Length + 1)..], out _));
        return ignored.Contains(title + "|0");
    }

    static int Overlaps(List<(long From, long To)> pending, GuideProgramme p, int beforeMin, int afterMin)
    {
        long a = Db.Unix(p.Start) - beforeMin * 60L, b = Db.Unix(p.Stop) + afterMin * 60L;
        return pending.Count(x => x.From < b && x.To > a);
    }

    /// <summary>
    /// Proposte non ancora notificate (chiave opera@inizio) e le segna come viste. Alla prima volta per il profilo le segna
    /// senza restituirle, per non notificare tutto insieme.
    /// </summary>
    public List<Proposal> TakeNew(long profileId, IReadOnlyList<Proposal> proposals, DateTimeOffset now)
    {
        using var c = Open();
        bool first = (Db.ScalarLong(c, "SELECT count(*) FROM proposals_seen WHERE profile_id = $p", ("$p", profileId)) ?? 0) == 0;
        var fresh = new List<Proposal>();
        using var tx = c.BeginTransaction();
        foreach (var p in proposals)
        {
            string key = p.Programme.WorkKey + "@" + Db.Unix(p.Programme.Start);
            if (Db.Exec(c, "INSERT OR IGNORE INTO proposals_seen (profile_id, key, seen_at) VALUES ($p, $k, $t)",
                ("$p", profileId), ("$k", key), ("$t", Db.Unix(now))) > 0 && !first) fresh.Add(p);
        }
        Db.Exec(c, "DELETE FROM proposals_seen WHERE seen_at < $old", ("$old", Db.Unix(now.AddDays(-30))));
        tx.Commit();
        return fresh;
    }
}
