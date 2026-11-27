using System.Text.Json;
using DoveLoFanno.Recording;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Data;

public sealed record RecordingRow(
    long Id, long ChannelId, string ChannelName, string Title, DateTimeOffset Start, DateTimeOffset Stop, int BeforeMin, int AfterMin,
    string Format, string State, int LostMinutes, IReadOnlyList<LostRange> LostRanges, string File, string Log, DateTimeOffset CreatedAt, long? WorkId,
    string Poster = "", long? LibraryItemId = null)
{
    public DateTimeOffset RecordFrom => Start.AddMinutes(-BeforeMin);
    public DateTimeOffset RecordTo => Stop.AddMinutes(AfterMin);
}

/// <summary>Parte della registrazione persa (PC in sospensione o partito in ritardo).</summary>
public sealed record LostRange(DateTimeOffset From, DateTimeOffset To)
{
    // nel database: [[da, a], ...] in secondi Unix
    internal static IReadOnlyList<LostRange> Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<long[][]>(json)?.Where(p => p.Length == 2)
                .Select(p => new LostRange(Db.FromUnix(p[0]), Db.FromUnix(p[1]))).ToList() ?? [];
        }
        catch (JsonException) { return []; }
    }
}

public sealed record PartRow(long Id, int PartNo, long? StreamId, string Url, string File, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, string ExitReason);

/// <summary>Registrazioni e spezzoni nel database.</summary>
public sealed class RecordingStore(string dbPath)
{
    public static readonly string[] Pending = ["scheduled", "recording", "merging"];

    SqliteConnection Open() => Database.Open(dbPath);

    /// <param name="seriesRuleId">la regola «Registra la serie» che l'ha programmata, con la chiave della puntata</param>
    public long Schedule(long channelId, string title, DateTimeOffset start, DateTimeOffset stop, int beforeMin, int afterMin, string format, DateTimeOffset now, long? workId = null, long? profileId = null, long? programmeId = null,
        long? seriesRuleId = null, string episodeKey = "")
    {
        if (stop <= start) throw new ArgumentException("la fine deve essere dopo l'inizio");
        if (format is not ("mp4" or "mp3")) throw new ArgumentException("formato non valido: " + format);
        using var c = Open();
        return Db.Insert(c, """
            INSERT INTO recordings (channel_id, work_id, profile_id, programme_id, title, start, stop, before_min, after_min, format, created_at, series_rule_id, episode_key)
            VALUES ($c, $w, $p, $pg, $t, $s, $e, $b, $a, $f, $now, $sr, $ek)
            """,
            ("$c", channelId), ("$w", workId), ("$p", profileId), ("$pg", programmeId), ("$t", title), ("$s", Db.Unix(start)), ("$e", Db.Unix(stop)),
            ("$b", beforeMin), ("$a", afterMin), ("$f", format), ("$now", Db.Unix(now)), ("$sr", seriesRuleId), ("$ek", episodeKey));
    }

    /// <summary>
    /// Opera e programma di una registrazione, per i nomi delle cartelle e il .nfo. Se la guida è stata ricaricata e il
    /// programma ha cambiato id, si cerca quello della stessa opera allo stesso orario. null se la registrazione non c'è.
    /// </summary>
    public RecordingMedia? Media(long id)
    {
        using var c = Open();
        return Db.Query(c, """
            SELECT coalesce(w.kind, ''), coalesce(w.title, ''), coalesce(nullif(w.year, 0), p.year, 0), w.tmdb_id, coalesce(w.tmdb_kind, ''),
                   coalesce(w.imdb_id, ''), coalesce(w.overview, ''), coalesce(p.description, ''), coalesce(p.categories, '[]'),
                   coalesce(p.directors, ''), coalesce(p.actors, ''), coalesce(p.episode, ''), coalesce(p.subtitle, ''), coalesce(p.group_name, '')
            FROM recordings r LEFT JOIN works w ON w.id = r.work_id
            LEFT JOIN programmes p ON p.id = coalesce(r.programme_id,
                (SELECT x.id FROM programmes x WHERE x.work_id = r.work_id AND x.start = r.start LIMIT 1))
            WHERE r.id = $id
            """,
            x => new RecordingMedia(x.GetString(0), x.GetString(1), x.GetInt32(2), Db.NInt(x, 3), x.GetString(4), x.GetString(5), x.GetString(6),
                x.GetString(7), x.GetString(8), x.GetString(9), x.GetString(10), x.GetString(11), x.GetString(12), x.GetString(13)),
            ("$id", id)).FirstOrDefault();
    }

