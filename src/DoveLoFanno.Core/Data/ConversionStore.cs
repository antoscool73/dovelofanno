using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Data;

/// <summary>Una conversione («Converti...»): file di partenza, formato, stato, file scritto e come.</summary>
public sealed record ConversionRow(long Id, long? ProfileId, string Source, string Title, string Format, string State, string Output, string Method,
    string Error, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt);

/// <summary>La coda delle conversioni: si fanno una alla volta, nell'ordine (ConversionHost).</summary>
public sealed class ConversionStore(string dbPath)
{
    SqliteConnection Open() => Database.Open(dbPath);
    const string Cols = "id, profile_id, source, title, format, state, output, method, error, created_at, finished_at";

    static ConversionRow Read(SqliteDataReader r) => new(r.GetInt64(0), Db.NLong(r, 1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5),
        r.GetString(6), r.GetString(7), r.GetString(8), Db.FromUnix(r.GetInt64(9)), Db.NTime(r, 10));

    public long Add(long? profileId, string source, string title, string format, DateTimeOffset now)
    {
        using var c = Open();
        return Db.Insert(c, "INSERT INTO conversions (profile_id, source, title, format, created_at) VALUES ($p, $s, $t, $f, $now)",
            ("$p", profileId), ("$s", source), ("$t", title), ("$f", format), ("$now", Db.Unix(now)));
    }

    /// <summary>Le ultime conversioni, le più recenti prima.</summary>
    public List<ConversionRow> List(int max = 50)
    {
        using var c = Open();
        return Db.Query(c, $"SELECT {Cols} FROM conversions ORDER BY id DESC LIMIT $m", Read, ("$m", max));
    }

    public ConversionRow? Get(long id)
    {
        using var c = Open();
        return Db.Query(c, $"SELECT {Cols} FROM conversions WHERE id = $id", Read, ("$id", id)).FirstOrDefault();
    }

    /// <summary>La prossima in coda, segnata «running»; null se non ce ne sono.</summary>
    public ConversionRow? TakeNext()
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        var next = Db.Query(c, $"SELECT {Cols} FROM conversions WHERE state = 'queued' ORDER BY id LIMIT 1", Read).FirstOrDefault();
        if (next == null) return null;
        Db.Exec(c, "UPDATE conversions SET state = 'running' WHERE id = $id", ("$id", next.Id));
        tx.Commit();
        return next with { State = "running" };
    }

    public void Finish(long id, bool ok, string output, string method, string error, string log, DateTimeOffset now)
    {
        using var c = Open();
        Db.Exec(c, "UPDATE conversions SET state = $s, output = $o, method = $m, error = $e, log = $l, finished_at = $now WHERE id = $id",
            ("$s", ok ? "done" : "failed"), ("$o", output), ("$m", method), ("$e", error), ("$l", log), ("$now", Db.Unix(now)), ("$id", id));
    }

    /// <summary>Annulla una conversione ancora in coda. Falso se non c'è o è già partita.</summary>
    public bool Cancel(long id)
    {
        using var c = Open();
        return Db.Exec(c, "UPDATE conversions SET state = 'cancelled' WHERE id = $id AND state = 'queued'", ("$id", id)) == 1;
    }

    /// <summary>All'avvio: una conversione rimasta «running» (server riavviato) torna in coda e si rifà da capo.</summary>
    public int Requeue()
    {
        using var c = Open();
        return Db.Exec(c, "UPDATE conversions SET state = 'queued' WHERE state = 'running'");
    }
}
