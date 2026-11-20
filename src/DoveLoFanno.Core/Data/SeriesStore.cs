using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DoveLoFanno.Epg;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Data;

/// <summary>Una regola «Registra la serie»: le puntate già programmate o registrate e la prossima.</summary>
public sealed record SeriesRule(long Id, long? ProfileId, string Title, bool Active, DateTimeOffset CreatedAt, DateTimeOffset? CheckedAt,
    int Scheduled, int Recorded, DateTimeOffset? NextStart, string NextChannel);

/// <summary>
/// Una puntata trovata in guida per una regola. Status: scheduled = programmata ora; pending = era già programmata
/// (dalla regola o a mano); replica = un altro passaggio della stessa puntata, saltato; done = già registrata;
/// not_recordable = canale che non è nelle playlist; limit = oltre il limite di registrazioni contemporanee.
/// </summary>
public sealed record SeriesEpisode(GuideProgramme Programme, string EpisodeKey, string Status, long? RecordingId);

/// <summary>Esito di un controllo: le puntate in guida, quelle programmate adesso e i numeri di episodio che mancano.</summary>
public sealed record SeriesCheck(SeriesRule Rule, IReadOnlyList<SeriesEpisode> Episodes, int NewlyScheduled, IReadOnlyList<string> Missing);

/// <summary>Impostazioni con cui programmare le puntate: margini, formato, limite di registrazioni contemporanee (0 = nessuno).</summary>
public sealed record SeriesDefaults(int BeforeMin, int AfterMin, string Format, int MaxParallel);

/// <summary>
/// «Registra la serie» (20/11/2026, richiesta di Antonio): una regola per il titolo di una serie o di un programma a
/// puntate. A ogni controllo (alla creazione e poi ogni mezz'ora, quando la guida si aggiorna) le puntate in guida si
/// programmano da sole, una volta per puntata su qualunque canale registrabile: le repliche si saltano (scelta di
/// Antonio). I film non hanno regole.
/// </summary>
public sealed partial class SeriesStore(string dbPath, GuideStore guide, RecordingStore recordings)
{
    static readonly string[] Kinds = ["series", "episode", "programme", "documentary"];
    SqliteConnection Open() => Database.Open(dbPath);