    const string Select = """
        SELECT r.id, r.channel_id, ch.name, r.title, r.start, r.stop, r.before_min, r.after_min, r.format, r.state,
               r.lost_minutes, r.lost_ranges, r.file, r.log, r.created_at, r.work_id,
               COALESCE(w.poster, ''),
               (SELECT li.id FROM library_items li WHERE li.recording_id = r.id AND li.missing_since IS NULL ORDER BY li.id LIMIT 1)
        FROM recordings r JOIN channels ch ON ch.id = r.channel_id LEFT JOIN works w ON w.id = r.work_id
        """;

    static RecordingRow Map(SqliteDataReader r) => new(
        r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), Db.FromUnix(r.GetInt64(4)), Db.FromUnix(r.GetInt64(5)),
        r.GetInt32(6), r.GetInt32(7), r.GetString(8), r.GetString(9), r.GetInt32(10), LostRange.Parse(r.GetString(11)), r.GetString(12), r.GetString(13),
        Db.FromUnix(r.GetInt64(14)), Db.NLong(r, 15),
        WorkStore.ImageUrl(r.GetString(16), "w342"), Db.NLong(r, 17));

    public RecordingRow? Get(long id)
    {
        using var c = Open();
        return Db.Query(c, Select + " WHERE r.id = $id", Map, ("$id", id)).FirstOrDefault();
    }

    public List<RecordingRow> List(DateTimeOffset? since = null)
    {
        using var c = Open();
        return Db.Query(c, Select + " WHERE r.deleted_at IS NULL AND ($since IS NULL OR r.stop >= $since) ORDER BY r.start DESC, r.id DESC", Map,
            ("$since", since is DateTimeOffset s ? Db.Unix(s) : null));
    }

    /// <summary>Registrazioni in programma da avviare: inizio meno margine meno <paramref name="lead"/> già passato.</summary>
    public List<RecordingRow> Due(DateTimeOffset now, TimeSpan lead)
    {
        using var c = Open();
        return Db.Query(c, Select + " WHERE r.state = 'scheduled' AND r.start - r.before_min * 60 - $lead <= $now ORDER BY r.start", Map,
            ("$lead", (long)lead.TotalSeconds), ("$now", Db.Unix(now)));
    }

    public void SetState(long id, string state)
    {
        using var c = Open();
        Db.Exec(c, "UPDATE recordings SET state = $s WHERE id = $id", ("$s", state), ("$id", id));
    }

    /// <summary>
    /// Modifica una registrazione non ancora partita (23/11/2026, richiesta di Antonio: la matita in Registrazioni): canale,
    /// titolo, orari, margini e formato. false se nel frattempo è partita, finita o annullata (lo scheduler la porta a
    /// «recording» quando la prende, quindi la condizione sullo stato basta a non toccarne una in corso).
    /// </summary>
    public bool Update(long id, long channelId, string title, DateTimeOffset start, DateTimeOffset stop, int beforeMin, int afterMin, string format)
    {
        if (stop <= start) throw new ArgumentException("la fine deve essere dopo l'inizio");
        if (format is not ("mp4" or "mp3")) throw new ArgumentException("formato non valido: " + format);
        using var c = Open();
        return Db.Exec(c, """
            UPDATE recordings SET channel_id = $c, title = $t, start = $s, stop = $e, before_min = $b, after_min = $a, format = $f
            WHERE id = $id AND state = 'scheduled'
            """,
            ("$c", channelId), ("$t", title), ("$s", Db.Unix(start)), ("$e", Db.Unix(stop)), ("$b", beforeMin), ("$a", afterMin),
            ("$f", format), ("$id", id)) == 1;
    }

    /// <summary>
    /// Nuova fine del programma e margine dopo, anche per una registrazione in corso. false se non è in programma né in
    /// corso (o la fine non è dopo l'inizio).
    /// </summary>
    public bool ChangeEnd(long id, DateTimeOffset stop, int afterMin)
    {
        if (afterMin < 0) throw new ArgumentException("margine negativo");
        using var c = Open();
        return Db.Exec(c, """
            UPDATE recordings SET stop = $e, after_min = $a WHERE id = $id AND state IN ('scheduled', 'recording') AND start < $e
            """, ("$e", Db.Unix(stop)), ("$a", afterMin), ("$id", id)) == 1;
    }

    /// <summary>
    /// Toglie dall'elenco una registrazione finita dopo averne cancellato il file (26/11/2026): resta nel database con
    /// deleted_at, così «Registra la serie» non riprende la puntata alla replica. false se è in programma o in corso.
    /// </summary>
    public bool MarkDeleted(long id, DateTimeOffset now)
    {
        using var c = Open();
        return Db.Exec(c, "UPDATE recordings SET deleted_at = $now, file = '' WHERE id = $id AND deleted_at IS NULL AND state NOT IN ('scheduled', 'recording', 'merging')",
            ("$now", Db.Unix(now)), ("$id", id)) == 1;
    }

    /// <summary>Annulla una registrazione non ancora partita; false se è già partita o finita.</summary>
    public bool Cancel(long id)
    {
        using var c = Open();
        return Db.Exec(c, "UPDATE recordings SET state = 'cancelled' WHERE id = $id AND state = 'scheduled'", ("$id", id)) == 1;
    }

    public void Finish(long id, RecordingOutcome o)
    {
        using var c = Open();
        string ranges = JsonSerializer.Serialize(o.LostRanges.Select(x => new[] { Db.Unix(x.From), Db.Unix(x.To) }));
        Db.Exec(c, "UPDATE recordings SET state = $s, file = $f, lost_minutes = $l, lost_ranges = $r, log = $log WHERE id = $id",
            ("$s", o.State), ("$f", o.Output), ("$l", o.LostMinutes), ("$r", ranges), ("$log", o.Log), ("$id", id));
    }

    public void Fail(long id, string reason)
    {
        using var c = Open();
        Db.Exec(c, "UPDATE recordings SET state = 'failed', log = CASE WHEN log = '' THEN $r ELSE log END WHERE id = $id", ("$r", reason), ("$id", id));
    }

    public void PartStarted(long recordingId, int no, long streamId, string url, string file, DateTimeOffset at)
    {
        using var c = Open();
        Db.Exec(c, """
            INSERT INTO recording_parts (recording_id, part_no, stream_id, url, file, started_at) VALUES ($r, $n, $s, $u, $f, $t)
            ON CONFLICT (recording_id, part_no) DO UPDATE SET stream_id = $s, url = $u, file = $f, started_at = $t, ended_at = NULL, exit_reason = ''
            """,
            ("$r", recordingId), ("$n", no), ("$s", streamId), ("$u", url), ("$f", file), ("$t", Db.Unix(at)));
    }

    public void PartEnded(long recordingId, RecordedPart p)
    {
        using var c = Open();
        Db.Exec(c, "UPDATE recording_parts SET ended_at = $t, exit_reason = $x WHERE recording_id = $r AND part_no = $n",
            ("$t", Db.Unix(p.Ended)), ("$x", p.Reason), ("$r", recordingId), ("$n", p.No));
    }

    /// <summary>
    /// Gli spezzoni registrati finora, anche quello che il server non ha chiuso (fermato a metà): per riprendere una
    /// registrazione dopo un riavvio.
    /// </summary>
    public List<PartRow> PartsSoFar(long recordingId)
    {
        using var c = Open();
        return Db.Query(c, "SELECT id, part_no, stream_id, url, file, started_at, ended_at, exit_reason FROM recording_parts WHERE recording_id = $r ORDER BY part_no",
            r => new PartRow(r.GetInt64(0), r.GetInt32(1), Db.NLong(r, 2), r.GetString(3), r.GetString(4), Db.FromUnix(r.GetInt64(5)), Db.NTime(r, 6), r.GetString(7)),
            ("$r", recordingId));
    }

    public List<PartRow> Parts(long recordingId)
    {
        using var c = Open();
        return Db.Query(c, "SELECT id, part_no, stream_id, url, file, started_at, ended_at, exit_reason FROM recording_parts WHERE recording_id = $r AND ended_at IS NOT NULL ORDER BY part_no",
            r => new PartRow(r.GetInt64(0), r.GetInt32(1), Db.NLong(r, 2), r.GetString(3), r.GetString(4), Db.FromUnix(r.GetInt64(5)), Db.NTime(r, 6), r.GetString(7)),
            ("$r", recordingId));
    }

    /// <summary>
    /// All'avvio del server: le registrazioni rimaste "in corso" (server fermato o PC spento) ripartono se non sono
    /// finite, altrimenti sono non riuscite. Restituisce quante ripartono.
    /// </summary>
    public int RecoverAfterRestart(DateTimeOffset now)
    {
        using var c = Open();
        int again = Db.Exec(c, "UPDATE recordings SET state = 'scheduled' WHERE state IN ('recording', 'merging') AND stop + after_min * 60 > $now", ("$now", Db.Unix(now)));
        Db.Exec(c, "UPDATE recordings SET state = 'failed', log = 'interrotta: il server si è fermato durante la registrazione' WHERE state IN ('recording', 'merging')");
        return again;
    }

    /// <summary>Registrazioni (in programma o in corso) che si sovrappongono a [from, to), margini compresi.</summary>
    public int Overlapping(DateTimeOffset from, DateTimeOffset to, long? exceptId = null)
    {
        using var c = Open();
        return (int)(Db.ScalarLong(c, """
            SELECT count(*) FROM recordings WHERE state IN ('scheduled', 'recording', 'merging') AND ($x IS NULL OR id <> $x)
              AND start - before_min * 60 < $to AND stop + after_min * 60 > $from
            """, ("$from", Db.Unix(from)), ("$to", Db.Unix(to)), ("$x", exceptId)) ?? 0);
    }
}
