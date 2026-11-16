using System.Text.Json;
using DoveLoFanno.Recording;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Data;

public sealed record RecordingRow(
    long Id, long ChannelId, string ChannelName, string Title, DateTimeOffset Start, DateTimeOffset Stop, int BeforeMin, int AfterMin,
    string Format, string State, int LostMinutes, string LostRanges, string File, string Log, DateTimeOffset CreatedAt, long? WorkId)
{
    public DateTimeOffset RecordFrom => Start.AddMinutes(-BeforeMin);
    public DateTimeOffset RecordTo => Stop.AddMinutes(AfterMin);
}

public sealed record PartRow(long Id, int PartNo, long? StreamId, string Url, string File, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, string ExitReason);

/// <summary>Registrazioni e spezzoni nel database.</summary>
public sealed class RecordingStore(string dbPath)
{
    public static readonly string[] Pending = ["scheduled", "recording", "merging"];

    SqliteConnection Open() => Database.Open(dbPath);

    public long Schedule(long channelId, string title, DateTimeOffset start, DateTimeOffset stop, int beforeMin, int afterMin, string format, DateTimeOffset now, long? workId = null, long? profileId = null)
    {
        if (stop <= start) throw new ArgumentException("la fine deve essere dopo l'inizio");
        if (format is not ("mp4" or "mp3")) throw new ArgumentException("formato non valido: " + format);
        using var c = Open();
        return Db.Insert(c, """
            INSERT INTO recordings (channel_id, work_id, profile_id, title, start, stop, before_min, after_min, format, created_at)
            VALUES ($c, $w, $p, $t, $s, $e, $b, $a, $f, $now)
            """,
            ("$c", channelId), ("$w", workId), ("$p", profileId), ("$t", title), ("$s", Db.Unix(start)), ("$e", Db.Unix(stop)),
            ("$b", beforeMin), ("$a", afterMin), ("$f", format), ("$now", Db.Unix(now)));
    }

    const string Select = """
        SELECT r.id, r.channel_id, ch.name, r.title, r.start, r.stop, r.before_min, r.after_min, r.format, r.state,
               r.lost_minutes, r.lost_ranges, r.file, r.log, r.created_at, r.work_id
        FROM recordings r JOIN channels ch ON ch.id = r.channel_id
        """;

    static RecordingRow Map(SqliteDataReader r) => new(
        r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), Db.FromUnix(r.GetInt64(4)), Db.FromUnix(r.GetInt64(5)),
        r.GetInt32(6), r.GetInt32(7), r.GetString(8), r.GetString(9), r.GetInt32(10), r.GetString(11), r.GetString(12), r.GetString(13),
        Db.FromUnix(r.GetInt64(14)), Db.NLong(r, 15));

    public RecordingRow? Get(long id)
    {
        using var c = Open();
        return Db.Query(c, Select + " WHERE r.id = $id", Map, ("$id", id)).FirstOrDefault();
    }

    public List<RecordingRow> List(DateTimeOffset? since = null)
    {
        using var c = Open();
        return Db.Query(c, Select + " WHERE $since IS NULL OR r.stop >= $since ORDER BY r.start DESC, r.id DESC", Map,
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