    /// <summary>
    /// Chiave della puntata, per non registrarla due volte: il numero di episodio («S6 E2»), altrimenti il titolo della
    /// puntata, altrimenti la descrizione (le repliche la ripetono uguale), altrimenti l'orario (ogni passaggio è a sé).
    /// </summary>
    public static string EpisodeKey(GuideProgramme p)
    {
        if (p.Episode.Trim().Length > 0) return "e:" + Regex.Replace(p.Episode.ToLowerInvariant(), @"\s+", "");
        string sub = Titles.TitleKey(p.Subtitle);
        if (sub.Length > 0) return "s:" + sub;
        string desc = Titles.TitleKey(p.Description.Length > 300 ? p.Description[..300] : p.Description);
        if (desc.Length >= 20) return "d:" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(desc)))[..16];
        return "t:" + p.Start.ToUnixTimeSeconds();
    }

    /// <summary>Titolo della registrazione: titolo e titolo della puntata se breve, come il Registra della finestra del programma.</summary>
    public static string RecordingTitle(GuideProgramme p) =>
        p.Subtitle.Length > 0 && p.Subtitle.Length <= 60 ? $"{p.Title} - {p.Subtitle}" : p.Title;

    /// <summary>
    /// Numeri di episodio che mancano tra quelli in guida, stagione per stagione («S6 E3»): i buchi tra il primo e
    /// l'ultimo. Al massimo 30 per stagione (oltre, la guida ha solo repliche sparse e non ha senso elencarle).
    /// </summary>
    public static List<string> MissingEpisodes(IEnumerable<string> episodes)
    {
        var bySeason = new Dictionary<int, SortedSet<int>>();
        foreach (string e in episodes)
        {
            var m = EpisodeRe().Match(e);
            if (!m.Success) continue;
            int s = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            int n = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (!bySeason.TryGetValue(s, out var set)) bySeason[s] = set = [];
            set.Add(n);
        }
        var res = new List<string>();
        foreach (var (s, set) in bySeason.OrderBy(x => x.Key))
        {
            var gaps = Enumerable.Range(set.Min, set.Max - set.Min + 1).Where(n => !set.Contains(n)).ToList();
            if (gaps.Count is > 0 and <= 30) res.AddRange(gaps.Select(n => $"S{s} E{n}"));
        }
        return res;
    }

    [GeneratedRegex(@"^\s*S\s*(\d+)\s*E\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeRe();

    /// <summary>Crea la regola per il titolo dell'opera (o ritrova quella attiva). null se l'opera non c'è o è un film.</summary>
    public long? Create(long? profileId, long workId, DateTimeOffset now)
    {
        using var c = Open();
        var w = Db.Query(c, "SELECT title, title_key, kind FROM works WHERE id = $w", r => (Title: r.GetString(0), Key: r.GetString(1), Kind: r.GetString(2)), ("$w", workId))
            .FirstOrDefault();
        if (w.Title == null || w.Key.Length == 0 || !Kinds.Contains(w.Kind)) return null;
        if (Db.ScalarLong(c, "SELECT id FROM series_rules WHERE title_key = $k AND active = 1", ("$k", w.Key)) is long existing) return existing;
        // il titolo della serie, senza il numero di episodio che LibraryStore aggiunge agli episodi
        return Db.Insert(c, "INSERT INTO series_rules (profile_id, title, title_key, created_at) VALUES ($p, $t, $k, $now)",
            ("$p", profileId), ("$t", w.Title), ("$k", w.Key), ("$now", Db.Unix(now)));
    }

    public List<SeriesRule> Rules(DateTimeOffset now, bool activeOnly = true)
    {
        using var c = Open();
        return Db.Query(c, $"""
            SELECT r.id, r.profile_id, r.title, r.active, r.created_at, r.checked_at,
                   (SELECT count(*) FROM recordings x WHERE x.series_rule_id = r.id AND x.state IN ('scheduled', 'recording', 'merging')),
                   (SELECT count(*) FROM recordings x WHERE x.series_rule_id = r.id AND x.state IN ('completed', 'incomplete')),
                   (SELECT x.start FROM recordings x WHERE x.series_rule_id = r.id AND x.state = 'scheduled' AND x.start > $now ORDER BY x.start LIMIT 1),
                   coalesce((SELECT ch.name FROM recordings x JOIN channels ch ON ch.id = x.channel_id
                             WHERE x.series_rule_id = r.id AND x.state = 'scheduled' AND x.start > $now ORDER BY x.start LIMIT 1), '')
            FROM series_rules r {(activeOnly ? "WHERE r.active = 1" : "")} ORDER BY r.title
            """,
            r => new SeriesRule(r.GetInt64(0), Db.NLong(r, 1), r.GetString(2), r.GetInt64(3) == 1, Db.FromUnix(r.GetInt64(4)), Db.NTime(r, 5),
                r.GetInt32(6), r.GetInt32(7), Db.NTime(r, 8), r.GetString(9)),
            ("$now", Db.Unix(now)));
    }

    public SeriesRule? Rule(long id, DateTimeOffset now) => Rules(now, activeOnly: false).FirstOrDefault(r => r.Id == id);

    /// <summary>Ferma la regola e annulla le sue registrazioni non ancora partite. Falso se non c'è o era già ferma.</summary>
    public bool Stop(long id)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        if (Db.Exec(c, "UPDATE series_rules SET active = 0 WHERE id = $id AND active = 1", ("$id", id)) == 0) return false;
        Db.Exec(c, "UPDATE recordings SET state = 'cancelled' WHERE series_rule_id = $id AND state = 'scheduled'", ("$id", id));
        tx.Commit();
        return true;
    }

    /// <summary>Controlla tutte le regole attive (il giro automatico); solo quelle che hanno programmato qualcosa.</summary>
    public List<SeriesCheck> CheckAll(DateTimeOffset now, SeriesDefaults d) =>
        [.. Rules(now).Select(r => Check(r.Id, now, d)).Where(x => x != null && x.NewlyScheduled > 0).Select(x => x!)];

    /// <summary>
    /// Cerca in guida le puntate della regola e programma quelle nuove. null se la regola non c'è o è ferma.
    /// </summary>
    public SeriesCheck? Check(long ruleId, DateTimeOffset now, SeriesDefaults d)
    {
        List<long> works;
        Dictionary<string, (long Id, string State)> taken;
        using (var c = Open())
        {
            var key = Db.Query(c, "SELECT title_key FROM series_rules WHERE id = $id AND active = 1", r => r.GetString(0), ("$id", ruleId)).FirstOrDefault();
            if (key == null) return null;
            works = Db.Query(c, "SELECT id FROM works WHERE title_key = $k AND kind IN ('series', 'episode', 'programme', 'documentary')",
                r => r.GetInt64(0), ("$k", key));
            // puntate già prese dalla regola: in programma, in corso o registrate (una non riuscita si può rifare)
            taken = Db.Query(c, """
                SELECT episode_key, id, state FROM recordings
                WHERE series_rule_id = $id AND episode_key <> '' AND state IN ('scheduled', 'recording', 'merging', 'completed', 'incomplete')
                """, r => (Key: r.GetString(0), Id: r.GetInt64(1), State: r.GetString(2)), ("$id", ruleId))
                .GroupBy(x => x.Key).ToDictionary(g => g.Key, g => (g.First().Id, g.First().State));
        }

        var episodes = new List<SeriesEpisode>();
        int added = 0;
        var upcoming = WorkAirings.Upcoming(guide, works, now).Values.SelectMany(x => x).Where(p => p.Start > now)
            .DistinctBy(p => p.Id).OrderBy(p => p.Start).ToList();
        foreach (var p in upcoming)
        {
            string ek = EpisodeKey(p);
            if (taken.TryGetValue(ek, out var t))
            {
                string status = t.State is "completed" or "incomplete" ? "done" : PendingIs(t.Id, p) ? "pending" : "replica";
                episodes.Add(new SeriesEpisode(p, ek, status, t.Id));
                continue;
            }
            if (p.Scheduled) { taken[ek] = (0, "scheduled"); episodes.Add(new SeriesEpisode(p, ek, "pending", null)); continue; }
            if (p.ChannelId is not long ch) { episodes.Add(new SeriesEpisode(p, ek, "not_recordable", null)); continue; }
            if (d.MaxParallel > 0 && recordings.Overlapping(p.Start.AddMinutes(-d.BeforeMin), p.Stop.AddMinutes(d.AfterMin)) >= d.MaxParallel)
            {
                episodes.Add(new SeriesEpisode(p, ek, "limit", null));
                continue;
            }
            long id = recordings.Schedule(ch, RecordingTitle(p), p.Start, p.Stop, d.BeforeMin, d.AfterMin, d.Format, now, p.WorkId, null, p.Id, ruleId, ek);
            taken[ek] = (id, "scheduled");
            episodes.Add(new SeriesEpisode(p with { Scheduled = true }, ek, "scheduled", id));
            added++;
        }
        using (var c = Open())
            Db.Exec(c, "UPDATE series_rules SET checked_at = $now WHERE id = $id", ("$now", Db.Unix(now)), ("$id", ruleId));
        var rule = Rule(ruleId, now)!;
        return new SeriesCheck(rule, episodes, added, MissingEpisodes(upcoming.Select(p => p.Episode)));

        // la registrazione presa per la puntata è proprio questo passaggio (stesso inizio): non è una replica
        bool PendingIs(long recId, GuideProgramme p)
        {
            using var c = Open();
            return Db.ScalarLong(c, "SELECT start FROM recordings WHERE id = $id", ("$id", recId)) == Db.Unix(p.Start);
        }
    }
}
